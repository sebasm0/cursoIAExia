using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.AI;

namespace RAG.Infrastructure.AI;

/// <summary>
/// Configuration for <see cref="RetryingChatClient"/>: how many times a
/// transient failure is retried and how long the first backoff wait lasts.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// Number of retries after the initial attempt. Default 2 retries means up
    /// to 3 total attempts before the last failure is rethrown.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Wait before the first retry; each subsequent retry doubles the wait
    /// (exponential backoff). Default 500ms.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>
/// Decorator (<see cref="IChatClient"/>) that retries transient chat-client
/// failures with exponential backoff before giving up. Retryable failures are
/// connection-level errors (<see cref="HttpRequestException"/> without a status
/// code), HTTP 5xx/429 statuses, and inner-client timeouts
/// (<see cref="TaskCanceledException"/>/<see cref="TimeoutException"/>) when the
/// caller has NOT cancelled. Caller cancellation and argument/validation errors
/// propagate immediately. The wait is injectable (<paramref name="delay"/>) so
/// backoff timing is deterministic under test; the production default is
/// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// </summary>
public sealed class RetryingChatClient(
    IChatClient inner,
    RetryOptions? options = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IChatClient
{
    private readonly IChatClient _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly RetryOptions _options = options ?? new RetryOptions();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _inner.GetService(serviceType, serviceKey);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await _inner.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken) && attempt < _options.MaxRetries)
            {
                attempt++;
                // If the caller cancels while waiting, the delay throws and the
                // cancellation propagates — no further attempts are made.
                await _delay(ComputeDelay(attempt), cancellationToken);
            }
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // C# forbids yield inside a try block that has a catch clause, so the
        // inner stream is consumed through TryMoveNextAsync (which owns the
        // try/catch and marks transient failures) and the yield statements stay
        // outside any try. A transient failure ends the pass, then the outer
        // loop either backs off and retries with a fresh enumerable or rethrows.
        var attempt = 0;
        while (true)
        {
            Exception? transientFailure = null;
            await using var enumerator = _inner
                .GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                var result = await TryMoveNextAsync(enumerator, cancellationToken);
                if (result.RetryableException is not null)
                {
                    transientFailure = result.RetryableException;
                    break;
                }

                if (!result.Moved)
                {
                    yield break; // clean end of stream
                }

                yield return enumerator.Current;
            }

            if (attempt >= _options.MaxRetries)
            {
                ExceptionDispatchInfo.Capture(transientFailure!).Throw();
            }

            attempt++;
            // If the caller cancels while waiting, the delay throws and the
            // cancellation propagates — no further attempts are made.
            await _delay(ComputeDelay(attempt), cancellationToken);
        }
    }

    public void Dispose() => _inner.Dispose();

    /// <summary>
    /// Advances the inner streaming enumerator once. Transient failures are
    /// returned as a marker so the iterator can decide retry vs rethrow;
    /// non-retryable exceptions (caller cancellation, argument errors) propagate
    /// as-is. The <see cref="ChatResponseUpdate"/> is read from
    /// <c>enumerator.Current</c> by the caller only when <see cref="MoveResult.Moved"/>
    /// is true.
    /// </summary>
    private static async Task<MoveResult> TryMoveNextAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        CancellationToken cancellationToken)
    {
        try
        {
            return new MoveResult(await enumerator.MoveNextAsync());
        }
        catch (Exception ex)
        {
            if (IsRetryable(ex, cancellationToken))
            {
                return new MoveResult(Moved: false, RetryableException: ex);
            }

            throw;
        }
    }

    private readonly record struct MoveResult(bool Moved, Exception? RetryableException = null);

    /// <summary>
    /// Exponential backoff: attempt 1 waits <c>BaseDelay</c>, attempt 2 waits
    /// <c>2 × BaseDelay</c>, and so on.
    /// </summary>
    private TimeSpan ComputeDelay(int attempt) =>
        TimeSpan.FromMilliseconds(_options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

    /// <summary>
    /// Decides whether <paramref name="exception"/> warrants another attempt.
    /// Caller cancellation always wins: if the caller's token is cancelled, no
    /// exception is retried. With a live token, connection failures, transient
    /// HTTP statuses (5xx/429) and inner-client timeouts are retryable; other
    /// exceptions (e.g. argument errors) are not.
    /// </summary>
    private static bool IsRetryable(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            // The Ollama client surfaces non-2xx responses and connection-level
            // failures as HttpRequestException (no status code ⇒ no HTTP
            // response ⇒ network/connection failure, which is transient).
            HttpRequestException http => IsTransientHttpStatus(http.StatusCode),
            // HttpClient timeout on .NET 5+: the token is cancelled internally
            // and a TimeoutException is nested; with a live caller token this
            // is a transient timeout, not caller cancellation.
            TimeoutException => true,
            TaskCanceledException => true,
            // Any other cancellation with a live caller token: not a timeout we
            // can retry (e.g. the inner client cancelled for its own reasons).
            OperationCanceledException => false,
            _ => false,
        };
    }

    private static bool IsTransientHttpStatus(HttpStatusCode? statusCode) => statusCode switch
    {
        null => true,                             // connection failure — no HTTP response
        HttpStatusCode.TooManyRequests => true,   // 429
        _ => (int)statusCode >= 500,              // 5xx
    };
}