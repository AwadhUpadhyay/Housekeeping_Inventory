using System.Data;
using Dapper;
using HotelHousekeepingApp.Data;
using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Models.ViewModels;

namespace HotelHousekeepingApp.Repositories;

public class InsufficientStockException : Exception
{
    public InsufficientStockException(string message) : base(message) { }
}

public interface IInventoryRepository
{
    Task<List<InventoryCategory>> GetCategoriesAsync();
    Task<List<InventoryItem>> GetAllItemsAsync(bool activeOnly = false, bool lowStockOnly = false);
    Task<List<RunningInventoryRowViewModel>> GetRunningInventoryAsync(bool lowStockOnly = false);
    Task<InventoryItem?> GetItemByIdAsync(int itemId);
    Task<int> CreateItemAsync(InventoryItem item);
    Task UpdateItemAsync(InventoryItem item);
    Task SetItemActiveAsync(int itemId, bool isActive);
    Task<bool> SkuExistsAsync(string? sku, int? excludeId = null);
    Task<int> AddStockAsync(int itemId, int quantity, string? remarks, int performedByUserId);
    Task<List<StockTransaction>> GetStockHistoryAsync(int itemId);
    Task<List<StockTransaction>> GetRecentStockInAsync(int count);
    Task<List<InventoryItem>> GetLowStockItemsAsync();

    Task TransferRunningInventoryAsync(
        string itemName,
        string fromLocation,
        string toLocation,
        int quantity,
        int performedByUserId,
        string? remarks = null);
}

public class InventoryRepository : IInventoryRepository
{
    private readonly ISqlConnectionFactory _factory;

    public InventoryRepository(ISqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<InventoryCategory>> GetCategoriesAsync()
    {
        using var conn = _factory.CreateConnection();

        var result = await conn.QueryAsync<InventoryCategory>(
            "SELECT CategoryId, CategoryName FROM dbo.InventoryCategories ORDER BY CategoryName");

        return result.ToList();
    }

    private const string SelectItemsBase = @"
        SELECT i.ItemId,
               i.ItemName,
               i.SKU,
               i.CategoryId,
               COALESCE(c.CategoryName, '') AS CategoryName,
               i.Unit,
               i.AvailableStock,
               i.MinStockLevel,
               i.IsActive,
               i.CreatedDate
        FROM dbo.InventoryItems i
        LEFT JOIN dbo.InventoryCategories c
            ON c.CategoryId = i.CategoryId";

    public async Task<List<InventoryItem>> GetAllItemsAsync(
        bool activeOnly = false,
        bool lowStockOnly = false)
    {
        using var conn = _factory.CreateConnection();

        var conditions = new List<string>();

        if (activeOnly)
            conditions.Add("i.IsActive = 1");

        if (lowStockOnly)
            conditions.Add("i.AvailableStock <= i.MinStockLevel");

        var where = conditions.Count > 0
            ? " WHERE " + string.Join(" AND ", conditions)
            : "";

        var sql = SelectItemsBase + where + " ORDER BY i.ItemName";

        var result = await conn.QueryAsync<InventoryItem>(sql);

        return result.ToList();
    }

    public async Task<List<RunningInventoryRowViewModel>> GetRunningInventoryAsync(
        bool lowStockOnly = false)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT
                i.ItemId,
                i.ItemName,
                i.SKU,
                COALESCE(c.CategoryName, '') AS CategoryName,
                i.Unit,
                i.AvailableStock,

                ISNULL(issue.FirstFloor, 0)
                    + ISNULL(trans.FirstFloor, 0) AS FirstFloor,

                ISNULL(issue.SecondFloor, 0)
                    + ISNULL(trans.SecondFloor, 0) AS SecondFloor,

                ISNULL(issue.ThirdFloor, 0)
                    + ISNULL(trans.ThirdFloor, 0) AS ThirdFloor,

                ISNULL(issue.FourthFloor, 0)
                    + ISNULL(trans.FourthFloor, 0) AS FourthFloor,

                ISNULL(issue.FifthFloor, 0)
                    + ISNULL(trans.FifthFloor, 0) AS FifthFloor,

                ISNULL(issue.SixthFloor, 0)
                    + ISNULL(trans.SixthFloor, 0) AS SixthFloor,

                ISNULL(issue.LinenRoom, 0)
                    + ISNULL(trans.LinenRoom, 0) AS LinenRoom,

                ISNULL(issue.Laundry, 0)
                    + ISNULL(trans.Laundry, 0) AS Laundry,

                ISNULL(discarded.Discarded, 0) AS Discarded,
                ISNULL(discarded.Missing, 0) AS Missing

            FROM dbo.InventoryItems i

            LEFT JOIN dbo.InventoryCategories c
                ON c.CategoryId = i.CategoryId

            /*
             * Issue Stored Item location quantities.
             *
             * IssueTransactions.ItemId points to StoredItems.StoredItemID.
             * Therefore it cannot be joined directly to InventoryItems.ItemId.
             *
             * We first join IssueTransactions to StoredItems using:
             *
             *     IssueTransactions.ItemId = StoredItems.StoredItemID
             *
             * Then we match the Stored Item to Running Inventory by ItemName.
             */
            LEFT JOIN
            (
                SELECT
                    LTRIM(RTRIM(si.ItemName)) AS ItemName,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = '1st Floor'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS FirstFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = '2nd Floor'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS SecondFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = '3rd Floor'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS ThirdFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = '4th Floor'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS FourthFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = '5th Floor'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS FifthFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = '6th Floor'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS SixthFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = 'Linen Room'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS LinenRoom,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(it.IssuedToName)) = 'Laundry'
                            THEN it.Quantity
                            ELSE 0
                        END
                    ) AS Laundry

