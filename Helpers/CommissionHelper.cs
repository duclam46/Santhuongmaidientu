using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace DOAN_BANHANG.Helpers;

public static class CommissionHelper
{
    public const decimal MallSurchargePercent = 2.0m; // Phương án A: Shop Mall cộng thêm +2% phí dịch vụ cố định Mall

    public sealed record CategoryPolicyResult(
        decimal CommissionRatePercent,
        decimal? MaxCommissionAmount,
        bool HasSizeGuide,
        bool IsReturnableDefault,
        string? InheritedFromCategoryName);

    public static decimal GetEffectiveCommissionRate(decimal baseCategoryRatePercent, bool isMallShop)
        => isMallShop ? baseCategoryRatePercent + MallSurchargePercent : baseCategoryRatePercent;

    public static async Task<decimal> GetDefaultCommissionRatePercentAsync(DataContext db, CancellationToken ct = default)
    {
        var setting = await db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == "Fee_Commission_Default", ct);

        if (setting != null && decimal.TryParse(setting.Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            return val <= 1m && val > 0m ? val * 100m : val;
        }

        return 10m; // Default 10%
    }

    public static CategoryPolicyResult ResolveCategoryPolicy(
        int categoryId,
        IReadOnlyDictionary<int, Category> categoryMap,
        decimal defaultCommissionPercent = 10m)
    {
        if (!categoryMap.TryGetValue(categoryId, out var current))
        {
            return new CategoryPolicyResult(defaultCommissionPercent, null, false, true, null);
        }

        decimal? rate = null;
        decimal? maxCap = null;
        string? inheritedFrom = null;
        bool hasSizeGuide = current.HasSizeGuide;
        bool isReturnable = current.IsReturnableDefault;

        var cursor = current;
        int depth = 0;
        while (cursor != null && depth < 10)
        {
            if (!rate.HasValue && cursor.CommissionRate.HasValue)
            {
                rate = cursor.CommissionRate.Value;
                maxCap = cursor.MaxCommissionAmount;
                if (cursor.Id != current.Id)
                {
                    inheritedFrom = cursor.Name;
                }
            }

            if (cursor.HasSizeGuide)
            {
                hasSizeGuide = true;
            }

            if (!cursor.IsReturnableDefault)
            {
                isReturnable = false;
            }

            if (cursor.ParentId.HasValue && categoryMap.TryGetValue(cursor.ParentId.Value, out var parent))
            {
                cursor = parent;
            }
            else
            {
                break;
            }
            depth++;
        }

        return new CategoryPolicyResult(
            CommissionRatePercent: rate ?? defaultCommissionPercent,
            MaxCommissionAmount: maxCap,
            HasSizeGuide: hasSizeGuide,
            IsReturnableDefault: isReturnable,
            InheritedFromCategoryName: inheritedFrom);
    }

    public static decimal CalculateItemCommission(
        decimal unitPrice,
        int quantity,
        decimal commissionRatePercent,
        decimal? maxCommissionAmountPerItem,
        bool isMallShop = false)
    {
        var effectiveRate = GetEffectiveCommissionRate(commissionRatePercent, isMallShop);
        if (unitPrice <= 0m || quantity <= 0 || effectiveRate <= 0m)
            return 0m;

        decimal perUnitComm = unitPrice * (effectiveRate / 100m);
        if (maxCommissionAmountPerItem.HasValue && maxCommissionAmountPerItem.Value > 0m && perUnitComm > maxCommissionAmountPerItem.Value)
        {
            perUnitComm = maxCommissionAmountPerItem.Value;
        }

        return Math.Round(perUnitComm * quantity, 0, MidpointRounding.AwayFromZero);
    }
}
