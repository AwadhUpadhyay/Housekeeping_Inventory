using System.ComponentModel.DataAnnotations;
using HotelHousekeepingApp.Models;

namespace HotelHousekeepingApp.Models.ViewModels;

public class RunningInventoryIssueViewModel
{
    [Required(ErrorMessage = "Please select an item.")]
    public string ItemName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select the source location.")]
    public string FromLocation { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select the destination location.")]
    public string ToLocation { get; set; } = string.Empty;

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; }

    public List<InventoryItem> Items { get; set; } = new();

    public List<string> Locations { get; set; } = new()
    {
        "1st Floor",
        "2nd Floor",
        "3rd Floor",
        "4th Floor",
        "5th Floor",
        "6th Floor",
        "Linen Room",
        "Laundry"
    };
}