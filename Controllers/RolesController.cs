using System.Security.Claims;
using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelHousekeepingApp.Controllers;

// Only Admin can view or add roles.
[Authorize(Roles = "Admin")]
public class RolesController : Controller
{
    private readonly IRoleRepository _roleRepository;
    private readonly IAuditService _auditService;

    public RolesController(IRoleRepository roleRepository, IAuditService auditService)
    {
        _roleRepository = roleRepository;
        _auditService = auditService;
    }

    private string CurrentUsername => User.Identity?.Name ?? "unknown";
    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<IActionResult> Index()
    {
        var roles = await _roleRepository.GetAllAsync();
        return View(roles);
    }

    public IActionResult Create() => View(new RoleFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleFormViewModel model)
    {
        if (await _roleRepository.NameExistsAsync(model.RoleName))
            ModelState.AddModelError(nameof(model.RoleName), "A role with this name already exists.");

        if (!ModelState.IsValid) return View(model);

        var role = new Role { RoleName = model.RoleName.Trim(), Description = model.Description };
        var newId = await _roleRepository.CreateAsync(role);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername, "ROLE_CREATED", "Roles", newId,
            $"Created role '{role.RoleName}'");

        TempData["Success"] = $"Role '{role.RoleName}' created successfully. It will now appear when creating or editing users.";
        return RedirectToAction(nameof(Index));
    }
}
