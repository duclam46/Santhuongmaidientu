using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Components;

public class CategoryMenuViewComponent : ViewComponent
{
    private readonly DataContext _context;

    public CategoryMenuViewComponent(DataContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync(CancellationToken cancellationToken)
    {
        // Load all categories once.
        var all = await _context.Categories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        // 3 levels: root -> dropdown -> sub_category
        var byParent = all.ToLookup(x => x.ParentId);
        var roots = byParent[null]
            .OrderBy(x => x.Name)
            .ToList();

        var menu = roots.Select(r => BuildNode(r, byParent, depth: 3)).ToList();
        return View(menu);
    }

    private static Category BuildNode(
        Category src,
        ILookup<int?, Category> byParent,
        int depth)
    {
        var node = CloneNode(src);

        if (depth <= 1)
        {
            return node;
        }

        var children = byParent[src.Id];
        if (children.Any())
        {
            node.Children = children
            .OrderBy(x => x.Name)
                .Select(c => BuildNode(c, byParent, depth - 1))
                .ToList();
        }

        return node;
    }

    private static Category CloneNode(Category src)
    {
        return new Category
        {
            Id = src.Id,
            ParentId = src.ParentId,
            Name = src.Name,
            Slug = src.Slug,
            Children = new List<Category>()
        };
    }
}
