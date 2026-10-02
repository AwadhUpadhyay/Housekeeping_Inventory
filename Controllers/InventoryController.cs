using System.Security.Claims;
using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClosedXML.Excel;

namespace HotelHousekeepingApp.Controllers;

[Authorize(Roles = "Admin,IT,Housekeeping")]
public class InventoryController : Controller
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IAuditService _auditService;

    public InventoryController(
        IInventoryRepository inventoryRepository,
        IAuditService auditService)
    {
        _inventoryRepository = inventoryRepository;
        _auditService = auditService;
    }

    private string CurrentUsername =>
        User.Identity?.Name ?? "unknown";

    private int CurrentUserId =>
        int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : 0;

    // =========================================================
    // RUNNING INVENTORY
    // =========================================================

    public async Task<IActionResult> Index(bool lowStockOnly = false)
    {
        var items =
            await _inventoryRepository.GetRunningInventoryAsync(
                lowStockOnly);

        ViewBag.LowStockOnly = lowStockOnly;

        return View(items);
    }

    // =========================================================
    // EXPORT RUNNING INVENTORY TO EXCEL
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        bool lowStockOnly = false)
    {
        var items =
            await _inventoryRepository.GetRunningInventoryAsync(
                lowStockOnly);

        using var workbook = new XLWorkbook();

        var worksheet =
            workbook.Worksheets.Add("Running Inventory");

        worksheet.Cell(1, 1).Value =
            "Running Inventory";

        worksheet.Range(1, 1, 1, 15).Merge();

        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;
        worksheet.Cell(1, 1).Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        worksheet.Cell(2, 1).Value =
            $"Generated: {DateTime.Now:dd-MM-yyyy HH:mm}";

        worksheet.Range(2, 1, 2, 15).Merge();

        worksheet.Cell(2, 1).Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        string[] headers =
        {
            "Item",
            "SKU",
            "Category",
            "Unit",
            "Running Stock",
            "1st Floor",
            "2nd Floor",
            "3rd Floor",
            "4th Floor",
            "5th Floor",
            "6th Floor",
            "Linen Room",
            "Laundry",
            "Discarded",
            "Missing"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(4, i + 1).Value =
                headers[i];
        }

        var headerRange =
            worksheet.Range(4, 1, 4, headers.Length);

        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        int row = 5;

        foreach (var item in items)
        {
            worksheet.Cell(row, 1).Value =
                item.ItemName;

            worksheet.Cell(row, 2).Value =
                string.IsNullOrWhiteSpace(item.SKU)
                    ? "—"
                    : item.SKU;

            worksheet.Cell(row, 3).Value =
                item.CategoryName;

            worksheet.Cell(row, 4).Value =
                item.Unit;

            worksheet.Cell(row, 5).Value =
                item.AvailableStock;

            worksheet.Cell(row, 6).Value =
                item.FirstFloor;

            worksheet.Cell(row, 7).Value =
                item.SecondFloor;

            worksheet.Cell(row, 8).Value =
                item.ThirdFloor;

            worksheet.Cell(row, 9).Value =
                item.FourthFloor;

            worksheet.Cell(row, 10).Value =
                item.FifthFloor;

            worksheet.Cell(row, 11).Value =
                item.SixthFloor;

            worksheet.Cell(row, 12).Value =
                item.LinenRoom;

            worksheet.Cell(row, 13).Value =
                item.Laundry;

            worksheet.Cell(row, 14).Value =
                item.Discarded;

            worksheet.Cell(row, 15).Value =
                item.Missing;

            row++;
        }

        var dataRange =
            worksheet.Range(
                5,
                5,
                Math.Max(5, row - 1),
                15);

        dataRange.Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        dataRange.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        worksheet.SheetView.FreezeRows(4);

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();

        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"RunningInventory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    // =========================================================
    // PRINT RUNNING INVENTORY
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Print(
        bool lowStockOnly = false)
    {
        var items =
            await _inventoryRepository.GetRunningInventoryAsync(
                lowStockOnly);

        ViewBag.LowStockOnly = lowStockOnly;
        ViewBag.PrintDate = DateTime.Now;

        return View("Print", items);
    }

    // =========================================================
    // CREATE INVENTORY ITEM
    // =========================================================

    [Authorize(Roles = "Admin,IT")]
    public async Task<IActionResult> Create()
    {
        var vm = new InventoryItemFormViewModel
        {
            Categories =
                await _inventoryRepository.GetCategoriesAsync()
        };

        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,IT")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        InventoryItemFormViewModel model)
    {
        model.Categories =
            await _inventoryRepository.GetCategoriesAsync();

        if (await _inventoryRepository.SkuExistsAsync(model.SKU))
        {
            ModelState.AddModelError(
                nameof(model.SKU),
                "This SKU already exists.");
        }

        if (!ModelState.IsValid)
            return View(model);

        var item = new InventoryItem
        {
            ItemName = model.ItemName,
            SKU = string.IsNullOrWhiteSpace(model.SKU)
                ? null
                : model.SKU.Trim().ToUpperInvariant(),
            CategoryId = model.CategoryId,
            Unit = model.Unit,
            AvailableStock = model.AvailableStock,
            MinStockLevel = model.MinStockLevel,
            IsActive = model.IsActive
        };

        var newId =
            await _inventoryRepository.CreateItemAsync(item);

        await _auditService.LogAsync(
            CurrentUserId,
            CurrentUsername,
            "ITEM_CREATED",
            "InventoryItems",
            newId);

        TempData["Success"] =
            "Item added successfully.";

        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // EDIT INVENTORY ITEM
    // =========================================================

    [Authorize(Roles = "Admin,IT")]
    public async Task<IActionResult> Edit(int id)
    {
        var item =
            await _inventoryRepository.GetItemByIdAsync(id);

        if (item is null)
            return NotFound();

        var vm = new InventoryItemFormViewModel
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            SKU = item.SKU,
            CategoryId = item.CategoryId,
            Unit = item.Unit,
            AvailableStock = item.AvailableStock,
            MinStockLevel = item.MinStockLevel,
            IsActive = item.IsActive,
            Categories =
                await _inventoryRepository.GetCategoriesAsync()
        };

        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,IT")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        InventoryItemFormViewModel model)
    {
        model.Categories =
            await _inventoryRepository.GetCategoriesAsync();

        if (await _inventoryRepository.SkuExistsAsync(
                model.SKU,
                model.ItemId))
        {
            ModelState.AddModelError(
                nameof(model.SKU),
                "This SKU already exists.");
        }

        if (!ModelState.IsValid)
            return View(model);

        var item = new InventoryItem
        {
            ItemId = model.ItemId,
            ItemName = model.ItemName,
            SKU = string.IsNullOrWhiteSpace(model.SKU)
                ? null
                : model.SKU.Trim().ToUpperInvariant(),
            CategoryId = model.CategoryId,
            Unit = model.Unit,
            MinStockLevel = model.MinStockLevel,
            IsActive = model.IsActive
        };

        await _inventoryRepository.UpdateItemAsync(item);

        await _auditService.LogAsync(
            CurrentUserId,
            CurrentUsername,
            "ITEM_UPDATED",
            "InventoryItems",
            item.ItemId);

        TempData["Success"] =
            "Item updated successfully.";

        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // ACTIVATE / DEACTIVATE
    // =========================================================

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(
        int id,
        bool activate)
    {
        await _inventoryRepository.SetItemActiveAsync(
            id,
            activate);

        await _auditService.LogAsync(
            CurrentUserId,
            CurrentUsername,
            activate
                ? "ITEM_ACTIVATED"
                : "ITEM_DEACTIVATED",
            "InventoryItems",
            id);

        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // ADD STOCK
    // =========================================================

    public async Task<IActionResult> AddStock(int id)
    {
        var item =
            await _inventoryRepository.GetItemByIdAsync(id);

        if (item is null)
            return NotFound();

        var vm = new AddStockViewModel
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            CurrentStock = item.AvailableStock
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStock(
        AddStockViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var newStock =
                await _inventoryRepository.AddStockAsync(
                    model.ItemId,
                    model.Quantity,
                    model.Remarks,
                    CurrentUserId);

            await _auditService.LogAsync(
                CurrentUserId,
                CurrentUsername,
                "STOCK_IN",
                "InventoryItems",
                model.ItemId,
                $"Added {model.Quantity}, new stock {newStock}");

            TempData["Success"] =
                $"Stock updated. New available stock: {newStock}.";

            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            var item =
                await _inventoryRepository.GetItemByIdAsync(
                    model.ItemId);

            model.ItemName =
                item?.ItemName;

            model.CurrentStock =
                item?.AvailableStock ?? 0;

            return View(model);
        }
    }

    // =========================================================
    // STOCK HISTORY
    // =========================================================

    public async Task<IActionResult> History(int id)
    {
        var item =
            await _inventoryRepository.GetItemByIdAsync(id);

        if (item is null)
            return NotFound();

        ViewBag.Item = item;

        var history =
            await _inventoryRepository.GetStockHistoryAsync(id);

        return View(history);
    }

    // =========================================================
    // RUNNING INVENTORY - ITEM TRANSFER
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> IssueItem()
    {
        var model = new RunningInventoryIssueViewModel
        {
            Items =
                await _inventoryRepository.GetAllItemsAsync(
                    activeOnly: true)
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueItem(
        RunningInventoryIssueViewModel model)
    {
        model.Items =
            await _inventoryRepository.GetAllItemsAsync(
                activeOnly: true);

        if (string.IsNullOrWhiteSpace(model.FromLocation))
        {
            ModelState.AddModelError(
                nameof(model.FromLocation),
                "Please select the source location.");
        }

        if (string.IsNullOrWhiteSpace(model.ToLocation))
        {
            ModelState.AddModelError(
                nameof(model.ToLocation),
                "Please select the destination location.");
        }

        if (!string.IsNullOrWhiteSpace(model.FromLocation) &&
            !string.IsNullOrWhiteSpace(model.ToLocation) &&
            model.FromLocation.Trim().Equals(
                model.ToLocation.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(model.ToLocation),
                "To location must be different from From location.");
        }

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            await _inventoryRepository.TransferRunningInventoryAsync(
                model.ItemName,
                model.FromLocation,
                model.ToLocation,
                model.Quantity,
                CurrentUserId);

            await _auditService.LogAsync(
                CurrentUserId,
                CurrentUsername,
                "RUNNING_STOCK_TRANSFER",
                "RunningInventoryTransfers",
                null,
                $"{model.ItemName}: {model.Quantity} transferred from {model.FromLocation} to {model.ToLocation}");

            TempData["Success"] =
                $"{model.Quantity} {model.ItemName} transferred from {model.FromLocation} to {model.ToLocation}.";

            return RedirectToAction(nameof(Index));
        }
        catch (InsufficientStockException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return View(model);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return View(model);
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return View(model);
        }
    }
}