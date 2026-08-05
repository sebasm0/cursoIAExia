using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using Xunit;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// Slice B modal tests (UDS-7 / B6): every destructive action in the app (user
/// delete, role delete) is gated behind the shared confirm modal, so the action
/// blocks until the user makes a choice. The rendered contract:
///   - the row-level trigger is a modal opener (data-bs-toggle/data-bs-target),
///     not a direct submit;
///   - the modal's Cancel button dismisses without submitting
///     (data-bs-dismiss);
///   - the modal's Confirm button (type=submit) is the only submit wired to the
///     destructive form, via the form attribute.
/// Runs over the real pipeline with InMemory Identity + TestAuthHandler.
/// </summary>
public class ConfirmModalRenderTests
{
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task UsersPage_DeleteAction_BlocksUntilModalChoice()
    {
        await using var factory = new AdminPolicyWebApplicationFactory([Permissions.AdminUsers], []);
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("bob", "P@ssw0rd1!", "bob@example.com");

        Guid bobId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            bobId = (await userManager.FindByNameAsync("bob"))!.Id;
        }

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // The trigger opens the modal instead of submitting the form directly.
        Assert.Contains($"data-bs-toggle=\"modal\" data-bs-target=\"#confirmModal-{bobId}\"", body);
        // The modal itself is rendered for this row.
        Assert.Contains($"id=\"confirmModal-{bobId}\"", body);
        // Cancel dismisses without submitting.
        Assert.Contains("data-bs-dismiss=\"modal\"", body);
        // The only submit wired to the delete form is the modal Confirm button.
        Assert.Contains($"type=\"submit\" form=\"delete-user-{bobId}\"", body);
        // Real copy, placeholder-neutral.
        Assert.Contains("This action cannot be undone.", body);
    }

    [Fact]
    public async Task RolesPage_DeleteAction_BlocksUntilModalChoice()
    {
        await using var factory = new AdminPolicyWebApplicationFactory([Permissions.AdminRoles], []);
        using var client = CreateClient(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var created = await roleManager.CreateAsync(new ApplicationRole { Name = "temp-role" });
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        var response = await client.GetAsync("/Admin/Roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // Role rows are gated the same way (non built-in, no members).
        Assert.Contains("data-bs-toggle=\"modal\"", body);
        Assert.Contains("data-bs-target=\"#confirmModal-", body);
        Assert.Contains("data-bs-dismiss=\"modal\"", body);
        Assert.Contains("type=\"submit\" form=\"delete-role-", body);
        Assert.Contains("This action cannot be undone.", body);
    }
}
