using System.ComponentModel.DataAnnotations;
using HotelHousekeepingApp.Models;

namespace HotelHousekeepingApp.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}

public class UserFormViewModel
{
    public int UserId { get; set; }

    [Required, StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required, StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    public List<Role> Roles { get; set; } = new();
}

public class RoleFormViewModel
{
    public int RoleId { get; set; }

    [Required, StringLength(50)]
    public string RoleName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Description { get; set; }
}

public class EmployeeFormViewModel
{
    public int EmployeeId { get; set; }

    [Required, StringLength(30)]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EmployeeImportResultViewModel
{
    public int TotalRowsRead { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicateCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class InventoryItemFormViewModel
{
    public int ItemId { get; set; }

    [Required, StringLength(150)]
    public string ItemName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? SKU { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required, StringLength(30)]
    public string Unit { get; set; } = "pcs";

    [Range(0, int.MaxValue)]
    public int AvailableStock { get; set; }

    [Range(0, int.MaxValue)]
    public int MinStockLevel { get; set; } = 10;

    public bool IsActive { get; set; } = true;

    public List<InventoryCategory> Categories { get; set; } = new();
}

public class AddStockViewModel
{
    public int ItemId { get; set; }
    public string? ItemName { get; set; }
    public int CurrentStock { get; set; }

    [Required, Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; }

    public string? Remarks { get; set; }
}

public class IssueItemViewModel
{
    public int? ItemId { get; set; }

    [StringLength(150)]
    public string? CustomItemName { get; set; }

    [Required, Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; }

    public int? IssuedToEmployeeId { get; set; }

    public string? IssuedToName { get; set; }

    public string? RoomNumber { get; set; }
    public string? Department { get; set; }
    public string? Purpose { get; set; }

    public int AuthorizedByUserId { get; set; }
    public string? AuthorizedByName { get; set; }

    public List<InventoryItem> Items { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
}

public class DashboardViewModel
{
    public string HotelName { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int TotalAvailableStock { get; set; }
    public int TotalIssuedToday { get; set; }
    public int TotalIssuedThisMonth { get; set; }
    public int LowStockCount { get; set; }
    public List<InventoryItem> LowStockItems { get; set; } = new();
    public List<IssueTransaction> RecentIssues { get; set; } = new();
    public List<StockTransaction> RecentStockIns { get; set; } = new();
    public List<DailyStat> DailyStats { get; set; } = new();
}

public class DailyStat
{
    public DateTime Date { get; set; }
    public int IssuedQuantity { get; set; }
    public int StockInQuantity { get; set; }
}

/* ============================================================
   REPORT VIEW MODELS
   ============================================================ */

public class ReportItemOption
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
}

public class ReportFilterViewModel
{
    public string ReportType { get; set; } = "Issue";

    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    // StoredItemID is used for transaction reports.
    public int? ItemId { get; set; }

    public string? EmployeeName { get; set; }
    public string? RoomNumber { get; set; }
    public int? AuthorizedByUserId { get; set; }

    public List<ReportItemOption> Items { get; set; } = new();
    public List<User> Users { get; set; } = new();

    public List<IssueTransaction> Results { get; set; } = new();

    public List<StockTransaction> StockResults { get; set; } = new();

    public List<RunningInventoryRowViewModel> RunningInventoryResults { get; set; } = new();

    public List<StoredItem> StoredItemsResults { get; set; } = new();
}