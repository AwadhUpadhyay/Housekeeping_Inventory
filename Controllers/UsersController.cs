using System.Security.Claims;
using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelHousekeepingApp.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;

    public UsersController(IUserRepository userRepository, IRoleRepository roleRepository,
        IPasswordHasher passwordHasher, IAuditService auditService)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
    }

    private string CurrentUsername => User.Identity?.Name ?? "unknown";
    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<IActionResult> Index()
    {
        var users = await _userRepository.GetAllAsync();
        return View(users);
    }

    public async Task<IActionResult> Create()
    {
        var vm = new UserFormViewModel { Roles = await _roleRepository.GetAllAsync() };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel model)
    {
        model.Roles = await _roleRepository.GetAllAsync();

        if (string.IsNullOrWhiteSpace(model.Password) || model.Password.Length < 6)
            ModelState.AddModelError(nameof(model.Password), "Password is required and must be at least 6 characters.");

        if (await _userRepository.UsernameExistsAsync(model.Username))
            ModelState.AddModelError(nameof(model.Username), "This username is already taken.");

        if (!ModelState.IsValid) return View(model);

        var user = new User
        {
            Username = model.Username.Trim(),
            PasswordHash = _passwordHasher.Hash(model.Password!),
            FullName = model.FullName,
            Email = model.Email,
            RoleId = model.RoleId,
            IsActive = model.IsActive,
            MustChangePassword = true,
            CreatedBy = CurrentUsername
        };

        var newId = await _userRepository.CreateAsync(user);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername, "USER_CREATED", "Users", newId,
            $"Created user '{user.Username}' with role id {user.RoleId}");

        TempData["Success"] = $"User '{user.Username}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user is null) return NotFound();

        var vm = new UserFormViewModel
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            RoleId = user.RoleId,
            IsActive = user.IsActive,
            Roles = await _roleRepository.GetAllAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserFormViewModel model)
    {
        model.Roles = await _roleRepository.GetAllAsync();
        if (!ModelState.IsValid) return View(model);

        var user = new User
        {
            UserId = model.UserId,
            FullName = model.FullName,
            Email = model.Email,
            RoleId = model.RoleId,
            IsActive = model.IsActive,
            CreatedBy = CurrentUsername
        };
        await _userRepository.UpdateAsync(user);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername, "USER_UPDATED", "Users", user.UserId);

        TempData["Success"] = "User updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, bool activate)
    {
        await _userRepository.SetActiveAsync(id, activate);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername, activate ? "USER_ACTIVATED" : "USER_DEACTIVATED", "Users", id);
        TempData["Success"] = activate ? "User activated." : "User deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult ResetPassword(int id, string username)
    {
        ViewBag.UserId = id;
        ViewBag.Username = username;
        return View();
    }

    [HttpPost]
    [ActionName("ResetPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPasswordConfirmed(int id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            ModelState.AddModelError(string.Empty, "New password must be at least 6 characters.");
            ViewBag.UserId = id;
            return View();
        }

        var hash = _passwordHasher.Hash(newPassword);
        await _userRepository.ResetPasswordAsync(id, hash, mustChangePassword: true);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername, "PASSWORD_RESET", "Users", id);

        TempData["Success"] = "Password reset successfully. The user must change it at next login.";
        return RedirectToAction(nameof(Index));
    }
}
