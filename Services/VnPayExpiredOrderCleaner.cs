using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Services;

/// <summary>
/// Background service that automatically cancels VNPay orders
/// that haven't been paid within the allowed time window (15 minutes).
/// </summary>
public sealed class VnPayExpiredOrderCleaner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VnPayExpiredOrderCleaner> _logger;

    /// <summary>
    /// How long a customer has to complete VNPay payment before the order is auto-cancelled.
    /// </summary>
    public static readonly TimeSpan PaymentTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often this service checks for expired orders.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    public VnPayExpiredOrderCleaner(
        IServiceScopeFactory scopeFactory,
        ILogger<VnPayExpiredOrderCleaner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VnPayExpiredOrderCleaner started. Timeout={Timeout}min, CheckInterval={Interval}min",
            PaymentTimeout.TotalMinutes, CheckInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CancelExpiredOrdersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while cancelling expired VNPay orders.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CancelExpiredOrdersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var cutoff = DateTime.Now - PaymentTimeout;

        // Find orders that are BankTransfer, still awaiting payment, and created before the cutoff
        var expiredOrders = await db.Orders
            .Where(o => o.PaymentMethod == "BankTransfer"
                && o.PaymentStatus == "AwaitingPayment"
                && o.OrderStatus != "Cancelled"
                && o.CreatedAt <= cutoff)
            .ToListAsync(cancellationToken);

        if (expiredOrders.Count == 0) return;

        foreach (var order in expiredOrders)
        {
            order.PaymentStatus = "Expired";
            order.OrderStatus = "Cancelled";
            order.UpdatedAt = DateTime.UtcNow;

            db.OrderTrackingLogs.Add(new OrderTrackingLog
            {
                OrderId = order.Id,
                Status = "Cancelled",
                Note = "Hệ thống tự động hủy do hết thời gian thanh toán",
                CreatedAt = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Auto-cancelled expired VNPay order. OrderCode={OrderCode}, CreatedAt={CreatedAt}",
                order.OrderCode, order.CreatedAt);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cancelled {Count} expired VNPay order(s).", expiredOrders.Count);
    }
}
