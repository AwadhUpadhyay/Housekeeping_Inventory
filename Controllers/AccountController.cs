using System.Security.Claims;
using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelHousekeepingApp.Controllers;

public class AccountController : Controller
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;

    private const int MaxFailedAttempts = 5;

    public AccountController(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IAuditService auditService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ViewBag.ReturnUrl = returnUrl;

        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userRepository.GetByUsernameAsync(
            model.Username.Trim());

        // Unknown username
        // No audit log is created.
        if (user is null)
        {
            ModelState.AddModelError(
                string.Empty,
                "Invalid username or password.");

            return View(model);
        }

        // Inactive account
        // No audit log is created.
        if (!user.IsActive)
        {
            ModelState.AddModelError(
                string.Empty,
                "This account has been deactivated. Contact your Administrator.");

            return View(model);
        }

        // Account locked
        // No audit log is created.
        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            ModelState.AddModelError(
                string.Empty,
                "Account locked due to too many failed attempts. Ask your Administrator to reset it.");

            return View(model);
        }

        // Wrong password
        // Keep login-failure tracking, but do not create an AuditLog.
        if (!_passwordHasher.Verify(
                model.Password,
                user.PasswordHash))
        {
            await _userRepository.RecordLoginFailureAsync(
                user.UserId);

            ModelState.AddModelError(
                string.Empty,
                "Invalid username or password.");

            return View(model);
        }

        // Successful login
        // Update user's login information, but do not create an AuditLog.
        await _userRepository.RecordLoginSuccessAsync(
            user.UserId);

        var claims = new List<Claim>
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                user.UserId.ToString()),

            new Claim(
                ClaimTypes.Name,
                user.Username),

            new Claim(
                "FullName",
                user.FullName),

            new Claim(
                ClaimTypes.Role,
                user.RoleName ?? string.Empty)
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false
            });

        if (!string.IsNullOrEmpty(returnUrl) &&
            Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // No audit log for logout.
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}