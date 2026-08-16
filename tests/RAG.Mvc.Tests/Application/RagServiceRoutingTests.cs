using Microsoft.Extensions.AI;
using Moq;
using RAG.Application.Services;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;
using Xunit;

namespace RAG.Mvc.Tests.Application;

/// <summary>
/// Unit tests for per-request model routing in <see cref="RagService"/> (spec
/// assistant-selection ASEL-2/ASEL-8: a known model id reaches
/// <c>ChatOptions.ModelId</c>, null/blank and unknown ids fall back to the
/// default model, and the retrieval pipeline runs identically for any selection,
/// guarding the embeddings and reranker contracts, ASEL-5/6).
/// The same resolved model must also drive the reranker's chat call, so the
/// latency fix (rerank no longer pinned to the default slow model) is covered.
/// </summary>
public class RagServiceRoutingTests
{
    [Fact]
    public async Task AskAsync_KnownModelId_SetsModelIdOnChatOptions()
    {
        var harness = new RoutingHarness();

        var answer = await harness.Service.AskAsync(
            "What is the capital of France?", modelId: "fast");

        Assert.Equal("Mocked answer.", answer);
        Assert.Equal("qwen2.5:1.5b", harness.CapturedOptions?.ModelId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskAsync_NullOrBlankModelId_UsesDefaultModel(string? modelId)
    {
        var harness = new RoutingHarness();

        var answer = await harness.Service.AskAsync("Question?", modelId: modelId);

        Assert.Equal("Mocked answer.", answer);
        Assert.Equal("phi3:mini", harness.CapturedOptions?.ModelId);
    }

    [Fact]
    public async Task AskAsync_UnknownModelId_FallsBackToDefaultModel()
    {
        var harness = new RoutingHarness();

        var answer = await harness.Service.AskAsync("Question?", modelId: "not-in-catalog");

        Assert.Equal("Mocked answer.", answer);
        Assert.Equal("phi3:mini", harness.CapturedOptions?.ModelId);
    }

    [Fact]
    public async Task AskAsync_KnownModelId_SetsModelIdOnRerank()
    {
        var harness = new RoutingHarness();

        await harness.Service.AskAsync("What is the capital of France?", modelId: "fast");

        // Latency fix contract: the resolved model must reach the reranker, not
        // only the final generation call (rerank previously ran on the default
        // slow model regardless of selection).
        Assert.Equal("qwen2.5:1.5b", harness.CapturedRerankOptions?.ModelId);
        // One resolution drives both LLM calls: rerank and generation agree.
        Assert.Equal(harness.CapturedOptions?.ModelId, harness.CapturedRerankOptions?.ModelId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskAsync_NullOrBlankModelId_RerankUsesDefaultModel(string? modelId)
    {
        var harness = new RoutingHarness();

        await harness.Service.AskAsync("Question?", modelId: modelId);

        Assert.Equal("phi3:mini", harness.CapturedRerankOptions?.ModelId);
    }

    [Fact]
    public async Task AskAsync_UnknownModelId_RerankFallsBackToDefaultModel()
    {
        var harness = new RoutingHarness();

        await harness.Service.AskAsync("Question?", modelId: "not-in-catalog");

        Assert.Equal("phi3:mini", harness.CapturedRerankOptions?.ModelId);
    }

    [Fact]
    public async Task AskAsync_SpanishQuery_PromptInstructsSpanish()
    {
        var harness = new RoutingHarness();

        await harness.Service.AskAsync("¿Cuál es la capital de Francia?");

        var prompt = harness.CapturedPrompt;
        Assert.NotNull(prompt);
        Assert.Contains("Answer in Spanish.", prompt);
    }

    [Fact]
    public async Task AskAsync_EnglishQuery_PromptInstructsEnglish()
    {
        var harness = new RoutingHarness();

        await harness.Service.AskAsync("What is the capital of France?");

        var prompt = harness.CapturedPrompt;
        Assert.NotNull(prompt);
        Assert.Contains("Answer in English.", prompt);
    }

    [Fact]
    public async Task AskAsync_PlainQueryWithoutMarkers_DefaultsToSpanish()
    {
        var harness = new RoutingHarness();

        // No accent marks, no Spanish punctuation: still defaults to Spanish
        // because the app UI is Spanish and small models follow an explicit
        // language instruction more reliably.
        await harness.Service.AskAsync("capital de francia");

        var prompt = harness.CapturedPrompt;
        Assert.NotNull(prompt);
        Assert.Contains("Answer in Spanish.", prompt);
    }

    [Fact]
    public async Task AskAsync_RetrievalPipeline_UnchangedForAnySelection()
    {
        var harness = new RoutingHarness();

        await harness.Service.AskAsync("Q1", modelId: "fast");
        await harness.Service.AskAsync("Q2", modelId: null);
        await harness.Service.AskAsync("Q3", modelId: "unknown-model");

        // Embedding, hybrid search and rerank must run exactly once per request,
        // identically for known, blank and unknown selections (ASEL-5/6).
        harness.EmbeddingGenerator.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
        harness.VectorStore.Verify(v => v.HybridSearchAsync(
            It.IsAny<ReadOnlyMemory<float>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
        harness.Reranker.Verify(r => r.RerankAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<SearchResult>>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    // ── DocsChat-3: streaming variant of AskAsync ──
    // AskStreamingAsync must keep the same routing contracts as AskAsync — the
    // resolved model reaches both the reranker and the streaming generation call,
    // retrieval runs once, and null/blank ids resolve to the default model — while
    // yielding the chat client's text updates in order.

    [Fact]
    public async Task AskStreamingAsync_ReturnsUpdatesFromChatClient()
    {
        var harness = new RoutingHarness();

        var updates = new List<string>();
        await foreach (var update in harness.Service.AskStreamingAsync("What is the capital of France?"))
        {
            updates.Add(update);
        }

        // The mock streams "Mock", "ed", " answer." — order and content prove the
        // updates flow through untouched.
        Assert.Equal(["Mock", "ed", " answer."], updates);
    }

    [Fact]
    public async Task AskStreamingAsync_KnownModelId_SetsModelIdOnChatOptions()
    {
        var harness = new RoutingHarness();

        await foreach (var _ in harness.Service.AskStreamingAsync("What is the capital of France?", modelId: "fast")) { }

        Assert.Equal("qwen2.5:1.5b", harness.CapturedOptions?.ModelId);
    }

    [Fact]
    public async Task AskStreamingAsync_KnownModelId_SetsModelIdOnRerank()
    {
        var harness = new RoutingHarness();

        await foreach (var _ in harness.Service.AskStreamingAsync("What is the capital of France?", modelId: "fast")) { }

        // Latency-fix contract (9c6292f): the resolved model must reach the
        // reranker in the streaming path too, agreeing with the generation call.
        Assert.Equal("qwen2.5:1.5b", harness.CapturedRerankOptions?.ModelId);
        Assert.Equal(harness.CapturedOptions?.ModelId, harness.CapturedRerankOptions?.ModelId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskStreamingAsync_NullOrBlankModelId_UsesDefaultModel(string? modelId)
    {
        var harness = new RoutingHarness();

        await foreach (var _ in harness.Service.AskStreamingAsync("Question?", modelId: modelId)) { }

        Assert.Equal("phi3:mini", harness.CapturedOptions?.ModelId);
    }

    [Fact]
    public async Task AskStreamingAsync_RunsRetrievalPipelineOnce()
    {
        var harness = new RoutingHarness();

        await foreach (var _ in harness.Service.AskStreamingAsync("Q1", modelId: "fast")) { }

        // A single streaming request must run embedding, hybrid search and rerank
        // exactly once — streaming only changes the final generation call (ASEL-5/6).
        harness.EmbeddingGenerator.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.VectorStore.Verify(v => v.HybridSearchAsync(
            It.IsAny<ReadOnlyMemory<float>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.Reranker.Verify(r => r.RerankAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<SearchResult>>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Citations (DocsChat-4): AskWithSourcesAsync exposes the top-ranked ──
    // fragments that backed the answer, in rerank order, capped at topKRank.
    // The sources array is ADDITIVE to the existing answer contract; retrieval
    // stays unchanged, only its results become visible to callers.

    [Fact]
    public async Task AskWithSourcesAsync_ReturnsAnswerAndTopRankedSources()
    {
        var harness = new RoutingHarness();

        var (answer, sources) = await harness.Service.AskWithSourcesAsync(
            "What is the capital of France?", modelId: "fast");

        // The answer keeps its contract; the sources mirror the reranked top
        // results (Paris before France borders Spain) as snippet-only refs.
        Assert.Equal("Mocked answer.", answer);
        Assert.Equal(2, sources.Count);
        Assert.Equal("Paris is the capital of France.", sources[0].Snippet);
        Assert.Equal("France borders Spain.", sources[1].Snippet);
    }

    [Fact]
    public async Task AskWithSourcesAsync_SourcesCarryFileNameFromChunkMetadata()
    {
        var results = new List<SearchResult>
        {
            new()
            {
                Chunk = new DocumentChunk
                {
                    Content = "Paris is the capital of France.",
                    Metadata = new() { ["source"] = "francia.pdf" },
                },
                RrfScore = 0.9,
            },
            new()
            {
                Chunk = new DocumentChunk
                {
                    Content = "France borders Spain.",
                    Metadata = new() { ["source"] = "espana.pdf" },
                },
                RrfScore = 0.8,
            },
        };
        var harness = new RoutingHarness(results);

        var (_, sources) = await harness.Service.AskWithSourcesAsync("What is the capital of France?");

        // The chunk's "source" metadata (the file name set at chunking time)
        // becomes the citation's fileName. No page-aware extraction exists yet
        // (the PDF parser flattens pages), so the optional page stays null.
        Assert.Equal("francia.pdf", sources[0].FileName);
        Assert.Equal("espana.pdf", sources[1].FileName);
        Assert.Null(sources[0].Page);
        Assert.Null(sources[1].Page);
    }

    [Fact]
    public async Task AskWithSourcesAsync_RespectsTopKRankLimit()
    {
        var harness = new RoutingHarness();

        var (_, sources) = await harness.Service.AskWithSourcesAsync(
            "What is the capital of France?", topKRank: 1);

        // Only the top reranked fragment survives the topKRank cut.
        Assert.Single(sources);
        Assert.Equal("Paris is the capital of France.", sources[0].Snippet);
    }

    [Fact]
    public async Task AskWithSourcesAsync_RunsRetrievalPipelineOnce()
    {
        var harness = new RoutingHarness();

        await harness.Service.AskWithSourcesAsync("Q1", modelId: "fast");

        // Sources are a view over the SAME single retrieval pass — adding the
        // citations must not double the embedding/search/rerank work (ASEL-5/6).
        harness.EmbeddingGenerator.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.VectorStore.Verify(v => v.HybridSearchAsync(
            It.IsAny<ReadOnlyMemory<float>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.Reranker.Verify(r => r.RerankAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<SearchResult>>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskStreamWithSourcesAsync_ReturnsDeltasAndSources()
    {
        var harness = new RoutingHarness();

        var (deltas, sources) = await harness.Service.AskStreamWithSourcesAsync(
            "What is the capital of France?");

        // Streaming contract preserved (deltas in order, unmodified) plus the
        // sources that backed the streamed answer.
        var updates = new List<string>();
        await foreach (var update in deltas)
        {
            updates.Add(update);
        }

        Assert.Equal(["Mock", "ed", " answer."], updates);
        Assert.Equal(2, sources.Count);
        Assert.Equal("Paris is the capital of France.", sources[0].Snippet);
        Assert.Equal("France borders Spain.", sources[1].Snippet);
    }
}

/// <summary>
/// Shared arrangement: real <see cref="AssistantCatalog"/> (3 entries) plus
/// mocked chat/embedding/vector/reranker so the full RAG pipeline runs with
/// deterministic retrieval results while the chat client captures the
/// <see cref="ChatOptions"/> it receives.
/// </summary>
internal sealed class RoutingHarness
{
    public Mock<IEmbeddingGenerator<string, Embedding<float>>> EmbeddingGenerator { get; }
    public Mock<IVectorStore> VectorStore { get; }
    public Mock<IReranker> Reranker { get; }
    public ChatOptions? CapturedOptions { get; private set; }
    public ChatOptions? CapturedRerankOptions { get; private set; }
    public string? CapturedPrompt { get; private set; }
    public RagService Service { get; }

    public RoutingHarness(IReadOnlyList<SearchResult>? searchResults = null)
    {
        EmbeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        EmbeddingGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]))]));

        // The two known fragments used by the routing tests; callers may inject
        // their own (e.g. chunks carrying "source" metadata) via the ctor.
        var results = searchResults ?? new List<SearchResult>
        {
            new() { Chunk = new DocumentChunk { Content = "Paris is the capital of France." }, RrfScore = 0.9 },
            new() { Chunk = new DocumentChunk { Content = "France borders Spain." }, RrfScore = 0.8 },
        };

        VectorStore = new Mock<IVectorStore>();
        VectorStore
            .Setup(v => v.HybridSearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);

        Reranker = new Mock<IReranker>();
        Reranker
            .Setup(r => r.RerankAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<SearchResult>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<SearchResult>, string?, CancellationToken>(
                (_, _, modelId, _) => CapturedRerankOptions = new ChatOptions { ModelId = modelId })
            .ReturnsAsync(results);

        var chat = new Mock<IChatClient>();
        chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (messages, options, _) =>
                {
                    CapturedOptions = options;
                    CapturedPrompt = messages
                        .Where(m => m.Role == ChatRole.User)
                        .Select(m => m.Text)
                        .LastOrDefault();
                })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Mocked answer.")));
        chat
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (messages, options, _) =>
                {
                    CapturedOptions = options;
                    CapturedPrompt = messages
                        .Where(m => m.Role == ChatRole.User)
                        .Select(m => m.Text)
                        .LastOrDefault();
                })
            .Returns(StreamingUpdates());

        var catalog = new AssistantCatalog("phi3:mini",
        [
            new AssistantDefinition("default", "Phi3 Mini", "phi3:mini", "Balanced quality and speed"),
            new AssistantDefinition("fast", "Qwen 1.5B", "qwen2.5:1.5b", "Fast answers"),
            new AssistantDefinition("tiny", "Llama 1B", "llama3.2:1b", "Fastest answers"),
        ]);

        Service = new RagService(
            EmbeddingGenerator.Object,
            VectorStore.Object,
            Reranker.Object,
            chat.Object,
            catalog);
    }

    /// <summary>
    /// Canned streaming deltas for <see cref="IChatClient.GetStreamingResponseAsync"/>:
    /// three text updates that concatenate to "Mocked answer." so streaming tests
    /// can assert the deltas arrive in order and unmodified.
    /// </summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> StreamingUpdates()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Mock");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "ed");
        yield return new ChatResponseUpdate(ChatRole.Assistant, " answer.");
    }
}