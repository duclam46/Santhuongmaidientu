using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using DOAN_BANHANG.Helpers;

namespace DOAN_BANHANG.Controllers;

[Route("cart")]
public class CartController : Controller
{
    private readonly DataContext _context;

    private const string SessionCartKey = "cart";
    private const string SessionCartSelectionKey = "cart_selected";

    public CartController(DataContext context)
    {
        _context = context;
    }

    private int? GetSignedInAccountId()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return null;

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idRaw, out var id) ? id : null;
    }

    private Dictionary<int, int> GetSessionCart()
        => HttpContext.Session.GetJson<Dictionary<int, int>>(SessionCartKey) ?? new Dictionary<int, int>();

    private void SaveSessionCart(Dictionary<int, int> cart)
        => HttpContext.Session.SetJson(SessionCartKey, cart);

    private sealed class CartSelectionState
    {
        public bool Initialized { get; set; }

        public HashSet<int> DeselectedIds { get; set; } = new();
    }

    private CartSelectionState GetCartSelectionState()
        => HttpContext.Session.GetJson<CartSelectionState>(SessionCartSelectionKey) ?? new CartSelectionState();

    private void SaveCartSelectionState(CartSelectionState state)
        => HttpContext.Session.SetJson(SessionCartSelectionKey, state);

    private static int NormalizeQuantity(int quantity)
    {
        if (quantity <= 0) return 1;
        return quantity;
    }

    private static int NormalizeSetQuantity(int quantity)
    {
        if (quantity < 0) return 0;
        return quantity;
    }

    private async Task<Cart> GetOrCreateCartAsync(int accountId, CancellationToken cancellationToken)
    {
        var cart = await _context.Carts
            .FirstOrDefaultAsync(c => c.AccountId == accountId, cancellationToken);

        if (cart is not null) return cart;

        cart = new Cart
        {
            AccountId = accountId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Carts.Add(cart);
        await _context.SaveChangesAsync(cancellationToken);
        return cart;
    }

    [HttpPost("add")]
    public async Task<IActionResult> Add([FromBody] AddToCartRequest? body, [FromForm] AddToCartRequest? form, CancellationToken cancellationToken)
    {
        var req = body ?? form;
        if (req is null || req.VariantId <= 0)
        {
            return BadRequest(new { message = "VariantId is required" });
        }

        var quantity = NormalizeQuantity(req.Quantity);

        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.Id == req.VariantId && v.IsActive && !v.IsDeleted, cancellationToken);

        if (variant is null)
        {
            return NotFound(new { message = "Sản phẩm không tìm thấy hoặc đã ngừng kinh doanh." });
        }

        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            var sessionCart = GetSessionCart();
            sessionCart.TryGetValue(req.VariantId, out var existingQty);
            var targetQty = existingQty + quantity;
            if (targetQty > variant.StockQuantity)
            {
                return BadRequest(new { message = $"Số lượng yêu cầu vượt quá tồn kho (chỉ còn {variant.StockQuantity} sản phẩm trong kho)" });
            }
            sessionCart[req.VariantId] = targetQty;
            SaveSessionCart(sessionCart);

            var itemCount = sessionCart.Values.Sum();
            return Ok(new { itemCount });
        }

        var cart = await GetOrCreateCartAsync(accountId.Value, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        var item = await _context.CartItems
            .FirstOrDefaultAsync(i => i.CartId == cart.Id && i.VariantId == req.VariantId, cancellationToken);

        var currentQty = item?.Quantity ?? 0;
        var newQty = currentQty + quantity;
        if (newQty > variant.StockQuantity)
        {
            return BadRequest(new { message = $"Số lượng yêu cầu vượt quá tồn kho (chỉ còn {variant.StockQuantity} sản phẩm trong kho)" });
        }

        if (item is null)
        {
            item = new CartItem
            {
                CartId = cart.Id,
                VariantId = req.VariantId,
                Quantity = quantity,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            _context.CartItems.Add(item);
        }
        else
        {
            item.Quantity = newQty;
            item.UpdatedAt = nowUtc;
        }

        cart.UpdatedAt = nowUtc;
        await _context.SaveChangesAsync(cancellationToken);

        var itemCountDb = await _context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cart.Id)
            .SumAsync(i => i.Quantity, cancellationToken);

        return Ok(new { itemCount = itemCountDb });
    }

    [HttpPost("update-quantity")]
    public async Task<IActionResult> UpdateQuantity([FromBody] UpdateCartQuantityRequest? body, [FromForm] UpdateCartQuantityRequest? form, CancellationToken cancellationToken)
    {
        var req = body ?? form;
        if (req is null || req.CartItemId <= 0)
        {
            return BadRequest(new { message = "CartItemId is required" });
        }

        var quantity = NormalizeSetQuantity(req.Quantity);

        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            var sessionCart = GetSessionCart();
            if (!sessionCart.ContainsKey(req.CartItemId))
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm trong giỏ." });
            }

            var variant = await _context.ProductVariants
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == req.CartItemId && v.IsActive && !v.IsDeleted, cancellationToken);

            if (variant is null)
            {
                return NotFound(new { message = "Sản phẩm không tồn tại." });
            }

            if (quantity > variant.StockQuantity)
            {
                return BadRequest(new { message = $"Số lượng yêu cầu vượt quá tồn kho (chỉ còn {variant.StockQuantity} sản phẩm trong kho)" });
            }

            if (quantity <= 0)
            {
                sessionCart.Remove(req.CartItemId);
            }
            else
            {
                sessionCart[req.CartItemId] = quantity;
            }

            SaveSessionCart(sessionCart);
            return Ok(new { itemCount = sessionCart.Values.Sum() });
        }

        var cart = await GetOrCreateCartAsync(accountId.Value, cancellationToken);

        var item = await _context.CartItems
            .Include(i => i.Variant)
            .FirstOrDefaultAsync(i => i.Id == req.CartItemId && i.CartId == cart.Id, cancellationToken);

        if (item is null)
        {
            return NotFound(new { message = "Cart item not found" });
        }

        if (item.Variant is null)
        {
            return NotFound(new { message = "Variant not found" });
        }

        if (quantity > item.Variant.StockQuantity)
        {
            return BadRequest(new { message = $"Số lượng yêu cầu vượt quá tồn kho (chỉ còn {item.Variant.StockQuantity} sản phẩm trong kho)" });
        }

        var nowUtc = DateTime.UtcNow;

        if (quantity <= 0)
        {
            _context.CartItems.Remove(item);
        }
        else
        {
            item.Quantity = quantity;
            item.UpdatedAt = nowUtc;
        }

        cart.UpdatedAt = nowUtc;
        await _context.SaveChangesAsync(cancellationToken);

        var itemCountDb = await _context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cart.Id)
            .SumAsync(i => i.Quantity, cancellationToken);

        return Ok(new { itemCount = itemCountDb });
    }

    [HttpPost("remove")]
    public async Task<IActionResult> Remove([FromBody] RemoveCartItemRequest? body, [FromForm] RemoveCartItemRequest? form, CancellationToken cancellationToken)
    {
        var req = body ?? form;
        if (req is null || req.CartItemId <= 0)
        {
            return BadRequest(new { message = "CartItemId is required" });
        }

        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            var sessionCart = GetSessionCart();
            if (!sessionCart.Remove(req.CartItemId))
            {
                return NotFound(new { message = "Cart item not found" });
            }

            SaveSessionCart(sessionCart);
            return Ok(new { itemCount = sessionCart.Values.Sum() });
        }

        var cart = await GetOrCreateCartAsync(accountId.Value, cancellationToken);

        var item = await _context.CartItems
            .FirstOrDefaultAsync(i => i.Id == req.CartItemId && i.CartId == cart.Id, cancellationToken);

        if (item is null)
        {
            return NotFound(new { message = "Cart item not found" });
        }

        var nowUtc = DateTime.UtcNow;
        _context.CartItems.Remove(item);
        cart.UpdatedAt = nowUtc;
        await _context.SaveChangesAsync(cancellationToken);

        var itemCountDb = await _context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cart.Id)
            .SumAsync(i => i.Quantity, cancellationToken);

        return Ok(new { itemCount = itemCountDb });
    }

    [HttpPost("select")]
    public IActionResult Select([FromBody] SetCartItemSelectedRequest? body, [FromForm] SetCartItemSelectedRequest? form)
    {
        var req = body ?? form;
        if (req is null || req.CartItemId <= 0)
        {
            return BadRequest(new { message = "CartItemId is required" });
        }

        var state = GetCartSelectionState();
        state.Initialized = true;

        if (req.Selected)
        {
            state.DeselectedIds.Remove(req.CartItemId);
        }
        else
        {
            state.DeselectedIds.Add(req.CartItemId);
        }

        SaveCartSelectionState(state);
        return Ok(new { ok = true });
    }

    [HttpPost("select-only")]
    public async Task<IActionResult> SelectOnly([FromBody] SelectOnlyCartItemRequest? body, [FromForm] SelectOnlyCartItemRequest? form, CancellationToken cancellationToken)
    {
        var req = body ?? form;
        if (req is null || req.VariantId <= 0)
        {
            return BadRequest(new { message = "VariantId is required" });
        }

        var state = GetCartSelectionState();
        state.Initialized = true;

        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            var sessionCart = GetSessionCart();
            if (!sessionCart.ContainsKey(req.VariantId))
            {
                return NotFound(new { message = "Variant not found in cart" });
            }

            var allIds = sessionCart.Keys.ToList();
            var selectedId = req.VariantId; // guest: CartItemId == VariantId
            state.DeselectedIds = allIds.Where(id => id != selectedId).ToHashSet();
            SaveCartSelectionState(state);
            return Ok(new { ok = true });
        }

        var cartId = await _context.Carts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId.Value)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!cartId.HasValue)
        {
            return NotFound(new { message = "Cart not found" });
        }

        var targetCartItemId = await _context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cartId.Value && i.VariantId == req.VariantId)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!targetCartItemId.HasValue)
        {
            return NotFound(new { message = "Cart item not found" });
        }

        var allCartItemIds = await _context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cartId.Value)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        var selected = targetCartItemId.Value;
        state.DeselectedIds = allCartItemIds.Where(id => id != selected).ToHashSet();
        SaveCartSelectionState(state);
        return Ok(new { ok = true });
    }

    [HttpGet("items")]
    public async Task<IActionResult> Items(CancellationToken cancellationToken)
    {
        var nowLocal = DateTime.Now;

        var selection = GetCartSelectionState();

        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            var sessionCart = GetSessionCart();
            if (sessionCart.Count == 0) return Ok(new CartItemsResponse());

            var variantIds = sessionCart.Keys.ToList();

            var variants = await _context.ProductVariants
                .AsNoTracking()
                .Where(v => variantIds.Contains(v.Id) && v.IsActive && !v.IsDeleted)
                .Include(v => v.Product)!.ThenInclude(p => p!.Images)
                .Include(v => v.Images)
                .Include(v => v.Color)
                .Include(v => v.Size)
                .ToListAsync(cancellationToken);

            var guestDtoItems = new List<CartItemDto>(variants.Count);
            foreach (var v in variants.OrderByDescending(x => x.Id))
            {
                var p = v.Product;
                if (!sessionCart.TryGetValue(v.Id, out var qty)) continue;

                var inSaleWindow = v.SalePrice.HasValue
                    && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                    && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal);
                var price = inSaleWindow ? v.SalePrice!.Value : v.Price;

                var imageUrl = v.Images
                    ?.OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .Select(x => x.ImageUrl)
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                    ?? p?.Images
                        ?.OrderByDescending(x => x.IsPrimary)
                        .ThenBy(x => x.SortOrder)
                        .ThenBy(x => x.Id)
                        .Select(x => x.ImageUrl)
                        .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                    ?? "assets/images/vitamin-c.png";

                var cartItemId = v.Id; // guest: treat as VariantId
                var isSelected = selection.Initialized ? !selection.DeselectedIds.Contains(cartItemId) : true;

                guestDtoItems.Add(new CartItemDto
                {
                    CartItemId = cartItemId,
                    VariantId = v.Id,
                    ProductName = p?.Name ?? "(N/A)",
                    ProductSlug = p?.Slug ?? string.Empty,
                    ImageUrl = imageUrl,
                    Color = v.Color?.Name,
                    Size = v.Size?.Name,
                    UnitPrice = price,
                    Quantity = qty,
                    IsSelected = isSelected
                });
            }

            var guestCount = guestDtoItems.Sum(x => x.Quantity);
            var guestSubTotal = guestDtoItems.Sum(x => x.UnitPrice * x.Quantity);

            var guestSelected = guestDtoItems.Where(x => x.IsSelected).ToList();
            var guestSelectedCount = guestSelected.Sum(x => x.Quantity);
            var guestSelectedSubTotal = guestSelected.Sum(x => x.UnitPrice * x.Quantity);

            return Ok(new CartItemsResponse
            {
                ItemCount = guestCount,
                SubTotal = guestSubTotal,
                SelectedItemCount = guestSelectedCount,
                SelectedSubTotal = guestSelectedSubTotal,
                Items = guestDtoItems
            });
        }

        var cartId = await _context.Carts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId.Value)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!cartId.HasValue)
        {
            return Ok(new CartItemsResponse());
        }

        var items = await _context.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cartId.Value)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Product)!.ThenInclude(p => p!.Images)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Images)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Color)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Size)
            .OrderByDescending(i => i.Id)
            .ToListAsync(cancellationToken);

        var dtoItems = new List<CartItemDto>(items.Count);
        foreach (var item in items)
        {
            var v = item.Variant;
            var p = v?.Product;

            var price = 0m;
            if (v is not null)
            {
                var inSaleWindow = v.SalePrice.HasValue
                    && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                    && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal);

                price = inSaleWindow ? v.SalePrice!.Value : v.Price;
            }

            var imageUrl = v?.Images
                ?.OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => x.ImageUrl)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                ?? p?.Images
                    ?.OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .Select(x => x.ImageUrl)
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                ?? "assets/images/vitamin-c.png";

            var isSelected = selection.Initialized ? !selection.DeselectedIds.Contains(item.Id) : true;

            dtoItems.Add(new CartItemDto
            {
                CartItemId = item.Id,
                VariantId = item.VariantId,
                ProductName = p?.Name ?? "(N/A)",
                ProductSlug = p?.Slug ?? string.Empty,
                ImageUrl = imageUrl,
                Color = v?.Color?.Name,
                Size = v?.Size?.Name,
                UnitPrice = price,
                Quantity = item.Quantity,
                IsSelected = isSelected
            });
        }

        var count = dtoItems.Sum(x => x.Quantity);
        var subTotal = dtoItems.Sum(x => x.UnitPrice * x.Quantity);

        var selected = dtoItems.Where(x => x.IsSelected).ToList();
        var selectedCount = selected.Sum(x => x.Quantity);
        var selectedSubTotal = selected.Sum(x => x.UnitPrice * x.Quantity);

        return Ok(new CartItemsResponse
        {
            ItemCount = count,
            SubTotal = subTotal,
            SelectedItemCount = selectedCount,
            SelectedSubTotal = selectedSubTotal,
            Items = dtoItems
        });
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions(int count = 6, CancellationToken cancellationToken = default)
    {
        var take = count <= 0 ? 6 : Math.Clamp(count, 1, 12);
        var nowLocal = DateTime.Now;

        var inCartProductIds = new HashSet<int>();
        var cartCategoryIds = new HashSet<int>();

        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            var sessionCart = GetSessionCart();
            var variantIds = sessionCart.Keys.ToList();
            if (variantIds.Count > 0)
            {
                var cartRefs = await (from v in _context.ProductVariants.AsNoTracking()
                                      join p in _context.Products.AsNoTracking() on v.ProductId equals p.Id
                                      where variantIds.Contains(v.Id)
                                      select new { p.Id, p.CategoryId })
                    .Distinct()
                    .ToListAsync(cancellationToken);

                foreach (var r in cartRefs)
                {
                    inCartProductIds.Add(r.Id);
                    cartCategoryIds.Add(r.CategoryId);
                }
            }
        }
        else
        {
            var cartId = await _context.Carts
                .AsNoTracking()
                .Where(c => c.AccountId == accountId.Value)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (cartId.HasValue)
            {
                var cartRefs = await (from i in _context.CartItems.AsNoTracking()
                                      join v in _context.ProductVariants.AsNoTracking() on i.VariantId equals v.Id
                                      join p in _context.Products.AsNoTracking() on v.ProductId equals p.Id
                                      where i.CartId == cartId.Value
                                      select new { p.Id, p.CategoryId })
                    .Distinct()
                    .ToListAsync(cancellationToken);

                foreach (var r in cartRefs)
                {
                    inCartProductIds.Add(r.Id);
                    cartCategoryIds.Add(r.CategoryId);
                }
            }
        }

        IQueryable<Product> baseQuery = _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active" && !p.IsDeleted);

        IQueryable<SuggestedProductDto> Project(IQueryable<Product> q)
        {
            return q
                .OrderByDescending(p => p.AverageRating)
                .ThenByDescending(p => p.ReviewCount)
                .ThenByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    ProductId = p.Id,
                    p.Name,
                    p.Slug,
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    PrimaryImageUrl = p.Images
                        .OrderByDescending(i => i.IsPrimary)
                        .ThenBy(i => i.SortOrder)
                        .ThenBy(i => i.Id)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),
                    Price = p.Variants
                        .Where(v => v.IsActive && !v.IsDeleted)
                        .Select(v => (decimal?)((v.SalePrice.HasValue
                                && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                                && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                            ? v.SalePrice.Value
                            : v.Price))
                        .Min() ?? 0m,
                    OldPrice = p.Variants
                        .Where(v => v.IsActive
                            && !v.IsDeleted
                            && v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal)
                            && v.SalePrice.Value < v.Price)
                        .Select(v => (decimal?)v.Price)
                        .Min()
                })
                .Select(x => new SuggestedProductDto
                {
                    ProductId = x.ProductId,
                    Name = x.Name,
                    Slug = x.Slug,
                    CategoryName = x.CategoryName,
                    PrimaryImageUrl = x.PrimaryImageUrl,
                    Price = x.Price,
                    OldPrice = x.OldPrice.HasValue && x.OldPrice.Value > x.Price ? x.OldPrice : null,
                    DiscountPercent = x.OldPrice.HasValue && x.OldPrice.Value > x.Price && x.OldPrice.Value > 0m
                        ? (int)Math.Round((x.OldPrice.Value - x.Price) * 100m / x.OldPrice.Value)
                        : 0
                });
        }

        var selected = new List<SuggestedProductDto>(take);
        var selectedIds = new HashSet<int>();

        async Task AppendAsync(IQueryable<Product> q)
        {
            if (selected.Count >= take) return;
            var remaining = take - selected.Count;
            var batch = await Project(q)
                .Where(x => !selectedIds.Contains(x.ProductId))
                .Take(remaining)
                .ToListAsync(cancellationToken);

            foreach (var it in batch)
            {
                if (selectedIds.Add(it.ProductId)) selected.Add(it);
            }
        }

        var cartCategoryList = cartCategoryIds.ToList();
        if (cartCategoryList.Count > 0)
        {
            // 1) Same category (cùng loại)
            await AppendAsync(baseQuery
                .Where(p => cartCategoryList.Contains(p.CategoryId))
                .Where(p => !inCartProductIds.Contains(p.Id)));

            if (selected.Count < take)
            {
                // 2) Parent category (danh mục lớn hơn) => siblings in the same parent
                var parentIds = await _context.Categories
                    .AsNoTracking()
                    .Where(c => cartCategoryList.Contains(c.Id) && c.ParentId.HasValue)
                    .Select(c => c.ParentId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                if (parentIds.Count > 0)
                {
                    var siblingCategoryIds = await _context.Categories
                        .AsNoTracking()
                        .Where(c => c.IsActive && !c.IsDeleted && c.ParentId.HasValue && parentIds.Contains(c.ParentId.Value))
                        .Select(c => c.Id)
                        .Distinct()
                        .ToListAsync(cancellationToken);

                    if (siblingCategoryIds.Count > 0)
                    {
                        await AppendAsync(baseQuery
                            .Where(p => siblingCategoryIds.Contains(p.CategoryId))
                            .Where(p => !inCartProductIds.Contains(p.Id)));
                    }
                }
            }
        }

        // 3) Fallback
        if (selected.Count < take)
        {
            await AppendAsync(baseQuery.Where(p => !inCartProductIds.Contains(p.Id)));
        }

        return Ok(new CartSuggestionsResponse { Items = selected });
    }
}
