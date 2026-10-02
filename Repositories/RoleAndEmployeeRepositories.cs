using Dapper;
using HotelHousekeepingApp.Data;
using HotelHousekeepingApp.Models;

namespace HotelHousekeepingApp.Repositories;

public interface IRoleRepository
{
    Task<List<Role>> GetAllAsync();
    Task<int> CreateAsync(Role role);
    Task<bool> NameExistsAsync(string roleName);
}

public class RoleRepository : IRoleRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RoleRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<List<Role>> GetAllAsync()
    {
        using var conn = _factory.CreateConnection();
        var result = await conn.QueryAsync<Role>("SELECT RoleId, RoleName, Description FROM dbo.Roles ORDER BY RoleName");
        return result.ToList();
    }

    public async Task<int> CreateAsync(Role role)
    {
        using var conn = _factory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.Roles (RoleName, Description)
            OUTPUT INSERTED.RoleId
            VALUES (@RoleName, @Description)";
        return await conn.ExecuteScalarAsync<int>(sql, role);
    }

    public async Task<bool> NameExistsAsync(string roleName)
    {
        using var conn = _factory.CreateConnection();
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.Roles WHERE RoleName = @RoleName", new { RoleName = roleName });
        return count > 0;
    }
}

public interface IEmployeeRepository
{
    Task<List<Employee>> GetAllAsync(bool activeOnly = false);
    Task<Employee?> GetByIdAsync(int employeeId);
    Task<int> CreateAsync(Employee employee);
    Task UpdateAsync(Employee employee);
    Task SetActiveAsync(int employeeId, bool isActive);
    Task<bool> CodeExistsAsync(string code, int? excludeId = null);
}

public class EmployeeRepository : IEmployeeRepository
{
    private readonly ISqlConnectionFactory _factory;
    public EmployeeRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<List<Employee>> GetAllAsync(bool activeOnly = false)
    {
        using var conn = _factory.CreateConnection();
        var sql = "SELECT * FROM dbo.Employees" + (activeOnly ? " WHERE IsActive = 1" : "") + " ORDER BY FullName";
        var result = await conn.QueryAsync<Employee>(sql);
        return result.ToList();
    }

    public async Task<Employee?> GetByIdAsync(int employeeId)
    {
        using var conn = _factory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Employee>(
            "SELECT * FROM dbo.Employees WHERE EmployeeId = @EmployeeId", new { EmployeeId = employeeId });
    }

    public async Task<int> CreateAsync(Employee employee)
    {
        using var conn = _factory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.Employees (EmployeeCode, FullName, Department, Designation, Phone, IsActive)
            OUTPUT INSERTED.EmployeeId
            VALUES (@EmployeeCode, @FullName, @Department, @Designation, @Phone, @IsActive)";
        return await conn.ExecuteScalarAsync<int>(sql, employee);
    }

    public async Task UpdateAsync(Employee employee)
    {
        using var conn = _factory.CreateConnection();
        const string sql = @"
            UPDATE dbo.Employees
            SET EmployeeCode = @EmployeeCode, FullName = @FullName, Department = @Department,
                Designation = @Designation, Phone = @Phone, IsActive = @IsActive
            WHERE EmployeeId = @EmployeeId";
        await conn.ExecuteAsync(sql, employee);
    }

    public async Task SetActiveAsync(int employeeId, bool isActive)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync("UPDATE dbo.Employees SET IsActive = @IsActive WHERE EmployeeId = @EmployeeId",
            new { IsActive = isActive, EmployeeId = employeeId });
    }

    public async Task<bool> CodeExistsAsync(string code, int? excludeId = null)
    {
        using var conn = _factory.CreateConnection();
        const string sql = @"
            SELECT COUNT(1) FROM dbo.Employees
            WHERE EmployeeCode = @Code AND (@ExcludeId IS NULL OR EmployeeId <> @ExcludeId)";
        var count = await conn.ExecuteScalarAsync<int>(sql, new { Code = code, ExcludeId = excludeId });
        return count > 0;
    }
}