                FROM dbo.IssueTransactions it

                INNER JOIN dbo.StoredItems si
                    ON si.StoredItemID = it.ItemId

                GROUP BY
                    LTRIM(RTRIM(si.ItemName))

            ) issue
                ON issue.ItemName = LTRIM(RTRIM(i.ItemName))

            /*
             * Running Inventory floor-to-floor transfers.
             */
            LEFT JOIN
            (
                SELECT
                    LTRIM(RTRIM(rt.ItemName)) AS ItemName,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = '1st Floor'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = '1st Floor'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS FirstFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = '2nd Floor'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = '2nd Floor'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS SecondFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = '3rd Floor'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = '3rd Floor'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS ThirdFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = '4th Floor'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = '4th Floor'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS FourthFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = '5th Floor'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = '5th Floor'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS FifthFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = '6th Floor'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = '6th Floor'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS SixthFloor,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = 'Linen Room'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = 'Linen Room'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS LinenRoom,

                    SUM(
                        CASE
                            WHEN LTRIM(RTRIM(rt.ToLocation)) = 'Laundry'
                                THEN rt.Quantity
                            WHEN LTRIM(RTRIM(rt.FromLocation)) = 'Laundry'
                                THEN -rt.Quantity
                            ELSE 0
                        END
                    ) AS Laundry

                FROM dbo.RunningInventoryTransfers rt

                GROUP BY
                    LTRIM(RTRIM(rt.ItemName))

            ) trans
                ON trans.ItemName = LTRIM(RTRIM(i.ItemName))

            /*
             * Discarded and Missing totals.
             */
            LEFT JOIN
            (
                SELECT
                    LTRIM(RTRIM(si.ItemName)) AS ItemName,

                    SUM(
                        CASE
                            WHEN st.TransactionType = 'DISCARD'
                            THEN st.Quantity
                            ELSE 0
                        END
                    ) AS Discarded,

                    SUM(
                        CASE
                            WHEN st.TransactionType = 'MISSING'
                            THEN st.Quantity
                            ELSE 0
                        END
                    ) AS Missing

                FROM dbo.StockTransactions st

                INNER JOIN dbo.StoredItems si
                    ON si.StoredItemID = st.ItemId

                WHERE st.TransactionType IN ('DISCARD', 'MISSING')

                GROUP BY
                    LTRIM(RTRIM(si.ItemName))

            ) discarded
                ON discarded.ItemName = LTRIM(RTRIM(i.ItemName))

            WHERE i.IsActive = 1
              AND
              (
                    @LowStockOnly = 0
                    OR i.AvailableStock <= i.MinStockLevel
              )

            ORDER BY i.ItemName";

        var result = await conn.QueryAsync<RunningInventoryRowViewModel>(
            sql,
            new
            {
                LowStockOnly = lowStockOnly
            });

        return result.ToList();
    }

    public async Task TransferRunningInventoryAsync(
        string itemName,
        string fromLocation,
        string toLocation,
        int quantity,
        int performedByUserId,
        string? remarks = null)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            throw new ArgumentException("Item is required.");

        if (string.IsNullOrWhiteSpace(fromLocation))
            throw new ArgumentException("From location is required.");

        if (string.IsNullOrWhiteSpace(toLocation))
            throw new ArgumentException("To location is required.");

        if (fromLocation.Trim().Equals(
                toLocation.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "From and To locations must be different.");
        }

        if (quantity <= 0)
            throw new ArgumentException(
                "Quantity must be at least 1.");

        using var conn = _factory.CreateConnection();

        conn.Open();

        using var transaction = conn.BeginTransaction();

        try
        {
            const string itemCheckSql = @"
                SELECT COUNT(1)
                FROM dbo.InventoryItems
                WHERE ItemName = @ItemName
                  AND IsActive = 1";

            var itemExists = await conn.ExecuteScalarAsync<int>(
                itemCheckSql,
                new
                {
                    ItemName = itemName.Trim()
                },
                transaction);

            if (itemExists == 0)
            {
                throw new ArgumentException(
                    "Selected item was not found.");
            }

            const string balanceSql = @"
                SELECT
                    ISNULL((
                        SELECT SUM(it.Quantity)
                        FROM dbo.IssueTransactions it
                        INNER JOIN dbo.StoredItems si
                            ON si.StoredItemID = it.ItemId
                        WHERE LTRIM(RTRIM(si.ItemName))
                              = LTRIM(RTRIM(i.ItemName))
                          AND LTRIM(RTRIM(it.IssuedToName))
                              = @FromLocation
                    ), 0)
                    +
                    ISNULL((
                        SELECT SUM(rt.Quantity)
                        FROM dbo.RunningInventoryTransfers rt
                        WHERE LTRIM(RTRIM(rt.ItemName))
                              = LTRIM(RTRIM(i.ItemName))
                          AND LTRIM(RTRIM(rt.ToLocation))
                              = @FromLocation
                    ), 0)
                    -
                    ISNULL((
                        SELECT SUM(rt.Quantity)
                        FROM dbo.RunningInventoryTransfers rt
                        WHERE LTRIM(RTRIM(rt.ItemName))
                              = LTRIM(RTRIM(i.ItemName))
                          AND LTRIM(RTRIM(rt.FromLocation))
                              = @FromLocation
                    ), 0)
                FROM dbo.InventoryItems i
                WHERE i.ItemName = @ItemName
                  AND i.IsActive = 1";

            var sourceBalance = await conn.ExecuteScalarAsync<int?>(
                balanceSql,
                new
                {
                    ItemName = itemName.Trim(),
                    FromLocation = fromLocation.Trim()
                },
                transaction) ?? 0;

            if (sourceBalance < quantity)
            {
                throw new InsufficientStockException(
                    $"Only {sourceBalance} quantity of {itemName} " +
                    $"is available at {fromLocation}.");
            }

            const string insertSql = @"
                INSERT INTO dbo.RunningInventoryTransfers
                (
                    ItemName,
                    FromLocation,
                    ToLocation,
                    Quantity,
                    TransactionType,
                    PerformedByUserId,
                    TransferDate,
                    Remarks
                )
                VALUES
                (
                    @ItemName,
                    @FromLocation,
                    @ToLocation,
                    @Quantity,
                    'RUNNING_TRANSFER',
                    @PerformedByUserId,
                    SYSUTCDATETIME(),
                    @Remarks
                )";

            await conn.ExecuteAsync(
                insertSql,
                new
                {
                    ItemName = itemName.Trim(),
                    FromLocation = fromLocation.Trim(),
                    ToLocation = toLocation.Trim(),
                    Quantity = quantity,
                    PerformedByUserId = performedByUserId,
                    Remarks = remarks
                },
                transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<InventoryItem?> GetItemByIdAsync(int itemId)
    {
        using var conn = _factory.CreateConnection();

        return await conn.QueryFirstOrDefaultAsync<InventoryItem>(
            SelectItemsBase + " WHERE i.ItemId = @ItemId",
            new
            {
                ItemId = itemId
            });
    }

    public async Task<int> CreateItemAsync(InventoryItem item)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            INSERT INTO dbo.InventoryItems
            (
                ItemName,
                SKU,
                CategoryId,
                Unit,
                AvailableStock,
                MinStockLevel,
                IsActive
            )
            OUTPUT INSERTED.ItemId
            VALUES
            (
                @ItemName,
                @SKU,
                @CategoryId,
                @Unit,
                @AvailableStock,
                @MinStockLevel,
                @IsActive
            )";

        return await conn.ExecuteScalarAsync<int>(
            sql,
            item);
    }

    public async Task UpdateItemAsync(InventoryItem item)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            UPDATE dbo.InventoryItems
            SET ItemName = @ItemName,
                SKU = @SKU,
                CategoryId = @CategoryId,
                Unit = @Unit,
                MinStockLevel = @MinStockLevel,
                IsActive = @IsActive,
                ModifiedDate = SYSUTCDATETIME()
            WHERE ItemId = @ItemId";

        await conn.ExecuteAsync(
            sql,
            item);
    }

    public async Task SetItemActiveAsync(
        int itemId,
        bool isActive)
    {
        using var conn = _factory.CreateConnection();

        await conn.ExecuteAsync(
            "UPDATE dbo.InventoryItems " +
            "SET IsActive = @IsActive " +
            "WHERE ItemId = @ItemId",
            new
            {
                IsActive = isActive,
                ItemId = itemId
            });
    }

    public async Task<bool> SkuExistsAsync(
        string? sku,
        int? excludeId = null)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return false;

        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT COUNT(1)
            FROM dbo.InventoryItems
            WHERE SKU = @Sku
              AND (@ExcludeId IS NULL OR ItemId <> @ExcludeId)";

        var count = await conn.ExecuteScalarAsync<int>(
            sql,
            new
            {
                Sku = sku,
                ExcludeId = excludeId
            });

        return count > 0;
    }

    public async Task<int> AddStockAsync(
        int itemId,
        int quantity,
        string? remarks,
        int performedByUserId)
    {
        using var conn = _factory.CreateConnection();

        var parameters = new DynamicParameters();

        parameters.Add("@ItemId", itemId);
        parameters.Add("@Quantity", quantity);
        parameters.Add("@Remarks", remarks);
        parameters.Add("@PerformedByUserId", performedByUserId);
        parameters.Add("@TransactionType", "STOCK_IN");

        var newStock = await conn.QuerySingleAsync<int>(
            "dbo.sp_AddStock",
            parameters,
            commandType: CommandType.StoredProcedure);

        return newStock;
    }

    public async Task<List<StockTransaction>> GetStockHistoryAsync(
        int itemId)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT
                st.StockTransactionId,
                st.ItemId,
                i.ItemName,
                st.TransactionType,
                st.Quantity,
                st.PreviousStock,
                st.NewStock,
                st.Remarks,
                st.PerformedByUserId,
                u.FullName AS PerformedByName,
                st.TransactionDate
            FROM dbo.StockTransactions st
            INNER JOIN dbo.InventoryItems i
                ON i.ItemId = st.ItemId
            INNER JOIN dbo.Users u
                ON u.UserId = st.PerformedByUserId
            WHERE st.ItemId = @ItemId
            ORDER BY st.TransactionDate DESC";

        var result = await conn.QueryAsync<StockTransaction>(
            sql,
            new
            {
                ItemId = itemId
            });

        return result.ToList();
    }

    public async Task<List<StockTransaction>> GetRecentStockInAsync(
        int count)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT TOP (@Count)
                st.StockTransactionId,
                st.ItemId,
                i.ItemName,
                st.TransactionType,
                st.Quantity,
                st.PreviousStock,
                st.NewStock,
                st.Remarks,
                st.PerformedByUserId,
                u.FullName AS PerformedByName,
                st.TransactionDate
            FROM dbo.StockTransactions st
            INNER JOIN dbo.InventoryItems i
                ON i.ItemId = st.ItemId
            INNER JOIN dbo.Users u
                ON u.UserId = st.PerformedByUserId
            ORDER BY st.TransactionDate DESC";

        var result = await conn.QueryAsync<StockTransaction>(
            sql,
            new
            {
                Count = count
            });

        return result.ToList();
    }

    public async Task<List<InventoryItem>> GetLowStockItemsAsync()
    {
        using var conn = _factory.CreateConnection();

        var sql = SelectItemsBase + @"
            WHERE i.IsActive = 1
              AND i.AvailableStock <= i.MinStockLevel
            ORDER BY i.AvailableStock";

        var result = await conn.QueryAsync<InventoryItem>(sql);

        return result.ToList();
    }
}