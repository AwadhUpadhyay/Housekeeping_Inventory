using Dapper;
using HotelHousekeepingApp.Data;
using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Models.ViewModels;

namespace HotelHousekeepingApp.Repositories;

public interface IReportRepository
{
    Task<List<ReportItemOption>> GetReportItemsAsync();

    Task<List<IssueTransaction>> SearchIssuesAsync(
        ReportFilterViewModel filter);

    Task<List<StockTransaction>> SearchStockInAsync(
        ReportFilterViewModel filter);

    Task<List<StockTransaction>> SearchDiscardedAsync(
        ReportFilterViewModel filter);

    Task<List<StockTransaction>> SearchMissingAsync(
        ReportFilterViewModel filter);

    Task<List<StoredItem>> SearchStoredItemsAsync(
        ReportFilterViewModel filter);
}

public class ReportRepository : IReportRepository
{
    private readonly ISqlConnectionFactory _factory;

    public ReportRepository(ISqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<ReportItemOption>> GetReportItemsAsync()
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT
                StoredItemID AS ItemId,
                ItemName
            FROM dbo.StoredItems
            WHERE IsActive = 1
            ORDER BY ItemName";

        var result = await conn.QueryAsync<ReportItemOption>(sql);

        return result.ToList();
    }

    public async Task<List<IssueTransaction>> SearchIssuesAsync(
        ReportFilterViewModel filter)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT
                it.IssueId,
                it.ItemId,
                it.CustomItemName,

                COALESCE(
                    si.ItemName,
                    it.CustomItemName
                ) AS ItemName,

                it.Quantity,
                it.IssuedToEmployeeId,
                it.IssuedToName,
                it.RoomNumber,
                it.Department,
                it.Purpose,

                it.IssuedByUserId,
                ub.FullName AS IssuedByName,

                it.AuthorizedByUserId,
                ua.FullName AS AuthorizedByName,

                it.PreviousStock,
                it.NewStock,
                it.IssueDate

            FROM dbo.IssueTransactions it

            LEFT JOIN dbo.StoredItems si
                ON si.StoredItemID = it.ItemId

            INNER JOIN dbo.Users ub
                ON ub.UserId = it.IssuedByUserId

            LEFT JOIN dbo.Users ua
                ON ua.UserId = it.AuthorizedByUserId

            WHERE
                (@FromDate IS NULL
                    OR it.IssueDate >= @FromDate)

                AND

                (@ToDate IS NULL
                    OR it.IssueDate < DATEADD(DAY, 1, @ToDate))

                AND

                (@ItemId IS NULL
                    OR it.ItemId = @ItemId)

                AND

                (@EmployeeName IS NULL
                    OR it.IssuedToName LIKE '%' + @EmployeeName + '%')

                AND

                (@RoomNumber IS NULL
                    OR it.RoomNumber LIKE '%' + @RoomNumber + '%')

                AND

                (@AuthorizedByUserId IS NULL
                    OR it.AuthorizedByUserId = @AuthorizedByUserId)

            ORDER BY it.IssueDate DESC";

        var result = await conn.QueryAsync<IssueTransaction>(
            sql,
            new
            {
                filter.FromDate,
                filter.ToDate,
                filter.ItemId,

                EmployeeName =
                    string.IsNullOrWhiteSpace(filter.EmployeeName)
                        ? null
                        : filter.EmployeeName.Trim(),

                RoomNumber =
                    string.IsNullOrWhiteSpace(filter.RoomNumber)
                        ? null
                        : filter.RoomNumber.Trim(),

                filter.AuthorizedByUserId
            });

