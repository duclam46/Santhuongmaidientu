using Microsoft.AspNetCore.Mvc;

namespace DOAN_BANHANG.Areas.Delivery.Controllers;

[Area("Delivery")]
[Route("Delivery")]
public class HomeController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
        => RedirectToAction("Index", "GhnSimulator", new { area = "Delivery" });
}
