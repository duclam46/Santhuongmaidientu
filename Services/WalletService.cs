using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Services;

/// <summary>
/// Service xử lý các nghiệp vụ ví số dư nội bộ (Store Credit).
/// Dùng transaction-level locking để tránh Race Condition.
/// </summary>
public sealed class WalletService
{
    private readonly DataContext _db;
    private readonly ILogger<WalletService> _logger;

    public WalletService(DataContext db, ILogger<WalletService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── Hằng số loại giao dịch ────────────────────────────────────────────────
    public const string TypeDeposit  = "DEPOSIT";   // Nạp tiền qua VNPAY
    public const string TypePayment  = "PAYMENT";   // Thanh toán đơn hàng
    public const string TypeRefund   = "REFUND";    // Hoàn tiền khi huỷ đơn
    public const string TypeCashback = "CASHBACK";  // Tặng thưởng

    // ─── Lấy hoặc tạo ví ────────────────────────────────────────────────────────

    /// <summary>Lấy ví của user. Nếu chưa có thì tạo mới (Balance = 0).</summary>
    public async Task<Wallet> GetOrCreateWalletAsync(int userId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            wallet = new Wallet
            {
                UserId = userId,
                Balance = 0m,
                Status = 1,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Wallets.Add(wallet);
            await _db.SaveChangesAsync(ct);
        }
        return wallet;
    }

    /// <summary>Lấy số dư hiện tại của user (0 nếu chưa có ví).</summary>
    public async Task<decimal> GetBalanceAsync(int userId, CancellationToken ct = default)
    {
        var balance = await _db.Wallets
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => (decimal?)w.Balance)
            .FirstOrDefaultAsync(ct);
        return balance ?? 0m;
    }

