using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Shop,Admin")]
public class SellerWalletController : Controller
{
    private readonly DataContext _db;
    private readonly WalletService _walletService;

    public SellerWalletController(DataContext db, WalletService walletService)
    {
        _db = db;
        _walletService = walletService;
    }

    private async Task<(int ShopId, string ShopName, bool IsMall)> ResolveShopAsync(int? queryShopId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(idStr, out int currentUserId);

        int targetShopId = currentUserId;
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            if (queryShopId.HasValue && queryShopId.Value > 0)
            {
                targetShopId = queryShopId.Value;
            }
            else
            {
                var firstShop = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Role == "Shop" && !a.IsDeleted);
                targetShopId = firstShop?.Id ?? currentUserId;
            }
        }

        var acc = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == targetShopId);

        return (
            targetShopId,
            acc?.Profile?.ShopName ?? acc?.Profile?.FullName ?? "Gian hàng Sellzy",
            acc?.Profile?.IsMall ?? false
        );
    }

    [HttpGet]
    [HttpGet("/Admin/SellerWallet")]
    public async Task<IActionResult> Index(int? shopId = null, CancellationToken ct = default)
    {
        var (targetShopId, shopName, isMall) = await ResolveShopAsync(shopId);
        var wallet = await _walletService.GetOrCreateWalletAsync(targetShopId, ct);

        var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Value, ct);
        decimal GetSettingRaw(string id, decimal defVal)
        {
            if (settings.TryGetValue(id, out var s) &&
                decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
            return defVal;
        }

        decimal sellerTaxPct = isMall ? 0m : GetSettingRaw("Tax_Seller_Rate", 1.5m);
        decimal defaultCommPct = GetSettingRaw("Fee_Commission_Default", 8m);
        var allCategories = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        var shopOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.ShopId == targetShopId)
            .Include(o => o.Voucher)
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.Product)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        var transactions = await _db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.WalletId == wallet.WalletId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var settledRefSet = new HashSet<string>(
            transactions
                .Where(t => !string.IsNullOrWhiteSpace(t.ReferenceId))
                .Select(t => t.ReferenceId!),
            StringComparer.OrdinalIgnoreCase);

        decimal pendingEscrowAmount = 0m;
        int pendingEscrowCount = 0;
        decimal unsettledCompletedAmount = 0m;
        int unsettledCompletedCount = 0;
        decimal totalSettledNetRevenue = 0m;

        var settlementRows = new List<SellerOrderSettlementRowVm>();

        foreach (var order in shopOrders)
        {
            decimal subTotal = order.SubTotal;
            decimal shopVoucherDisc = order.ShopDiscountAmount > 0m
                ? order.ShopDiscountAmount
                : (order.Voucher?.ShopId != null ? order.DiscountAmount : 0m);
            decimal effectiveSubTotal = Math.Max(0m, subTotal - shopVoucherDisc);
            decimal shopDiscountScale = subTotal > 0m ? (effectiveSubTotal / subTotal) : 1m;

            decimal comm = 0m;
            foreach (var item in order.Items)
            {
                if (item.CommissionAmount > 0)
                {
                    comm += item.CommissionAmount;
                }
                else
                {
                    var catId = item.Variant?.Product?.CategoryId ?? 0;
                    var policy = CommissionHelper.ResolveCategoryPolicy(catId, allCategories, defaultCommPct);
                    var rawComm = CommissionHelper.CalculateItemCommission(
                        item.UnitPrice, item.Quantity, policy.CommissionRatePercent, policy.MaxCommissionAmount, isMall);
                    comm += Math.Round(rawComm * shopDiscountScale, 0);
                }
            }

            decimal tax = effectiveSubTotal * (sellerTaxPct / 100m);
            decimal netPayout = Math.Max(0m, effectiveSubTotal - comm - tax);

            bool isCompleted = order.OrderStatus == "Delivered" || order.OrderStatus == "Completed";
            bool isCancelled = order.OrderStatus == "Cancelled" || order.OrderStatus == "Returned" || order.OrderStatus == "Rejected";
            string settleRef = $"SETTLE-{order.OrderCode}";
            bool isCreditedToWallet = settledRefSet.Contains(settleRef) || settledRefSet.Contains(order.OrderCode);

            if (isCompleted)
            {
                totalSettledNetRevenue += netPayout;
                if (!isCreditedToWallet)
                {
                    unsettledCompletedAmount += netPayout;
                    unsettledCompletedCount++;
                }
            }
            else if (!isCancelled)
            {
                pendingEscrowAmount += netPayout;
                pendingEscrowCount++;
            }

            settlementRows.Add(new SellerOrderSettlementRowVm
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                OrderStatus = order.OrderStatus,
                GrossSales = subTotal,
                ShopVoucherDiscount = shopVoucherDisc,
                PlatformCommission = comm,
                SellerTax = tax,
                NetPayout = netPayout,
                IsCompleted = isCompleted,
                IsCancelled = isCancelled,
                IsCreditedToWallet = isCreditedToWallet,
                CreatedAt = order.CreatedAt
            });
        }

        decimal totalWithdrawn = transactions
            .Where(t => t.TransactionType == "WITHDRAW" && t.Amount < 0)
            .Sum(t => Math.Abs(t.Amount));

        var payoutRequests = await _db.PayoutRequests
            .AsNoTracking()
            .Where(p => p.ShopId == targetShopId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        var vm = new SellerWalletDashboardVm
        {
            ShopId = targetShopId,
            ShopName = shopName,
            IsMall = isMall,
            CurrentBalance = wallet.Balance,
            PendingEscrowAmount = pendingEscrowAmount,
            PendingEscrowCount = pendingEscrowCount,
            UnsettledCompletedAmount = unsettledCompletedAmount,
            UnsettledCompletedCount = unsettledCompletedCount,
            TotalSettledNetRevenue = totalSettledNetRevenue,
            TotalWithdrawnAmount = totalWithdrawn,
            Transactions = transactions,
            PayoutRequests = payoutRequests,
            OrderSettlements = settlementRows.Take(30).ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncSettledOrders(int? shopId = null, CancellationToken ct = default)
    {
        var (targetShopId, _, isMall) = await ResolveShopAsync(shopId);
        var wallet = await _walletService.GetOrCreateWalletAsync(targetShopId, ct);

        var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Value, ct);
        decimal GetSettingRaw(string id, decimal defVal)
        {
            if (settings.TryGetValue(id, out var s) &&
                decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
            return defVal;
        }

        decimal sellerTaxPct = isMall ? 0m : GetSettingRaw("Tax_Seller_Rate", 1.5m);
        decimal defaultCommPct = GetSettingRaw("Fee_Commission_Default", 8m);
        var allCategories = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        var completedOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.ShopId == targetShopId && (o.OrderStatus == "Delivered" || o.OrderStatus == "Completed"))
            .Include(o => o.Voucher)
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.Product)
            .ToListAsync(ct);

        var existingRefs = await _db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.WalletId == wallet.WalletId && t.ReferenceId != null)
            .Select(t => t.ReferenceId!)
            .ToListAsync(ct);
        var refSet = new HashSet<string>(existingRefs, StringComparer.OrdinalIgnoreCase);

        int syncedCount = 0;
        decimal syncedAmount = 0m;

        foreach (var order in completedOrders)
        {
            string settleRef = $"SETTLE-{order.OrderCode}";
            if (refSet.Contains(settleRef) || refSet.Contains(order.OrderCode))
                continue;

            decimal subTotal = order.SubTotal;
            decimal shopVoucherDisc = order.ShopDiscountAmount > 0m
                ? order.ShopDiscountAmount
                : (order.Voucher?.ShopId != null ? order.DiscountAmount : 0m);
            decimal effectiveSubTotal = Math.Max(0m, subTotal - shopVoucherDisc);
            decimal shopDiscountScale = subTotal > 0m ? (effectiveSubTotal / subTotal) : 1m;

            decimal comm = 0m;
            foreach (var item in order.Items)
            {
                if (item.CommissionAmount > 0)
                    comm += item.CommissionAmount;
                else
                {
                    var catId = item.Variant?.Product?.CategoryId ?? 0;
                    var policy = CommissionHelper.ResolveCategoryPolicy(catId, allCategories, defaultCommPct);
                    var rawComm = CommissionHelper.CalculateItemCommission(
                        item.UnitPrice, item.Quantity, policy.CommissionRatePercent, policy.MaxCommissionAmount, isMall);
                    comm += Math.Round(rawComm * shopDiscountScale, 0);
                }
            }

            decimal tax = effectiveSubTotal * (sellerTaxPct / 100m);
            decimal netPayout = Math.Max(0m, effectiveSubTotal - comm - tax);
            if (netPayout <= 0) continue;

            wallet.Balance += netPayout;
            wallet.UpdatedAt = DateTime.UtcNow;

            string voucherNote = shopVoucherDisc > 0 ? $", Voucher Shop tài trợ {shopVoucherDisc:N0}₫" : "";
            _db.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.WalletId,
                Amount = netPayout,
                TransactionType = "SETTLEMENT",
                ReferenceId = settleRef,
                Description = $"Quyết toán doanh thu đơn #{order.OrderCode} (Đã khấu trừ phí sàn {comm:N0}₫, thuế {tax:N0}₫{voucherNote})",
                BalanceAfter = wallet.Balance,
                CreatedAt = DateTime.UtcNow
            });

            refSet.Add(settleRef);
            syncedCount++;
            syncedAmount += netPayout;
        }

        if (syncedCount > 0)
        {
            await _db.SaveChangesAsync(ct);
            TempData["AdminToast"] = $"Đã quyết toán thành công {syncedCount} đơn hàng hoàn tất (+{syncedAmount:N0}₫) vào Ví Doanh thu của Shop!";
        }
        else
        {
            TempData["AdminToast"] = "Toàn bộ đơn hàng hoàn tất đều đã được quyết toán vào Ví trước đó.";
        }

        return RedirectToAction(nameof(Index), new { shopId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(decimal amount, string bankName, string accountNumber, string accountHolder, int? shopId = null, CancellationToken ct = default)
    {
        var (targetShopId, _, _) = await ResolveShopAsync(shopId);
        var wallet = await _walletService.GetOrCreateWalletAsync(targetShopId, ct);

        if (amount < 50000m)
        {
            TempData["AdminToastError"] = "Số tiền rút tối thiểu là 50.000₫.";
            return RedirectToAction(nameof(Index), new { shopId });
        }

        if (string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(accountNumber) || string.IsNullOrWhiteSpace(accountHolder))
        {
            TempData["AdminToastError"] = "Vui lòng nhập đầy đủ Tên ngân hàng, Số tài khoản và Tên chủ tài khoản.";
            return RedirectToAction(nameof(Index), new { shopId });
        }

        if (wallet.Balance < amount)
        {
            TempData["AdminToastError"] = $"Số dư khả dụng không đủ ({wallet.Balance:N0}₫). Hãy bấm 'Quyết toán đơn hoàn tất vào Ví' nếu bạn có đơn hàng mới giao xong.";
            return RedirectToAction(nameof(Index), new { shopId });
        }

        wallet.Balance -= amount;
        wallet.UpdatedAt = DateTime.UtcNow;

        string refCode = $"WD-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var cleanBank = bankName.Trim();
        var cleanAccNo = accountNumber.Trim();
        var cleanHolder = accountHolder.Trim().ToUpperInvariant();

        _db.PayoutRequests.Add(new PayoutRequest
        {
            RequestCode = refCode,
            ShopId = targetShopId,
            Amount = amount,
            BankName = cleanBank,
            AccountNumber = cleanAccNo,
            AccountHolder = cleanHolder,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        });

        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.WalletId,
            Amount = -amount,
            TransactionType = "WITHDRAW",
            ReferenceId = refCode,
            Description = $"Tạm giữ chờ duyệt lệnh rút tiền về {cleanBank} • STK: {cleanAccNo} ({cleanHolder})",
            BalanceAfter = wallet.Balance,
            CreatedAt = DateTime.UtcNow
        });

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"💸 Yêu cầu rút doanh thu mới #{refCode} ({amount:N0}₫)",
            Content = $"Gian hàng ID #{targetShopId} gửi lệnh rút {amount:N0}₫ về {cleanBank} ({cleanAccNo} - {cleanHolder}). Vui lòng kiểm tra và phê duyệt giải ngân.",
            TargetType = "Role",
            TargetRole = "Admin",
            SentByAdminId = targetShopId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        TempData["AdminToast"] = $"Đã gửi yêu cầu rút {amount:N0}₫ về {cleanBank} ({cleanAccNo})! Mã yêu cầu #{refCode} đang chờ Kế toán Sàn phê duyệt giải ngân.";
        return RedirectToAction(nameof(Index), new { shopId });
    }
}

public class SellerWalletDashboardVm
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public bool IsMall { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal PendingEscrowAmount { get; set; }
    public int PendingEscrowCount { get; set; }
    public decimal UnsettledCompletedAmount { get; set; }
    public int UnsettledCompletedCount { get; set; }
    public decimal TotalSettledNetRevenue { get; set; }
    public decimal TotalWithdrawnAmount { get; set; }
    public List<WalletTransaction> Transactions { get; set; } = new();
    public List<PayoutRequest> PayoutRequests { get; set; } = new();
    public List<SellerOrderSettlementRowVm> OrderSettlements { get; set; } = new();
}

public class SellerOrderSettlementRowVm
{
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public decimal GrossSales { get; set; }
    public decimal ShopVoucherDiscount { get; set; }
    public decimal PlatformCommission { get; set; }
    public decimal SellerTax { get; set; }
    public decimal NetPayout { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsCreditedToWallet { get; set; }
    public DateTime CreatedAt { get; set; }
}
