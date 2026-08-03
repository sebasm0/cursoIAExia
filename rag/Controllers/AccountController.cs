using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using rag.Models;
using RAG.Infrastructure.Identity;

namespace rag.Controllers;

/// <summary>
/// Account flows (spec user-auth): login, POST-only logout, forgot/reset password
/// via the console email stub, access denied. There is no public signup — accounts
/// are created by administrators (AUTH-6). Every POST is antiforgery-protected
/// (design D5 — no global antiforgery filter).
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender emailSender,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.UserName, model.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User {UserName} signed in", model.UserName);

            // AUTH-1: only safe local URLs are followed — otherwise go home.
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        if (result.IsLockedOut)
        {
            // AUTH-2: lockout after MaxFailedAccessAttempts within the window.
            ModelState.AddModelError(string.Empty, "This account is locked out. Please try again later.");
            return View(model);
        }

        // AUTH-2: generic message — never reveal which input was wrong.
        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // AUTH-3: logout is POST-only. There is no GET action, so a GET request
        // to /Account/Logout returns 404 and never signs the user out.
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User signed out");
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetLink = Url.Action(
                nameof(ResetPassword), "Account",
                new { userId = user.Id, token }, protocol: Request.Scheme);
            await _emailSender.SendAsync(
                model.Email,
                "Password reset",
                $"Reset your password: <a href=\"{resetLink}\">link</a>");
        }

        // AUTH-4: identical generic confirmation whether or not the account exists —
        // the difference is observable only in the console/email stub output.
        ViewData["Message"] = "If an account exists for that email address, a password reset link has been sent.";
        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPassword(string? userId = null, string? token = null)
    {
        return View(new ResetPasswordViewModel
        {
            UserId = userId ?? string.Empty,
            Token = token ?? string.Empty,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(model.UserId);
        var result = user is null
            ? IdentityResult.Failed(new IdentityError { Description = "Invalid password reset token." })
            : await _userManager.ResetPasswordAsync(user, model.Token, model.Password);

        if (result.Succeeded)
        {
            ViewData["Message"] = "Your password has been reset. You can now sign in with your new password.";
            return View(model);
        }

        // AUTH-5: generic error — never leak whether the account or token was the problem.
        ViewData["Error"] = "Invalid or expired password reset token. Please request a new reset link.";
        return View(model);
    }
}
