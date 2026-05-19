namespace DOAN_BANHANG.ViewModels;

// ========== Admin ViewModels ==========

public class AdminBlogListVm
{
    public List<AdminBlogPostItemVm> Posts { get; set; } = new();
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public string? SearchQuery { get; set; }
    public string? StatusFilter { get; set; }
}

public class AdminBlogPostItemVm
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string Status { get; set; } = "Draft";
    public int ViewCount { get; set; }
    public int CommentCount { get; set; }
    public int ProductCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class BlogPostCreateVm
{
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public string Status { get; set; } = "Draft";
    public List<int> SelectedProductIds { get; set; } = new();
}

public class BlogPostEditVm
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public string? CoverImageUrl { get; set; }
    public string Status { get; set; } = "Draft";
    public List<int> SelectedProductIds { get; set; } = new();
    public List<BlogProductPickerItem> AvailableProducts { get; set; } = new();
}

public class BlogProductPickerItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public string Slug { get; set; } = string.Empty;
}

// ========== Public ViewModels ==========

public class BlogListVm
{
    public List<BlogPostCardVm> Posts { get; set; } = new();
    public List<BlogPostCardVm> RecentPosts { get; set; } = new();
    public List<BlogProductCategoryVm> ProductCategories { get; set; } = new();
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public string? SearchQuery { get; set; }
    public string? ShopSlug { get; set; }
    public string? Sort { get; set; }
    public string? CategorySlug { get; set; }
}

public class BlogProductCategoryVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int PostCount { get; set; }
}

public class BlogPostCardVm
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? CoverImageUrl { get; set; }
    public int ViewCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? ShopLogoUrl { get; set; }
    public string? ShopSlug { get; set; }
    public DateTime CreatedAt { get; set; }

    public string? ProductCategoryName { get; set; }
    public string? ProductCategorySlug { get; set; }
}

public class BlogPostDetailsVm
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public string? CoverImageUrl { get; set; }
    public int ViewCount { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Shop info
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? ShopLogoUrl { get; set; }
    public string? ShopSlug { get; set; }

    // Linked products
    public List<BlogLinkedProductVm> LinkedProducts { get; set; } = new();

    // Comments
    public List<BlogCommentVm> Comments { get; set; } = new();
    public int TotalComments { get; set; }

    // Current user info for commenting
    public bool IsLoggedIn { get; set; }
    public int? CurrentAccountId { get; set; }
    public string? CurrentUserName { get; set; }
    public string? CurrentUserAvatar { get; set; }
    
    public List<BlogPostCardVm> RecentPosts { get; set; } = new();

    public string? ProductCategoryName { get; set; }
    public string? ProductCategorySlug { get; set; }
}

public class BlogLinkedProductVm
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public class BlogCommentVm
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? ParentCommentId { get; set; }
    public List<BlogCommentVm> Replies { get; set; } = new();
}
