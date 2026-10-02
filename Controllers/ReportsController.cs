using ClosedXML.Excel;
using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HotelHousekeepingApp.Controllers;

[Authorize(Roles = "Admin,IT,Housekeeping")]
public class ReportsController : Controller
{
    private readonly IReportRepository _reportRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IUserRepository _userRepository;
    private readonly IHotelInfoProvider _hotelInfo;

    public ReportsController(
        IReportRepository reportRepository,
        IInventoryRepository inventoryRepository,
        IUserRepository userRepository,
        IHotelInfoProvider hotelInfo)
    {
        _reportRepository = reportRepository;
        _inventoryRepository = inventoryRepository;
        _userRepository = userRepository;
        _hotelInfo = hotelInfo;
    }


    [HttpGet]
    public async Task<IActionResult> Index(
        ReportFilterViewModel filter)
    {
        await LoadReportDataAsync(filter);

        return View(filter);
    }


    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        ReportFilterViewModel filter)
    {
        await LoadReportDataAsync(filter);

        using var workbook = new XLWorkbook();

        switch (filter.ReportType)
        {
            case "StockIn":

                CreateStockExcel(
                    workbook,
                    "Stock In Report",
                    filter.StockResults);

                break;

            case "Discard":

                CreateStockExcel(
                    workbook,
                    "Discard Report",
                    filter.StockResults);

                break;

            case "Missing":

                CreateStockExcel(
                    workbook,
                    "Missing Report",
                    filter.StockResults);

                break;

            case "RunningInventory":

                CreateRunningInventoryExcel(
                    workbook,
                    filter.RunningInventoryResults);

                break;

            case "StoredItems":

                CreateStoredItemsExcel(
                    workbook,
                    filter.StoredItemsResults);

                break;

            default:

                CreateIssueExcel(
                    workbook,
                    filter.Results);

                break;
        }


        using var stream = new MemoryStream();

        workbook.SaveAs(stream);


        var safeReportName =
            GetReportTitle(filter.ReportType)
                .Replace(" ", "");


        var fileName =
            $"{safeReportName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";


        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }


    [HttpGet]
    public async Task<IActionResult> ExportPdf(
        ReportFilterViewModel filter)
    {
        await LoadReportDataAsync(filter);

        var title =
            GetReportTitle(filter.ReportType);

        var hotelName =
            _hotelInfo.HotelName;

        var hotelAddress =
            _hotelInfo.Address;


        var document =
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(
                        PageSizes.A4.Landscape());

                    page.Margin(25);


                    page.DefaultTextStyle(
                        x => x.FontSize(8));


                    page.Header().Column(col =>
                    {
                        col.Item()
                            .Text(hotelName)
                            .FontSize(16)
                            .Bold();


                        col.Item()
                            .Text(hotelAddress)
                            .FontSize(9);


                        col.Item()
                            .Text(title)
                            .FontSize(12)
                            .SemiBold();


                        col.Item()
                            .Text(
                                $"Generated: {DateTime.Now:dd-MMM-yyyy HH:mm}")
                            .FontSize(8);


                        col.Item()
                            .PaddingTop(5)
                            .LineHorizontal(1);
                    });


                    page.Content()
                        .Element(content =>
                        {
                            switch (filter.ReportType)
                            {
                                case "StockIn":

                                    BuildStockPdf(
                                        content,
                                        filter.StockResults,
                                        "Stock In Report");

                                    break;


                                case "Discard":

                                    BuildStockPdf(
                                        content,
                                        filter.StockResults,
                                        "Discard Report");

                                    break;


                                case "Missing":

                                    BuildStockPdf(
                                        content,
                                        filter.StockResults,
                                        "Missing Report");

                                    break;


                                case "RunningInventory":

                                    BuildRunningInventoryPdf(
                                        content,
                                        filter.RunningInventoryResults);

                                    break;


                                case "StoredItems":

                                    BuildStoredItemsPdf(
                                        content,
                                        filter.StoredItemsResults);

                                    break;


                                default:

                                    BuildIssuePdf(
                                        content,
                                        filter.Results);

                                    break;
                            }
                        });


                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                            x.Span(" of ");
                            x.TotalPages();
                        });
                });
            });


        var bytes =
            document.GeneratePdf();


        var safeReportName =
            title.Replace(" ", "");


        var fileName =
            $"{safeReportName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";


        return File(
            bytes,
            "application/pdf",
            fileName);
    }


    private async Task LoadReportDataAsync(
        ReportFilterViewModel filter)
    {
        filter.Items =
            await _reportRepository
                .GetReportItemsAsync();


        filter.Users =
            await _userRepository
                .GetAllAsync();


        switch (filter.ReportType)
        {
            case "StockIn":

                filter.StockResults =
                    await _reportRepository
                        .SearchStockInAsync(filter);

                break;


            case "Discard":

                filter.StockResults =
                    await _reportRepository
                        .SearchDiscardedAsync(filter);

                break;


            case "Missing":

                filter.StockResults =
                    await _reportRepository
                        .SearchMissingAsync(filter);

                break;


            case "RunningInventory":

                filter.RunningInventoryResults =
                    await _inventoryRepository
                        .GetRunningInventoryAsync(false);


                if (filter.ItemId.HasValue)
                {
                    var selectedItem =
                        filter.Items.FirstOrDefault(
                            x => x.ItemId ==
                                 filter.ItemId.Value);


                    if (selectedItem != null)
                    {
                        filter.RunningInventoryResults =
                            filter.RunningInventoryResults
                                .Where(x =>
                                    string.Equals(
                                        x.ItemName.Trim(),
                                        selectedItem.ItemName.Trim(),
                                        StringComparison.OrdinalIgnoreCase))
                                .ToList();
                    }
                }

                break;


            case "StoredItems":

                filter.StoredItemsResults =
                    await _reportRepository
                        .SearchStoredItemsAsync(filter);

                break;


            default:

                filter.ReportType = "Issue";

                filter.Results =
                    await _reportRepository
                        .SearchIssuesAsync(filter);

                break;
        }
    }


    private static string GetReportTitle(
        string? reportType)
    {
        return reportType switch
        {
            "StockIn" =>
                "Stock In / Receiving Report",

            "Discard" =>
                "Discard Report",

            "Missing" =>
                "Missing Report",

            "RunningInventory" =>
                "Running Inventory Report",

            "StoredItems" =>
                "Stored Items Stock Report",

            _ =>
                "Issue Report"
        };
    }


    private static void CreateIssueExcel(
        XLWorkbook workbook,
        List<Models.IssueTransaction> results)
    {
        var sheet =
            workbook.Worksheets.Add(
                "Issue Report");


        AddExcelTitle(
            sheet,
            "Housekeeping Item Issue Report");


        string[] headers =
        {
            "Date",
            "Item",
            "Qty",
            "Issued To",
            "Room",
            "Department",
            "Purpose",
            "Issued By",
            "Authorized By"
        };


        AddExcelHeaders(
            sheet,
            headers,
            5);


        var row = 6;


        foreach (var r in results)
        {
            sheet.Cell(row, 1).Value =
                r.IssueDate
                    .ToString("dd-MMM-yyyy HH:mm");


            sheet.Cell(row, 2).Value =
                r.ItemName ?? "";


            sheet.Cell(row, 3).Value =
                r.Quantity;


            sheet.Cell(row, 4).Value =
                r.IssuedToName;


            sheet.Cell(row, 5).Value =
                r.RoomNumber ?? "";


            sheet.Cell(row, 6).Value =
                r.Department ?? "";


            sheet.Cell(row, 7).Value =
                r.Purpose ?? "";


            sheet.Cell(row, 8).Value =
                r.IssuedByName ?? "";


            sheet.Cell(row, 9).Value =
                r.AuthorizedByName ?? "";


            row++;
        }


        sheet.Columns()
            .AdjustToContents();
    }


    private static void CreateStockExcel(
        XLWorkbook workbook,
        string title,
        List<Models.StockTransaction> results)
    {
        var sheet =
            workbook.Worksheets.Add(title);


        AddExcelTitle(
            sheet,
            title);


        string[] headers =
        {
            "Date",
            "Item",
            "Qty",
            "Previous Stock",
            "New Stock",
            "Rate",
            "Amount",
            "Remarks",
            "Performed By"
        };


        AddExcelHeaders(
            sheet,
            headers,
            5);


        var row = 6;


        foreach (var r in results)
        {
            sheet.Cell(row, 1).Value =
                r.TransactionDate
                    .ToString("dd-MMM-yyyy HH:mm");


            sheet.Cell(row, 2).Value =
                r.ItemName ?? "";


            sheet.Cell(row, 3).Value =
                r.Quantity;


            sheet.Cell(row, 4).Value =
                r.PreviousStock;


            sheet.Cell(row, 5).Value =
                r.NewStock;


            sheet.Cell(row, 6).Value =
                r.Rate;


            sheet.Cell(row, 7).Value =
                r.Amount;


            sheet.Cell(row, 8).Value =
                r.Remarks ?? "";


            sheet.Cell(row, 9).Value =
                r.PerformedByName ?? "";


            row++;
        }


        sheet.Columns()
            .AdjustToContents();
    }


    private static void CreateRunningInventoryExcel(
        XLWorkbook workbook,
        List<RunningInventoryRowViewModel> results)
    {
        var sheet =
            workbook.Worksheets.Add(
                "Running Inventory");


        AddExcelTitle(
            sheet,
            "Running Inventory Report");


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


        AddExcelHeaders(
            sheet,
            headers,
            5);


        var row = 6;


        foreach (var r in results)
        {
            sheet.Cell(row, 1).Value =
                r.ItemName;

            sheet.Cell(row, 2).Value =
                r.SKU ?? "";

            sheet.Cell(row, 3).Value =
                r.CategoryName;

            sheet.Cell(row, 4).Value =
                r.Unit;

            sheet.Cell(row, 5).Value =
                r.AvailableStock;

            sheet.Cell(row, 6).Value =
                r.FirstFloor;

            sheet.Cell(row, 7).Value =
                r.SecondFloor;

            sheet.Cell(row, 8).Value =
                r.ThirdFloor;

            sheet.Cell(row, 9).Value =
                r.FourthFloor;

            sheet.Cell(row, 10).Value =
                r.FifthFloor;

            sheet.Cell(row, 11).Value =
                r.SixthFloor;

            sheet.Cell(row, 12).Value =
                r.LinenRoom;

            sheet.Cell(row, 13).Value =
                r.Laundry;

            sheet.Cell(row, 14).Value =
                r.Discarded;

            sheet.Cell(row, 15).Value =
                r.Missing;

            row++;
        }


        sheet.Columns()
            .AdjustToContents();
    }


    private static void CreateStoredItemsExcel(
        XLWorkbook workbook,
        List<Models.StoredItem> results)
    {
        var sheet =
            workbook.Worksheets.Add(
                "Stored Items");


        AddExcelTitle(
            sheet,
            "Stored Items Stock Report");


        string[] headers =
        {
            "Item",
            "Category",
            "Unit",
            "Rate",
            "Current Stock",
            "Minimum",
            "Status"
        };


        AddExcelHeaders(
            sheet,
            headers,
            5);


        var row = 6;


        foreach (var r in results)
        {
            sheet.Cell(row, 1).Value =
                r.ItemName;

            sheet.Cell(row, 2).Value =
                r.Category ?? "";

            sheet.Cell(row, 3).Value =
                r.Unit ?? "";

            sheet.Cell(row, 4).Value =
                r.Rate;

            sheet.Cell(row, 5).Value =
                r.AvailableStock;

            sheet.Cell(row, 6).Value =
                r.MinStockLevel;

            sheet.Cell(row, 7).Value =
                r.IsActive
                    ? "Active"
                    : "Inactive";

            row++;
        }


        sheet.Columns()
            .AdjustToContents();
    }


    private static void AddExcelTitle(
        IXLWorksheet sheet,
        string title)
    {
        sheet.Cell(1, 1).Value =
            title;


        sheet.Cell(1, 1)
            .Style.Font.Bold = true;


        sheet.Cell(1, 1)
            .Style.Font.FontSize = 14;


        sheet.Cell(2, 1).Value =
            $"Report generated: {DateTime.Now:dd-MMM-yyyy HH:mm}";
    }


    private static void AddExcelHeaders(
        IXLWorksheet sheet,
        string[] headers,
        int row)
    {
        for (var c = 0;
             c < headers.Length;
             c++)
        {
            var cell =
                sheet.Cell(row, c + 1);


            cell.Value =
                headers[c];


            cell.Style.Font.Bold =
                true;


            cell.Style.Fill.BackgroundColor =
                XLColor.FromHtml("#2c3e50");


            cell.Style.Font.FontColor =
                XLColor.White;
        }
    }


    private static void BuildIssuePdf(
        IContainer container,
        List<Models.IssueTransaction> results)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(
                columns =>
                {
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(0.6f);
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.2f);
                });


            AddPdfHeader(
                table,
                new[]
                {
                    "Date",
                    "Item",
                    "Qty",
                    "Issued To",
                    "Room",
                    "Department",
                    "Purpose",
                    "Issued By",
                    "Authorized By"
                });


            foreach (var r in results)
            {
                PdfCell(
                    table,
                    r.IssueDate
                        .ToString("dd-MMM-yy HH:mm"));


                PdfCell(
                    table,
                    r.ItemName ?? "");


                PdfCell(
                    table,
                    r.Quantity.ToString());


                PdfCell(
                    table,
                    r.IssuedToName);


                PdfCell(
                    table,
                    r.RoomNumber ?? "");


                PdfCell(
                    table,
                    r.Department ?? "");


                PdfCell(
                    table,
                    r.Purpose ?? "");


                PdfCell(
                    table,
                    r.IssuedByName ?? "");


                PdfCell(
                    table,
                    r.AuthorizedByName ?? "");
            }
        });
    }


    private static void BuildStockPdf(
        IContainer container,
        List<Models.StockTransaction> results,
        string title)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(
                columns =>
                {
                    columns.RelativeColumn(1.3f);
                    columns.RelativeColumn(1.8f);
                    columns.RelativeColumn(0.6f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(0.9f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1.7f);
                    columns.RelativeColumn(1.2f);
                });


            AddPdfHeader(
                table,
                new[]
                {
                    "Date",
                    "Item",
                    "Qty",
                    "Previous",
                    "New Stock",
                    "Rate",
                    "Amount",
                    "Remarks",
                    "Performed By"
                });


            foreach (var r in results)
            {
                PdfCell(
                    table,
                    r.TransactionDate
                        .ToString("dd-MMM-yy HH:mm"));


                PdfCell(
                    table,
                    r.ItemName ?? "");


                PdfCell(
                    table,
                    r.Quantity.ToString());


                PdfCell(
                    table,
                    r.PreviousStock.ToString());


                PdfCell(
                    table,
                    r.NewStock.ToString());


                PdfCell(
                    table,
                    r.Rate.ToString("0.00"));


                PdfCell(
                    table,
                    r.Amount.ToString("0.00"));


                PdfCell(
                    table,
                    r.Remarks ?? "");


                PdfCell(
                    table,
                    r.PerformedByName ?? "");
            }
        });
    }


    private static void BuildRunningInventoryPdf(
        IContainer container,
        List<RunningInventoryRowViewModel> results)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(
                columns =>
                {
                    for (var i = 0;
                         i < 15;
                         i++)
                    {
                        columns.RelativeColumn();
                    }
                });


            AddPdfHeader(
                table,
                new[]
                {
                    "Item",
                    "SKU",
                    "Category",
                    "Unit",
                    "Running",
                    "1st",
                    "2nd",
                    "3rd",
                    "4th",
                    "5th",
                    "6th",
                    "Linen",
                    "Laundry",
                    "Discarded",
                    "Missing"
                });


            foreach (var r in results)
            {
                PdfCell(
                    table,
                    r.ItemName);

                PdfCell(
                    table,
                    r.SKU ?? "");

                PdfCell(
                    table,
                    r.CategoryName);

                PdfCell(
                    table,
                    r.Unit);

                PdfCell(
                    table,
                    r.AvailableStock.ToString());

                PdfCell(
                    table,
                    r.FirstFloor.ToString());

                PdfCell(
                    table,
                    r.SecondFloor.ToString());

                PdfCell(
                    table,
                    r.ThirdFloor.ToString());

                PdfCell(
                    table,
                    r.FourthFloor.ToString());

                PdfCell(
                    table,
                    r.FifthFloor.ToString());

                PdfCell(
                    table,
                    r.SixthFloor.ToString());

                PdfCell(
                    table,
                    r.LinenRoom.ToString());

                PdfCell(
                    table,
                    r.Laundry.ToString());

                PdfCell(
                    table,
                    r.Discarded.ToString());

                PdfCell(
                    table,
                    r.Missing.ToString());
            }
        });
    }


    private static void BuildStoredItemsPdf(
        IContainer container,
        List<Models.StoredItem> results)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(
                columns =>
                {
                    columns.RelativeColumn(2f);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1f);
                });


            AddPdfHeader(
                table,
                new[]
                {
                    "Item",
                    "Category",
                    "Unit",
                    "Rate",
                    "Current Stock",
                    "Minimum",
                    "Status"
                });


            foreach (var r in results)
            {
                PdfCell(
                    table,
                    r.ItemName);

                PdfCell(
                    table,
                    r.Category ?? "");

                PdfCell(
                    table,
                    r.Unit ?? "");

                PdfCell(
                    table,
                    r.Rate.ToString("0.00"));

                PdfCell(
                    table,
                    r.AvailableStock.ToString());

                PdfCell(
                    table,
                    r.MinStockLevel.ToString());

                PdfCell(
                    table,
                    r.IsActive
                        ? "Active"
                        : "Inactive");
            }
        });
    }


    private static void AddPdfHeader(
        TableDescriptor table,
        string[] headers)
    {
        foreach (var h in headers)
        {
            table.Cell()
                .Background("#2c3e50")
                .Padding(3)
                .Text(h)
                .FontColor("#ffffff")
                .SemiBold();
        }
    }


    private static void PdfCell(
        TableDescriptor table,
        string value)
    {
        table.Cell()
            .BorderBottom(0.5f)
            .Padding(3)
            .Text(value ?? "");
    }
}