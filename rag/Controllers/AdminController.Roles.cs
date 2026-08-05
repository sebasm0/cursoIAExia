using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using rag.Models;
using RAG.Infrastructure.Identity;

namespace rag.Controllers;

/// <summary>
/// Admin pages — roles and the role→permission matrix (spec user-admin).
/// The roles index/create/delete are gated by <c>admin.roles</c> and the
/// permission matrix by <c>admin.permissions</c> (RBAC-4). Part of
/// <see cref="AdminController"/> (user management lives in the
/// <c>AdminController.cs</c> partial).
///
/// Attribute routing keeps the spec URLs (/Admin/Roles/Edit/{id}) — the
/// conventional {controller}/{action}/{id?} route cannot express 4-segment URLs.
/// </summary>
public partial class AdminController
{
    // ── ADMIN-4: Roles index (admin.roles) ──

    [Authorize(Policy = Permissions.AdminRoles)]
    [HttpGet("Roles")]
    public async Task<IActionResult> Roles()
    {
        var roles = await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync();

        var rows = new List<RoleRow>(roles.Count);
        foreach (var role in roles)
        {
            var claims = await _roleManager.GetClaimsAsync(role);
            var members = await _userManager.GetUsersInRoleAsync(role.Name!);
            rows.Add(new RoleRow(
                role.Id,
                role.Name!,
                claims.Count(c => c.Type == Permissions.ClaimType),
                members.Count,
                IsBuiltInRole(role.Name!)));
        }

        return View("Roles/Index", new RolesIndexViewModel { Roles = rows });
    }

    // ── ADMIN-5: Role create (admin.roles) ──

    [Authorize(Policy = Permissions.AdminRoles)]
    [HttpGet("Roles/Create")]
    public IActionResult RolesCreate()
    {
        return View("Roles/Create");
    }

    [Authorize(Policy = Permissions.AdminRoles)]
    [HttpPost("Roles/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RolesCreate(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            // Empty-key so asp-validation-summary="ModelOnly" renders it.
            ModelState.AddModelError(string.Empty, "Role name is required.");
            return View("Roles/Create");
        }

        var name = roleName.Trim();
        if (await _roleManager.RoleExistsAsync(name))
        {
            // ADMIN-5: duplicate role names are rejected.
            ModelState.AddModelError(string.Empty, $"Role '{name}' already exists.");
            return View("Roles/Create");
        }

        var result = await _roleManager.CreateAsync(new ApplicationRole { Name = name });
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View("Roles/Create");
        }

        _logger.LogInformation("Admin created role {RoleName}", name);
        return RedirectToAction(nameof(Roles));
    }

    // ── ADMIN-4: role delete guards (admin.roles) ──

    [Authorize(Policy = Permissions.AdminRoles)]
    [HttpPost("Roles/Delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RolesDelete(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return NotFound();
        }

        if (IsBuiltInRole(role.Name!))
        {
            TempData["AdminError"] = $"The built-in '{role.Name}' role cannot be deleted.";
            return RedirectToAction(nameof(Roles));
        }

        var members = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (members.Count > 0)
        {
            TempData["AdminError"] = $"Role '{role.Name}' still has members and cannot be deleted.";
            return RedirectToAction(nameof(Roles));
        }

        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            TempData["AdminError"] = "Could not delete the role.";
        }
        else
        {
            _logger.LogInformation("Admin deleted role {RoleName}", role.Name);
        }

        return RedirectToAction(nameof(Roles));
    }

    // ── ADMIN-6: Role edit permission matrix (admin.permissions) ──

    [Authorize(Policy = Permissions.AdminPermissions)]
    [HttpGet("Roles/Edit/{id}")]
    public async Task<IActionResult> RolesEdit(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return NotFound();
        }

        var granted = (await _roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        return View("Roles/Edit", new RolePermissionsViewModel
        {
            RoleId = role.Id,
            RoleName = role.Name!,
            Permissions = Permissions.All
                .Select(p => new PermissionCheckbox { Permission = p, IsChecked = granted.Contains(p) })
                .ToList(),
        });
    }

    [Authorize(Policy = Permissions.AdminPermissions)]
    [HttpPost("Roles/Edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RolesEdit(Guid id, RolePermissionsViewModel model)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return NotFound();
        }

        var selected = (model.SelectedPermissions ?? []).ToHashSet(StringComparer.Ordinal);
        var claims = await _roleManager.GetClaimsAsync(role);
        var granted = claims
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        // ADMIN-6: diff the checked set against the role's current permission
        // claims (RBAC-2) and persist only what changed.
        foreach (var permission in Permissions.All)
        {
            if (selected.Contains(permission) && !granted.Contains(permission))
            {
                var add = await _roleManager.AddClaimAsync(
                    role, new Claim(Permissions.ClaimType, permission));
                if (!add.Succeeded)
                {
                    AddIdentityErrors(add);
                    return RedirectToAction(nameof(RolesEdit), new { id });
                }
            }
            else if (!selected.Contains(permission) && granted.Contains(permission))
            {
                var existing = claims.First(c => c.Type == Permissions.ClaimType && c.Value == permission);
                var remove = await _roleManager.RemoveClaimAsync(role, existing);
                if (!remove.Succeeded)
                {
                    AddIdentityErrors(remove);
                    return RedirectToAction(nameof(RolesEdit), new { id });
                }
            }
        }

        _logger.LogInformation("Admin updated permission matrix for role {RoleName}", role.Name);
        return RedirectToAction(nameof(RolesEdit), new { id });
    }
}
