using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Helpers;

public static class ShopSlugHelper
{
    public static async Task<string> GenerateUniqueShopSlugAsync(DataContext db, string shopName, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(shopName);
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "doi-tac";
        if (baseSlug.Length > 180) baseSlug = baseSlug[..180];

        var candidate = baseSlug;
        var i = 1;

        while (await db.UserProfiles.AsNoTracking().AnyAsync(p => p.ShopSlug == candidate, cancellationToken))
        {
            candidate = $"{baseSlug}-{i}";
            i++;
            if (candidate.Length > 200)
            {
                candidate = candidate[..200];
            }
        }

        return candidate;
    }

    public static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        var noDiacritics = sb
            .ToString()
            .Normalize(NormalizationForm.FormC);

        var slug = Regex.Replace(noDiacritics, @"\s+", "-", RegexOptions.Compiled);
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty, RegexOptions.Compiled);
        slug = Regex.Replace(slug, @"\-+", "-", RegexOptions.Compiled).Trim('-');

        return slug;
    }
}
