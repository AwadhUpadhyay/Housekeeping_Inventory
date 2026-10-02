using System.Security.Claims;
using ClosedXML.Excel;
using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelHousekeepingApp.Controllers;

[Authorize(Roles = "Admin,IT")]
public class EmployeesController : Controller
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IAuditService _auditService;

    public EmployeesController(IEmployeeRepository employeeRepository, IAuditService auditService)
    {
        _employeeRepository = employeeRepository;
        _auditService = auditService;
    }

    private string CurrentUsername => User.Identity?.Name ?? "unknown";
    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<IActionResult> Index()
    {
        var employees = await _employeeRepository.GetAllAsync();
        return View(employees);
    }

    public IActionResult Create() => View(new EmployeeFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EmployeeFormViewModel model)
    {
        if (await _employeeRepository.CodeExistsAsync(model.EmployeeCode))
            ModelState.AddModelError(nameof(model.EmployeeCode), "This employee code already exists.");

        if (!ModelState.IsValid) return View(model);

        var employee = new Employee
        {
            EmployeeCode = model.EmployeeCode.Trim(),
            FullName = model.FullName,
            Department = model.Department,
            Designation = model.Designation,
            Phone = model.Phone,
            IsActive = model.IsActive
        };
        var newId = await _employeeRepository.CreateAsync(employee);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername, "EMPLOYEE_CREATED", "Employees", newId);

        TempData["Success"] = "Employee added successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var e = await _employeeRepository.GetByIdAsync(id);
        if (e is null) return NotFound();

        var vm = new EmployeeFormViewModel
        {
            EmployeeId = e.EmployeeId,
            EmployeeCode = e.EmployeeCode,
            FullName = e.FullName,
            Department = e.Department,
            Designation = e.Designation,
            Phone = e.Phone,
            IsActive = e.IsActive
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EmployeeFormViewModel model)
    {
        if (await _employeeRepository.CodeExistsAsync(model.EmployeeCode, model.EmployeeId))
            ModelState.AddModelError(nameof(model.EmployeeCode), "This employee code already exists.");

        if (!ModelState.IsValid) return View(model);

        var employee = new Employee
        {
            EmployeeId = model.EmployeeId,
            EmployeeCode = model.EmployeeCode.Trim(),
            FullName = model.FullName,
            Department = model.Department,
            Designation = model.Designation,
            Phone = model.Phone,
            IsActive = model.IsActive
        };
        await _employeeRepository.UpdateAsync(employee);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername, "EMPLOYEE_UPDATED", "Employees", employee.EmployeeId);

        TempData["Success"] = "Employee updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, bool activate)
    {
        await _employeeRepository.SetActiveAsync(id, activate);
        await _auditService.LogAsync(CurrentUserId, CurrentUsername,
            activate ? "EMPLOYEE_ACTIVATED" : "EMPLOYEE_DEACTIVATED", "Employees", id);
        return RedirectToAction(nameof(Index));
    }

    // ---- Excel import ----

    [HttpGet]
    public IActionResult Import() => View(new EmployeeImportResultViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)] // 10 MB is generous for an employee spreadsheet
    public async Task<IActionResult> Import(IFormFile file)
    {
        var result = new EmployeeImportResultViewModel();

        if (file is null || file.Length == 0)
        {
            result.Errors.Add("Please choose an .xlsx file to upload.");
            return View(result);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".xlsx")
        {
            result.Errors.Add("Only .xlsx (Excel) files are supported.");
            return View(result);
        }

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        try
        {
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);

            // Expected columns (by header text, case-insensitive), in any order:
            // EmployeeCode | FullName | Department | Designation | Phone
            var headerRow = sheet.FirstRowUsed();
            if (headerRow is null)
            {
                result.Errors.Add("The sheet appears to be empty.");
                return View(result);
            }

            var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in headerRow.CellsUsed())
            {
                var header = cell.GetString().Trim();
                if (!string.IsNullOrEmpty(header))
                    columnMap[header] = cell.Address.ColumnNumber;
            }

            if (!columnMap.ContainsKey("EmployeeCode") || !columnMap.ContainsKey("FullName"))
            {
                result.Errors.Add("The sheet must have at least 'EmployeeCode' and 'FullName' columns in the header row.");
                return View(result);
            }

            var dataRows = sheet.RowsUsed().Skip(1); // skip header row
            foreach (var row in dataRows)
            {
                result.TotalRowsRead++;

                string GetCol(string name) =>
                    columnMap.TryGetValue(name, out var colNum) ? row.Cell(colNum).GetString().Trim() : string.Empty;

                var code = GetCol("EmployeeCode");
                var fullName = GetCol("FullName");

                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(fullName))
                {
                    result.Errors.Add($"Row {row.RowNumber()}: EmployeeCode and FullName are required — skipped.");
                    continue;
                }

                if (await _employeeRepository.CodeExistsAsync(code))
                {
                    result.SkippedDuplicateCount++;
                    continue;
                }

                var employee = new Employee
                {
                    EmployeeCode = code,
                    FullName = fullName,
                    Department = GetCol("Department"),
                    Designation = GetCol("Designation"),
                    Phone = GetCol("Phone"),
                    IsActive = true
                };

                await _employeeRepository.CreateAsync(employee);
                result.ImportedCount++;
            }

            await _auditService.LogAsync(CurrentUserId, CurrentUsername, "EMPLOYEES_IMPORTED", "Employees", null,
                $"Imported {result.ImportedCount} of {result.TotalRowsRead} rows, {result.SkippedDuplicateCount} duplicates skipped");
        }
        catch (Exception ex)
        {
            result.Errors.Add("Could not read the file: " + ex.Message);
        }

        if (result.ImportedCount > 0)
            TempData["Success"] = $"Imported {result.ImportedCount} employee(s) successfully.";

        return View(result);
    }
}
