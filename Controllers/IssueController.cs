using System.Security.Claims;
using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelHousekeepingApp.Controllers;

[Authorize(Roles = "Admin,IT,Housekeeping")]
public class IssueController : Controller
{
    private readonly IIssueRepository _issueRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IAuditService _auditService;

    public IssueController(IIssueRepository issueRepository, IInventoryRepository inventoryRepository,
        IEmployeeRepository employeeRepository, IAuditService auditService)
    {
        _issueRepository = issueRepository;
        _inventoryRepository = inventoryRepository;
        _employeeRepository = employeeRepository;
        _auditService = auditService;
    }

    private string CurrentUsername => User.Identity?.Name ?? "unknown";
    private string CurrentFullName => User.FindFirst("FullName")?.Value ?? CurrentUsername;
    private int CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public async Task<IActionResult> Index()
    {
        var recent = await _issueRepository.GetRecentAsync(50);
        return View(recent);
    }

    public async Task<IActionResult> Create()
    {
        var vm = await BuildFormAsync(new IssueItemViewModel());
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IssueItemViewModel model)
    {
        // Either a real inventory item OR a typed name must be given — not both required, but at least one.
        var hasInventoryItem = model.ItemId.HasValue && model.ItemId.Value > 0;
        var hasCustomName = !string.IsNullOrWhiteSpace(model.CustomItemName);

        if (!hasInventoryItem && !hasCustomName)
        {
            ModelState.AddModelError(string.Empty, "Please either select an item from the list, or type an item name manually.");
        }

        // Authorized By is no longer a user choice — it's always the person currently logged in.
        model.AuthorizedByUserId = CurrentUserId;
        model.AuthorizedByName = CurrentFullName;

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        try
        {
            var issue = new IssueTransaction
            {
                ItemId = hasInventoryItem ? model.ItemId : null,
                CustomItemName = hasCustomName ? model.CustomItemName!.Trim() : null,
                Quantity = model.Quantity,
                IssuedToEmployeeId = model.IssuedToEmployeeId,
                IssuedToName = string.IsNullOrWhiteSpace(model.IssuedToName) ? string.Empty : model.IssuedToName.Trim(),
                RoomNumber = model.RoomNumber,
                Department = model.Department,
                Purpose = model.Purpose,
                IssuedByUserId = CurrentUserId,
                AuthorizedByUserId = CurrentUserId
            };

            var newStock = await _issueRepository.IssueItemAsync(issue);
            var itemLabel = issue.CustomItemName ?? $"item #{issue.ItemId}";
            await _auditService.LogAsync(CurrentUserId, CurrentUsername, "ITEM_ISSUED", "IssueTransactions", issue.ItemId,
                $"Issued {model.Quantity} of {itemLabel} to '{issue.IssuedToName}', room {model.RoomNumber}" +
                (newStock.HasValue ? $", remaining stock {newStock}" : " (non-inventory item, no stock tracked)"));

            TempData["Success"] = newStock.HasValue
                ? $"Item issued successfully. Remaining stock: {newStock}."
                : "Item issued successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model));
        }
    }

    private async Task<IssueItemViewModel> BuildFormAsync(IssueItemViewModel model)
    {
        model.Items = await _issueRepository.GetIssueableItemsAsync();
        model.Employees = await _employeeRepository.GetAllAsync(activeOnly: true);
        model.AuthorizedByName = CurrentFullName;
        return model;
    }
}
