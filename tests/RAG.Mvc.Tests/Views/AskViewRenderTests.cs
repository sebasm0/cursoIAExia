using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using RAG.Mvc.Tests.Controllers;
using Xunit;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// Slice B view-render tests for the Ask flow (spec ASK-9..ASK-11): the Ask
/// form, the Answer screen (question echo + answer + "Ask another") and the
/// service-unavailable state render per the design system over the real
/// WebApplicationFactory pipeline. No database is touched — the rag.ask policy
/// is satisfied by the TestAuthHandler and the AI stack is stubbed.
/// </summary>
public class AskViewRenderTests
{
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── ASK-9: Ask screen renders per design system ──

    [Fact]
    public async Task Ask_Page_RendersDesignSystemForm()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.RagAsk], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Ask");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ask a Question", body);
        Assert.Contains("name=\"Query\"", body);
        // Design-system helper hint (real copy, no placeholder text).
        Assert.Contains("Answers are generated from your uploaded documents only.", body);
        Assert.DoesNotContain("lorem", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── ASK-9: validation errors render on the form (ASK-5 unchanged) ──

    [Fact]
    public async Task Ask_Post_EmptyQuery_ReRendersFormWithValidationError()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.RagAsk], []);
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token, ("Query", "   ")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // ASK-5 unchanged: a blank query re-renders the form with a visible
        // server-side validation error and no pipeline call. The non-nullable
        // Query property triggers the implicit required validation (.NET 8+
        // non-nullable reference types) whose message wins the field error;
        // the controller's own message is asserted at unit level in
        // AskControllerTests (ModelState).
        Assert.Contains("input-validation-error", body);
        Assert.Contains("The Query field is required.", body);
        Assert.Contains("name=\"Query\"", body);
        Assert.Contains("Answers are generated from your uploaded documents only.", body);
    }

    // ── ASK-10: Answer screen renders question echo + answer + Ask another ──

    [Fact]
    public async Task Ask_Post_ValidQuestion_RendersEchoAnswerAndAskAnother()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token, ("Query", "What is the capital of France?")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Question echo.
        Assert.Contains("Your question", body);
        Assert.Contains("What is the capital of France?", body);
        // Generated answer.
        Assert.Contains("Answer", body);
        Assert.Contains("Paris", body);
        // ASK-10: "Ask another" returns to the Ask form (no conversation state).
        Assert.Contains("Ask another", body);
    }

    // ── ASK-13: empty answer renders a non-blank fallback ──

    [Fact]
    public async Task Ask_Post_EmptyResponse_RendersFallback()
    {
        await using var factory = new EmptyAnswerRagWebApplicationFactory();
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token, ("Query", "Produce no answer")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // ASK-13: neither ErrorMessage nor Answer is populated — the else
        // branch renders a non-blank fallback instead of an empty page.
        Assert.Contains("No answer was generated", body);
        // Retry / back-navigation actions remain available.
        Assert.Contains("Try Again", body);
        Assert.Contains("Back to Home", body);
        // The fallback must not collide with the error branch.
        Assert.DoesNotContain("Service unavailable", body);
    }

    // ── ASK-11: service-unavailable state per design system ──

    [Fact]
    public async Task Ask_Post_ServiceUnavailable_RendersFriendlyError()
    {
        await using var factory = new FailingRagWebApplicationFactory();
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token, ("Query", "Is the service up?")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Service unavailable", body);
        Assert.Contains("temporarily unavailable", body);
        Assert.Contains("Try Again", body);
        // ASK-4: no stack traces or internal details.
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("Stack Trace", body);
    }
}

/// <summary>
/// Ask-flow factory where the chat client throws: drives the ASK-11
/// service-unavailable branch (RagService.AskAsync fails, the controller
/// catches it and renders the friendly error state).
/// </summary>
public class FailingRagWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication([Permissions.RagAsk], []);

            RemoveService<IChatClient>(services);

            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Ollama is unreachable"));

            services.AddSingleton<IChatClient>(mockChat.Object);
        });
    }
}

/// <summary>
/// Ask-flow factory where the chat client returns an empty assistant message:
/// <c>RagService.AskAsync</c> succeeds with "" (no exception), so the controller
/// renders the result view with BOTH <c>ErrorMessage</c> and <c>Answer</c> empty
/// — the ASK-13 scenario that previously produced a blank page.
/// </summary>
public class EmptyAnswerRagWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication([Permissions.RagAsk], []);

            RemoveService<IChatClient>(services);

            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

            services.AddSingleton<IChatClient>(mockChat.Object);
        });
    }
}
