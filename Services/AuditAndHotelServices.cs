using HotelHousekeepingApp.Models;
using HotelHousekeepingApp.Repositories;
using Microsoft.AspNetCore.Http;

namespace HotelHousekeepingApp.Services;

public interface IAuditService
{
    Task LogAsync(
        int? userId,
        string username,
        string action,
        string? entityName = null,
        int? entityId = null,
        string? details = null,
        string? ipAddress = null);
}

public class AuditService : IAuditService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(
        IAuditRepository auditRepository,
        IHttpContextAccessor httpContextAccessor)
    {
        _auditRepository = auditRepository;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task LogAsync(
        int? userId,
        string username,
        string action,
        string? entityName = null,
        int? entityId = null,
        string? details = null,
        string? ipAddress = null)
    {
        // Automatically get the IP address from the current request
        // if the caller did not provide one.
        var ip = ipAddress;

        if (string.IsNullOrWhiteSpace(ip))
        {
            ip = _httpContextAccessor.HttpContext?
                .Connection
                .RemoteIpAddress?
                .ToString();
        }

        var log = new AuditLog
        {
            UserId = userId,
            Username = username,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
            IPAddress = ip,
            LogDate = DateTime.UtcNow
        };

        return _auditRepository.AddAsync(log);
    }
}

public interface IHotelInfoProvider
{
    string HotelName { get; }
    string Address { get; }
}

public class HotelInfoProvider : IHotelInfoProvider
{
    public string HotelName { get; }
    public string Address { get; }

    public HotelInfoProvider(IConfiguration configuration)
    {
        HotelName = configuration["HotelInfo:HotelName"] ?? "Hotel";
        Address = configuration["HotelInfo:Address"] ?? string.Empty;
    }
}