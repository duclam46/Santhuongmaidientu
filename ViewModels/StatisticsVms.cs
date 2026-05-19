using System.Collections.Generic;

namespace DOAN_BANHANG.ViewModels;

public class AdminDashboardViewModel
{
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalShops { get; set; }
    public decimal AverageOrderValue { get; set; }
    public double RevenueGrowthRate { get; set; } // Percentage

    // Compatibility for old views
    public int StorageUsagePercentage { get; set; } = 45;
    public List<double[]> SystemActivityData { get; set; } = new();

    // Charts
    public List<string> RevenueLabels { get; set; } = new();
    public List<decimal> RevenueValues { get; set; } = new();

    public List<string> OrderStatusLabels { get; set; } = new();
    public List<int> OrderStatusValues { get; set; } = new();

    public List<string> TopStoreNames { get; set; } = new();
    public List<decimal> TopStoreRevenues { get; set; } = new();

    public List<string> CategoryNames { get; set; } = new();
    public List<decimal> CategoryRevenue { get; set; } = new();

    // System Monitoring (Flot/Apex)
    public List<double[]> UserTrafficData { get; set; } = new();
    public List<double[]> ServerLoadData { get; set; } = new();

    // KPIs (Knob)
    public int ActiveUserPercentage { get; set; }
    public int OrderCompletionRate { get; set; }
    public int PartnerApprovalRate { get; set; }
    public int CustomerCount { get; set; }
    public int ShopCount { get; set; }
}

public class SellerDashboardViewModel
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public decimal TotalProfit { get; set; }
    public int TotalOrders { get; set; }
    public int TotalProducts { get; set; }
    public double AverageRating { get; set; }
    public decimal AverageOrderValue { get; set; }
    public int TotalCustomers { get; set; }
    public int LowStockItemsCount { get; set; }

    // Strategic Metrics
    public List<string> RevenueLabels { get; set; } = new();
    public List<decimal> RevenueValues { get; set; } = new();
    public List<decimal> ProfitValues { get; set; } = new();

    public List<string> OrderStatusLabels { get; set; } = new();
    public List<int> OrderStatusValues { get; set; } = new();

    public List<string> TopProductNames { get; set; } = new();
    public List<int> TopProductSales { get; set; } = new();
    public List<decimal> TopProductRevenue { get; set; } = new();

    // Sales by Category in Shop
    public List<string> ShopCategoryNames { get; set; } = new();
    public List<int> ShopCategoryCounts { get; set; } = new();

    // KPIs (Knob)
    public int StockLevelHealth { get; set; }
    public int CustomerSatisfactionScore { get; set; } // 0-100
    public int FulfillmentRate { get; set; }
    public double ReturnRate { get; set; }

    public List<ProductRevenueDetailVm> ProductDetails { get; set; } = new();
    public List<OrderFinancialDetailVm> OrderFinancialDetails { get; set; } = new();
}

public class OrderFinancialDetailVm
{
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public decimal GrossSales { get; set; } // SubTotal
    public decimal VatAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal SellerTaxAmount { get; set; }
    public decimal MarketingFeeAmount { get; set; }
    public decimal NetProfit { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProductRevenueDetailVm
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
    public int CurrentStock { get; set; }
    public double AverageRating { get; set; }
}

public class FinancialReportViewModel
{
    public decimal TotalSystemRevenue { get; set; } // Total Commission + others
    public decimal TotalGrossRevenue { get; set; } // Total SubTotal of all paid orders
    public decimal TotalCommission { get; set; }
    public decimal TotalVatCollected { get; set; }
    public decimal TotalSellerTax { get; set; }
    public int TotalOrders { get; set; }

    public List<ShopFinancialDetailVm> Shops { get; set; } = new();
}

public class ShopFinancialDetailVm
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalGrossSales { get; set; } // SubTotal
    public decimal TotalVat { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalSellerTax { get; set; }
    public decimal TotalPayout { get; set; }
}
