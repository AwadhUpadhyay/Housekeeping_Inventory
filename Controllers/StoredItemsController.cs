using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.IO;
using ClosedXML.Excel;
using HotelHousekeepingApp.Data;
using HotelHousekeepingApp.Models;

namespace HotelHousekeepingApp.Controllers;

public class StoredItemsController : Controller
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IWebHostEnvironment _environment;

    public StoredItemsController(
        ISqlConnectionFactory connectionFactory,
        IWebHostEnvironment environment)
    {
        _connectionFactory = connectionFactory;
        _environment = environment;
    }

    // =========================================================
    // INDEX - ITEM MASTER
    // =========================================================
    public IActionResult Index()
    {
        var items = new List<StoredItem>();

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT
                StoredItemID,
                ItemName,
                Category,
                Unit,
                Rate,
                AvailableStock,
                MinStockLevel,
                IsActive,
                CreatedAt,
                ModifiedAt
            FROM dbo.StoredItems
            ORDER BY ItemName";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(MapStoredItem(reader));
        }

        return View(items);
    }

    // =========================================================
    // CREATE - GET
    // =========================================================
    [HttpGet]
    public IActionResult Create()
    {
        return View(new StoredItem
        {
            Unit = "pcs",
            MinStockLevel = 10,
            Rate = 0,
            IsActive = true
        });
    }

    // =========================================================
    // CREATE - POST
    // =========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(StoredItem item)
    {
        ValidateItem(item);

        if (!ModelState.IsValid)
            return View(item);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = @"
            INSERT INTO dbo.StoredItems
            (
                ItemName,
                Category,
                Unit,
                Rate,
                AvailableStock,
                MinStockLevel,
                IsActive,
                CreatedAt
            )
            VALUES
            (
                @ItemName,
                @Category,
                @Unit,
                @Rate,
                @AvailableStock,
                @MinStockLevel,
                @IsActive,
                GETDATE()
            )";

        AddParameter(command, "@ItemName", item.ItemName.Trim());
        AddParameter(command, "@Category", item.Category!.Trim());
        AddParameter(command, "@Unit",
            string.IsNullOrWhiteSpace(item.Unit)
                ? DBNull.Value
                : item.Unit.Trim());
        AddParameter(command, "@Rate", item.Rate);
        AddParameter(command, "@AvailableStock", item.AvailableStock);
        AddParameter(command, "@MinStockLevel", item.MinStockLevel);
        AddParameter(command, "@IsActive", item.IsActive);

        command.ExecuteNonQuery();

        TempData["Success"] = "Item created successfully.";

        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // EDIT - GET
    // =========================================================
    [HttpGet]
    public IActionResult Edit(int id)
    {
        var item = GetItem(id);

        if (item == null)
            return NotFound();

        return View(item);
    }

    // =========================================================
    // EDIT - POST
    // =========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(StoredItem item)
    {
        ValidateItem(item);

        if (!ModelState.IsValid)
            return View(item);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = @"
            UPDATE dbo.StoredItems
            SET
                ItemName = @ItemName,
                Category = @Category,
                Unit = @Unit,
                Rate = @Rate,
                MinStockLevel = @MinStockLevel,
                IsActive = @IsActive,
                ModifiedAt = GETDATE()
            WHERE StoredItemID = @StoredItemID";

        AddParameter(command, "@StoredItemID", item.StoredItemID);
        AddParameter(command, "@ItemName", item.ItemName.Trim());
        AddParameter(command, "@Category", item.Category!.Trim());
        AddParameter(command, "@Unit",
            string.IsNullOrWhiteSpace(item.Unit)
                ? DBNull.Value
                : item.Unit.Trim());
        AddParameter(command, "@Rate", item.Rate);
        AddParameter(command, "@MinStockLevel", item.MinStockLevel);
        AddParameter(command, "@IsActive", item.IsActive);

        command.ExecuteNonQuery();

        TempData["Success"] = "Item updated successfully.";

        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // NEW ENTRY - GET
    // =========================================================
    [HttpGet]
public IActionResult NewEntry(int? storedItemId)
{
    var items = GetActiveItems();

    ViewBag.Items = items;
    ViewBag.SelectedItemId = storedItemId;
    ViewBag.RunningStock = GetRunningStockMap(items);

    return View();

}

    // =========================================================
    // NEW ENTRY - POST
    //
    // RECEIVING -> STOCK IN
    // DISCARD   -> STOCK OUT + OPTIONAL ATTACHMENT
    // MISSING   -> STOCK OUT
    // =========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult NewEntry(
        int StoredItemID,
        string TransactionType,
        int Quantity,
        decimal Rate,
        string? Remarks,
        IFormFile? attachmentFile)
    {
        if (StoredItemID <= 0)
        {
            TempData["Error"] = "Please select an item.";
            return RedirectToAction(nameof(NewEntry));
        }

        if (Quantity <= 0)
        {
            TempData["Error"] = "Quantity must be greater than zero.";
            return RedirectToAction(nameof(NewEntry), new { storedItemId = StoredItemID });
        }

        if (TransactionType != "RECEIVING" &&
            TransactionType != "DISCARD" &&
            TransactionType != "MISSING")
        {
            TempData["Error"] = "Invalid transaction type.";
            return RedirectToAction(nameof(NewEntry), new { storedItemId = StoredItemID });
        }

        // Keep the existing rule: attachment is optional and only available for Discard.
        if (attachmentFile != null &&
            attachmentFile.Length > 0 &&
            TransactionType != "DISCARD")
        {
            TempData["Error"] = "Attachment can only be added for Discarded entries.";
            return RedirectToAction(nameof(NewEntry), new { storedItemId = StoredItemID });
        }

        if (TransactionType == "DISCARD" &&
            attachmentFile != null &&
            attachmentFile.Length > 0)
        {
            const long maxFileSize = 200 * 1024;

            if (attachmentFile.Length > maxFileSize)
            {
                TempData["Error"] = "Attachment size cannot be more than 200 KB.";
                return RedirectToAction(nameof(NewEntry), new { storedItemId = StoredItemID });
            }

            var extension = Path.GetExtension(attachmentFile.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };

            if (!allowedExtensions.Contains(extension))
            {
                TempData["Error"] = "Invalid attachment type. Only JPG, JPEG, PNG and PDF files are allowed.";
                return RedirectToAction(nameof(NewEntry), new { storedItemId = StoredItemID });
            }
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();

            // =================================================
            // NEW RECEIVING
            // Stored Items stock increases.
            // =================================================
            if (TransactionType == "RECEIVING")
            {
                if (Rate < 0)
                {
                    TempData["Error"] = "Rate cannot be negative.";
                    return RedirectToAction(nameof(NewEntry), new { storedItemId = StoredItemID });
                }

                using var command = connection.CreateCommand();
                command.CommandText = "dbo.sp_AddStock";
                command.CommandType = CommandType.StoredProcedure;

                AddParameter(command, "@ItemId", StoredItemID);
                AddParameter(command, "@Quantity", Quantity);
                AddParameter(command, "@Rate", Rate);
                AddParameter(command, "@Remarks",
                    string.IsNullOrWhiteSpace(Remarks) ? DBNull.Value : Remarks.Trim());
                AddParameter(command, "@PerformedByUserId", GetCurrentUserId());
                AddParameter(command, "@TransactionType", "NEW");

                decimal transactionAmount = 0;

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                        transactionAmount = Convert.ToDecimal(reader["Amount"]);
                }

                TempData["Success"] =
                    $"New receiving completed successfully. Quantity: {Quantity}, Rate: ₹{Rate:N2}, Amount: ₹{transactionAmount:N2}.";
            }
            else
            {
                // =================================================
                // DISCARD / MISSING
                // IMPORTANT: these transactions reduce RUNNING INVENTORY,
                // not StoredItems.
                // =================================================
                using var transaction = connection.BeginTransaction();

                string itemName = "";
                decimal itemRate = 0;
                int runningInventoryItemId = 0;
                int previousRunningStock = 0;
                int newRunningStock = 0;
                int stockTransactionId = 0;
                decimal transactionAmount = 0;

                // Lock the Stored Item only to obtain its master details/rate.
                using (var itemCommand = connection.CreateCommand())
                {
                    itemCommand.Transaction = transaction;
                    itemCommand.CommandText = @"
                        SELECT ItemName, Rate
                        FROM dbo.StoredItems WITH (UPDLOCK, HOLDLOCK)
                        WHERE StoredItemID = @StoredItemID
                          AND IsActive = 1";

                    AddParameter(itemCommand, "@StoredItemID", StoredItemID);

                    using var reader = itemCommand.ExecuteReader();
                    if (!reader.Read())
                        throw new Exception("The selected stored item does not exist or is inactive.");

                    itemName = reader["ItemName"]?.ToString() ?? "";
                    itemRate = Convert.ToDecimal(reader["Rate"]);
                }

                // Running Inventory is the existing InventoryItems table.
                // Match the already-existing item by name; no new table or UI is created.
                using (var runningCommand = connection.CreateCommand())
                {
                    runningCommand.Transaction = transaction;
                    runningCommand.CommandText = @"
                        SELECT TOP 1 ItemId, AvailableStock
                        FROM dbo.InventoryItems WITH (UPDLOCK, HOLDLOCK)
                        WHERE ItemName = @ItemName
                          AND IsActive = 1
                        ORDER BY ItemId";

                    AddParameter(runningCommand, "@ItemName", itemName);

                    using var reader = runningCommand.ExecuteReader();
                    if (!reader.Read())
                    {
                        throw new Exception(
                            $"'{itemName}' is not available in Running Inventory. Please ensure the item exists there before recording Discard or Missing.");
                    }

                    runningInventoryItemId = Convert.ToInt32(reader["ItemId"]);
                    previousRunningStock = Convert.ToInt32(reader["AvailableStock"]);
                }

                if (Quantity > previousRunningStock)
                {
                    throw new Exception(
                        $"Cannot {TransactionType.ToLowerInvariant()} {Quantity} of '{itemName}'. Only {previousRunningStock} is available in Running Inventory.");
                }

                newRunningStock = previousRunningStock - Quantity;
                transactionAmount = Quantity * itemRate;

                // Decrease Running Inventory only.
                using (var updateCommand = connection.CreateCommand())
                {
                    updateCommand.Transaction = transaction;
                    updateCommand.CommandText = @"
                        UPDATE dbo.InventoryItems
                        SET AvailableStock = @NewStock
                        WHERE ItemId = @ItemId";

                    AddParameter(updateCommand, "@NewStock", newRunningStock);
                    AddParameter(updateCommand, "@ItemId", runningInventoryItemId);
                    updateCommand.ExecuteNonQuery();
                }

                // Keep the existing StockTransactions/history/report functionality.
                // ItemId remains the StoredItems ID because that is what the existing
                // StoredItems history and discard reports use.
                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = @"
                        INSERT INTO dbo.StockTransactions
                        (
                            ItemId,
                            TransactionType,
                            Quantity,
                            PreviousStock,
                            NewStock,
                            Remarks,
                            PerformedByUserId,
                            TransactionDate,
                            Rate,
                            Amount
                        )
                        VALUES
                        (
                            @ItemId,
                            @TransactionType,
                            @Quantity,
                            @PreviousStock,
                            @NewStock,
                            @Remarks,
                            @PerformedByUserId,
                            GETDATE(),
                            @Rate,
                            @Amount
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    AddParameter(insertCommand, "@ItemId", StoredItemID);
                    AddParameter(insertCommand, "@TransactionType", TransactionType);
                    AddParameter(insertCommand, "@Quantity", Quantity);
                    AddParameter(insertCommand, "@PreviousStock", previousRunningStock);
                    AddParameter(insertCommand, "@NewStock", newRunningStock);
                    AddParameter(insertCommand, "@Remarks",
                        string.IsNullOrWhiteSpace(Remarks) ? DBNull.Value : Remarks.Trim());
                    AddParameter(insertCommand, "@PerformedByUserId", GetCurrentUserId());
                    AddParameter(insertCommand, "@Rate", itemRate);
                    AddParameter(insertCommand, "@Amount", transactionAmount);

                    stockTransactionId = Convert.ToInt32(insertCommand.ExecuteScalar());
                }

                // Save the existing optional attachment for Discard only.
                if (TransactionType == "DISCARD" &&
                    attachmentFile != null &&
                    attachmentFile.Length > 0)
                {
                    var uploadFolder = Path.Combine(
                        _environment.WebRootPath,
                        "uploads",
                        "stock-transactions");

                    Directory.CreateDirectory(uploadFolder);

                    var originalFileName = Path.GetFileName(attachmentFile.FileName);
                    var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
                    var storedFileName = $"{stockTransactionId}_{Guid.NewGuid():N}{extension}";
                    var physicalFilePath = Path.Combine(uploadFolder, storedFileName);

                    using (var fileStream = new FileStream(physicalFilePath, FileMode.Create))
                    {
                        attachmentFile.CopyTo(fileStream);
                    }

                    var relativePath = "/uploads/stock-transactions/" + storedFileName;

                    using var attachmentCommand = connection.CreateCommand();
                    attachmentCommand.Transaction = transaction;
                    attachmentCommand.CommandText = @"
                        UPDATE dbo.StockTransactions
                        SET
                            AttachmentFileName = @AttachmentFileName,
                            AttachmentPath = @AttachmentPath,
                            AttachmentContentType = @AttachmentContentType,
                            AttachmentSize = @AttachmentSize
                        WHERE StockTransactionId = @StockTransactionId";

                    AddParameter(attachmentCommand, "@AttachmentFileName", originalFileName);
                    AddParameter(attachmentCommand, "@AttachmentPath", relativePath);
                    AddParameter(attachmentCommand, "@AttachmentContentType",
                        string.IsNullOrWhiteSpace(attachmentFile.ContentType)
                            ? GetContentType(extension)
                            : attachmentFile.ContentType);
                    AddParameter(attachmentCommand, "@AttachmentSize", Convert.ToInt32(attachmentFile.Length));
                    AddParameter(attachmentCommand, "@StockTransactionId", stockTransactionId);

                    attachmentCommand.ExecuteNonQuery();
                }

                transaction.Commit();

                var entryName = TransactionType == "DISCARD" ? "Discarded" : "Missing";
                TempData["Success"] =
                    $"{entryName} transaction completed successfully. Quantity: {Quantity}, Running Inventory remaining: {newRunningStock}, Amount: ₹{transactionAmount:N2}.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(NewEntry), new { storedItemId = StoredItemID });
    }

    // =========================================================
    // HISTORY
    // =========================================================
    [HttpGet]
    public IActionResult History(int id, int? itemId)
    {
        var actualItemId = itemId ?? id;

        var item = GetItem(actualItemId);

        if (item == null)
            return NotFound();

        var transactions = new List<StockTransaction>();

        // Attachment information is kept separately so the existing
        // StockTransaction model does not need to be changed.
        var attachments =
            new Dictionary<int, AttachmentInfo>();

        using var connection =
            _connectionFactory.CreateConnection();

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
            SELECT
                st.StockTransactionId,
                st.ItemId,
                si.ItemName,
                st.TransactionType,
                st.Quantity,
                st.PreviousStock,
                st.NewStock,
                st.Remarks,
                st.PerformedByUserId,
                u.FullName AS PerformedByName,
                st.Rate,
                st.Amount,
                st.TransactionDate,
                st.AttachmentFileName,
                st.AttachmentPath,
                st.AttachmentContentType,
                st.AttachmentSize
            FROM dbo.StockTransactions st
            INNER JOIN dbo.StoredItems si
                ON si.StoredItemID = st.ItemId
            LEFT JOIN dbo.Users u
                ON u.UserId = st.PerformedByUserId
            WHERE st.ItemId = @ItemId
            ORDER BY st.TransactionDate DESC";

        AddParameter(command, "@ItemId", actualItemId);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var transactionId =
                Convert.ToInt32(reader["StockTransactionId"]);

            transactions.Add(new StockTransaction
            {
                StockTransactionId = transactionId,
                ItemId = Convert.ToInt32(reader["ItemId"]),
                ItemName = reader["ItemName"]?.ToString(),
                TransactionType =
                    reader["TransactionType"]?.ToString() ?? "",
                Quantity = Convert.ToInt32(reader["Quantity"]),
                PreviousStock =
                    Convert.ToInt32(reader["PreviousStock"]),
                NewStock = Convert.ToInt32(reader["NewStock"]),
                Remarks =
                    reader["Remarks"] == DBNull.Value
                        ? null
                        : reader["Remarks"].ToString(),
                PerformedByUserId =
                    Convert.ToInt32(reader["PerformedByUserId"]),
                PerformedByName =
                    reader["PerformedByName"] == DBNull.Value
                        ? null
                        : reader["PerformedByName"].ToString(),
                Rate = Convert.ToDecimal(reader["Rate"]),
                Amount = Convert.ToDecimal(reader["Amount"]),
                TransactionDate =
                    Convert.ToDateTime(reader["TransactionDate"])
            });

            if (reader["AttachmentPath"] != DBNull.Value)
            {
                attachments[transactionId] =
                    new AttachmentInfo
                    {
                        FileName =
                            reader["AttachmentFileName"] == DBNull.Value
                                ? null
                                : reader["AttachmentFileName"].ToString(),

                        Path =
                            reader["AttachmentPath"] == DBNull.Value
                                ? null
                                : reader["AttachmentPath"].ToString(),

                        ContentType =
                            reader["AttachmentContentType"] == DBNull.Value
                                ? null
                                : reader["AttachmentContentType"].ToString(),

                        Size =
                            reader["AttachmentSize"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    reader["AttachmentSize"])
                    };
            }
        }

        ViewBag.Item = item;
        ViewBag.Attachments = attachments;

        return View(transactions);
    }

    // =========================================================
    // DISCARDED ITEMS DASHBOARD
    // =========================================================
    [HttpGet]
    public IActionResult DiscardedItems(
        DateTime? fromDate,
        DateTime? toDate,
        int? itemId)
    {
        var records = GetDiscardedItems(
            fromDate,
            toDate,
            itemId);

        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.ItemId = itemId;

        ViewBag.Items = GetActiveItems();

        return View(records);
    }

    // =========================================================
    // DISCARDED REPORT
    // =========================================================
    [HttpGet]
    public IActionResult DiscardReport(
        DateTime? fromDate,
        DateTime? toDate,
        int? itemId)
    {
        var records = GetDiscardedItems(
            fromDate,
            toDate,
            itemId);

        ViewBag.FromDate =
            fromDate?.ToString("dd-MMM-yyyy")
            ?? "All Dates";

        ViewBag.ToDate =
            toDate?.ToString("dd-MMM-yyyy")
            ?? "All Dates";

        return View(records);
    }

    // =========================================================
    // STORED ITEMS EXCEL REPORT
    // Downloads only:
    // Item Name, Category, Unit, Rate, Available Stock
    // =========================================================
    [HttpGet]
    public IActionResult ExportStoredItemsExcel()
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ItemName, Category, Unit, Rate, AvailableStock
            FROM dbo.StoredItems
            WHERE IsActive = 1
            ORDER BY ItemName";

        using var reader = command.ExecuteReader();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Stored Items");

        worksheet.Cell(1, 1).Value = "Item Name";
        worksheet.Cell(1, 2).Value = "Category";
        worksheet.Cell(1, 3).Value = "Unit";
        worksheet.Cell(1, 4).Value = "Rate";
        worksheet.Cell(1, 5).Value = "Available Stock";

        var row = 2;
        while (reader.Read())
        {
            worksheet.Cell(row, 1).Value = reader["ItemName"]?.ToString() ?? "";
            worksheet.Cell(row, 2).Value = reader["Category"] == DBNull.Value ? "" : reader["Category"]?.ToString() ?? "";
            worksheet.Cell(row, 3).Value = reader["Unit"] == DBNull.Value ? "" : reader["Unit"]?.ToString() ?? "";
            worksheet.Cell(row, 4).Value = Convert.ToDecimal(reader["Rate"]);
            worksheet.Cell(row, 5).Value = Convert.ToInt32(reader["AvailableStock"]);
            row++;
        }

        var header = worksheet.Range(1, 1, 1, 5);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightBlue;
        worksheet.Column(4).Style.NumberFormat.Format = "₹#,##0.00";
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"StoredItems_{DateTime.Now:yyyy-MM-dd_HHmmss}.xlsx";
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }


    private Dictionary<int, int> GetRunningStockMap(List<StoredItem> items)
    {
        var result = new Dictionary<int, int>();
        if (items.Count == 0)
            return result;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT si.StoredItemID, ISNULL(ii.AvailableStock, 0) AS RunningStock
            FROM dbo.StoredItems si
            LEFT JOIN dbo.InventoryItems ii
                ON ii.ItemName = si.ItemName
               AND ii.IsActive = 1
            WHERE si.IsActive = 1";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[Convert.ToInt32(reader["StoredItemID"])] =
                Convert.ToInt32(reader["RunningStock"]);
        }

        return result;
    }

    // =========================================================
    // GET ITEM
    // =========================================================
    private StoredItem? GetItem(int id)
    {
        using var connection =
            _connectionFactory.CreateConnection();

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
            SELECT
                StoredItemID,
                ItemName,
                Category,
                Unit,
                Rate,
                AvailableStock,
                MinStockLevel,
                IsActive,
                CreatedAt,
                ModifiedAt
            FROM dbo.StoredItems
            WHERE StoredItemID = @StoredItemID";

        AddParameter(command, "@StoredItemID", id);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return null;

        return MapStoredItem(reader);
    }

    // =========================================================
    // ACTIVE ITEMS
    // =========================================================
    private List<StoredItem> GetActiveItems()
    {
        var items = new List<StoredItem>();

        using var connection =
            _connectionFactory.CreateConnection();

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
            SELECT
                StoredItemID,
                ItemName,
                Category,
                Unit,
                Rate,
                AvailableStock,
                MinStockLevel,
                IsActive,
                CreatedAt,
                ModifiedAt
            FROM dbo.StoredItems
            WHERE IsActive = 1
            ORDER BY ItemName";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(MapStoredItem(reader));
        }

        return items;
    }

    // =========================================================
    // DISCARDED ITEMS DATA
    // =========================================================
    private List<DiscardedItemViewModel> GetDiscardedItems(
        DateTime? fromDate,
        DateTime? toDate,
        int? itemId)
    {
        var records = new List<DiscardedItemViewModel>();

        using var connection =
            _connectionFactory.CreateConnection();

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
            SELECT
                st.StockTransactionId,
                st.ItemId,
                si.ItemName,
                si.Category,
                si.Unit,
                st.Quantity,
                st.PreviousStock,
                st.NewStock,
                st.Rate,
                st.Amount,
                st.Remarks,
                st.TransactionDate,
                st.PerformedByUserId,
                u.FullName AS PerformedByName,
                st.AttachmentFileName,
                st.AttachmentPath,
                st.AttachmentContentType,
                st.AttachmentSize
            FROM dbo.StockTransactions st
            INNER JOIN dbo.StoredItems si
                ON si.StoredItemID = st.ItemId
            LEFT JOIN dbo.Users u
                ON u.UserId = st.PerformedByUserId
            WHERE st.TransactionType = 'DISCARD'
              AND (@ItemId IS NULL OR st.ItemId = @ItemId)
              AND (@FromDate IS NULL OR st.TransactionDate >= @FromDate)
              AND (@ToDate IS NULL OR st.TransactionDate < DATEADD(DAY, 1, @ToDate))
            ORDER BY st.TransactionDate DESC,
                     st.StockTransactionId DESC";

        AddParameter(command, "@ItemId",
            itemId.HasValue
                ? itemId.Value
                : DBNull.Value);

        AddParameter(command, "@FromDate",
            fromDate.HasValue
                ? fromDate.Value.Date
                : DBNull.Value);

        AddParameter(command, "@ToDate",
            toDate.HasValue
                ? toDate.Value.Date
                : DBNull.Value);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            records.Add(new DiscardedItemViewModel
            {
                StockTransactionId =
                    Convert.ToInt32(
                        reader["StockTransactionId"]),

                ItemId =
                    Convert.ToInt32(
                        reader["ItemId"]),

                ItemName =
                    reader["ItemName"]?.ToString()
                    ?? "",

                Category =
                    reader["Category"] == DBNull.Value
                        ? null
                        : reader["Category"]?.ToString(),

                Unit =
                    reader["Unit"] == DBNull.Value
                        ? null
                        : reader["Unit"]?.ToString(),

                Quantity =
                    Convert.ToInt32(
                        reader["Quantity"]),

                PreviousStock =
                    Convert.ToInt32(
                        reader["PreviousStock"]),

                NewStock =
                    Convert.ToInt32(
                        reader["NewStock"]),

                Rate =
                    Convert.ToDecimal(
                        reader["Rate"]),

                Amount =
                    Convert.ToDecimal(
                        reader["Amount"]),

                Remarks =
                    reader["Remarks"] == DBNull.Value
                        ? null
                        : reader["Remarks"]?.ToString(),

                TransactionDate =
                    Convert.ToDateTime(
                        reader["TransactionDate"]),

                PerformedByUserId =
                    Convert.ToInt32(
                        reader["PerformedByUserId"]),

                PerformedByName =
                    reader["PerformedByName"] == DBNull.Value
                        ? null
                        : reader["PerformedByName"]?.ToString(),

                AttachmentFileName =
                    reader["AttachmentFileName"] == DBNull.Value
                        ? null
                        : reader["AttachmentFileName"]?.ToString(),

                AttachmentPath =
                    reader["AttachmentPath"] == DBNull.Value
                        ? null
                        : reader["AttachmentPath"]?.ToString(),

                AttachmentContentType =
                    reader["AttachmentContentType"] == DBNull.Value
                        ? null
                        : reader["AttachmentContentType"]?.ToString(),

                AttachmentSize =
                    reader["AttachmentSize"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            reader["AttachmentSize"])
            });
        }

        return records;
    }

    // =========================================================
    // VALIDATION
    // =========================================================
    private void ValidateItem(StoredItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ItemName))
        {
            ModelState.AddModelError(
                "ItemName",
                "Item name is required.");
        }

        if (string.IsNullOrWhiteSpace(item.Category))
        {
            ModelState.AddModelError(
                "Category",
                "Category is required.");
        }

        if (item.Rate <= 0)
        {
            ModelState.AddModelError(
                "Rate",
                "Rate must be greater than zero.");
        }

        if (item.AvailableStock < 0)
        {
            ModelState.AddModelError(
                "AvailableStock",
                "Stock cannot be negative.");
        }

        if (item.MinStockLevel < 0)
        {
            ModelState.AddModelError(
                "MinStockLevel",
                "Minimum stock level cannot be negative.");
        }
    }

    // =========================================================
    // CURRENT USER
    // =========================================================
    private int GetCurrentUserId()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var claim =
                User.Claims.FirstOrDefault(
                    c =>
                        c.Type == "UserId" ||
                        c.Type == "userid" ||
                        c.Type ==
                            System.Security.Claims
                                .ClaimTypes
                                .NameIdentifier);

            if (claim != null &&
                int.TryParse(
                    claim.Value,
                    out int userId))
            {
                return userId;
            }
        }

        return 1;
    }

    // =========================================================
    // CONTENT TYPE
    // =========================================================
    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    // =========================================================
    // STORED ITEM MAPPER
    // =========================================================
    private static StoredItem MapStoredItem(IDataRecord reader)
    {
        return new StoredItem
        {
            StoredItemID =
                Convert.ToInt32(reader["StoredItemID"]),

            ItemName =
                reader["ItemName"]?.ToString() ?? "",

            Category =
                reader["Category"] == DBNull.Value
                    ? null
                    : reader["Category"]?.ToString(),

            Unit =
                reader["Unit"] == DBNull.Value
                    ? null
                    : reader["Unit"]?.ToString(),

            Rate =
                Convert.ToDecimal(reader["Rate"]),

            AvailableStock =
                Convert.ToInt32(reader["AvailableStock"]),

            MinStockLevel =
                Convert.ToInt32(reader["MinStockLevel"]),

            IsActive =
                Convert.ToBoolean(reader["IsActive"]),

            CreatedAt =
                Convert.ToDateTime(reader["CreatedAt"]),

            ModifiedAt =
                reader["ModifiedAt"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(
                        reader["ModifiedAt"])
        };
    }

    // =========================================================
    // SQL PARAMETER HELPER
    // =========================================================
    private static void AddParameter(
        IDbCommand command,
        string name,
        object value)
    {
        var parameter =
            command.CreateParameter();

        parameter.ParameterName = name;
        parameter.Value =
            value ?? DBNull.Value;

        command.Parameters.Add(parameter);
    }

    // =========================================================
    // VIEW MODELS
    // =========================================================
    public class AttachmentInfo
    {
        public string? FileName { get; set; }
        public string? Path { get; set; }
        public string? ContentType { get; set; }
        public int? Size { get; set; }
    }

    public class DiscardedItemViewModel
    {
        public int StockTransactionId { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public string? Category { get; set; }
        public string? Unit { get; set; }
        public int Quantity { get; set; }
        public int PreviousStock { get; set; }
        public int NewStock { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public string? Remarks { get; set; }
        public DateTime TransactionDate { get; set; }
        public int PerformedByUserId { get; set; }
        public string? PerformedByName { get; set; }
        public string? AttachmentFileName { get; set; }
        public string? AttachmentPath { get; set; }
        public string? AttachmentContentType { get; set; }
        public int? AttachmentSize { get; set; }

        public bool HasAttachment =>
            !string.IsNullOrWhiteSpace(AttachmentPath);
    }
}
