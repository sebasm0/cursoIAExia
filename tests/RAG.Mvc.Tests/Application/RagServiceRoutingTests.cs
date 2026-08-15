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
    public async Task AskAsync_Prompt_InstructsToAnswerInTheQuestionLanguage()
    {
        var harness = new RoutingHarness();

        await harness.Service.AskAsync("¿Cuál es la capital de Francia?");

        var prompt = harness.CapturedPrompt;
        Assert.NotNull(prompt);
        Assert.Contains("same language", prompt, StringComparison.OrdinalIgnoreCase);
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
            It.IsAny<CancellationToken>()), Times.Exactly(3));
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
    public string? CapturedPrompt { get; private set; }
    public RagService Service { get; }

    public RoutingHarness()
    {
        EmbeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        EmbeddingGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]))]));

        var results = new List<SearchResult>
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
                It.IsAny<CancellationToken>()))
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
}