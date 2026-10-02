using HotelHousekeepingApp.Models.ViewModels;
using HotelHousekeepingApp.Repositories;
using HotelHousekeepingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelHousekeepingApp.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IIssueRepository _issueRepository;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IHotelInfoProvider _hotelInfo;

    public HomeController(IInventoryRepository inventoryRepository, IIssueRepository issueRepository,
        IDashboardRepository dashboardRepository, IHotelInfoProvider hotelInfo)
    {
        _inventoryRepository = inventoryRepository;
        _issueRepository = issueRepository;
        _dashboardRepository = dashboardRepository;
        _hotelInfo = hotelInfo;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _inventoryRepository.GetAllItemsAsync(activeOnly: true);
        var lowStock = await _inventoryRepository.GetLowStockItemsAsync();
        var recentIssues = await _issueRepository.GetRecentAsync(10);
        var todayIssues = await _issueRepository.GetTodayAsync();
        var recentStockIn = await _inventoryRepository.GetRecentStockInAsync(10);
        var dailyStats = await _dashboardRepository.GetDailyStatsAsync(14);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var vm = new DashboardViewModel
        {
            HotelName = _hotelInfo.HotelName,
            TotalItems = items.Count,
            TotalAvailableStock = items.Sum(i => i.AvailableStock),
            TotalIssuedToday = todayIssues.Sum(i => i.Quantity),
            TotalIssuedThisMonth = dailyStats.Where(d => d.Date >= monthStart).Sum(d => d.IssuedQuantity),
            LowStockCount = lowStock.Count,
            LowStockItems = lowStock,
            RecentIssues = recentIssues,
            RecentStockIns = recentStockIn,
            DailyStats = dailyStats
        };

        return View(vm);
    }

    public IActionResult Error() => View();
}
