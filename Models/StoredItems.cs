using System.ComponentModel.DataAnnotations;

namespace HotelHousekeepingApp.Models;

public class StoredItem
{
    public int StoredItemID { get; set; }

    public string ItemName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category is required.")]
    public string? Category { get; set; }

    public string? Unit { get; set; }

    public decimal Rate { get; set; }

    public int AvailableStock { get; set; }

    public int MinStockLevel { get; set; } = 10;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public bool IsLowStock =>
        AvailableStock <= MinStockLevel;
}