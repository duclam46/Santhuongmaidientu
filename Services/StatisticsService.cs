using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DOAN_BANHANG.Services;

public class StatisticsService
{
    private readonly DataContext _context;

    public StatisticsService(DataContext context)
    {
        _context = context;
    }

    public async Task<AdminDashboardViewModel> GetAdminDashboardStatsAsync()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var lastMonthStart = startOfMonth.AddMonths(-1);
        var sixMonthsAgo = now.AddMonths(-6);

        var orders = await _context.Orders
            .Where(o => o.PaymentStatus == "Paid" || o.OrderStatus == "Delivered" || o.OrderStatus == "Completed")
            .ToListAsync();

        var totalRevenue = orders.Sum(o => o.FinalAmount);
        var totalOrders = orders.Count;
        var totalCustomers = await _context.Accounts.CountAsync(a => a.Role == "Customer");
        var totalShops = await _context.Accounts.CountAsync(a => a.Role == "Shop");

        var thisMonthRevenue = orders.Where(o => o.CreatedAt >= startOfMonth).Sum(o => o.FinalAmount);
        var lastMonthRevenue = orders.Where(o => o.CreatedAt >= lastMonthStart && o.CreatedAt < startOfMonth).Sum(o => o.FinalAmount);
        double growthRate = lastMonthRevenue > 0 ? (double)((thisMonthRevenue - lastMonthRevenue) / lastMonthRevenue * 100) : 100;

        var categoryData = await _context.OrderItems
            .Include(oi => oi.Variant)
            .Include(oi => oi.Variant!.Product)
            .Include(oi => oi.Variant!.Product!.Category)
            .Where(oi => oi.Order!.PaymentStatus == "Paid" || oi.Order!.OrderStatus == "Delivered" || oi.Order!.OrderStatus == "Completed")
            .GroupBy(oi => oi.Variant!.Product!.Category!.Name)
            .Select(g => new { Name = g.Key ?? "Uncategorized", Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync();

        var vm = new AdminDashboardViewModel
        {
            TotalRevenue = totalRevenue,
            TotalOrders = totalOrders,
            TotalCustomers = totalCustomers,
            TotalShops = totalShops,
            CustomerCount = totalCustomers,
            ShopCount = totalShops,
            AverageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0,
            RevenueGrowthRate = Math.Round(growthRate, 1),
            CategoryNames = categoryData.Select(c => c.Name).ToList(),
            CategoryRevenue = categoryData.Select(c => c.Revenue).ToList(),
            ActiveUserPercentage = 78,
            OrderCompletionRate = 92,
            PartnerApprovalRate = 85,
            StorageUsagePercentage = 45
        };

        var monthlyRevenue = orders
            .Where(o => o.CreatedAt >= sixMonthsAgo)
            .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(o => o.FinalAmount) })
            .ToList();

        for (int i = 5; i >= 0; i--)
        {
            var date = now.AddMonths(-i);
            vm.RevenueLabels.Add(date.ToString("MMM yyyy"));
            var rev = monthlyRevenue.FirstOrDefault(m => m.Year == date.Year && m.Month == date.Month)?.Revenue ?? 0;
            vm.RevenueValues.Add(rev);
        }

        var topStores = orders
            .GroupBy(o => o.ShopId)
            .Select(g => new { ShopId = g.Key, Revenue = g.Sum(o => o.FinalAmount) })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .ToList();

        var storeIds = topStores.Select(s => s.ShopId).ToList();
        var storesInfo = await _context.Accounts
            .Include(a => a.Profile)
            .Where(a => storeIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Profile?.ShopName ?? a.Profile?.FullName ?? a.Email);

        vm.TopStoreNames = topStores.Select(s => storesInfo.ContainsKey(s.ShopId) ? storesInfo[s.ShopId] : $"Shop {s.ShopId}").ToList();
        vm.TopStoreRevenues = topStores.Select(s => s.Revenue).ToList();