    // ─── Nạp tiền ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Nghiệp vụ 1: Nạp tiền vào ví sau khi VNPAY IPN xác nhận thành công.
    /// Sử dụng DB transaction để đảm bảo atomic.
    /// </summary>
    public async Task<(bool Success, string Message)> DepositAsync(
        int userId,
        decimal amount,
        string vnpayTransactionNo,
        string description,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            return (false, "Số tiền nạp không hợp lệ.");

        // Kiểm tra trùng lặp IPN (idempotency)
        var alreadyProcessed = await _db.WalletTransactions
            .AnyAsync(t => t.ReferenceId == vnpayTransactionNo && t.TransactionType == TypeDeposit, ct);

        if (alreadyProcessed)
        {
            _logger.LogWarning("Wallet deposit already processed. VNPayTxn={VNPayTxn}", vnpayTransactionNo);
            return (true, "Giao dịch đã được xử lý trước đó.");
        }

        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var wallet = await GetOrCreateWalletAsync(userId, ct);

            if (wallet.Status != 1)
                return (false, "Ví đang bị khoá, không thể nạp tiền.");

            wallet.Balance += amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            _db.WalletTransactions.Add(new WalletTransaction
            {
                WalletId      = wallet.WalletId,
                Amount        = amount,
                TransactionType = TypeDeposit,
                ReferenceId   = vnpayTransactionNo,
                Description   = description,
                BalanceAfter  = wallet.Balance,
                CreatedAt     = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            _logger.LogInformation("Wallet deposit success. UserId={UserId} Amount={Amount} NewBalance={Balance}",
                userId, amount, wallet.Balance);
            return (true, "Nạp tiền thành công.");
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(ct);
            _logger.LogError(ex, "Wallet deposit failed. UserId={UserId}", userId);
            return (false, "Nạp tiền thất bại. Vui lòng thử lại.");
        }
    }

    // ─── Thanh toán bằng ví ──────────────────────────────────────────────────────

    /// <summary>
    /// Nghiệp vụ 2: Trừ tiền ví để thanh toán đơn hàng.
    /// Dùng RepeatableRead để chống Race Condition.
    /// </summary>
    public async Task<(bool Success, string Message)> PayWithWalletAsync(
        int userId,
        decimal amount,
        string orderCode,
        string description,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            return (false, "Số tiền thanh toán không hợp lệ.");

        await using var dbTx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, ct);
        try
        {
            // Lock row để đọc số dư thực tế, tránh race condition
            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId, ct);

            if (wallet is null)
                return (false, "Tài khoản chưa có ví. Vui lòng nạp tiền trước.");

            if (wallet.Status != 1)
                return (false, "Ví đang bị khoá.");

            if (wallet.Balance < amount)
                return (false, $"Số dư không đủ. Hiện có: {wallet.Balance:N0}đ, cần: {amount:N0}đ.");

            wallet.Balance -= amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            _db.WalletTransactions.Add(new WalletTransaction
            {
                WalletId        = wallet.WalletId,
                Amount          = -amount,
                TransactionType = TypePayment,
                ReferenceId     = orderCode,
                Description     = description,
                BalanceAfter    = wallet.Balance,
                CreatedAt       = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            _logger.LogInformation("Wallet payment success. UserId={UserId} Amount={Amount} NewBalance={Balance} Order={Order}",
                userId, amount, wallet.Balance, orderCode);
            return (true, "Thanh toán bằng số dư thành công.");
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(ct);
            _logger.LogError(ex, "Wallet payment failed. UserId={UserId} Order={Order}", userId, orderCode);
            return (false, "Thanh toán thất bại. Vui lòng thử lại.");
        }
    }

    // ─── Hoàn tiền ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Nghiệp vụ 3: Hoàn tiền vào ví khi đơn hàng bị huỷ.
    /// Chỉ hoàn nếu đơn đã thanh toán bằng ví (PaymentMethod = "Wallet").
    /// </summary>
    public async Task<(bool Success, string Message)> RefundAsync(
        int userId,
        decimal amount,
        string orderCode,
        string description,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            return (false, "Số tiền hoàn không hợp lệ.");

        // Idempotency: không hoàn 2 lần cho cùng 1 đơn
        var alreadyRefunded = await _db.WalletTransactions
            .AnyAsync(t => t.ReferenceId == orderCode && t.TransactionType == TypeRefund, ct);

        if (alreadyRefunded)
        {
            _logger.LogWarning("Wallet refund already done for Order={Order}", orderCode);
            return (true, "Đơn hàng đã được hoàn tiền trước đó.");
        }

        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var wallet = await GetOrCreateWalletAsync(userId, ct);

            wallet.Balance += amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            _db.WalletTransactions.Add(new WalletTransaction
            {
                WalletId        = wallet.WalletId,
                Amount          = amount,
                TransactionType = TypeRefund,
                ReferenceId     = orderCode,
                Description     = description,
                BalanceAfter    = wallet.Balance,
                CreatedAt       = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            _logger.LogInformation("Wallet refund success. UserId={UserId} Amount={Amount} Order={Order}",
                userId, amount, orderCode);
            return (true, "Hoàn tiền vào số dư thành công.");
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(ct);
            _logger.LogError(ex, "Wallet refund failed. UserId={UserId} Order={Order}", userId, orderCode);
            return (false, "Hoàn tiền thất bại. Vui lòng liên hệ hỗ trợ.");
        }
    }

    // ─── Lịch sử giao dịch ────────────────────────────────────────────────────

    /// <summary>Lấy lịch sử giao dịch phân trang.</summary>
    public async Task<(List<WalletTransaction> Items, int TotalCount)> GetTransactionHistoryAsync(
        int userId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var wallet = await _db.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null) return (new(), 0);

        var query = _db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.WalletId == wallet.WalletId)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    // ─── Điểm danh nhận xu hàng ngày ──────────────────────────────────────────

    /// <summary>
    /// Lấy trạng thái điểm danh hiện tại của người dùng.
    /// </summary>
    public async Task<WalletAttendanceState> GetAttendanceStateAsync(int userId, CancellationToken ct = default)
    {
        var wallet = await GetOrCreateWalletAsync(userId, ct);
        
        string todayStr = DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd");
        string yesterdayStr = DateTime.UtcNow.AddHours(7).AddDays(-1).ToString("yyyyMMdd");
        
        bool hasCheckedInToday = await _db.WalletTransactions.AnyAsync(
            t => t.WalletId == wallet.WalletId && t.ReferenceId == $"ATTENDANCE-{userId}-{todayStr}", ct);
            
        int streak = 0;
        if (hasCheckedInToday)
        {
            streak = 1;
            for (int i = 1; i < 7; i++)
            {
                string dateStr = DateTime.UtcNow.AddHours(7).AddDays(-i).ToString("yyyyMMdd");
                bool hasCheckedIn = await _db.WalletTransactions.AnyAsync(
                    t => t.WalletId == wallet.WalletId && t.ReferenceId == $"ATTENDANCE-{userId}-{dateStr}", ct);
                if (hasCheckedIn) streak++;
                else break;
            }
        }
        else
        {
            bool hasCheckedInYesterday = await _db.WalletTransactions.AnyAsync(
                t => t.WalletId == wallet.WalletId && t.ReferenceId == $"ATTENDANCE-{userId}-{yesterdayStr}", ct);
                
            if (hasCheckedInYesterday)
            {
                streak = 1;
                for (int i = 2; i <= 7; i++)
                {
                    string dateStr = DateTime.UtcNow.AddHours(7).AddDays(-i).ToString("yyyyMMdd");
                    bool hasCheckedIn = await _db.WalletTransactions.AnyAsync(
                        t => t.WalletId == wallet.WalletId && t.ReferenceId == $"ATTENDANCE-{userId}-{dateStr}", ct);
                    if (hasCheckedIn) streak++;
                    else break;
                }
                
                // Nếu chuỗi đạt 7 ngày hôm qua, thì hôm nay chưa điểm danh sẽ reset bắt đầu chuỗi mới
                if (streak >= 7)
                {
                    streak = 0;
                }
            }
            else
            {
                streak = 0;
            }
        }

        // Cấu hình mức thưởng cho 7 ngày điểm danh (VND): Ngày 1-3 nhận 200đ, Ngày 4-6 nhận 300đ, Ngày 7 nhận 500đ
        var rewardConfig = new decimal[] { 200m, 200m, 200m, 300m, 300m, 300m, 500m };
        
        int nextDayIndex = hasCheckedInToday ? (streak % 7) : (streak + 1) % 8;
        if (nextDayIndex == 0) nextDayIndex = 1;
        
        decimal nextReward = rewardConfig[nextDayIndex - 1];

        var dailyRewards = new List<WalletDailyRewardItem>();
        for (int day = 1; day <= 7; day++)
        {
            decimal rewardVal = rewardConfig[day - 1];
            string status = "locked"; // locked, claimable, claimed
            
            if (hasCheckedInToday)
            {
                if (day <= streak) status = "claimed";
                else if (day == streak + 1 && streak < 7) status = "claimable";
            }
            else
            {
                if (day <= streak) status = "claimed";
                else if (day == streak + 1) status = "claimable";
            }

            dailyRewards.Add(new WalletDailyRewardItem
            {
                DayNumber = day,
                RewardAmount = rewardVal,
                RewardFormatted = $"{rewardVal:N0}",
                Status = status
            });
        }

        return new WalletAttendanceState
        {
            HasCheckedInToday = hasCheckedInToday,
            StreakCount = streak,
            NextRewardAmount = nextReward,
            NextRewardFormatted = $"{nextReward:N0}",
            DailyRewards = dailyRewards
        };
    }

    /// <summary>
    /// Thực hiện điểm danh nhận xu hàng ngày cho người dùng.
    /// </summary>
    public async Task<(bool Success, string Message, decimal RewardClaimed, WalletAttendanceState NewState)> CheckInAsync(
        int userId, CancellationToken ct = default)
    {
        var wallet = await GetOrCreateWalletAsync(userId, ct);
        if (wallet.Status != 1)
        {
            return (false, "Ví đang bị khoá.", 0m, null!);
        }

        string todayStr = DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd");
        string refId = $"ATTENDANCE-{userId}-{todayStr}";

        // 1. Idempotency check
        var alreadyCheckedIn = await _db.WalletTransactions.AnyAsync(
            t => t.WalletId == wallet.WalletId && t.ReferenceId == refId, ct);
            
        if (alreadyCheckedIn)
        {
            var currentState = await GetAttendanceStateAsync(userId, ct);
            return (false, "Hôm nay bạn đã điểm danh nhận xu rồi!", 0m, currentState);
        }

        // 2. Tính toán streak mới
        string yesterdayStr = DateTime.UtcNow.AddHours(7).AddDays(-1).ToString("yyyyMMdd");
        bool hasCheckedInYesterday = await _db.WalletTransactions.AnyAsync(
            t => t.WalletId == wallet.WalletId && t.ReferenceId == $"ATTENDANCE-{userId}-{yesterdayStr}", ct);
            
        int oldStreak = 0;
        if (hasCheckedInYesterday)
        {
            oldStreak = 1;
            for (int i = 2; i <= 7; i++)
            {
                string dateStr = DateTime.UtcNow.AddHours(7).AddDays(-i).ToString("yyyyMMdd");
                bool hasCheckedIn = await _db.WalletTransactions.AnyAsync(
                    t => t.WalletId == wallet.WalletId && t.ReferenceId == $"ATTENDANCE-{userId}-{dateStr}", ct);
                if (hasCheckedIn) oldStreak++;
                else break;
            }
            
            // Nếu đã nhận đủ 7 ngày liên tục, streak mới sẽ quay về ngày 1
            if (oldStreak >= 7)
            {
                oldStreak = 0;
            }
        }

        int newStreak = oldStreak + 1;
        var rewardConfig = new decimal[] { 200m, 200m, 200m, 300m, 300m, 300m, 500m };
        decimal rewardAmount = rewardConfig[newStreak - 1];

        // 3. Thực thi lưu trữ transaction
        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            wallet.Balance += rewardAmount;
            wallet.UpdatedAt = DateTime.UtcNow;

            _db.WalletTransactions.Add(new WalletTransaction
            {
                WalletId        = wallet.WalletId,
                Amount          = rewardAmount,
                TransactionType = TypeCashback,
                ReferenceId     = refId,
                Description     = $"Điểm danh nhận xu: Ngày {newStreak}",
                BalanceAfter    = wallet.Balance,
                CreatedAt       = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            _logger.LogInformation("Attendance checkin success. UserId={UserId} Day={Day} Reward={Reward}", 
                userId, newStreak, rewardAmount);

            var newState = await GetAttendanceStateAsync(userId, ct);
            return (true, $"Điểm danh thành công! Bạn nhận được +{rewardAmount:N0}đ xu tích luỹ.", rewardAmount, newState);
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(ct);
            _logger.LogError(ex, "Attendance checkin failed. UserId={UserId}", userId);
            return (false, "Điểm danh thất bại. Vui lòng thử lại sau.", 0m, null!);
        }
    }
}

// ─── Định nghĩa các Class hỗ trợ điểm danh ──────────────────────────────────────

public class WalletAttendanceState
{
    public bool HasCheckedInToday { get; set; }
    public int StreakCount { get; set; }
    public decimal NextRewardAmount { get; set; }
    public string NextRewardFormatted { get; set; } = "0";
    public List<WalletDailyRewardItem> DailyRewards { get; set; } = new();
}

public class WalletDailyRewardItem
{
    public int DayNumber { get; set; }
    public decimal RewardAmount { get; set; }
    public string RewardFormatted { get; set; } = "0";
    public string Status { get; set; } = "locked"; // claimed, claimable, locked
}
