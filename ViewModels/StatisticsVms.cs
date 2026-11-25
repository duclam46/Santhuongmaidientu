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

    // Real-time System & Operations Monitoring Telemetry
    public int SystemHealthScore { get; set; } = 100;
    public long DbLatencyMs { get; set; }
    public double ProcessMemoryMb { get; set; }
    public double ManagedHeapMb { get; set; }
    public int ActiveThreadCount { get; set; }
    public string AppUptimeDisplay { get; set; } = string.Empty;
    public DateTime RefreshedAtLocal { get; set; } = DateTime.Now;

    // Real Marketplace Operations Counters
    public int TotalAllOrders { get; set; }
    public int PendingOrdersCount { get; set; }
    public int CompletedOrdersCount { get; set; }
    public int CancelledOrdersCount { get; set; }
    public double CancelRatePercent { get; set; }
    public decimal EstimatedPlatformCommission { get; set; }

    public int MallShopCount { get; set; }
    public int StandardShopCount { get; set; }
    public int PendingPartnerAppsCount { get; set; }
    public int BlockedAccountsCount { get; set; }
    public int TotalActiveProducts { get; set; }
    public int LowStockVariantsCount { get; set; }
    public int OutOfStockVariantsCount { get; set; }
    public int ActiveVouchersCount { get; set; }
    public double PlatformAvgRating { get; set; } = 5.0;
    public int TotalReviewsCount { get; set; }

    // Real Chart Series
    public List<int> MonthlyOrderCounts { get; set; } = new();
    public List<string> Recent7DayLabels { get; set; } = new();
    public List<int> Recent7DayNewUsers { get; set; } = new();
    public List<int> Recent7DayOrders { get; set; } = new();
    public bool CategoryChartIsProductFallback { get; set; }
    public List<AdminShopPerfRowVm> ShopPerformances { get; set; } = new();
}

public class AdminShopPerfRowVm
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public bool IsMall { get; set; }
    public int ProductCount { get; set; }
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
}

public class SellerDashboardViewModel
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? ShopSlug { get; set; }
    public bool IsMall { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalProfit { get; set; }
    public int TotalOrders { get; set; }
    public int TotalAllOrders { get; set; }
    public int TotalProducts { get; set; }
    public double AverageRating { get; set; }
    public decimal AverageOrderValue { get; set; }
    public int TotalCustomers { get; set; }
    public int RepeatCustomersCount { get; set; }
    public double RepeatCustomerRatePercent { get; set; }
    public int LowStockItemsCount { get; set; }
    public int OutOfStockItemsCount { get; set; }
    public int ExpiringSoonCount { get; set; }
    public decimal TotalCostOfGoods { get; set; }

    // Financial Breakdown & Active Fee Rates
    public decimal TotalShopVoucherDiscount { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalSellerTax { get; set; }
    public decimal TotalMarketing { get; set; }
    public decimal TotalNetPayoutToShop { get; set; }
    public decimal NetProfitMarginPercent { get; set; }
    public decimal GrossMarginPercent { get; set; }
    public decimal ShopInventoryValue { get; set; }
    public int ShopStockUnits { get; set; }

    public decimal VatRatePercent { get; set; } = 8m;
    public decimal SellerTaxRatePercent { get; set; } = 1.5m;
    public decimal MarketingRatePercent { get; set; } = 6m;
    public decimal ShopAvgCommissionRatePercent { get; set; } = 10m;
    public decimal ShopAvgCogsRatePercent { get; set; } = 55m;
    public decimal RecommendedAnchorOrderValue { get; set; }

    // ML Forecasting Series (6 historical months + 3 forecast months)
    public List<string> ForecastLabels { get; set; } = new();
    public List<decimal?> HistoricalRevenueSeries { get; set; } = new();
    public List<decimal?> BaselineForecastSeries { get; set; } = new();
    public List<decimal?> StrategyForecastSeries { get; set; } = new();
    public decimal NextMonthBaselineForecast { get; set; }
    public decimal NextMonthStrategyForecast { get; set; }
    public decimal NextMonthProjectedProfit { get; set; }
    public double ForecastConfidencePercent { get; set; } = 89.5;
    public double PredictedGrowthPercent { get; set; }

    // BCG Portfolio Counters
    public int StarProductCount { get; set; }
    public int CashCowProductCount { get; set; }
    public int QuestionMarkProductCount { get; set; }
    public int OptimizeProductCount { get; set; }
    public int RestockAlertProductCount { get; set; }

    // Strategic Metrics (30-day Cash Flow & Legacy Series)
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
    public int ShopAiHealthScore { get; set; } = 88;

    public List<ProductRevenueDetailVm> ProductDetails { get; set; } = new();
    public List<OrderFinancialDetailVm> OrderFinancialDetails { get; set; } = new();
    public List<ExpiringProductAlertVm> ExpiringSoonProducts { get; set; } = new();

    // AI & ML Strategic Collections for Seller
    public List<SellerProductStrategyVm> ProductStrategies { get; set; } = new();
    public List<AiCategoryStrategyVm> ShopCategoryStrategies { get; set; } = new();
    public List<AiCampaignRecommendationVm> UpcomingCampaigns { get; set; } = new();
    public List<SellerStrategicActionVm> StrategicActions { get; set; } = new();
}

public class SellerProductStrategyVm
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetMarginPercent { get; set; }
    public int CurrentStock { get; set; }
    public double DailyVelocity { get; set; }
    public int EstimatedDaysUntilStockout { get; set; }
    public int RecommendedRestockQty { get; set; }
    public double AverageRating { get; set; }
    public int AiProductScore { get; set; }
    public string BcgSegment { get; set; } = string.Empty; // Star, CashCow, QuestionMark, Optimize
    public string BcgBadgeText { get; set; } = string.Empty;
    public string BcgBadgeClass { get; set; } = string.Empty;
    public string AiStrategicAction { get; set; } = string.Empty;
}

