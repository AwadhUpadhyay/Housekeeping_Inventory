using HotelHousekeepingApp.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelHousekeepingApp.Controllers;

[Authorize(Roles = "Admin")]
public class AuditController : Controller
{
    private readonly IAuditRepository _auditRepository;
    public AuditController(IAuditRepository auditRepository) => _auditRepository = auditRepository;

    public async Task<IActionResult> Index()
    {
        var logs = await _auditRepository.GetRecentAsync(300);
        return View(logs);
    }
}
