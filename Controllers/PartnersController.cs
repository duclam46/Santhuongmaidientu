using System.Diagnostics;
using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOAN_BANHANG.Controllers;

[Route("doi-tac")]
public class PartnersController : Controller
{
    private readonly DataContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly GhnClient _ghn;
    private readonly ILogger<PartnersController> _logger;

    public PartnersController(DataContext db, IPasswordHasher<Account> passwordHasher, GhnClient ghn, ILogger<PartnersController> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _ghn = ghn;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (HasQueryFlag("ghnTest"))
        {
            await RunGhnSmokeTestAsync(cancellationToken);
        }

        var vm = new PartnerRegisterVm
        {
            SuccessMessage = TempData["PartnerSuccess"] as string
        };

        if (User?.Identity?.IsAuthenticated ?? false)
        {
            var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idRaw, out var accountId) && accountId > 0)
            {
                var account = await _db.Accounts
                    .AsNoTracking()
                    .Include(a => a.Profile)
                    .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

                if (account is not null)
                {
                    if (string.Equals(account.Role, "Shop", StringComparison.OrdinalIgnoreCase))
                    {
                        vm.HideForm = true;
                        vm.HideFormMessage = "Tài khoản của bạn đã là nhà bán hàng.";
                        if (string.IsNullOrWhiteSpace(vm.SuccessMessage))
                        {
                            vm.SuccessMessage = vm.HideFormMessage;
                        }
                    }
                    else
                    {
                        var hasPending = await _db.PartnerApplications
                            .AsNoTracking()
                            .AnyAsync(a => a.AccountId == account.Id && a.Status == "Pending", cancellationToken);

                        if (hasPending)
                        {
                            vm.HideForm = true;
                            vm.HideFormMessage = "Bạn đã nộp đơn đăng ký đối tác và đang chờ xét duyệt.";
                            if (string.IsNullOrWhiteSpace(vm.SuccessMessage))
                            {
                                vm.SuccessMessage = vm.HideFormMessage;
                            }
                        }
                    }

                    vm.Email = account.Email;
                    vm.FullName = account.Profile?.FullName;
                    vm.PhoneNumber = account.Profile?.PhoneNumber;
                    vm.ShopName = account.Profile?.ShopName;
                    vm.ShopDescription = account.Profile?.ShopDescription;

                    var shopAddr = await _db.UserAddresses
                        .AsNoTracking()
                        .Where(a => a.AccountId == account.Id && a.AddressType == AddressType.Shop)
                        .OrderByDescending(a => a.IsDefault)
                        .ThenByDescending(a => a.UpdatedAt)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (shopAddr is not null)
                    {
                        vm.ProvinceId = shopAddr.ProvinceId;
                        vm.ProvinceName = shopAddr.ProvinceName;
                        vm.DistrictId = shopAddr.DistrictId;
                        vm.DistrictName = shopAddr.DistrictName;
                        vm.WardCode = shopAddr.WardCode;
                        vm.WardName = shopAddr.WardName;
                        vm.StreetAddress = shopAddr.StreetAddress;
                        vm.Address = ComposeAddress(shopAddr);
                    }
                    else
                    {
                        vm.Address = account.Profile?.Address;
                    }
                }
                else
                {
                    vm.Email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
                }
            }
            else
            {
                vm.Email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
            }
        }

        return View(vm);
    }

    private static string ComposeAddress(UserAddress a)
        => ComposeAddress(a.StreetAddress, a.WardName, a.DistrictName, a.ProvinceName);

    private static string ComposeAddress(string? streetAddress, string? wardName, string? districtName, string? provinceName)
        => PartnerShopAddressHelper.ComposeAddress(streetAddress, wardName, districtName, provinceName);

    private bool HasQueryFlag(string name)
    {
        if (Request?.Query is null) return false;
        if (!Request.Query.ContainsKey(name)) return false;

        var raw = Request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var v = raw.Trim().ToLowerInvariant();
        return v is "1" or "true" or "yes" or "on";
    }

    private async Task RunGhnSmokeTestAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var ua = Request.Headers.UserAgent.ToString();
        var referer = Request.Headers.Referer.ToString();

        _logger.LogInformation("[GHN][Partners] smoke-test START ip={IP} ua={UA} referer={Referer}", ip, ua, referer);

        try
        {
            var provinces = await _ghn.GetProvincesAsync(cancellationToken);
            _logger.LogInformation("[GHN][Partners] provinces ok count={Count}", provinces.Count);

            if (provinces.Count == 0)
            {
                _logger.LogWarning("[GHN][Partners] smoke-test STOP (no provinces) elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
                return;
            }

            // Some provinces/districts (esp. in dev/sandbox) may return empty lists.
            // Search within reasonable limits to produce a decisive PASS/FAIL.
            const int maxProvincesToTry = 20;
            const int maxDistrictsToTryPerProvince = 30;

            foreach (var p in provinces.Where(p => p.ProvinceId > 0).Take(maxProvincesToTry))
            {
                var ds = await _ghn.GetDistrictsAsync(p.ProvinceId, cancellationToken);
                _logger.LogInformation("[GHN][Partners] districts ok provinceId={ProvinceId} count={Count}", p.ProvinceId, ds.Count);
                if (ds.Count == 0) continue;

                foreach (var d in ds.Where(d => d.DistrictId > 0).Take(maxDistrictsToTryPerProvince))
                {
                    var ws = await _ghn.GetWardsAsync(d.DistrictId, cancellationToken);
                    _logger.LogInformation("[GHN][Partners] wards ok districtId={DistrictId} count={Count}", d.DistrictId, ws.Count);
                    if (ws.Count == 0) continue;

                    var ward = ws.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.WardCode)) ?? ws.FirstOrDefault();
                    _logger.LogInformation(
                        "[GHN][Partners] smoke-test OK sample={Province}-{District}-{Ward} elapsedMs={ElapsedMs}",
                        p.ProvinceName,
                        d.DistrictName,
                        ward?.WardName ?? "?",
                        sw.ElapsedMilliseconds);
                    return;
                }
            }

            _logger.LogWarning(
                "[GHN][Partners] smoke-test STOP (no wards found in first {ProvinceCount} provinces x {DistrictCount} districts) elapsedMs={ElapsedMs}",
                maxProvincesToTry,
                maxDistrictsToTryPerProvince,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GHN][Partners] smoke-test FAILED elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        }
    }

    private static bool TryNormalizePartnerShopAddress(
        string? fullName,
        string? phoneNumber,
        int provinceId,
        string? provinceName,
        int districtId,
        string? districtName,
        string? wardCode,
        string? wardName,
        string? streetAddress,
        out string error,
        out UserAddress normalized)
        => PartnerShopAddressHelper.TryNormalizeShopAddress(
            fullName,
            phoneNumber,
            provinceId,
            provinceName,
            districtId,
            districtName,
            wardCode,
            wardName,
            streetAddress,
            out error,
            out normalized);

    [HttpPost("dang-ky")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([FromForm] PartnerRegisterVm model, CancellationToken cancellationToken)
    {
        var isSignedIn = User?.Identity?.IsAuthenticated ?? false;

        var fullName = string.IsNullOrWhiteSpace(model.FullName) ? null : model.FullName.Trim();
        if (fullName is not null && fullName.Length > 150) fullName = fullName[..150];

        var email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
        if (email is not null && email.Length > 255) email = email[..255];

        if (isSignedIn && string.IsNullOrWhiteSpace(email))
        {
            email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        }

        var phoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        if (phoneNumber is not null && phoneNumber.Length > 20) phoneNumber = phoneNumber[..20];

        var shopName = string.IsNullOrWhiteSpace(model.ShopName) ? null : model.ShopName.Trim();
        if (shopName is not null && shopName.Length > 200) shopName = shopName[..200];

        var shopDescription = string.IsNullOrWhiteSpace(model.ShopDescription) ? null : model.ShopDescription.Trim();

        var provinceId = model.ProvinceId;
        var districtId = model.DistrictId;

        var provinceName = string.IsNullOrWhiteSpace(model.ProvinceName) ? null : model.ProvinceName.Trim();
        if (provinceName is not null && provinceName.Length > 100) provinceName = provinceName[..100];

        var districtName = string.IsNullOrWhiteSpace(model.DistrictName) ? null : model.DistrictName.Trim();
        if (districtName is not null && districtName.Length > 100) districtName = districtName[..100];

        var wardCode = string.IsNullOrWhiteSpace(model.WardCode) ? null : model.WardCode.Trim();
        if (wardCode is not null && wardCode.Length > 50) wardCode = wardCode[..50];

        var wardName = string.IsNullOrWhiteSpace(model.WardName) ? null : model.WardName.Trim();
        if (wardName is not null && wardName.Length > 100) wardName = wardName[..100];

        var streetAddress = string.IsNullOrWhiteSpace(model.StreetAddress) ? null : model.StreetAddress.Trim();
        if (streetAddress is not null && streetAddress.Length > 500) streetAddress = streetAddress[..500];

        var address = ComposeAddress(streetAddress, wardName, districtName, provinceName);
        if (address.Length > 500) address = address[..500];

        model.FullName = fullName;
        model.Email = email;
        model.PhoneNumber = phoneNumber;
        model.ShopName = shopName;
        model.ShopDescription = shopDescription;
        model.Address = address;
        model.ProvinceId = provinceId;
        model.ProvinceName = provinceName;
        model.DistrictId = districtId;
        model.DistrictName = districtName;
        model.WardCode = wardCode;
        model.WardName = wardName;
        model.StreetAddress = streetAddress;

        if (string.IsNullOrWhiteSpace(shopName))
        {
            ModelState.AddModelError(nameof(model.ShopName), "Vui lòng nhập tên cửa hàng/đối tác.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Email), "Vui lòng nhập email hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Vui lòng nhập số điện thoại.");
        }

        if (provinceId <= 0 || string.IsNullOrWhiteSpace(provinceName))
        {
            ModelState.AddModelError(nameof(model.ProvinceId), "Vui lòng chọn Tỉnh/Thành phố.");
        }

        if (districtId <= 0 || string.IsNullOrWhiteSpace(districtName))
        {
            ModelState.AddModelError(nameof(model.DistrictId), "Vui lòng chọn Quận/Huyện.");
        }

        if (string.IsNullOrWhiteSpace(wardCode) || string.IsNullOrWhiteSpace(wardName))
        {
            ModelState.AddModelError(nameof(model.WardCode), "Vui lòng chọn Phường/Xã.");
        }

        if (string.IsNullOrWhiteSpace(streetAddress))
        {
            ModelState.AddModelError(nameof(model.StreetAddress), "Vui lòng nhập địa chỉ (số nhà, tên đường).");
        }

        if (!isSignedIn)
        {
            var password = model.Password ?? string.Empty;
            var confirm = model.ConfirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError(nameof(model.Password), "Vui lòng nhập mật khẩu.");
            }

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.ConfirmPassword), "Mật khẩu xác nhận không khớp.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        // Ensure the submitted GHN-style address is complete.
        if (!TryNormalizePartnerShopAddress(fullName, phoneNumber, provinceId, provinceName, districtId, districtName, wardCode, wardName, streetAddress,
            out var addrError, out _))
        {
            ModelState.AddModelError(string.Empty, addrError);
            return View("Index", model);
        }

        if (isSignedIn)
        {
            var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                ModelState.AddModelError(string.Empty, "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");
                return View("Index", model);
            }

            var account = await _db.Accounts
                .Include(a => a.Profile)
                .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

            if (account is null)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                ModelState.AddModelError(string.Empty, "Tài khoản không tồn tại. Vui lòng đăng nhập lại.");
                return View("Index", model);
            }

            if (string.Equals(account.Role, "Shop", StringComparison.OrdinalIgnoreCase))
            {
                TempData["PartnerSuccess"] = "Tài khoản của bạn đã là nhà bán hàng.";
                return RedirectToAction(nameof(Index));
            }

            var currentEmail = account.Email;
            if (!string.IsNullOrWhiteSpace(email)
                && !string.Equals(email, currentEmail, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Email), "Email không khớp với tài khoản đang đăng nhập.");
                model.Email = currentEmail;
                return View("Index", model);
            }

            var hasPending = await _db.PartnerApplications
                .AsNoTracking()
                .AnyAsync(a => a.AccountId == account.Id && a.Status == "Pending", cancellationToken);

            if (hasPending)
            {
                TempData["PartnerSuccess"] = "Bạn đã gửi phiếu đăng ký đối tác và đang chờ xét duyệt.";
                return RedirectToAction(nameof(Index));
            }

            _db.PartnerApplications.Add(new PartnerApplication
            {
                AccountId = account.Id,
                FullName = fullName,
                Email = account.Email,
                PhoneNumber = phoneNumber,
                ShopName = shopName,
                ShopDescription = shopDescription,
                Address = address,
                ProvinceId = provinceId,
                ProvinceName = provinceName,
                DistrictId = districtId,
                DistrictName = districtName,
                WardCode = wardCode,
                WardName = wardName,
                StreetAddress = streetAddress,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);

            TempData["PartnerSuccess"] = "Đã gửi phiếu đăng ký đối tác. Vui lòng chờ Admin xét duyệt.";
            return RedirectToAction(nameof(Index));
        }

        // Not signed in: create a new Customer account, then submit partner application for approval.
        var existing = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email && !a.IsDeleted, cancellationToken);

        if (existing is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "Email này đã được đăng ký. Hãy đăng nhập rồi đăng ký đối tác bằng tài khoản đó.");
            return View("Index", model);
        }

        var accountToCreate = new Account
        {
            Email = email!,
            PasswordHash = null,
            AuthProvider = null,
            AuthProviderUserId = null,
            Role = "Customer",
            Status = "Active",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };
        accountToCreate.PasswordHash = _passwordHasher.HashPassword(accountToCreate, model.Password ?? string.Empty);

        _db.Accounts.Add(accountToCreate);
        await _db.SaveChangesAsync(cancellationToken);

        var profile = new UserProfile
        {
            AccountId = accountToCreate.Id,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.UserProfiles.Add(profile);

        _db.PartnerApplications.Add(new PartnerApplication
        {
            AccountId = accountToCreate.Id,
            FullName = fullName,
            Email = email!,
            PhoneNumber = phoneNumber,
            ShopName = shopName,
            ShopDescription = shopDescription,
            Address = address,
            ProvinceId = provinceId,
            ProvinceName = provinceName,
            DistrictId = districtId,
            DistrictName = districtName,
            WardCode = wardCode,
            WardName = wardName,
            StreetAddress = streetAddress,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        await RefreshSignInAsync(accountToCreate);

        TempData["PartnerSuccess"] = "Đã gửi phiếu đăng ký đối tác. Vui lòng chờ Admin xét duyệt.";
        return RedirectToAction(nameof(Index));
    }

    private async Task RefreshSignInAsync(Account account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Email, account.Email),
            new(ClaimTypes.Name, account.Email),
            new(ClaimTypes.Role, account.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });
    }

}
