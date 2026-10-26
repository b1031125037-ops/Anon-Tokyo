using System;
using System.Collections.Generic;
using System.IO;
using AnonTokyo.Shop;
using UnityEngine;

public static class ShopOfflineChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public string[] results; }
    const long Start = 1700000000;

    public static void Run(ShopCatalog catalog, string output)
    {
        catalog=ShopLedgerFixture.Catalog(catalog);
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (ok, label) =>
        {
            if (!ok) throw new InvalidOperationException("OFFLINE CHECK FAILED: " + label);
            results.Add(label);
        };
        var layout = Minimal();
        var economy = Ready(catalog, layout, 100);
        int initialCash = economy.cash;
        check(economy.CaptureOfflineBaseline(catalog, layout, Start, out _) &&
              economy.offlineWasEligible && economy.offlineStockSnapshot.Count == 1 &&
              economy.offlineStockSnapshot[0].quantity == 100,
            "Departure freezes open shop eligibility and the displayed SKU's physical shelf plus warehouse stock");
        check(economy.TryApplyOfflineIncome(catalog, layout, Start + 3600, out var report, out _) &&
              report.unitsSold == 5 && report.income == 50 && report.creditedSeconds == 3600 &&
              economy.Shelf("top-1").quantity == 5 && economy.Stock("top_tshirt").available == 90 &&
              economy.cash == initialCash + 50 && economy.Stock("top_tshirt").sold == 5 &&
              economy.revenue == 50 && economy.offlineRevenue == 50 && economy.offlineUnitsSold == 5,
            "One hour sells exactly five percent of departure stock, drawing from shelves before the warehouse");
        check(economy.transactions == 0 && economy.popularity == 0 && economy.Validate(catalog, layout, out _),
            "Offline income conserves inventory while leaving visitor transactions and popularity unchanged");
        string before = JsonUtility.ToJson(economy);
        check(economy.TryApplyOfflineIncome(catalog, layout, Start + 3600, out report, out _) &&
              report.unitsSold == 0 && report.income == 0 && report.elapsedSeconds == 0 &&
              JsonUtility.ToJson(economy) == before,
            "Applying the same UTC time twice cannot duplicate a reward or modify the snapshot");
        check(economy.TryApplyOfflineIncome(catalog, layout, Start, out report, out _) &&
              report.clockWentBack && report.unitsSold == 0 && JsonUtility.ToJson(economy) == before &&
              !economy.CaptureOfflineBaseline(catalog, layout, Start, out _) && JsonUtility.ToJson(economy) == before,
            "Clock rollback neither grants a reward nor moves the saved high-water time backwards");
        check(!economy.TryApplyOfflineIncome(catalog, layout, 0, out _, out _) &&
              !economy.CaptureOfflineBaseline(catalog, layout, long.MaxValue, out _) &&
              JsonUtility.ToJson(economy) == before,
            "Unsupported UTC times are rejected without mutating inventory or the baseline");

        var capped = Ready(catalog, layout, 100);
        capped.CaptureOfflineBaseline(catalog, layout, Start, out _);
        check(capped.TryApplyOfflineIncome(catalog, layout, Start + 3 * 86400, out report, out _) &&
              report.capped && report.elapsedSeconds == 3 * 86400 && report.creditedSeconds == 86400 &&
              report.unitsSold == 100 && capped.Stock("top_tshirt").available == 0 &&
              capped.Shelf("top-1").quantity == 0 && capped.Validate(catalog, layout, out _),
            "Absences beyond twenty-four hours are capped and never sell more than all physical departure stock");
        long cappedIncome = capped.revenue;
        check(capped.TryApplyOfflineIncome(catalog, layout, Start + 4 * 86400, out report, out _) &&
              report.unitsSold == 0 && capped.revenue == cappedIncome &&
              capped.offlineRemainders.TrueForAll(value => value.unitsNumerator == 0),
            "Depleted stock generates no further money and leaves no fractional claim against future purchases");
        var half = Ready(catalog, layout, 100);
        half.CaptureOfflineBaseline(catalog, layout, Start, out _);
        check(half.TryApplyOfflineIncome(catalog, layout, Start + 10 * 3600, out report, out _) &&
              report.unitsSold == 50 && half.Stock("top_tshirt").available == 50 &&
              half.Shelf("top-1").quantity == 0,
            "Ten offline hours use fifty percent of the departure baseline, not an exponential remaining-stock formula");

        var small = Ready(catalog, layout, 1);
        small.CaptureOfflineBaseline(catalog, layout, Start, out _);
        long time = Start;
        bool tinySuccess = true;
        for (int i = 0; i < 19; i++)
        {
            time += 3600;
            tinySuccess &= small.TryApplyOfflineIncome(catalog, layout, time, out report, out _) &&
                report.unitsSold == 0 && small.Stock("top_tshirt").sold == 0;
        }
        check(tinySuccess && small.offlineRemainders[0].unitsNumerator == 68400,
            "Fractional units accumulate across nineteen short one-hour absences instead of being discarded");
        string fractionPath = Path.Combine(output, "fractional-progress.json");
        ShopSession.SaveToPath(layout, small, catalog, fractionPath);
        var fractionLoad = ShopSession.LoadFromPath(catalog, fractionPath, out _);
        check(fractionLoad != null && fractionLoad.economy.offlineRemainders[0].unitsNumerator == 68400 &&
              fractionLoad.economy.offlineLastCheckUtcSeconds == time,
            "Combined save and load preserve the sub-unit remainder and UTC baseline losslessly");
        small = fractionLoad.economy;
        check(small.TryApplyOfflineIncome(catalog, layout, time + 3600, out report, out _) &&
              report.unitsSold == 1 && small.Stock("top_tshirt").sold == 1 && small.offlineUnitsSold == 1 &&
              small.offlineRemainders[0].unitsNumerator == 0 && small.Validate(catalog, layout, out _),
            "The twentieth one-hour absence sells a single remaining item exactly once");

        var frozen = Ready(catalog, layout, 100);
        frozen.PostFor(FacilityKind.Checkout).level = 3;
        frozen.CaptureOfflineBaseline(catalog, layout, Start, out _);
        frozen.shopOpen = false;
        frozen.PostFor(FacilityKind.Checkout).level = 1;
        frozen.AssignStaff(catalog, "checkout-1", "", out _);
        check(frozen.TryApplyOfflineIncome(catalog, layout, Start + 3600, out report, out _) &&
              report.wasEligible && report.unitsSold == 5 && report.income == 50 &&
              report.lines[0].unitPrice == 10 && !frozen.offlineWasEligible,
            "Departure eligibility and checkout upgrade price remain frozen even if current state changes before resume");
        var closed = Ready(catalog, layout, 100);
        closed.shopOpen = false;
        closed.CaptureOfflineBaseline(catalog, layout, Start, out _);
        closed.shopOpen = true;
        check(closed.TryApplyOfflineIncome(catalog, layout, Start + 86400, out report, out _) &&
              !report.wasEligible && report.unitsSold == 0 && closed.Stock("top_tshirt").sold == 0 &&
              closed.offlineWasEligible,
            "Opening a previously closed snapshot on resume cannot retroactively earn closed-period money");
        var unstaffed = Ready(catalog, layout, 100);
        unstaffed.AssignStaff(catalog, "checkout-1", "", out _);
        unstaffed.CaptureOfflineBaseline(catalog, layout, Start, out _);
        check(!unstaffed.offlineWasEligible &&
              unstaffed.TryApplyOfflineIncome(catalog, layout, Start + 86400, out report, out _) && report.unitsSold == 0,
            "An open flag alone is insufficient when no cashier was assigned at departure");
        var blockedLayout = Minimal();
        blockedLayout.facilities.Find(value => value.instanceId == "checkout-1").rotation = 0;
        var blocked = Ready(catalog, blockedLayout, 100);
        check(blocked.CaptureOfflineBaseline(catalog, blockedLayout, Start, out _) && !blocked.offlineWasEligible,
            "A checkout whose exact customer front is outside the shop is ineligible for offline sales");

        var exclusive = Ready(catalog, layout, 90);
        exclusive.Stock("head_ribbon").contracted = true;
        exclusive.Stock("head_ribbon").available = 20;
        exclusive.Stock("head_ribbon").purchased = 20;
        exclusive.Order(catalog, "top_tshirt", out _);
        exclusive.AddToBasket(catalog, "top-1", "unpaid", 3, out _);
        exclusive.BeginRestock(catalog, layout, "top-1", out _);
        var stock = exclusive.Stock("top_tshirt");
        stock.returned += 7; stock.purchased += 7;
        int available = stock.available;
        int shelfCount = exclusive.Shelf("top-1").quantity;
        int reserved = stock.reserved;
        string orderBefore = JsonUtility.ToJson(exclusive.orders[0]);
        string basketBefore = JsonUtility.ToJson(exclusive.baskets[0]);
        string jobBefore = JsonUtility.ToJson(exclusive.job);
        check(exclusive.Validate(catalog, layout, out _) &&
              exclusive.CaptureOfflineBaseline(catalog, layout, Start, out _) &&
              exclusive.offlineStockSnapshot.Count == 1 &&
              exclusive.offlineStockSnapshot[0].quantity == available + shelfCount,
            "Only displayed SKU shelf and unreserved warehouse units enter the offline snapshot");
        check(exclusive.TryApplyOfflineIncome(catalog, layout, Start + 86400, out report, out _) &&
              report.unitsSold == available + shelfCount && stock.available == 0 && stock.reserved == reserved &&
              stock.returned == 7 && exclusive.GetBasketQuantity("unpaid") == 3 &&
              exclusive.Stock("head_ribbon").available == 20 &&
              JsonUtility.ToJson(exclusive.orders[0]) == orderBefore &&
              JsonUtility.ToJson(exclusive.baskets[0]) == basketBefore && JsonUtility.ToJson(exclusive.job) == jobBefore &&
              exclusive.Validate(catalog, layout, out _),
            "Pending goods, unpaid baskets, reserved tasks, return buffers and undisplayed SKUs remain untouched");
        var carried = Ready(catalog, layout, 30);
        carried.Shelf("top-1").quantity = 0;
        carried.Stock("top_tshirt").available = 30;
        carried.BeginRestock(catalog, layout, "top-1", out _);
        carried.Pickup(out _);
        carried.CaptureOfflineBaseline(catalog, layout, Start, out _);
        check(carried.offlineStockSnapshot[0].quantity == 20 &&
              carried.TryApplyOfflineIncome(catalog, layout, Start + 86400, out report, out _) &&
              report.unitsSold == 20 && carried.Carrying == 10 && carried.job.pickedUp &&
              carried.Validate(catalog, layout, out _),
            "Items already carried by a replenisher are excluded and remain owned by the existing task");

        var multiLayout = Minimal();
        multiLayout.facilities.Add(new PlacedFacility { instanceId = "top-extra", definitionId = "top", x = 0, z = 3 });
        var multi = Ready(catalog, multiLayout, 100);
        multi.Shelf("top-extra").quantity = 10;
        multi.Stock("top_tshirt").available -= 10;
        multi.Stock("shoes_canvas").contracted = true;
        multi.Stock("shoes_canvas").available = 40;
        multi.Stock("shoes_canvas").purchased = 40;
        multi.CaptureOfflineBaseline(catalog, multiLayout, Start, out _);
        check(multi.offlineStockSnapshot.Count == 2 &&
              multi.TryApplyOfflineIncome(catalog, multiLayout, Start + 3600, out report, out _) &&
              report.unitsSold == 7 && report.lines.Count == 2 &&
              multi.Stock("top_tshirt").sold == 5 && multi.Stock("shoes_canvas").sold == 2 &&
              multi.Validate(catalog, multiLayout, out _),
            "Duplicate displays of one SKU are counted once while separate displayed categories sell independently");

        var test = Ready(catalog, layout, 3);
        test.EnableTestResources();
        int testCash = test.cash;
        test.CaptureOfflineBaseline(catalog, layout, Start, out _);
        check(test.TryApplyOfflineIncome(catalog, layout, Start + 86400, out report, out _) &&
              report.unitsSold == 3 && test.Stock("top_tshirt").purchased == 3 &&
              test.cash == testCash && test.revenue == 30 && test.Validate(catalog, layout, out _),
            "Infinite-resource mode only sells real existing stock and records revenue without generating new units");
        check(test.TryApplyOfflineIncome(catalog, layout, Start + 2 * 86400, out report, out _) &&
              report.unitsSold == 0 && test.Stock("top_tshirt").purchased == 3,
            "Offline settlement never invokes infinite automatic replenishment on empty shelves");

        var overflow = Ready(catalog, layout, 100);
        overflow.cash = int.MaxValue;
        overflow.CaptureOfflineBaseline(catalog, layout, Start, out _);
        before = JsonUtility.ToJson(overflow);
        check(!overflow.TryApplyOfflineIncome(catalog, layout, Start + 3600, out _, out _) &&
              JsonUtility.ToJson(overflow) == before,
            "A cash overflow rejects the entire settlement without stock debit, remainder change or timestamp advancement");
        overflow.cash = 0;
        check(overflow.TryApplyOfflineIncome(catalog, layout, Start + 3600, out report, out _) && report.unitsSold == 5,
            "A failed atomic settlement can be retried once after the blocking balance condition is resolved");
        string settledPath = Path.Combine(output, "offline-settled.json");
        ShopSession.SaveToPath(layout, overflow, catalog, settledPath);
        var loaded = ShopSession.LoadFromPath(catalog, settledPath, out _);
        check(loaded != null && loaded.economy.offlineUnitsSold == 5 && loaded.economy.offlineRevenue == 50 &&
              loaded.economy.offlineLastCheckUtcSeconds == Start + 3600 &&
              loaded.economy.TryApplyOfflineIncome(catalog, layout, Start + 3600, out report, out _) && report.unitsSold == 0,
            "Saving and restarting after settlement retains the reward and prevents a second claim for the same interval");
        var fresh = Ready(catalog, layout, 100);
        check(fresh.TryApplyOfflineIncome(catalog, layout, Start + 1000000, out report, out _) &&
              report.initializedBaseline && report.unitsSold == 0 && fresh.offlineLastCheckUtcSeconds == Start + 1000000,
            "A legacy economy without a recorded departure starts a baseline without retroactive rewards");

        string legacyJson = JsonUtility.ToJson(new ShopSession { layout = layout, economy = Ready(catalog, layout, 100) });
        foreach (string field in new[]
        {
            "\"offlineSnapshotVersion\":0", "\"offlineLastCheckUtcSeconds\":0",
            "\"offlineUnitsSold\":0", "\"offlineRevenue\":0", "\"offlineWasEligible\":false",
            "\"offlineStockSnapshot\":[]", "\"offlineRemainders\":[]"
        })
            legacyJson = legacyJson.Replace("," + field, "").Replace(field + ",", "");
        string legacyPath = Path.Combine(output, "before-offline-support.json");
        File.WriteAllText(legacyPath, legacyJson);
        var legacy = ShopSession.LoadFromPath(catalog, legacyPath, out _);
        check(!legacyJson.Contains("\"offlineSnapshotVersion\"") && legacy != null &&
              legacy.economy.offlineSnapshotVersion == 0 &&
              legacy.economy.TryApplyOfflineIncome(catalog, layout, Start, out report, out _) &&
              report.initializedBaseline && report.unitsSold == 0,
            "Actual JSON omitting all new fields migrates without rejecting old saves or inventing past earnings");
        var malformed = Clone(economy);
        malformed.offlineLastCheckUtcSeconds = -1;
        check(!malformed.Validate(catalog, layout, out _), "Negative offline timestamps invalidate a save");
        malformed = Clone(economy);
        malformed.offlineRemainders[0].unitsNumerator = ShopEconomy.OfflineUnitDenominator;
        check(!malformed.Validate(catalog, layout, out _), "Out-of-range fractional progress cannot enter a validated snapshot");
        malformed = Clone(economy);
        malformed.offlineStockSnapshot.Add(malformed.offlineStockSnapshot[0]);
        check(!malformed.Validate(catalog, layout, out _), "Repeated SKU departure records cannot multiply offline sales");
        malformed = Clone(economy);
        malformed.offlineUnitsSold = 10000;
        check(!malformed.Validate(catalog, layout, out _), "Offline lifetime sales cannot exceed the real completed-sales ledger");
        malformed = Clone(economy);
        malformed.offlineRevenue = malformed.revenue + 1;
        check(!malformed.Validate(catalog, layout, out _), "Offline lifetime revenue cannot exceed total recorded revenue");
        check(economy.AddToBasket(catalog, "top-1", "after-offline", 1, out _) &&
              economy.QuoteBasketAtCheckout(catalog, "after-offline", "checkout-1", out _) &&
              economy.PayBasket("after-offline", out _) && economy.transactions == 1 &&
              economy.offlineUnitsSold == 5 && economy.popularity == 1 && economy.Validate(catalog, layout, out _),
            "Ordinary paid baskets continue to raise visit statistics independently after offline earnings");

        File.WriteAllText(Path.Combine(output, "offline-verification.json"),
            JsonUtility.ToJson(new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("SHOP_OFFLINE_CHECKS_OK " + results.Count);
    }

    static ShopLayout Minimal()
    {
        var layout = ShopModel.CreateDefault();
        layout.facilities.RemoveAll(item => item.definitionId == "head" || item.definitionId == "bottom" ||
            item.definitionId == "fitting" || item.definitionId == "stage");
        return layout;
    }
    static ShopEconomy Ready(ShopCatalog catalog, ShopLayout layout, int count)
    {
        var economy = ShopEconomy.CreateDefault(catalog, layout);
        economy.shopOpen = true;
        var stock = economy.Stock("top_tshirt");
        stock.contracted = true;
        stock.purchased = count;
        economy.Shelf("top-1").quantity = Math.Min(10, count);
        stock.available = count - economy.Shelf("top-1").quantity;
        return economy;
    }
    static ShopEconomy Clone(ShopEconomy economy) => JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));
}
