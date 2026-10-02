using System.Data;
using Microsoft.Data.SqlClient;

namespace HotelHousekeepingApp.Data;

public interface ISqlConnectionFactory
{
    IDbConnection CreateConnection();
}

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("HotelHousekeepingDb")
            ?? throw new InvalidOperationException("Connection string 'HotelHousekeepingDb' not found in appsettings.json.");
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
