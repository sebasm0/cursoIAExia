using Moq;
using RAG.Application.Services;
using RAG.Domain.Abstractions;
using RAG.Domain.Chat;
using Xunit;

namespace RAG.Mvc.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ChatHistoryService"/> (spec CH-2/CH-3): the role
/// guard, the trimmed content bound, source normalization, the model credit
/// snapshot and the 50-message window. The store is mocked so each test also
/// proves exactly what the service asks the store to persist (CH-2 contract).
/// </summary>
public class ChatHistoryServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Store mock that echoes back the message it is asked to persist.</summary>
    private static Mock<IChatHistoryStore> CreateStore()
    {
        var store = new Mock<IChatHistoryStore>();
        store
            .Setup(s => s.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);
        return store;
    }

    // ── CH-3: valid messages persist with claim userId + trimmed content ──

    [Fact]
    public async Task AddAsync_ValidUserMessage_PersistsWithClaimUserIdAndTrimmedContent()
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);

        var result = await service.AddAsync(UserId, "user", "  Hola, mundo.  ", null, null);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Message);
        Assert.Equal(UserId, result.Message.UserId);
        Assert.Equal("user", result.Message.Role);
        Assert.Equal("Hola, mundo.", result.Message.Content);

        // The store receives exactly the message derived from the principal (CH-2/CH-3).
        store.Verify(s => s.AddAsync(
            It.Is<ChatMessage>(m =>
                m.UserId == UserId && m.Role == "user" && m.Content == "Hola, mundo."),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_ValidAssistantMessage_Persists()
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);

        var result = await service.AddAsync(UserId, "assistant", "Respuesta del modelo.", null, null);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Message);
        Assert.Equal("assistant", result.Message.Role);
        Assert.Equal("Respuesta del modelo.", result.Message.Content);
        store.Verify(s => s.AddAsync(
            It.Is<ChatMessage>(m => m.Role == "assistant"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CH-3/D10: modelId stored as sent (credit snapshot), null → null ──

    [Theory]
    [InlineData("phi3:mini", "phi3:mini")]
    [InlineData("  qwen2.5:1.5b  ", "qwen2.5:1.5b")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public async Task AddAsync_ModelId_StoredAsSent(string? modelId, string? expected)
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);

        var result = await service.AddAsync(UserId, "assistant", "Respuesta.", modelId, null);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Message);
        Assert.Equal(expected, result.Message.ModelId);
    }

    // ── CH-3: sources normalize (null/empty → empty list) and map to domain ──

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task AddAsync_NullOrEmptySources_NormalizesToEmptyList(int? ignore)
    {
        _ = ignore;
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);
        IReadOnlyList<SourceRef>? sources = ignore is null ? null : [];

        var result = await service.AddAsync(UserId, "assistant", "Respuesta.", null, sources);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Message);
        Assert.Empty(result.Message.Sources);
    }

    [Fact]
    public async Task AddAsync_WithSources_MapsWireSourceRefToDomainChatSource()
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);
        var sources = new List<SourceRef>
        {
            new("francia.pdf", "Paris es la capital.", 3),
            new(null, "Sin archivo.", null),
        };

        var result = await service.AddAsync(UserId, "assistant", "Respuesta.", null, sources);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Message);
        Assert.Equal(2, result.Message.Sources.Count);
        Assert.Equal(new ChatSource("francia.pdf", "Paris es la capital.", 3), result.Message.Sources[0]);
        Assert.Equal(new ChatSource(null, "Sin archivo.", null), result.Message.Sources[1]);
    }

    // ── CH-3/D10: content bound — exact max accepted, beyond rejected ──

    [Fact]
    public async Task AddAsync_ContentAtExactMaxLength_Accepted()
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);
        var content = new string('a', ChatHistoryService.MaxContentLength);

        var result = await service.AddAsync(UserId, "user", content, null, null);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Message);
        Assert.Equal(content.Length, result.Message.Content.Length);
    }

    [Fact]
    public async Task AddAsync_OversizeContent_RejectedAndNothingPersisted()
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);
        var content = new string('a', ChatHistoryService.MaxContentLength + 1);

        var result = await service.AddAsync(UserId, "user", content, null, null);

        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.NotNull(result.ErrorMessage);
        store.Verify(s => s.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CH-3: role must be exactly user|assistant, content non-empty after trim ──

    [Theory]
    [InlineData("system")]
    [InlineData("System")]
    [InlineData("model")]
    [InlineData("")]
    public async Task AddAsync_InvalidRole_RejectedAndNothingPersisted(string role)
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);

        var result = await service.AddAsync(UserId, role, "contenido válido", null, null);

        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.NotNull(result.ErrorMessage);
        store.Verify(s => s.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AddAsync_EmptyContent_RejectedAndNothingPersisted(string? content)
    {
        var store = CreateStore();
        var service = new ChatHistoryService(store.Object);

        var result = await service.AddAsync(UserId, "user", content, null, null);

        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.NotNull(result.ErrorMessage);
        store.Verify(s => s.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CH-5: GetRecentAsync reads the caller's window with the default limit ──

    [Fact]
    public async Task GetRecentAsync_UsesDefaultLimitOf50()
    {
        var store = new Mock<IChatHistoryStore>();
        store
            .Setup(s => s.GetRecentAsync(UserId, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessage { UserId = UserId, Role = "user", Content = "Hola" }]);
        var service = new ChatHistoryService(store.Object);

        var messages = await service.GetRecentAsync(UserId);

        var message = Assert.Single(messages);
        Assert.Equal("Hola", message.Content);
        store.Verify(s => s.GetRecentAsync(UserId, 50, It.IsAny<CancellationToken>()), Times.Once);
    }
}