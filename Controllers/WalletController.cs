using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

[Route("my-wallet")]
public sealed class WalletController : Controller
{
    private readonly DataContext   _db;
    private readonly WalletService _wallet;
    private readonly VnPayService  _vnPay;
    private readonly ILogger<WalletController> _logger;

    // Giới hạn nạp tiền: tối thiểu 10k, tối đa 50 triệu
    private const decimal MinTopUp = 10_000m;
    private const decimal MaxTopUp = 50_000_000m;

    // Prefix phân biệt giao dịch nạp ví vs thanh toán đơn hàng trong VNPAY TxnRef
    private const string TopUpPrefix = "WALLET-";

    public WalletController(DataContext db, WalletService wallet, VnPayService vnPay,
        ILogger<WalletController> logger)
    {
        _db     = db;
        _wallet = wallet;
        _vnPay  = vnPay;
        _logger = logger;
    }

    private int? GetAccountId()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return null;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /my-wallet  →  Trang chủ ví
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Redirect("/auth/login");

        var balance = await _wallet.GetBalanceAsync(accountId.Value, ct);
        var (txns, total) = await _wallet.GetTransactionHistoryAsync(accountId.Value, page, 15, ct);

        var vm = new WalletIndexVm
        {
            Balance      = balance,
            Transactions = txns,
            Page         = page,
            PageSize     = 15,
            TotalCount   = total
        };

        return View(vm);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST /my-wallet/topup  →  Tạo giao dịch nạp tiền → redirect VNPAY
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost("topup")]
    [ValidateAntiForgeryToken]
    public IActionResult TopUp([FromForm] decimal amount, CancellationToken ct)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Redirect("/auth/login");

        if (amount < MinTopUp || amount > MaxTopUp)
        {
            TempData["WalletError"] = $"Số tiền nạp phải từ {MinTopUp:N0}đ đến {MaxTopUp:N0}đ.";
            return Redirect("/my-wallet");
        }

        // TxnRef = WALLET-{accountId}-{timestamp} – unique và dễ parse
        var txnRef  = $"{TopUpPrefix}{accountId.Value}-{DateTime.Now:yyyyMMddHHmmss}";
        var orderInfo = $"Nạp số dư tài khoản #{accountId.Value}";

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        
        var request = HttpContext.Request;
        var scheme = request.Scheme;
        var host = request.Host.ToUriComponent();
        var callbackUrl = $"{scheme}://{host}/my-wallet/topup-return";

        var payUrl = _vnPay.CreatePaymentUrl(txnRef, amount, orderInfo, ipAddress, customReturnUrl: callbackUrl);

        return Redirect(payUrl);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /my-wallet/topup-return  →  VNPAY Return URL (user được redirect về)
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("topup-return")]
    public async Task<IActionResult> TopUpReturn(CancellationToken ct)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Redirect("/auth/login");

        if (!_vnPay.ValidateSignature(Request.Query))
        {
            TempData["WalletError"] = "Chữ ký VNPAY không hợp lệ.";
            return Redirect("/my-wallet");
        }

        var data = _vnPay.ParseReturnData(Request.Query);

        if (!data.IsSuccess)
        {
            TempData["WalletError"] = $"Nạp tiền thất bại (mã: {data.ResponseCode}). Vui lòng thử lại.";
            return Redirect("/my-wallet");
        }

        // Verify TxnRef thuộc về user đang đăng nhập
        if (!data.TxnRef.StartsWith($"{TopUpPrefix}{accountId.Value}-", StringComparison.Ordinal))
        {
            TempData["WalletError"] = "Giao dịch không hợp lệ.";
            return Redirect("/my-wallet");
        }

        var description = $"Nạp tiền qua VNPAY - Mã GD: {data.TransactionNo}";
        var (ok, msg) = await _wallet.DepositAsync(accountId.Value, data.Amount, data.TransactionNo, description, ct);

        if (ok)
            TempData["WalletSuccess"] = $"Nạp {data.Amount:N0}đ vào số dư thành công!";
        else
            TempData["WalletError"] = msg;

        return Redirect("/my-wallet");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /my-wallet/topup-ipn  →  VNPAY IPN Webhook (server-to-server)
    //     VNPay gọi đây để xác nhận thanh toán — không phụ thuộc vào browser
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("topup-ipn")]
    public async Task<IActionResult> TopUpIpn(CancellationToken ct)
    {
        if (!_vnPay.ValidateSignature(Request.Query))
        {
            _logger.LogWarning("Wallet IPN: invalid signature");
            return Ok(new { RspCode = "97", Message = "Invalid checksum" });
        }

        var data = _vnPay.ParseReturnData(Request.Query);

        if (!data.IsSuccess)
        {
            _logger.LogInformation("Wallet IPN: payment not success. TxnRef={TxnRef} Code={Code}", data.TxnRef, data.ResponseCode);
            return Ok(new { RspCode = "00", Message = "Confirm success (not paid)" });
        }

        // Parse accountId từ TxnRef = "WALLET-{accountId}-{timestamp}"
        var parts = data.TxnRef.Split('-');
        if (parts.Length < 3 || !int.TryParse(parts[1], out var userId))
        {
            _logger.LogWarning("Wallet IPN: cannot parse userId from TxnRef={TxnRef}", data.TxnRef);
            return Ok(new { RspCode = "01", Message = "Invalid TxnRef format" });
        }

        var description = $"Nạp tiền qua VNPAY - Mã GD: {data.TransactionNo}";
        var (ok, msg) = await _wallet.DepositAsync(userId, data.Amount, data.TransactionNo, description, ct);

        _logger.LogInformation("Wallet IPN deposit result: {Ok} {Msg} UserId={UserId} Amount={Amount}",
            ok, msg, userId, data.Amount);

        return Ok(new { RspCode = "00", Message = "Confirm success" });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /my-wallet/balance  →  API trả về số dư (dùng cho AJAX ở checkout)
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance(CancellationToken ct)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue)
            return Unauthorized(new { message = "Unauthorized" });

        var balance = await _wallet.GetBalanceAsync(accountId.Value, ct);
        return Ok(new { balance, balanceFormatted = $"{balance:N0}" });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /my-wallet/attendance  →  Lấy thông tin điểm danh hàng ngày
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("attendance")]
    public async Task<IActionResult> GetAttendance(CancellationToken ct)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue)
            return Unauthorized(new { message = "Bạn cần đăng nhập để điểm danh." });

        var state = await _wallet.GetAttendanceStateAsync(accountId.Value, ct);
        return Ok(state);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST /my-wallet/attendance/claim  →  Thực hiện điểm danh nhận xu
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost("attendance/claim")]
    public async Task<IActionResult> ClaimAttendance(CancellationToken ct)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue)
            return Unauthorized(new { message = "Bạn cần đăng nhập để điểm danh." });

        var (success, msg, reward, state) = await _wallet.CheckInAsync(accountId.Value, ct);
        if (!success)
        {
            return BadRequest(new { message = msg, state });
        }

        return Ok(new { message = msg, reward, rewardFormatted = $"{reward:N0}", state });
    }
}
