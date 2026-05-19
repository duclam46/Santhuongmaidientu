using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

public class BlogController : Controller
{
    private readonly DataContext _db;

    public BlogController(DataContext db)
    {
        _db = db;
    }

    private int? GetCurrentAccountId()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return null;
        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var id) || id <= 0) return null;
        return id;
    }

    // ==================== INDEX ====================
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? shop, string? sort, string? category, int page = 1, CancellationToken ct = default)
    {
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        var basePostsForCounts = _db.BlogPosts
            .Where(b => !b.IsDeleted && b.Status == "Published")
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            basePostsForCounts = basePostsForCounts.Where(b => b.Title.Contains(term) || (b.Summary != null && b.Summary.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(shop))
        {
            basePostsForCounts = basePostsForCounts.Where(b => b.Shop != null && b.Shop.Profile != null && b.Shop.Profile.ShopSlug == shop);
        }

        var productCategories = await (
                from b in basePostsForCounts
                join bp in _db.BlogPostProducts.AsNoTracking() on b.Id equals bp.BlogPostId
                join p in _db.Products.AsNoTracking().Where(p => !p.IsDeleted) on bp.ProductId equals p.Id
                join c in _db.Categories.AsNoTracking().Where(c => c.IsActive && !c.IsDeleted) on p.CategoryId equals c.Id
                group b by new { c.Id, c.Name, c.Slug, c.SortOrder } into g
                select new BlogProductCategoryVm
                {
                    Id = g.Key.Id,
                    Name = g.Key.Name,
                    Slug = g.Key.Slug,
                    SortOrder = g.Key.SortOrder,
                    PostCount = g.Select(x => x.Id).Distinct().Count()
                })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        var q = _db.BlogPosts
            .Include(b => b.Shop).ThenInclude(s => s!.Profile)
            .Include(b => b.Comments)
            .Where(b => !b.IsDeleted && b.Status == "Published")
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(b => b.Title.Contains(term) || (b.Summary != null && b.Summary.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(shop))
        {
            q = q.Where(b => b.Shop != null && b.Shop.Profile != null && b.Shop.Profile.ShopSlug == shop);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            q = q.Where(b => b.BlogPostProducts.Any(bp =>
                bp.Product != null
                && !bp.Product.IsDeleted
                && bp.Product.Category != null
                && bp.Product.Category.IsActive
                && !bp.Product.Category.IsDeleted
                && bp.Product.Category.Slug == category));
        }

        sort = string.IsNullOrWhiteSpace(sort) ? "newest" : sort.Trim().ToLowerInvariant();
        q = sort switch
        {
            "oldest" => q.OrderBy(b => b.PublishedAt ?? b.CreatedAt),
            "most-viewed" => q.OrderByDescending(b => b.ViewCount).ThenByDescending(b => b.PublishedAt ?? b.CreatedAt),
            "most-commented" => q.OrderByDescending(b => b.Comments.Count(c => !c.IsDeleted)).ThenByDescending(b => b.PublishedAt ?? b.CreatedAt),
            "a-z-order" => q.OrderBy(b => b.Title),
            "z-a-order" => q.OrderByDescending(b => b.Title),
            _ => q.OrderByDescending(b => b.PublishedAt ?? b.CreatedAt)
        };

        var totalItems = await q.CountAsync(ct);
        var pageSize = 12;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var posts = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BlogPostCardVm
            {
                Id = b.Id,
                Title = b.Title,
                Slug = b.Slug,
                Summary = b.Summary,
                CoverImageUrl = b.CoverImageUrl,
                ViewCount = b.ViewCount,
                CommentCount = b.Comments.Count(c => !c.IsDeleted),
                PublishedAt = b.PublishedAt,
                ShopName = b.Shop != null && b.Shop.Profile != null ? b.Shop.Profile.ShopName ?? "" : "",
                ShopLogoUrl = b.Shop != null && b.Shop.Profile != null ? b.Shop.Profile.ShopLogoUrl : null,
                ShopSlug = b.Shop != null && b.Shop.Profile != null ? b.Shop.Profile.ShopSlug : null,
                CreatedAt = b.CreatedAt,
                ProductCategoryName = b.BlogPostProducts
                    .Where(bp => bp.Product != null
                                 && !bp.Product.IsDeleted
                                 && bp.Product.Category != null
                                 && bp.Product.Category.IsActive
                                 && !bp.Product.Category.IsDeleted)
                    .OrderBy(bp => bp.SortOrder)
                    .Select(bp => bp.Product!.Category!.Name)
                    .FirstOrDefault(),
                ProductCategorySlug = b.BlogPostProducts
                    .Where(bp => bp.Product != null
                                 && !bp.Product.IsDeleted
                                 && bp.Product.Category != null
                                 && bp.Product.Category.IsActive
                                 && !bp.Product.Category.IsDeleted)
                    .OrderBy(bp => bp.SortOrder)
                    .Select(bp => bp.Product!.Category!.Slug)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var recentPosts = await _db.BlogPosts
            .Where(b => !b.IsDeleted && b.Status == "Published")
            .OrderByDescending(b => b.PublishedAt ?? b.CreatedAt)
            .Take(5)
            .Select(b => new BlogPostCardVm
            {
                Id = b.Id,
                Title = b.Title,
                Slug = b.Slug,
                CoverImageUrl = b.CoverImageUrl,
                PublishedAt = b.PublishedAt,
                CreatedAt = b.CreatedAt,
                CommentCount = b.Comments.Count(c => !c.IsDeleted),
                ProductCategoryName = b.BlogPostProducts
                    .Where(bp => bp.Product != null
                                 && !bp.Product.IsDeleted
                                 && bp.Product.Category != null
                                 && bp.Product.Category.IsActive
                                 && !bp.Product.Category.IsDeleted)
                    .OrderBy(bp => bp.SortOrder)
                    .Select(bp => bp.Product!.Category!.Name)
                    .FirstOrDefault(),
                ProductCategorySlug = b.BlogPostProducts
                    .Where(bp => bp.Product != null
                                 && !bp.Product.IsDeleted
                                 && bp.Product.Category != null
                                 && bp.Product.Category.IsActive
                                 && !bp.Product.Category.IsDeleted)
                    .OrderBy(bp => bp.SortOrder)
                    .Select(bp => bp.Product!.Category!.Slug)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var vm = new BlogListVm
        {
            Posts = posts,
            RecentPosts = recentPosts,
            ProductCategories = productCategories,
            CurrentPage = page,
            TotalPages = totalPages,
            SearchQuery = search,
            ShopSlug = shop,
            Sort = sort,
            CategorySlug = category
        };

        return View(vm);
    }

    // ==================== DETAILS ====================
    [HttpGet]
    [Route("blog/{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken ct)
    {
        var post = await _db.BlogPosts
            .Include(b => b.Shop).ThenInclude(s => s!.Profile)
            .Include(b => b.BlogPostProducts)
                .ThenInclude(bp => bp.Product)
                    .ThenInclude(p => p!.Images)
            .Include(b => b.BlogPostProducts)
                .ThenInclude(bp => bp.Product)
                    .ThenInclude(p => p!.Variants)
            .Include(b => b.Comments.Where(c => !c.IsDeleted))
                .ThenInclude(c => c.Account)
                    .ThenInclude(a => a!.Profile)
            .FirstOrDefaultAsync(b => b.Slug == slug && !b.IsDeleted && b.Status == "Published", ct);

        if (post == null) return NotFound();

        // Increment view count
        post.ViewCount++;
        await _db.SaveChangesAsync(ct);

        var accountId = GetCurrentAccountId();
        Account? currentUser = null;
        if (accountId.HasValue)
        {
            currentUser = await _db.Accounts
                .AsNoTracking()
                .Include(a => a.Profile)
                .FirstOrDefaultAsync(a => a.Id == accountId.Value, ct);
        }

        // Build comments tree
        var allComments = post.Comments
            .Where(c => !c.IsDeleted)
            .Select(c => new BlogCommentVm
            {
                Id = c.Id,
                AccountId = c.AccountId,
                UserName = c.Account?.Profile?.FullName ?? c.Account?.Email ?? "Ẩn danh",
                AvatarUrl = c.Account?.Profile?.AvatarUrl,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                ParentCommentId = c.ParentCommentId
            })
            .ToList();

        var rootComments = allComments.Where(c => c.ParentCommentId == null).OrderByDescending(c => c.CreatedAt).ToList();
        foreach (var root in rootComments)
        {
            root.Replies = allComments
                .Where(c => c.ParentCommentId == root.Id)
                .OrderBy(c => c.CreatedAt)
                .ToList();
        }

        var linkedProducts = post.BlogPostProducts
            .OrderBy(bp => bp.SortOrder)
            .Where(bp => bp.Product != null && !bp.Product.IsDeleted)
            .Select(bp => new BlogLinkedProductVm
            {
                ProductId = bp.Product!.Id,
                Name = bp.Product.Name,
                Slug = bp.Product.Slug,
                ImageUrl = bp.Product.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                           ?? bp.Product.Images.Select(i => i.ImageUrl).FirstOrDefault(),
                Price = bp.Product.Variants.Where(v => v.IsDefault).Select(v => v.Price).FirstOrDefault(),
                SalePrice = bp.Product.Variants.Where(v => v.IsDefault).Select(v => v.SalePrice).FirstOrDefault(),
                AverageRating = bp.Product.AverageRating,
                ReviewCount = bp.Product.ReviewCount
            })
            .ToList();

        var vm = new BlogPostDetailsVm
        {
            Id = post.Id,
            Title = post.Title,
            Slug = post.Slug,
            Summary = post.Summary,
            Content = post.Content,
            CoverImageUrl = post.CoverImageUrl,
            ViewCount = post.ViewCount,
            PublishedAt = post.PublishedAt,
            CreatedAt = post.CreatedAt,
            ShopId = post.ShopId,
            ShopName = post.Shop?.Profile?.ShopName ?? "",
            ShopLogoUrl = post.Shop?.Profile?.ShopLogoUrl,
            ShopSlug = post.Shop?.Profile?.ShopSlug,
            LinkedProducts = linkedProducts,
            Comments = rootComments,
            TotalComments = allComments.Count,
            IsLoggedIn = accountId.HasValue,
            CurrentAccountId = accountId,
            CurrentUserName = currentUser?.Profile?.FullName ?? currentUser?.Email,
            CurrentUserAvatar = currentUser?.Profile?.AvatarUrl,
            RecentPosts = await _db.BlogPosts
                .Where(b => !b.IsDeleted && b.Status == "Published" && b.Slug != slug)
                .OrderByDescending(b => b.PublishedAt ?? b.CreatedAt)
                .Take(5)
                .Select(b => new BlogPostCardVm
                {
                    Id = b.Id,
                    Title = b.Title,
                    Slug = b.Slug,
                    CoverImageUrl = b.CoverImageUrl,
                    PublishedAt = b.PublishedAt,
                    CreatedAt = b.CreatedAt
                })
                .ToListAsync(ct),
            ProductCategoryName = post.BlogPostProducts
                .Where(bp => bp.Product != null
                             && !bp.Product.IsDeleted
                             && bp.Product.Category != null
                             && bp.Product.Category.IsActive
                             && !bp.Product.Category.IsDeleted)
                .OrderBy(bp => bp.SortOrder)
                .Select(bp => bp.Product!.Category!.Name)
                .FirstOrDefault(),
            ProductCategorySlug = post.BlogPostProducts
                .Where(bp => bp.Product != null
                             && !bp.Product.IsDeleted
                             && bp.Product.Category != null
                             && bp.Product.Category.IsActive
                             && !bp.Product.Category.IsDeleted)
                .OrderBy(bp => bp.SortOrder)
                .Select(bp => bp.Product!.Category!.Slug)
                .FirstOrDefault()
        };

        return View(vm);
    }

    // ==================== ADD COMMENT ====================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int blogPostId, string content, int? parentCommentId, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        
        // FOR TESTING: allow anonymous comments
        if (!accountId.HasValue)
        {
            // Try to find a guest or first user account
            accountId = (await _db.Accounts.FirstOrDefaultAsync(ct))?.Id ?? 1;
        }

        if (string.IsNullOrWhiteSpace(content))
            return Json(new { success = false, message = "Nội dung bình luận không được để trống." });

        if (content.Length > 1000)
            return Json(new { success = false, message = "Bình luận không được vượt quá 1000 ký tự." });

        var post = await _db.BlogPosts.FirstOrDefaultAsync(b => b.Id == blogPostId && !b.IsDeleted && b.Status == "Published", ct);
        if (post == null)
            return Json(new { success = false, message = "Bài viết không tồn tại." });

        // Validate parent comment if replying
        if (parentCommentId.HasValue)
        {
            var parentExists = await _db.BlogComments.AnyAsync(c => c.Id == parentCommentId.Value && c.BlogPostId == blogPostId && !c.IsDeleted, ct);
            if (!parentExists)
                return Json(new { success = false, message = "Bình luận gốc không tồn tại." });
        }

        var comment = new BlogComment
        {
            BlogPostId = blogPostId,
            AccountId = accountId.Value,
            ParentCommentId = parentCommentId,
            Content = content.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.BlogComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        // Return the new comment data
        var user = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId.Value, ct);

        return Json(new
        {
            success = true,
            id = comment.Id,
            accountId = comment.AccountId,
            userName = user?.Profile?.FullName ?? user?.Email ?? "Ẩn danh",
            avatarUrl = user?.Profile?.AvatarUrl,
            content = comment.Content,
            createdAt = comment.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            parentCommentId = comment.ParentCommentId
        });
    }

    // ==================== DELETE COMMENT ====================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComment(int commentId, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
            return Json(new { success = false, message = "Bạn cần đăng nhập." });

        var comment = await _db.BlogComments
            .Include(c => c.BlogPost)
            .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted, ct);

        if (comment == null)
            return Json(new { success = false, message = "Bình luận không tồn tại." });

        // Only the comment author or the shop owner can delete
        if (comment.AccountId != accountId.Value && comment.BlogPost?.ShopId != accountId.Value)
            return Json(new { success = false, message = "Bạn không có quyền xóa bình luận này." });

        comment.IsDeleted = true;
        comment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Json(new { success = true });
    }
}
