using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using rag.Models;
using RAG.Infrastructure.Identity;

namespace rag.Controllers;

/// <summary>
/// Admin pages — user management (spec user-admin). Users CRUD is gated by
/// <c>admin.users</c> (RBAC-4); the roles and permission-matrix actions live in
/// the <see cref="AdminController.Roles"/> partial (<c>admin.roles</c> /
/// <c>admin.permissions</c>). Every POST is antiforgery-protected (design D5 —
/// no global antiforgery filter). Accounts are created here; there is no public
/// signup.
///
/// Attribute routing keeps the spec URLs (/Admin/Users/Create) — the conventional
/// {controller}/{action}/{id?} route cannot express 4-segment URLs.
/// </summary>
[Route("Admin")]
public partial class AdminController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    // ── ADMIN-1: Users index (admin.users) ──

    [Authorize(Policy = Permissions.AdminUsers)]
    [HttpGet("Users")]
    public async Task<IActionResult> Users()
    {
        var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();

        var rows = new List<UserRow>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            rows.Add(new UserRow(user.Id, user.UserName!, user.Email!, roles.ToList()));
        }

        ViewData["CurrentUserId"] = _userManager.GetUserId(User);
        return View("Users/Index", new UsersIndexViewModel { Users = rows });
    }

    // ── ADMIN-2: User create (admin.users) ──

    [Authorize(Policy = Permissions.AdminUsers)]
    [HttpGet("Users/Create")]
    public async Task<IActionResult> UsersCreate()
    {
        return View("Users/Create", new CreateUserViewModel
        {
            AvailableRoles = await GetRoleNamesAsync(),
        });
    }

    [Authorize(Policy = Permissions.AdminUsers)]
    [HttpPost("Users/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UsersCreate(CreateUserViewModel model)
    {
        model.AvailableRoles = await GetRoleNamesAsync();

        if (!ModelState.IsValid)
        {
            return View("Users/Create", model);
        }

        var user = new ApplicationUser { UserName = model.UserName, Email = model.Email };
        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            // ADMIN-2: duplicate username/email -> validation error, no account created.
            AddIdentityErrors(result);
            return View("Users/Create", model);
        }

        if (model.SelectedRoles.Count > 0)
        {
            var roleResult = await _userManager.AddToRolesAsync(user, model.SelectedRoles);
            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult);
                return View("Users/Create", model);
            }
        }

        _logger.LogInformation("Admin created user {UserName} with roles {Roles}",
            model.UserName, string.Join(", ", model.SelectedRoles));
        return RedirectToAction(nameof(Users));
    }

    // ── ADMIN-3: User edit and role assignment (admin.users) ──

    [Authorize(Policy = Permissions.AdminUsers)]
    [HttpGet("Users/Edit/{id}")]
    public async Task<IActionResult> UsersEdit(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        return View("Users/Edit", new EditUserViewModel
        {
            UserId = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            SelectedRoles = roles.ToList(),
            AvailableRoles = await GetRoleNamesAsync(),
        });
    }

    [Authorize(Policy = Permissions.AdminUsers)]
    [HttpPost("Users/Edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UsersEdit(Guid id, EditUserViewModel model)
    {
        model.AvailableRoles = await GetRoleNamesAsync();

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        model.UserId = user.Id;
        var selected = model.SelectedRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = (await _userManager.GetRolesAsync(user)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ADMIN-3 guard: the admin must not strip their own last admin.users grant
        // (prevents admin lockout). Compute effective permissions before/after.
        var isSelf = Guid.TryParse(_userManager.GetUserId(User), out var selfId) && selfId == user.Id;
        if (isSelf)
        {
            var before = await GetEffectivePermissionsAsync(current);
            var after = await GetEffectivePermissionsAsync(selected);
            if (before.Contains(Permissions.AdminUsers) && !after.Contains(Permissions.AdminUsers))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "You cannot remove your own last admin permission (admin lockout guard).");
                return View("Users/Edit", model);
            }
        }

        // Email update first (may fail on a duplicate) before mutating role membership.
        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await _userManager.SetEmailAsync(user, model.Email);
            if (!emailResult.Succeeded)
            {
                AddIdentityErrors(emailResult);
                return View("Users/Edit", model);
            }
        }

        var addRoles = selected.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
        var removeRoles = current.Except(selected, StringComparer.OrdinalIgnoreCase).ToList();

        if (addRoles.Count > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(user, addRoles);
            if (!addResult.Succeeded)
            {
                AddIdentityErrors(addResult);
                return View("Users/Edit", model);
            }
        }

        if (removeRoles.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, removeRoles);
            if (!removeResult.Succeeded)
            {
                AddIdentityErrors(removeResult);
                return View("Users/Edit", model);
            }
        }

        _logger.LogInformation("Admin edited user {UserName}: roles {Removed} removed, {Added} added",
            user.UserName, string.Join(", ", removeRoles), string.Join(", ", addRoles));
        return RedirectToAction(nameof(Users));
    }

    // ── ADMIN-1: self-delete guard (admin.users) ──

    [Authorize(Policy = Permissions.AdminUsers)]
    [HttpPost("Users/Delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UsersDelete(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (Guid.TryParse(_userManager.GetUserId(User), out var selfId) && selfId == user.Id)
        {
            TempData["AdminError"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(Users));
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            TempData["AdminError"] = "Could not delete the user.";
        }
        else
        {
            _logger.LogInformation("Admin deleted user {UserName}", user.UserName);
        }

        return RedirectToAction(nameof(Users));
    }

    // ── shared helpers ──

    private async Task<IReadOnlyList<string>> GetRoleNamesAsync()
        => await _roleManager.Roles.Select(r => r.Name!).OrderBy(n => n).ToListAsync();

    private async Task<HashSet<string>> GetEffectivePermissionsAsync(IEnumerable<string> roleNames)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleName in roleNames)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            foreach (var claim in await _roleManager.GetClaimsAsync(role))
            {
                if (claim.Type == Permissions.ClaimType)
                {
                    permissions.Add(claim.Value);
                }
            }
        }

        return permissions;
    }

    private static bool IsBuiltInRole(string roleName)
        => Permissions.SeedRoles.ContainsKey(roleName);

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
