using Microsoft.AspNetCore.Mvc;

namespace DOAN_BANHANG.Controllers;

public class CatalogController : Controller
{
    [HttpGet]
    public IActionResult Category(
        string slug,
        int? childCategoryId,
        int? rating,
        decimal? minPrice,
        decimal? maxPrice,
        int? page,
        string? sort)
    {
        // This controller is kept only for backwards compatibility.
        // Main category route is handled by ProductsController via the "category" route.
        return RedirectToRoute("category", new
        {
            slug,
            childCategoryId,
            rating,
            minPrice,
            maxPrice,
            page,
            sort
        });
    }
}
