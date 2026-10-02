namespace HotelHousekeepingApp.Models.ViewModels;

public class RunningInventoryRowViewModel
{
    public int ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string? SKU { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public int AvailableStock { get; set; }

    public int FirstFloor { get; set; }

    public int SecondFloor { get; set; }

    public int ThirdFloor { get; set; }

    public int FourthFloor { get; set; }

    public int FifthFloor { get; set; }

    public int SixthFloor { get; set; }

    public int LinenRoom { get; set; }

    public int Laundry { get; set; }

    public int Discarded { get; set; }

    public int Missing { get; set; }

    public int TotalIssuedToLocations =>
        FirstFloor
        + SecondFloor
        + ThirdFloor
        + FourthFloor
        + FifthFloor
        + SixthFloor
        + LinenRoom
        + Laundry;
}