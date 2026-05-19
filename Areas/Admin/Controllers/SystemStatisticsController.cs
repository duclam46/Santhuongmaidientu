using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Microsoft.AspNetCore.Authorization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize]
public class SystemStatisticsController : Controller
{
    private readonly StatisticsService _statisticsService;
    private readonly Models.DataContext _context;

    public SystemStatisticsController(StatisticsService statisticsService, Models.DataContext context)
    {
        _statisticsService = statisticsService;
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Shop")) return await Seller(null);
        return await Admin();
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Admin()
    {
        var adminStats = await _statisticsService.GetAdminDashboardStatsAsync();
        return View("~/Areas/Admin/Views/Statistics/Admin.cshtml", adminStats);
    }

    [HttpGet]
    [Authorize(Roles = "Shop,Admin")] // Admin can still see for testing/support
    public async Task<IActionResult> Seller(int? shopId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(userIdStr, out int currentUserId);

        // If Shop role, they can ONLY see their own stats
        if (role == "Shop")
        {
            shopId = currentUserId;
        }
        else if (role == "Admin" && (!shopId.HasValue || shopId <= 0))
        {
            // Admin testing fallback
            var firstShop = await _context.Accounts.FirstOrDefaultAsync(a => a.Role == "Shop");
            shopId = firstShop?.Id ?? 0;
        }

        if (shopId <= 0)
        {
            return Content("Không tìm thấy shop nào để hiển thị thống kê.");
        }

        var sellerStats = await _statisticsService.GetSellerDashboardStatsAsync(shopId.Value);
        return View("~/Areas/Admin/Views/Statistics/Seller.cshtml", sellerStats);
    }

    [HttpGet]
    [Authorize(Roles = "Shop,Admin")]
    public IActionResult ProfitCalculator()
    {
        return View("~/Areas/Admin/Views/Statistics/ProfitCalculator.cshtml");
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> FinancialReport()
    {
        var report = await _statisticsService.GetFinancialReportAsync();
        return View("~/Areas/Admin/Views/Statistics/FinancialReport.cshtml", report);
    }
}
