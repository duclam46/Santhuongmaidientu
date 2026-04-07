using Microsoft.AspNetCore.Mvc;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