        var statusCounts = await _context.Orders
            .GroupBy(o => o.OrderStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        vm.OrderStatusLabels = statusCounts.Select(s => s.Status).ToList();
        vm.OrderStatusValues = statusCounts.Select(s => s.Count).ToList();

        Random rnd = new Random();
        for (int i = 0; i < 24; i++)
        {
            var val = rnd.Next(100, 500);
            vm.UserTrafficData.Add(new double[] { i, val });
            vm.ServerLoadData.Add(new double[] { i, rnd.Next(20, 60) });
            vm.SystemActivityData.Add(new double[] { i, val / 10.0 });
        }

        return vm;
    }

    public async Task<SellerDashboardViewModel> GetSellerDashboardStatsAsync(int shopId)
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);

        var shop = await _context.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == shopId);
        
        var shopOrders = await _context.Orders
            .Where(o => o.ShopId == shopId)
            .Include(o => o.Items)
                .ThenInclude(oi => oi.Variant)
                    .ThenInclude(v => v!.Product)
                        .ThenInclude(p => p!.Category)
            .ToListAsync();

        // Chỉ tính các đơn hàng đã hoàn tất (Delivered hoặc Completed)
        var successfulOrders = shopOrders.Where(o => o.OrderStatus == "Delivered" || o.OrderStatus == "Completed").ToList();
        var returnedOrders = shopOrders.Where(o => o.OrderStatus == "Cancelled" || o.OrderStatus == "Returned").ToList();
        var totalRevenue = successfulOrders.Sum(o => o.SubTotal);
        var totalOrders = shopOrders.Count;
        var totalProducts = await _context.Products.CountAsync(p => p.ShopId == shopId);

        // Tính toán lợi nhuận thực tế từ cấu hình tài chính hệ thống
        var settings = await _context.SystemSettings.ToDictionaryAsync(s => s.Id, s => s.Value);
        decimal GetSettingPercent(string id, decimal defaultValue)
        {
            if (settings.TryGetValue(id, out var valStr) && decimal.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                return val / 100m;
            }
            return defaultValue / 100m;
        }

        var vatRate = GetSettingPercent("Tax_VAT_Default", 8m);
        var sellerTaxRate = GetSettingPercent("Tax_Seller_Rate", 1.5m);
        var commFashionRate = GetSettingPercent("Fee_Commission_Fashion", 12m);
        var commCosmeticsRate = GetSettingPercent("Fee_Commission_Cosmetics", 10m);
        var marketingRate = GetSettingPercent("Fee_Marketing", 6m);

        decimal totalProfit = 0m;
        var orderFinancialDetails = new List<OrderFinancialDetailVm>();
        foreach (var order in successfulOrders)
        {
            decimal orderSubTotal = order.SubTotal;
            decimal orderCommission = 0m;
            foreach (var item in order.Items)
            {
                var categoryName = item.Variant?.Product?.Category?.Name?.ToLower() ?? "";
                decimal commRate = categoryName.Contains("thời trang") 
                    ? commFashionRate 
                    : (categoryName.Contains("mỹ phẩm") ? commCosmeticsRate : 0.10m);
                orderCommission += item.LineTotal * commRate;
            }

            decimal orderVat = orderSubTotal * vatRate;
            decimal orderSellerTax = orderSubTotal * sellerTaxRate;
            decimal orderMarketing = orderSubTotal * marketingRate;

            decimal orderProfit = orderSubTotal - (orderCommission + orderSellerTax + orderMarketing);
            totalProfit += orderProfit;

            orderFinancialDetails.Add(new OrderFinancialDetailVm
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                GrossSales = orderSubTotal,
                VatAmount = orderVat,
                CommissionAmount = orderCommission,
                SellerTaxAmount = orderSellerTax,
                MarketingFeeAmount = orderMarketing,
                NetProfit = orderProfit,
                CreatedAt = order.CreatedAt
            });
        }

        // Tính điểm hài lòng thực tế từ đánh giá sản phẩm
        var reviews = await _context.ProductReviews
            .Include(r => r.Product)
            .Where(r => r.Product!.ShopId == shopId)
            .ToListAsync();
        double avgRating = reviews.Any() ? reviews.Average(r => r.Rating) : 5.0;

        var lowStockCount = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.Product!.ShopId == shopId && v.StockQuantity < 10)
            .CountAsync();

        var vm = new SellerDashboardViewModel
        {
            ShopId = shopId,
            ShopName = shop?.Profile?.ShopName ?? shop?.Profile?.FullName ?? "Cửa hàng của bạn",
            TotalRevenue = totalRevenue,
            TotalProfit = totalProfit,
            TotalOrders = successfulOrders.Count, // Số đơn thành công
            TotalProducts = totalProducts,
            AverageRating = Math.Round(avgRating, 1),
            AverageOrderValue = successfulOrders.Count > 0 ? totalRevenue / successfulOrders.Count : 0,
            TotalCustomers = successfulOrders.Select(o => o.CustomerId).Distinct().Count(),
            StockLevelHealth = 85,
            CustomerSatisfactionScore = (int)(avgRating * 20), // Chuyển sang thang điểm 100
            FulfillmentRate = totalOrders > 0 ? (int)Math.Round((double)successfulOrders.Count * 100 / totalOrders) : 0,
            ReturnRate = totalOrders > 0 ? Math.Round((double)returnedOrders.Count * 100 / totalOrders, 1) : 0,
            LowStockItemsCount = lowStockCount,
            OrderFinancialDetails = orderFinancialDetails.OrderByDescending(o => o.CreatedAt).ToList()
        };

        var dailyStats = orderFinancialDetails
            .Where(o => o.CreatedAt >= thirtyDaysAgo)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.GrossSales), Profit = g.Sum(o => o.NetProfit) })
            .ToList();

        for (int i = 29; i >= 0; i--)
        {
            var date = now.Date.AddDays(-i);
            vm.RevenueLabels.Add(date.ToString("dd/MM"));
            var stat = dailyStats.FirstOrDefault(d => d.Date == date);
            vm.RevenueValues.Add(stat?.Revenue ?? 0);
            vm.ProfitValues.Add(stat?.Profit ?? 0);
        }

        // Chi tiết doanh thu theo sản phẩm
        var productStats = await _context.OrderItems
            .Include(oi => oi.Order)
            .Include(oi => oi.Variant)
            .Include(oi => oi.Variant!.Product)
            .Where(oi => oi.Order!.ShopId == shopId && (oi.Order.OrderStatus == "Delivered" || oi.Order.OrderStatus == "Completed"))
            .GroupBy(oi => new { 
                oi.Variant!.ProductId, 
                oi.ProductName
            })
            .Select(g => new ProductRevenueDetailVm {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                UnitsSold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(10)
            .ToListAsync();

        // Bổ sung stock, rating và ảnh cho từng sản phẩm trong danh sách
        foreach (var p in productStats)
        {
            var product = await _context.Products
                .Include(prod => prod.Images)
                .Include(prod => prod.Variants)
                .FirstOrDefaultAsync(prod => prod.Id == p.ProductId);

            if (product != null)
            {
                p.CurrentStock = product.Variants.Sum(v => v.StockQuantity);
                p.ImageUrl = product.Images.FirstOrDefault(img => img.IsPrimary)?.ImageUrl 
                           ?? product.Images.FirstOrDefault()?.ImageUrl 
                           ?? "";
            }
            
            var pReviews = reviews.Where(r => r.ProductId == p.ProductId).ToList();
            p.AverageRating = pReviews.Any() ? pReviews.Average(r => r.Rating) : 5.0;
        }
        vm.ProductDetails = productStats;

        var topProducts = productStats.Take(5).ToList();
        vm.TopProductNames = topProducts.Select(p => p.ProductName).ToList();
        vm.TopProductSales = topProducts.Select(p => p.UnitsSold).ToList();
        vm.TopProductRevenue = topProducts.Select(p => p.Revenue).ToList();

        var shopCategoryData = await _context.OrderItems
            .Include(oi => oi.Order)
            .Include(oi => oi.Variant)
            .Include(oi => oi.Variant!.Product)
            .Include(oi => oi.Variant!.Product!.Category)
            .Where(oi => oi.Order!.ShopId == shopId && (oi.Order.OrderStatus == "Delivered" || oi.Order.OrderStatus == "Completed"))
            .GroupBy(oi => oi.Variant!.Product!.Category!.Name)
            .Select(g => new { Name = g.Key ?? "Khác", Count = g.Count() })
            .ToListAsync();
        vm.ShopCategoryNames = shopCategoryData.Select(c => c.Name).ToList();
        vm.ShopCategoryCounts = shopCategoryData.Select(c => c.Count).ToList();

        var statusCounts = shopOrders.GroupBy(o => o.OrderStatus).Select(g => new { Status = g.Key, Count = g.Count() }).ToList();
        vm.OrderStatusLabels = statusCounts.Select(s => s.Status).ToList();
        vm.OrderStatusValues = statusCounts.Select(s => s.Count).ToList();

        return vm;
    }

    public async Task<FinancialReportViewModel> GetFinancialReportAsync()
    {
        var orders = await _context.Orders
            .Where(o => o.PaymentStatus == "Paid" || o.OrderStatus == "Delivered" || o.OrderStatus == "Completed")
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.Product)
                        .ThenInclude(p => p!.Category)
            .ToListAsync();

        var shopsInfo = await _context.Accounts
            .Include(a => a.Profile)
            .Where(a => a.Role == "Shop")
            .ToDictionaryAsync(a => a.Id, a => a.Profile?.ShopName ?? a.Profile?.FullName ?? a.Email);

        var settings = await _context.SystemSettings.ToDictionaryAsync(s => s.Id, s => s.Value);
        decimal GetSettingPercent(string id, decimal defaultValue)
        {
            if (settings.TryGetValue(id, out var valStr) && decimal.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                return val / 100m;
            }
            return defaultValue / 100m;
        }

        var vatRate = GetSettingPercent("Tax_VAT_Default", 8m);
        var sellerTaxRate = GetSettingPercent("Tax_Seller_Rate", 1.5m);
        var commFashionRate = GetSettingPercent("Fee_Commission_Fashion", 12m);
        var commCosmeticsRate = GetSettingPercent("Fee_Commission_Cosmetics", 10m);

        var report = new FinancialReportViewModel();
        var shopDetails = new Dictionary<int, ShopFinancialDetailVm>();

        foreach (var order in orders)
        {
            if (!shopDetails.ContainsKey(order.ShopId))
            {
                shopDetails[order.ShopId] = new ShopFinancialDetailVm
                {
                    ShopId = order.ShopId,
                    ShopName = shopsInfo.ContainsKey(order.ShopId) ? shopsInfo[order.ShopId] : $"Shop {order.ShopId}"
                };
            }

            var detail = shopDetails[order.ShopId];
            detail.OrderCount++;

            decimal orderSubTotal = 0;
            decimal orderCommission = 0;

            foreach (var item in order.Items)
            {
                var lineTotal = item.LineTotal;
                orderSubTotal += lineTotal;

                // Commission rate based on category
                var categoryName = item.Variant?.Product?.Category?.Name?.ToLower() ?? "";
                decimal commRate = categoryName.Contains("thời trang") 
                    ? commFashionRate 
                    : (categoryName.Contains("mỹ phẩm") ? commCosmeticsRate : 0.10m);
                orderCommission += lineTotal * commRate;
            }

            decimal orderVat = orderSubTotal * vatRate;
            decimal orderSellerTax = orderSubTotal * sellerTaxRate;
            decimal orderPayout = orderSubTotal - orderCommission - orderSellerTax;

            detail.TotalGrossSales += orderSubTotal;
            detail.TotalVat += orderVat;
            detail.TotalCommission += orderCommission;
            detail.TotalSellerTax += orderSellerTax;
            detail.TotalPayout += orderPayout;

            report.TotalGrossRevenue += orderSubTotal;
            report.TotalCommission += orderCommission;
            report.TotalVatCollected += orderVat;
            report.TotalSellerTax += orderSellerTax;
            report.TotalOrders++;
        }

        report.TotalSystemRevenue = report.TotalCommission; // Primary revenue is commission
        report.Shops = shopDetails.Values.OrderByDescending(s => s.TotalGrossSales).ToList();

        return report;
    }
}
