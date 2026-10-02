using System.Data;
using Dapper;
using HotelHousekeepingApp.Data;
using HotelHousekeepingApp.Models;

namespace HotelHousekeepingApp.Repositories;

public interface IIssueRepository
{
    Task<int?> IssueItemAsync(IssueTransaction issue);
    Task<List<InventoryItem>> GetIssueableItemsAsync();
    Task<List<IssueTransaction>> GetRecentAsync(int count);
    Task<List<IssueTransaction>> GetTodayAsync();
}

public class IssueRepository : IIssueRepository
{
    private readonly ISqlConnectionFactory _factory;

    public IssueRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string SelectBase = @"
        SELECT it.IssueId, it.ItemId, it.CustomItemName,
               COALESCE(i.ItemName, it.CustomItemName) AS ItemName,
               it.Quantity, it.IssuedToEmployeeId, it.IssuedToName,
               it.RoomNumber, it.Department, it.Purpose, it.IssuedByUserId, ub.FullName AS IssuedByName,
               it.AuthorizedByUserId, ua.FullName AS AuthorizedByName, it.PreviousStock, it.NewStock, it.IssueDate
        FROM dbo.IssueTransactions it
        LEFT JOIN dbo.InventoryItems i ON i.ItemId = it.ItemId
        INNER JOIN dbo.Users ub ON ub.UserId = it.IssuedByUserId
        LEFT JOIN dbo.Users ua ON ua.UserId = it.AuthorizedByUserId";

    public async Task<List<InventoryItem>> GetIssueableItemsAsync()
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT
                StoredItemID AS ItemId,
                ItemName,
                Category,
                Unit,
                AvailableStock,
                MinStockLevel,
                IsActive
            FROM dbo.StoredItems
            WHERE IsActive = 1
            ORDER BY ItemName";

        var result = await conn.QueryAsync<InventoryItem>(sql);
        return result.ToList();
    }

    public async Task<int?> IssueItemAsync(IssueTransaction issue)
    {
        if (!issue.ItemId.HasValue)
            return null;

        using var conn = _factory.CreateConnection();
        conn.Open();

        using var tx = conn.BeginTransaction();

        var stored = await conn.QuerySingleOrDefaultAsync<StoredIssueRow>(@"
            SELECT
                StoredItemID,
                ItemName,
                Unit,
                Category,
                AvailableStock,
                MinStockLevel
            FROM dbo.StoredItems WITH (UPDLOCK, HOLDLOCK)
            WHERE StoredItemID = @ItemId
              AND IsActive = 1",
            new { ItemId = issue.ItemId.Value }, tx);

        if (stored is null)
            throw new InvalidOperationException(
                "The selected item does not exist in Stored Items.");

        if (issue.Quantity > stored.AvailableStock)
            throw new InsufficientStockException(
                $"Cannot issue {issue.Quantity} of '{stored.ItemName}'. Only {stored.AvailableStock} is available in Stored Items.");

        // Find the corresponding Running Inventory item.
        var running = await conn.QuerySingleOrDefaultAsync<RunningIssueRow>(@"
            SELECT TOP 1
                ItemId,
                AvailableStock
            FROM dbo.InventoryItems WITH (UPDLOCK, HOLDLOCK)
            WHERE ItemName = @ItemName
              AND IsActive = 1
            ORDER BY ItemId",
            new
            {
                stored.ItemName
            }, tx);

        // If it does not exist, create it automatically.
        if (running is null)
        {
            var newRunningItemId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.InventoryItems
                (
                    ItemName,
                    Unit,
                    AvailableStock,
                    MinStockLevel,
                    IsActive
                )
                VALUES
                (
                    @ItemName,
                    @Unit,
                    0,
                    @MinStockLevel,
                    1
                );

                SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    ItemName = stored.ItemName,
                    Unit = stored.Unit,
                    MinStockLevel = stored.MinStockLevel
                }, tx);

            running = new RunningIssueRow
            {
                ItemId = newRunningItemId,
                AvailableStock = 0
            };
        }

        var newStoredStock = stored.AvailableStock - issue.Quantity;
        var newRunningStock = running.AvailableStock + issue.Quantity;

        // Deduct from Stored Items.
        await conn.ExecuteAsync(@"
            UPDATE dbo.StoredItems
            SET AvailableStock = @NewStock,
                ModifiedAt = GETDATE()
            WHERE StoredItemID = @StoredItemID",
            new
            {
                NewStock = newStoredStock,
                StoredItemID = stored.StoredItemID
            }, tx);

        // Add to Running Inventory.
        await conn.ExecuteAsync(@"
            UPDATE dbo.InventoryItems
            SET AvailableStock = @NewStock
            WHERE ItemId = @ItemId",
            new
            {
                NewStock = newRunningStock,
                ItemId = running.ItemId
            }, tx);

        /*
         * IMPORTANT:
         * IssueTransactions.ItemId has a foreign key to StoredItems.StoredItemID.
         * Therefore we MUST keep the original StoredItemID here.
         */
        await conn.ExecuteAsync(@"
            INSERT INTO dbo.IssueTransactions
            (
                ItemId,
                CustomItemName,
                Quantity,
                IssuedToEmployeeId,
                IssuedToName,
                RoomNumber,
                Department,
                Purpose,
                IssuedByUserId,
                AuthorizedByUserId,
                PreviousStock,
                NewStock,
                IssueDate
            )
            VALUES
            (
                @ItemId,
                @CustomItemName,
                @Quantity,
                @IssuedToEmployeeId,
                @IssuedToName,
                @RoomNumber,
                @Department,
                @Purpose,
                @IssuedByUserId,
                @AuthorizedByUserId,
                @PreviousStock,
                @NewStock,
                GETDATE()
            )",
            new
            {
                // MUST be StoredItemID because of FK_IssueTransactions_StoredItems
                ItemId = stored.StoredItemID,

                issue.CustomItemName,
                issue.Quantity,
                issue.IssuedToEmployeeId,
                issue.IssuedToName,
                issue.RoomNumber,
                issue.Department,
                issue.Purpose,
                issue.IssuedByUserId,
                issue.AuthorizedByUserId,

                PreviousStock = running.AvailableStock,
                NewStock = newRunningStock
            }, tx);

        tx.Commit();

        return newStoredStock;
    }

    public async Task<List<IssueTransaction>> GetRecentAsync(int count)
    {
        using var conn = _factory.CreateConnection();

        var sql = SelectBase +
                  " ORDER BY it.IssueDate DESC OFFSET 0 ROWS FETCH NEXT @Count ROWS ONLY";

        var result = await conn.QueryAsync<IssueTransaction>(
            sql,
            new { Count = count });

        return result.ToList();
    }

    public async Task<List<IssueTransaction>> GetTodayAsync()
    {
        using var conn = _factory.CreateConnection();

        var sql = SelectBase +
                  " WHERE CAST(it.IssueDate AS DATE) = CAST(SYSUTCDATETIME() AS DATE)";

        var result = await conn.QueryAsync<IssueTransaction>(sql);

        return result.ToList();
    }

    private sealed class StoredIssueRow
    {
        public int StoredItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string Unit { get; set; } = "";
        public string? Category { get; set; }
        public int AvailableStock { get; set; }
        public int MinStockLevel { get; set; }
    }

    private sealed class RunningIssueRow
    {
        public int ItemId { get; set; }
        public int AvailableStock { get; set; }
    }
}