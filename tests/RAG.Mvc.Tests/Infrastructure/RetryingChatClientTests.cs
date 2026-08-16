using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RAG.Infrastructure.AI;
using Xunit;

namespace RAG.Mvc.Tests.Infrastructure;

/// <summary>
/// Unit tests for the <see cref="RetryingChatClient"/> decorator (resilience
/// spec RETRY-1): transient Ollama failures (connection drops, HTTP 5xx/429,
/// timeouts) retry with exponential backoff before giving up, while caller
/// cancellation and argument errors propagate immediately without retrying.
/// The decorator wraps <see cref="IChatClient"/> so RAG service code is
/// untouched — retries happen at the DI boundary.
/// </summary>
public class RetryingChatClientTests
{
    private static readonly ChatMessage UserMessage = new(ChatRole.User, "Hello");

    private static ChatResponse OkResponse(string text = "OK") =>
        new(new ChatMessage(ChatRole.Assistant, text));

    private static Mock<IChatClient> NewChatMock() => new();

    private static RetryingChatClient NewClient(
        Mock<IChatClient> mock,
        RetryOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(mock.Object, options ?? new RetryOptions(), delay);

    // ── RETRY-1a: transient failure is retried and eventually succeeds ──

    [Fact]
    public async Task GetResponseAsync_TransientFailure_RetriesAndSucceeds()
    {
        var mock = NewChatMock();
        mock.SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection reset"))
            .ReturnsAsync(OkResponse("Recovered."));

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2 });

        var response = await client.GetResponseAsync([UserMessage]);

        Assert.Equal("Recovered.", response.Text);
        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── RETRY-1b: exhausting all retries rethrows the last failure ──

    [Fact]
    public async Task GetResponseAsync_TransientFailure_ExhaustsRetriesAndThrows()
    {
        var mock = NewChatMock();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ollama unavailable"));

        // MaxRetries = 2 → 3 total attempts before giving up.
        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([UserMessage]));

        Assert.Contains("ollama unavailable", ex.Message);
        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    // ── RETRY-1c: caller cancellation is NEVER retried ──