public class SellerStrategicActionVm
{
    public string PillarTitle { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string BadgeClass { get; set; } = string.Empty;
    public string MetricHighlight { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string ActionStep { get; set; } = string.Empty;
    public string ExpectedImpact { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = string.Empty;
    public string ActionButtonText { get; set; } = string.Empty;
}

public class ExpiringProductAlertVm
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public int DaysRemaining { get; set; }
    public int TotalStock { get; set; }
}

public class OrderFinancialDetailVm
{
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public decimal GrossSales { get; set; } // SubTotal
    public decimal ShopVoucherDiscountAmount { get; set; }
    public decimal PlatformVoucherDiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal SellerTaxAmount { get; set; }
    public decimal MarketingFeeAmount { get; set; }
    public decimal CostOfGoodsAmount { get; set; }
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
    public decimal TotalSystemRevenue { get; set; } // Total Commission
    public decimal TotalPlatformVoucherSubsidy { get; set; }
    public decimal TotalShopVoucherDiscount { get; set; }
    public decimal NetPlatformCommissionAfterSubsidy { get; set; }
    public decimal TotalGrossRevenue { get; set; } // Total SubTotal of all paid orders
    public decimal TotalCommission { get; set; }
    public decimal TotalVatCollected { get; set; }
    public decimal TotalSellerTax { get; set; }
    public decimal TotalNetPayoutToShops { get; set; }
    public int TotalOrders { get; set; }

    public decimal MallGrossRevenue { get; set; }
    public decimal StandardGrossRevenue { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public int TotalActiveProducts { get; set; }
    public int TotalStockUnits { get; set; }

    // Configuration rates currently active
    public decimal VatRatePercent { get; set; } = 8m;
    public decimal SellerTaxRatePercent { get; set; } = 1.5m;
    public decimal MallSurchargePercent { get; set; } = 2m;
    public decimal DefaultCommissionPercent { get; set; } = 10m;

    // ML Forecasting Series (6 historical months + 3 forecast months)
    public List<string> ForecastLabels { get; set; } = new();
    public List<decimal?> HistoricalRevenueSeries { get; set; } = new();
    public List<decimal?> BaselineForecastSeries { get; set; } = new();
    public List<decimal?> CampaignForecastSeries { get; set; } = new();
    public decimal NextMonthBaselineForecast { get; set; }
    public decimal NextMonthCampaignForecast { get; set; }
    public double ForecastConfidencePercent { get; set; } = 88.5;
    public double PredictedGrowthPercent { get; set; }

    // AI Strategic Recommendations (Holidays, Mega Sales & Category Insights)
    public List<AiCampaignRecommendationVm> UpcomingCampaigns { get; set; } = new();
    public List<AiCategoryStrategyVm> CategoryStrategies { get; set; } = new();

    public List<ShopFinancialDetailVm> Shops { get; set; } = new();
}

public class AiCampaignRecommendationVm
{
    public string EventName { get; set; } = string.Empty;
    public string EventDateDisplay { get; set; } = string.Empty;
    public int DaysUntil { get; set; }
    public string EventTypeBadge { get; set; } = string.Empty;
    public string BadgeColorClass { get; set; } = string.Empty;
    public string TargetCategories { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string SuggestedVoucherRule { get; set; } = string.Empty;
    public int ExpectedOrderUpliftPercent { get; set; }
    public decimal ProjectedRevenue { get; set; }
    public double AiPriorityScore { get; set; }
}

public class AiCategoryStrategyVm
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal CommissionRatePercent { get; set; }
    public decimal? MaxCommissionAmount { get; set; }
    public int ProductCount { get; set; }
    public int StockUnits { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal HistoricalRevenue { get; set; }
    public int AiPotentialScore { get; set; }
    public string StrategyTag { get; set; } = string.Empty;
    public string StrategyTagClass { get; set; } = string.Empty;
    public string AiAdvice { get; set; } = string.Empty;
}

public class ShopFinancialDetailVm
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? ShopSlug { get; set; }
    public bool IsMall { get; set; }
    public int ProductCount { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalGrossSales { get; set; } // SubTotal
    public decimal TotalShopVoucherDiscount { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalSellerTax { get; set; }
    public decimal TotalPayout { get; set; }
}
