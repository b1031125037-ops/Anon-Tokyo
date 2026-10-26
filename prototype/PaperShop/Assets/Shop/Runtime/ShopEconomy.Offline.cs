using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    [Serializable]
    public sealed class OfflineStockSnapshot
    {
        public string productId;
        public int quantity, unitPrice;
    }

    [Serializable]
    public sealed class OfflineSaleRemainder
    {
        public string productId;
        // One whole unit is 72,000 item-seconds (5% of one item per hour).
        // Integer storage preserves exact progress through JsonUtility saves.
        public int unitsNumerator;
    }

    [Serializable]
    public sealed class OfflineIncomeLine
    {
        public string productId;
        public int quantity, unitPrice;
        public long income;
    }

    [Serializable]
    public sealed class OfflineIncomeReport
    {
        public long elapsedSeconds, creditedSeconds, unitsSold, income;
        public bool wasEligible, initializedBaseline, capped, clockWentBack;
        public List<OfflineIncomeLine> lines = new List<OfflineIncomeLine>();
        public string message;
    }

    public sealed partial class ShopEconomy
    {
        public const int OfflineMaxSeconds = 24 * 60 * 60;
        public const double OfflineHourlyRate = .05;
        public const int OfflineUnitDenominator = 72000;
        const long LastSupportedUtcSecond = 253402300799L;

        // A departure snapshot freezes eligibility, prices and each displayed SKU's
        // available stock. Unpaid baskets and committed deliveries never enter it.
        public int offlineSnapshotVersion;
        public long offlineLastCheckUtcSeconds, offlineUnitsSold, offlineRevenue;
        public bool offlineWasEligible;
        public List<OfflineStockSnapshot> offlineStockSnapshot = new List<OfflineStockSnapshot>();
        public List<OfflineSaleRemainder> offlineRemainders = new List<OfflineSaleRemainder>();

        public bool CaptureOfflineBaseline(ShopCatalog catalog, ShopLayout layout, long nowUtcSeconds, out string reason)
        {
            if (!Validate(catalog, layout, out reason)) return false;
            if (!ValidOfflineTime(nowUtcSeconds)) return Fail("离线结算时间无效。", out reason);
            if (nowUtcSeconds < offlineLastCheckUtcSeconds)
                return Fail("检测到系统时间回拨，保留上次离线时间，等待时钟恢复。", out reason);
            CaptureOfflineBaselineUnchecked(catalog, layout, nowUtcSeconds);
            reason = "已记录离开时间与可售现货。";
            return true;
        }

        /// <summary>
        /// Call once on startup/resume, before returning baskets or cancelling jobs.
        /// All allocation and overflow checks precede the inventory commit. Callers
        /// persist the resulting state before exposing its reward to the player.
        /// </summary>
        public bool TryApplyOfflineIncome(ShopCatalog catalog, ShopLayout layout, long nowUtcSeconds,
            out OfflineIncomeReport report, out string reason)
        {
            report = new OfflineIncomeReport();
            if (!Validate(catalog, layout, out reason)) return false;
            if (!ValidOfflineTime(nowUtcSeconds)) return Fail("离线结算时间无效。", out reason);
            if (offlineSnapshotVersion == 0)
            {
                CaptureOfflineBaselineUnchecked(catalog, layout, nowUtcSeconds);
                report.initializedBaseline = true;
                report.message = reason = "已建立离线收益记录；旧存档首次载入不追溯收益。";
                return true;
            }
            if (nowUtcSeconds < offlineLastCheckUtcSeconds)
            {
                report.clockWentBack = true;
                report.message = reason = "检测到系统时间回拨，本次不结算，保留上次记录时间。";
                return true;
            }
            report.elapsedSeconds = nowUtcSeconds - offlineLastCheckUtcSeconds;
            report.creditedSeconds = Math.Min(report.elapsedSeconds, OfflineMaxSeconds);
            report.capped = report.elapsedSeconds > OfflineMaxSeconds;
            report.wasEligible = offlineWasEligible;
            if (report.elapsedSeconds == 0)
            {
                report.message = reason = "没有新的离线时间，未重复领取收益。";
                return true;
            }

            var shelfDebits = new Dictionary<ShelfStock, int>();
            var stockDebits = new Dictionary<ProductStock, int>();
            var soldByStock = new Dictionary<ProductStock, int>();
            var nextRemainders = new List<OfflineSaleRemainder>();
            foreach (var remainder in offlineRemainders)
                nextRemainders.Add(new OfflineSaleRemainder
                { productId = remainder.productId, unitsNumerator = remainder.unitsNumerator });
            if (offlineWasEligible)
            {
                long saleSeconds = Math.Min(report.creditedSeconds, OfflineUnitDenominator);
                foreach (var snapshot in offlineStockSnapshot)
                {
                    var stock = Stock(snapshot.productId);
                    var remainder = nextRemainders.Find(value => value.productId == snapshot.productId);
                    long numerator = snapshot.quantity * saleSeconds +
                        (remainder == null ? 0 : remainder.unitsNumerator);
                    // Only displayed products are eligible, even if other warehouse
                    // products exist. The real stock is checked again before debit.
                    long current = 0;
                    bool displayed = false;
                    foreach (var shelf in shelves)
                        if (shelf.productId == snapshot.productId)
                        { displayed = true; current += shelf.quantity; }
                    if (displayed) current += stock.available;
                    int wanted = (int)Math.Min(snapshot.quantity, numerator / OfflineUnitDenominator);
                    int count = (int)Math.Min(current, wanted);
                    int residual = (int)(numerator % OfflineUnitDenominator);
                    // Full depletion cannot leave fractional claims on future purchases.
                    if (count >= current || count >= snapshot.quantity) residual = 0;
                    if (remainder == null)
                    {
                        remainder = new OfflineSaleRemainder { productId = snapshot.productId };
                        nextRemainders.Add(remainder);
                    }
                    remainder.unitsNumerator = residual;
                    if (count == 0) continue;
                    long amount = (long)count * snapshot.unitPrice;
                    if (report.income > long.MaxValue - amount ||
                        (long)stock.sold + count > int.MaxValue)
                        return Fail("离线成交累计超过支持范围，未消耗库存。", out reason);
                    int outstanding = count;
                    foreach (var shelf in shelves)
                    {
                        if (shelf.productId != snapshot.productId || outstanding == 0) continue;
                        int debit = Math.Min(shelf.quantity, outstanding);
                        if (debit > 0) shelfDebits.Add(shelf, debit);
                        outstanding -= debit;
                    }
                    if (outstanding > 0) stockDebits.Add(stock, outstanding);
                    soldByStock.Add(stock, count);
                    report.unitsSold += count;
                    report.income += amount;
                    report.lines.Add(new OfflineIncomeLine
                    { productId = snapshot.productId, quantity = count, unitPrice = snapshot.unitPrice, income = amount });
                }
            }
            if (revenue > long.MaxValue - report.income || offlineRevenue > long.MaxValue - report.income ||
                offlineUnitsSold > long.MaxValue - report.unitsSold || (!testMode && report.income > int.MaxValue - (long)cash))
                return Fail("离线收益超过余额或统计支持范围，未消耗库存。", out reason);

            foreach (var debit in shelfDebits) debit.Key.quantity -= debit.Value;
            foreach (var debit in stockDebits) debit.Key.available -= debit.Value;
            foreach (var sale in soldByStock) sale.Key.sold += sale.Value;
            offlineRemainders = nextRemainders;
            revenue += report.income;
            offlineRevenue += report.income;
            offlineUnitsSold += report.unitsSold;
            if (!testMode) cash += (int)report.income;
            CaptureOfflineBaselineUnchecked(catalog, layout, nowUtcSeconds);
            report.message = reason = !report.wasEligible ? "离开时未营业或无可用收银岗位，本次无离线收益。" :
                report.unitsSold == 0 ? "离线时间已记录，未满一件的销售进度保留至下次。" :
                "离线售出 " + report.unitsSold + " 件 · 收入 +" + report.income +
                (report.capped ? "（最多计算 24 小时）" : "");
            return true;
        }

        void CaptureOfflineBaselineUnchecked(ShopCatalog catalog, ShopLayout layout, long nowUtcSeconds)
        {
            var checkout = PostFor(FacilityKind.Checkout);
            var placement = checkout == null ? null : layout.facilities.Find(value => value.instanceId == checkout.instanceId);
            offlineWasEligible = shopOpen && checkout != null && !string.IsNullOrEmpty(checkout.staffId) &&
                ShopModel.CanUseCheckoutFront(layout, catalog, placement, out _);
            offlineStockSnapshot = new List<OfflineStockSnapshot>();
            if (offlineWasEligible)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var shelf in shelves)
                {
                    if (!seen.Add(shelf.productId)) continue;
                    var stock = Stock(shelf.productId);
                    if (!stock.contracted) continue;
                    long count = stock.available;
                    foreach (var other in shelves) if (other.productId == shelf.productId) count += other.quantity;
                    if (count <= 0) continue;
                    offlineStockSnapshot.Add(new OfflineStockSnapshot
                    {
                        productId = shelf.productId, quantity = (int)count,
                        unitPrice = CheckoutPrice(catalog, shelf.productId, checkout.instanceId)
                    });
                }
            }
            offlineSnapshotVersion = 1;
            offlineLastCheckUtcSeconds = nowUtcSeconds;
        }

        bool ValidateOffline(ShopCatalog catalog, out string reason)
        {
            if (offlineSnapshotVersion < 0 || offlineSnapshotVersion > 1 || offlineLastCheckUtcSeconds < 0 ||
                offlineLastCheckUtcSeconds > LastSupportedUtcSecond || offlineUnitsSold < 0 || offlineRevenue < 0 ||
                offlineRevenue > revenue || offlineStockSnapshot == null || offlineRemainders == null ||
                (offlineSnapshotVersion == 1 && offlineLastCheckUtcSeconds == 0) ||
                (offlineSnapshotVersion == 0 && (offlineLastCheckUtcSeconds != 0 || offlineWasEligible ||
                    offlineStockSnapshot.Count != 0 || offlineRemainders.Count != 0 || offlineUnitsSold != 0 || offlineRevenue != 0)))
                return Fail("离线收益记录、时间或统计无效。", out reason);
            long sold = 0;
            foreach (var stock in stocks) sold += stock.sold;
            if (offlineUnitsSold > sold || (offlineUnitsSold == 0 && offlineRevenue != 0))
                return Fail("离线已售量或收入与真实库存账目不符。", out reason);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in offlineStockSnapshot)
                if (item == null || string.IsNullOrEmpty(item.productId) || !ids.Add(item.productId) ||
                    Product(catalog, item.productId) == null || item.quantity <= 0 || item.unitPrice < 0)
                    return Fail("离线现货快照含未知商品、重复项或无效数量。", out reason);
            if (!offlineWasEligible && offlineStockSnapshot.Count != 0)
                return Fail("未营业的离线记录不能包含可售快照。", out reason);
            ids.Clear();
            foreach (var item in offlineRemainders)
                if (item == null || string.IsNullOrEmpty(item.productId) || !ids.Add(item.productId) ||
                    Product(catalog, item.productId) == null ||
                    item.unitsNumerator < 0 || item.unitsNumerator >= OfflineUnitDenominator)
                    return Fail("离线不足一件的累计进度无效。", out reason);
            reason = "";
            return true;
        }

        static bool ValidOfflineTime(long seconds) => seconds > 0 && seconds <= LastSupportedUtcSecond;
    }
}
