using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Services;

/// <summary>
/// Tự động hoàn tất (Auto-Complete) và giải ngân đối soát Escrow vào Ví Gian hàng
/// đối với các đơn hàng Đã giao (Delivered) quá 5 ngày mà không phát sinh Trả hàng / Khiếu nại.
/// </summary>
public class OrderAutoSettlementWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OrderAutoSettlementWorker> _logger;

    public OrderAutoSettlementWorker(IServiceProvider services, ILogger<OrderAutoSettlementWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessEligibleOrdersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrderAutoSettlementWorker encountered an error.");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task ProcessEligibleOrdersAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();

        var cutoffUtc = DateTime.UtcNow.AddDays(-5);

        var candidates = await db.Orders
            .Where(o => o.OrderStatus == "Delivered" && o.UpdatedAt <= cutoffUtc)
            .Include(o => o.Voucher)
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.Product)
            .Take(50)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        var candidateIds = candidates.Select(c => c.Id).ToList();

        var blockedByReturn = (await db.ReturnRequests
            .AsNoTracking()
            .Where(r => candidateIds.Contains(r.OrderId) && r.Status != "Rejected")
            .Select(r => r.OrderId)
            .ToListAsync(ct)).ToHashSet();

        var blockedByDispute = (await db.OrderDisputes
            .AsNoTracking()
            .Where(d => candidateIds.Contains(d.OrderId) && (d.Status == "Open" || d.Status == "UnderReview"))
            .Select(d => d.OrderId)
            .ToListAsync(ct)).ToHashSet();

        var settings = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Value, ct);
        decimal GetSetting(string id, decimal defVal)
        {
            if (settings.TryGetValue(id, out var raw) &&
                decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return defVal;
        }

        decimal rawSellerTaxRate = GetSetting("Tax_Seller_Rate", 1.5m);
        decimal defaultCommPct = GetSetting("Fee_Commission_Default", 8m);
        var allCategories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        foreach (var order in candidates)
        {
            if (blockedByReturn.Contains(order.Id) || blockedByDispute.Contains(order.Id))
                continue;

            bool isMall = await db.UserProfiles.AsNoTracking()
                .AnyAsync(p => p.AccountId == order.ShopId && p.IsMall, ct);

            order.OrderStatus = "Completed";
            order.UpdatedAt = DateTime.UtcNow;

            db.OrderTrackingLogs.Add(new OrderTrackingLog
            {
                OrderId = order.Id,
                Status = "Completed",
                Note = "Hệ thống tự động hoàn tất đơn hàng & giải ngân Escrow sau 5 ngày giao thành công không phát sinh khiếu nại.",
                CreatedAt = DateTime.UtcNow
            });

            var wallet = await walletService.GetOrCreateWalletAsync(order.ShopId, ct);
            string settleRef = $"SETTLE-{order.OrderCode}";
            bool alreadySettled = await db.WalletTransactions
                .AsNoTracking()
                .AnyAsync(t => t.WalletId == wallet.WalletId && (t.ReferenceId == settleRef || t.ReferenceId == order.OrderCode), ct);

            if (!alreadySettled)
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

                decimal sellerTaxPct = isMall ? 0m : rawSellerTaxRate;
                decimal tax = effectiveSubTotal * (sellerTaxPct / 100m);
                decimal netPayout = Math.Max(0m, effectiveSubTotal - comm - tax);

                if (netPayout > 0)
                {
                    wallet.Balance += netPayout;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    db.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId = wallet.WalletId,
                        Amount = netPayout,
                        TransactionType = "SETTLEMENT",
                        ReferenceId = settleRef,
                        Description = $"Tự động quyết toán Escrow đơn #{order.OrderCode} (+{netPayout:N0}₫)",
                        BalanceAfter = wallet.Balance,
                        CreatedAt = DateTime.UtcNow
                    });

                    db.SystemNotifications.Add(new SystemNotification
                    {
                        Title = $"💰 Đã tự động quyết toán đơn #{order.OrderCode} (+{netPayout:N0}₫)",
                        Content = $"Đơn hàng #{order.OrderCode} đã hoàn tất chu kỳ bảo vệ người mua và được cộng {netPayout:N0}₫ thực nhận vào Ví Gian hàng.",
                        TargetType = "Account",
                        TargetAccountId = order.ShopId,
                        SentByAdminId = order.ShopId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
