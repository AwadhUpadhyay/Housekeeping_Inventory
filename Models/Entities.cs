namespace HotelHousekeepingApp.Models;

public class Role
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public string? RoleName { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
}

public class Employee
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class InventoryCategory
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
}

public class InventoryItem
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Unit { get; set; } = "pcs";
    public int AvailableStock { get; set; }
    public int MinStockLevel { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsLowStock => AvailableStock <= MinStockLevel;
}

public class StockTransaction
{
    public int StockTransactionId { get; set; }

    // Stores StoredItems.StoredItemID
    public int ItemId { get; set; }

    public string? ItemName { get; set; }

    public string TransactionType { get; set; } = "STOCK_IN";

    public int Quantity { get; set; }

    public int PreviousStock { get; set; }

    public int NewStock { get; set; }

    public string? Remarks { get; set; }

    public int PerformedByUserId { get; set; }

    public string? PerformedByName { get; set; }

    public decimal Rate { get; set; }

    public decimal Amount { get; set; }

    public DateTime TransactionDate { get; set; }
 
    public string? AttachmentFileName { get; set; }

    public string? AttachmentPath { get; set; }

    public string? AttachmentContentType { get; set; }

    public int? AttachmentSize { get; set; }

}

public class IssueTransaction
{
    public int IssueId { get; set; }
    public int? ItemId { get; set; }
    public string? CustomItemName { get; set; }
    public string? ItemName { get; set; }
    public int Quantity { get; set; }
    public int? IssuedToEmployeeId { get; set; }
    public string IssuedToName { get; set; } = string.Empty;
    public string? RoomNumber { get; set; }
    public string? Department { get; set; }
    public string? Purpose { get; set; }
    public int IssuedByUserId { get; set; }
    public string? IssuedByName { get; set; }
    public int? AuthorizedByUserId { get; set; }
    public string? AuthorizedByName { get; set; }
    public int? PreviousStock { get; set; }
    public int? NewStock { get; set; }
    public DateTime IssueDate { get; set; }
    public bool IsCustomItem => ItemId is null;
}

public class AuditLog
{
    public int AuditLogId { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IPAddress { get; set; }
    public DateTime LogDate { get; set; }
}