        return result.ToList();
    }

    public async Task<List<StockTransaction>> SearchStockInAsync(
        ReportFilterViewModel filter)
    {
        return await SearchStockTransactionsAsync(
            filter,
            "STOCK_IN");
    }

    public async Task<List<StockTransaction>> SearchDiscardedAsync(
        ReportFilterViewModel filter)
    {
        return await SearchStockTransactionsAsync(
            filter,
            "DISCARD");
    }

    public async Task<List<StockTransaction>> SearchMissingAsync(
        ReportFilterViewModel filter)
    {
        return await SearchStockTransactionsAsync(
            filter,
            "MISSING");
    }

    private async Task<List<StockTransaction>> SearchStockTransactionsAsync(
        ReportFilterViewModel filter,
        string transactionType)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
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

            WHERE
                st.TransactionType = @TransactionType

                AND

                (@FromDate IS NULL
                    OR st.TransactionDate >= @FromDate)

                AND

                (@ToDate IS NULL
                    OR st.TransactionDate < DATEADD(DAY, 1, @ToDate))

                AND

                (@ItemId IS NULL
                    OR st.ItemId = @ItemId)

            ORDER BY st.TransactionDate DESC";

        var result = await conn.QueryAsync<StockTransaction>(
            sql,
            new
            {
                TransactionType = transactionType,
                filter.FromDate,
                filter.ToDate,
                filter.ItemId
            });

        return result.ToList();
    }

    public async Task<List<StoredItem>> SearchStoredItemsAsync(
        ReportFilterViewModel filter)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
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

            WHERE
                (@ItemId IS NULL
                    OR StoredItemID = @ItemId)

            ORDER BY ItemName";

        var result = await conn.QueryAsync<StoredItem>(
            sql,
            new
            {
                filter.ItemId
            });

        return result.ToList();
    }
}


// ============================================================
// AUDIT REPOSITORY
// ============================================================

public interface IAuditRepository
{
    Task AddAsync(AuditLog log);
    Task<List<AuditLog>> GetRecentAsync(int count);
}

public class AuditRepository : IAuditRepository
{
    private readonly ISqlConnectionFactory _factory;

    public AuditRepository(ISqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task AddAsync(AuditLog log)
    {
        var excludedActions = new[]
        {
            "LOGIN_SUCCESS",
            "LOGIN_FAILED",
            "LOGOUT",
            "LOGIN",
            "LOGOUT"
        };

        if (excludedActions.Contains(
                log.Action?.Trim(),
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        using var conn = _factory.CreateConnection();

        const string sql = @"
            INSERT INTO dbo.AuditLogs
            (
                UserId,
                Username,
                Action,
                EntityName,
                EntityId,
                Details,
                IPAddress
            )
            VALUES
            (
                @UserId,
                @Username,
                @Action,
                @EntityName,
                @EntityId,
                @Details,
                @IPAddress
            )";

        await conn.ExecuteAsync(sql, log);
    }

    public async Task<List<AuditLog>> GetRecentAsync(int count)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            SELECT TOP (@Count)
                *
            FROM dbo.AuditLogs
            WHERE Action NOT IN
            (
                'LOGIN_SUCCESS',
                'LOGIN_FAILED',
                'LOGOUT',
                'LOGIN',
                'LOGOUT'
            )
            ORDER BY LogDate DESC";

        var result = await conn.QueryAsync<AuditLog>(
            sql,
            new { Count = count });

        return result.ToList();
    }
}


// ============================================================
// DASHBOARD REPOSITORY
// ============================================================

public interface IDashboardRepository
{
    Task<List<DailyStat>> GetDailyStatsAsync(int days);
}

public class DashboardRepository : IDashboardRepository
{
    private readonly ISqlConnectionFactory _factory;

    public DashboardRepository(ISqlConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<DailyStat>> GetDailyStatsAsync(int days)
    {
        using var conn = _factory.CreateConnection();

        const string sql = @"
            ;WITH DateRange AS
            (
                SELECT
                    CAST(
                        DATEADD(
                            DAY,
                            -(@Days - 1),
                            SYSUTCDATETIME()
                        ) AS DATE
                    ) AS D

                UNION ALL

                SELECT
                    DATEADD(DAY, 1, D)
                FROM DateRange
                WHERE D < CAST(SYSUTCDATETIME() AS DATE)
            )

            SELECT
                D AS Date,

                ISNULL(
                    (
                        SELECT SUM(Quantity)
                        FROM dbo.IssueTransactions it
                        WHERE CAST(it.IssueDate AS DATE) = D
                    ),
                    0
                ) AS IssuedQuantity,

                ISNULL(
                    (
                        SELECT SUM(Quantity)
                        FROM dbo.StockTransactions st
                        WHERE CAST(st.TransactionDate AS DATE) = D
                    ),
                    0
                ) AS StockInQuantity

            FROM DateRange

            OPTION (MAXRECURSION 100)";

        var result = await conn.QueryAsync<DailyStat>(
            sql,
            new { Days = days });

        return result.ToList();
    }
}