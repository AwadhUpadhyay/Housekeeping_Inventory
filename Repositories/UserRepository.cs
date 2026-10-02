using Dapper;
using HotelHousekeepingApp.Data;
using HotelHousekeepingApp.Models;

namespace HotelHousekeepingApp.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByIdAsync(int userId);
    Task<List<User>> GetAllAsync();
    Task<int> CreateAsync(User user);
    Task UpdateAsync(User user);
    Task SetActiveAsync(int userId, bool isActive);
    Task ResetPasswordAsync(int userId, string newPasswordHash, bool mustChangePassword);
    Task RecordLoginSuccessAsync(int userId);
    Task RecordLoginFailureAsync(int userId);
    Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null);
}

public class UserRepository : IUserRepository
{
    private readonly ISqlConnectionFactory _factory;
    public UserRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string SelectBase = @"
        SELECT u.UserId, u.Username, u.PasswordHash, u.FullName, u.Email, u.RoleId,
               r.RoleName, u.IsActive, u.MustChangePassword, u.FailedLoginAttempts,
               u.LastLoginDate, u.CreatedDate, u.CreatedBy
        FROM dbo.Users u
        INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId";

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var conn = _factory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            SelectBase + " WHERE u.Username = @Username", new { Username = username });
    }

    public async Task<User?> GetByIdAsync(int userId)
    {
        using var conn = _factory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            SelectBase + " WHERE u.UserId = @UserId", new { UserId = userId });
    }

    public async Task<List<User>> GetAllAsync()
    {
        using var conn = _factory.CreateConnection();
        var result = await conn.QueryAsync<User>(SelectBase + " ORDER BY u.FullName");
        return result.ToList();
    }

    public async Task<int> CreateAsync(User user)
    {
        using var conn = _factory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.Users (Username, PasswordHash, FullName, Email, RoleId, IsActive, MustChangePassword, CreatedBy)
            OUTPUT INSERTED.UserId
            VALUES (@Username, @PasswordHash, @FullName, @Email, @RoleId, @IsActive, @MustChangePassword, @CreatedBy)";
        return await conn.ExecuteScalarAsync<int>(sql, user);
    }

    public async Task UpdateAsync(User user)
    {
        using var conn = _factory.CreateConnection();
        const string sql = @"
            UPDATE dbo.Users
            SET FullName = @FullName, Email = @Email, RoleId = @RoleId,
                IsActive = @IsActive, ModifiedDate = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
            WHERE UserId = @UserId";
        await conn.ExecuteAsync(sql, new { user.FullName, user.Email, user.RoleId, user.IsActive,
            ModifiedBy = user.CreatedBy, user.UserId });
    }

    public async Task SetActiveAsync(int userId, bool isActive)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE dbo.Users SET IsActive = @IsActive, ModifiedDate = SYSUTCDATETIME() WHERE UserId = @UserId",
            new { IsActive = isActive, UserId = userId });
    }

    public async Task ResetPasswordAsync(int userId, string newPasswordHash, bool mustChangePassword)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Users
            SET PasswordHash = @PasswordHash, MustChangePassword = @MustChangePassword,
                FailedLoginAttempts = 0, ModifiedDate = SYSUTCDATETIME()
            WHERE UserId = @UserId",
            new { PasswordHash = newPasswordHash, MustChangePassword = mustChangePassword, UserId = userId });
    }

    public async Task RecordLoginSuccessAsync(int userId)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Users SET LastLoginDate = SYSUTCDATETIME(), FailedLoginAttempts = 0
            WHERE UserId = @UserId", new { UserId = userId });
    }

    public async Task RecordLoginFailureAsync(int userId)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Users SET FailedLoginAttempts = FailedLoginAttempts + 1
            WHERE UserId = @UserId", new { UserId = userId });
    }

    public async Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null)
    {
        using var conn = _factory.CreateConnection();
        const string sql = @"
            SELECT COUNT(1) FROM dbo.Users
            WHERE Username = @Username AND (@ExcludeUserId IS NULL OR UserId <> @ExcludeUserId)";
        var count = await conn.ExecuteScalarAsync<int>(sql, new { Username = username, ExcludeUserId = excludeUserId });
        return count > 0;
    }
}