    [Fact]
    public async Task GetResponseAsync_CallerCancelled_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mock = NewChatMock();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("caller cancelled"));

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2 });

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.GetResponseAsync([UserMessage], cancellationToken: cts.Token));

        // A single attempt — the cancelled token must suppress the retry.
        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RETRY-1d: a timeout with a live caller token IS retryable ──

    [Fact]
    public async Task GetResponseAsync_TimeoutWithNoCancellation_Retries()
    {
        using var cts = new CancellationTokenSource(); // never cancelled

        var mock = NewChatMock();
        mock.SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("http client timeout"))
            .ReturnsAsync(OkResponse("Slow but alive."));

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        var response = await client.GetResponseAsync([UserMessage], cancellationToken: cts.Token);

        Assert.Equal("Slow but alive.", response.Text);
        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetResponseAsync_TimeoutException_Retries()
    {
        var mock = NewChatMock();
        mock.SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("generation took too long"))
            .ReturnsAsync(OkResponse());

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await client.GetResponseAsync([UserMessage]);

        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── RETRY-1e: HTTP 5xx and 429 statuses are retried; 4xx is not ──

    [Fact]
    public async Task GetResponseAsync_Http503_Retries()
    {
        var mock = NewChatMock();
        mock.SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(OkResponse());

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await client.GetResponseAsync([UserMessage]);

        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetResponseAsync_Http429_Retries()
    {
        var mock = NewChatMock();
        mock.SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests))
            .ReturnsAsync(OkResponse());

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await client.GetResponseAsync([UserMessage]);

        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetResponseAsync_Http400_DoesNotRetry()
    {
        var mock = NewChatMock();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Bad request", null, HttpStatusCode.BadRequest));

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([UserMessage]));

        // A client error is the caller's fault — retrying cannot fix it.
        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RETRY-1f: argument/validation errors are never retried ──

    [Fact]
    public async Task GetResponseAsync_ArgumentException_DoesNotRetry()
    {
        var mock = NewChatMock();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("invalid options"));

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetResponseAsync([UserMessage]));

        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RETRY-1g: cancellation requested BETWEEN attempts stops retrying ──

    [Fact]
    public async Task GetResponseAsync_CancellationBetweenAttempts_StopsRetrying()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        var mock = NewChatMock();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, _, _) =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException<ChatResponse>(new HttpRequestException("transient"))
                    : Task.FromException<ChatResponse>(new TaskCanceledException("cancelled now"));
            });

        // The injected delay cancels the caller token after the first failure —
        // the second attempt must surface the cancellation instead of retrying.
        Func<TimeSpan, CancellationToken, Task> delay = (_, _) =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) }, delay);

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.GetResponseAsync([UserMessage], cancellationToken: cts.Token));

        Assert.Equal(2, calls);
    }

    // ── RETRY-2: streaming retries a transient failure on first enumeration ──

    [Fact]
    public async Task GetStreamingResponseAsync_TransientFailure_Retries()
    {
        var calls = 0;
        var mock = NewChatMock();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, _, _) =>
            {
                calls++;
                return Updates(fail: calls == 1);
            });

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        var updates = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([UserMessage]))
        {
            updates.Add(update.Text);
        }

        // The first (failing) enumeration is retried; the second delivers the
        // full delta sequence in order.
        Assert.Equal(["Mock", "ed", " answer."], updates);
        mock.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CallerCancelled_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mock = NewChatMock();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Updates(fail: true)); // throws HttpRequestException on enumeration

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        // Caller cancellation wins over the exception type: even a transient
        // failure must NOT be retried once the caller's token is cancelled.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([UserMessage], cancellationToken: cts.Token))
            {
            }
        });

        Assert.Contains("transient streaming failure", ex.Message);
        mock.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RETRY-1h: MaxRetries = 0 disables retries entirely ──

    [Fact]
    public async Task GetResponseAsync_MaxRetriesZero_DoesNotRetry()
    {
        var mock = NewChatMock();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transient"));

        var client = NewClient(mock, new RetryOptions { MaxRetries = 0, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([UserMessage]));

        // Zero retries: a single attempt, then the failure propagates.
        mock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RETRY-3: exponential backoff waits a minimum base delay, doubling ──

    [Fact]
    public async Task GetResponseAsync_Backoff_DoublesExponentiallyFromBaseDelay()
    {
        var delays = new List<TimeSpan>();
        Func<TimeSpan, CancellationToken, Task> delay = (d, _) =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        };

        var mock = NewChatMock();
        mock.SetupSequence(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transient"))
            .ThrowsAsync(new HttpRequestException("transient"))
            .ReturnsAsync(OkResponse());

        var client = NewClient(mock, new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(500) }, delay);

        await client.GetResponseAsync([UserMessage]);

        // Deterministic via the injected fake delay: first wait == BaseDelay,
        // second wait doubles it (exponential backoff).
        Assert.Equal(
            [TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)],
            delays);
        Assert.All(delays, d => Assert.True(d >= TimeSpan.FromMilliseconds(500),
            $"retry wait {d} must be at least the base delay"));
    }

    // ── RETRY-4: remaining IChatClient surface delegates to the inner client ──

    [Fact]
    public void GetService_DelegatesToInner()
    {
        var mock = NewChatMock();
        mock.Setup(c => c.GetService(typeof(string), null)).Returns("from-inner");

        var client = NewClient(mock);

        Assert.Equal("from-inner", client.GetService(typeof(string)));
        mock.Verify(c => c.GetService(typeof(string), null), Times.Once);
    }

    [Fact]
    public void Dispose_DelegatesToInner()
    {
        var mock = NewChatMock();

        NewClient(mock).Dispose();

        mock.Verify(c => c.Dispose(), Times.Once);
    }

    // ── DI wiring: Program.cs must register the decorated client ──

    [Fact]
    public void Host_RegistersChatClientWrappedInRetryingDecorator()
    {
        using var factory = new DecoratorWiringFactory();
        using var scope = factory.Services.CreateScope();

        var chat = scope.ServiceProvider.GetRequiredService<IChatClient>();

        // The app must resolve the decorator, not a bare OllamaChatClient.
        Assert.IsType<RetryingChatClient>(chat);
    }

    /// <summary>
    /// Canned streaming deltas; <paramref name="fail"/> makes the enumeration
    /// throw <see cref="HttpRequestException"/> before yielding anything, so the
    /// retry path for streaming is exercised deterministically.
    /// </summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> Updates(bool fail)
    {
        if (fail)
        {
            throw new HttpRequestException("transient streaming failure");
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, "Mock");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "ed");
        yield return new ChatResponseUpdate(ChatRole.Assistant, " answer.");
    }
}

/// <summary>
/// Minimal host factory that keeps the real AI service registrations (so the
/// <see cref="IChatClient"/> resolved is the actual Program.cs wiring) but
/// disables DB migrate/seed — same pattern as <c>TimeoutHostFactory</c>.
/// </summary>
public sealed class DecoratorWiringFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:ApplyMigrationsOnStartup"] = "false",
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            });
        });
    }
}