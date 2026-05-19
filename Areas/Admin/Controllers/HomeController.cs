using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
// Triggering restart for cleanup...

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Shop")]
public class HomeController : Controller
{
    private readonly StatisticsService _statisticsService;

    public HomeController(StatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    public async Task<IActionResult> Index()
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase) && int.TryParse(userIdStr, out int shopId))
        {
            var sellerStats = await _statisticsService.GetSellerDashboardStatsAsync(shopId);
            return View("SellerDashboard", sellerStats);
        }
        
        var adminStats = await _statisticsService.GetAdminDashboardStatsAsync();
        return View("AdminDashboard", adminStats);
    }
}
