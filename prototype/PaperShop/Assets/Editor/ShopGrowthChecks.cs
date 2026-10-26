using System;
using System.Collections.Generic;
using System.IO;
using AnonTokyo.Shop;
using UnityEngine;

public static class ShopGrowthChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public string[] results; }

    public static void Run(ShopCatalog catalog, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (ok, label) =>
        {
            if (!ok) throw new InvalidOperationException("GROWTH CHECK FAILED: " + label);
            results.Add(label);
        };
        var economy = Ready(catalog, out var layout);
        check(economy.ExpansionLevel == 0 && economy.NextExpansionCost == 600 &&
              economy.NextExpansionPopularity == 10 && economy.claimedGrowthTaskIds.Count == 0 &&
              economy.GetGrowthTaskStates().Count == 10 && economy.NextGrowthTask.definition.id == "staff_ready",
            "An existing shop starts at area stage zero with ten ordered unclaimed tasks and the first expansion quote");
        string before = Snapshot(layout, economy);
        check(!economy.TryClaimGrowthTask(catalog, layout, "missing", out _) &&
              !economy.TryClaimGrowthTask(catalog, layout, "first_contract", out _) &&
              Snapshot(layout, economy) == before,
            "Unknown or out-of-order task claims do not mutate the shop or money");
        string originalStaff = economy.AssignedStaffId(FacilityKind.Restock);
        economy.AssignStaff(catalog, "restock-1", "", out _);
        before = Snapshot(layout, economy);
        check(!economy.TryClaimGrowthTask(catalog, layout, "staff_ready", out _) &&
              economy.NextGrowthTask.progress == 2 && Snapshot(layout, economy) == before,
            "The staff task requires all three distinct assigned roles");
        economy.AssignStaff(catalog, "restock-1", originalStaff, out _);
        int money = economy.cash;
        check(economy.TryClaimGrowthTask(catalog, layout, "staff_ready", out _) && economy.cash == money + 50 &&
              economy.growthVersion == 1 && economy.NextGrowthTask.definition.id == "first_contract",
            "A completed current task grants the exact normal-mode soft-currency reward and unlocks its successor");
        before = Snapshot(layout, economy);
        check(!economy.TryClaimGrowthTask(catalog, layout, "staff_ready", out _) && Snapshot(layout, economy) == before,
            "A second reward claim is rejected without duplicating money");
        check(economy.Contract(catalog, "top_tshirt", out _) &&
              economy.TryClaimGrowthTask(catalog, layout, "first_contract", out _),
            "A real SKU contract satisfies the first-contract task");
        before = Snapshot(layout, economy);
        check(!economy.TryClaimGrowthTask(catalog, layout, "shelf_ready", out _) && Snapshot(layout, economy) == before,
            "A bound but empty counter does not satisfy the stock-on-display task");
        Supply(economy, "top-1", 10);
        check(economy.TryClaimGrowthTask(catalog, layout, "shelf_ready", out _) &&
              !economy.TryClaimGrowthTask(catalog, layout, "first_checkout", out _),
            "Physical displayed stock satisfies shelving, but neither stock nor contracts fake a completed sale");
        check(economy.AddToBasket(catalog, "top-1", "growth-first", 1, out _) &&
              !economy.TryClaimGrowthTask(catalog, layout, "first_checkout", out _) &&
              economy.PayBasket("growth-first", out _) &&
              economy.TryClaimGrowthTask(catalog, layout, "first_checkout", out _) &&
              economy.OnlineUnitsSold == 1,
            "Only completing payment satisfies the first real transaction; an unpaid basket does not");
        check(economy.AddToBasket(catalog, "top-1", "growth-nine", 9, out _) &&
              economy.PayBasket("growth-nine", out _) && economy.OnlineUnitsSold == 10 &&
              economy.TryClaimGrowthTask(catalog, layout, "sell_ten", out _),
            "Online quantity progress counts every paid basket unit rather than just customer visits");
        before = Snapshot(layout, economy);
        check(!economy.TryClaimGrowthTask(catalog, layout, "three_counters", out _) && Snapshot(layout, economy) == before,
            "The third-counter task cannot be claimed while owning only two counters");
        check(economy.BuyFixture(catalog, layout, "head", out _, out _) &&
              economy.StoredFixtureCount == 1 && economy.TryClaimGrowthTask(catalog, layout, "three_counters", out _),
            "Purchasing a third real counter satisfies ownership even before it is placed");
        check(economy.UpgradePost("checkout-1", out _) &&
              economy.TryClaimGrowthTask(catalog, layout, "upgraded_post", out _),
            "A real service-post upgrade unlocks the service-improvement reward");
        before = Snapshot(layout, economy);
        check(!economy.TryClaimGrowthTask(catalog, layout, "first_expansion", out _) &&
              !economy.TryExpandShop(catalog, layout, out _) && Snapshot(layout, economy) == before,
            "Expansion and its reward remain unavailable before the popularity gate is reached");
        economy.popularity = 10;
        economy.RefreshShopLevel();
        economy.popularity = 0;
        var preserved = ShopModel.Clone(layout);
        money = economy.cash;
        check(economy.TryExpandShop(catalog, layout, out _) && layout.width == 10 && layout.depth == 8 &&
              economy.areaExpansionLevel == 1 && economy.cash == money - 600 && economy.ShopLevel == 2 &&
              economy.FixtureLimit == 10 && economy.NextExpansionCost == 1200 && economy.NextExpansionPopularity == 20,
            "Historical peak twenty permanently qualifies a 600-coin first expansion independently of current popularity");
        check(PreservedFurniture(preserved, layout) && ShopModel.WarehousePickup(layout).x == 9 &&
              economy.Validate(catalog, layout, out _) && ShopModel.ValidateCheckoutAccess(layout, catalog, out _),
            "Expansion preserves every ordinary fixture and its service data while moving the warehouse to the new right wall");
        check(economy.TryClaimGrowthTask(catalog, layout, "first_expansion", out _) &&
              economy.NextGrowthTask.definition.id == "assortment_growth",
            "The first actual room expansion unlocks the matching reward and next growth task");
        Sell(catalog, economy, 40);
        check(economy.OnlineUnitsSold == 50 && !economy.NextGrowthTask.complete &&
              !economy.TryClaimGrowthTask(catalog, layout, "assortment_growth", out _),
            "Fifty online units alone do not satisfy the four-SKU assortment requirement");
        foreach (string id in new[] { "shoes_canvas", "head_ribbon", "bottom_skirt" })
        {
            if(!economy.OwnsDisplayFor(catalog,id))
                check(economy.BuyFixture(catalog,layout,catalog.products.Find(p=>p.id==id).category,out _,out _),"Assortment contract first requires its corresponding display");
            if (!economy.Contract(catalog, id, out string contractReason))
                throw new InvalidOperationException("GROWTH CONTRACT SETUP: " + id + " " + contractReason);
        }
        check(economy.TryClaimGrowthTask(catalog, layout, "assortment_growth", out _) &&
              economy.NextGrowthTask.definition.id == "second_expansion",
            "The combined assortment task needs both four contracted SKUs and fifty online paid units");
        economy.popularity = 20;
        economy.RefreshShopLevel();
        economy.popularity = 1;
        preserved = ShopModel.Clone(layout);
        money = economy.cash;
        check(economy.TryExpandShop(catalog, layout, out _) && economy.cash == money - 1200 &&
              layout.width == 12 && layout.depth == 8 && economy.ExpansionLevel == 2 &&
              economy.ShopLevel == 3 && economy.FixtureLimit == 14 && economy.NextExpansionCost == 1800 &&
              economy.NextExpansionPopularity == 30 && PreservedFurniture(preserved, layout),
            "The second expansion costs 1200 at historical peak fifty and ends at twelve by eight without relocating furniture");
        check(economy.TryClaimGrowthTask(catalog, layout, "second_expansion", out _) &&
              economy.claimedGrowthTaskIds.Count == 10 && economy.NextGrowthTask == null,
            "Completing the second room expansion finishes all ten ordered rewards exactly once");
        before = Snapshot(layout, economy);
        check(!economy.TryExpandShop(catalog, layout, out _) &&
              !economy.TryClaimGrowthTask(catalog, layout, "second_expansion", out _) &&
              Snapshot(layout, economy) == before,
            "Third expansion remains popularity-gated and completed growth rewards cannot be replayed");

        string path = Path.Combine(output, "growth-complete.json");
        ShopSession.SaveToPath(layout, economy, catalog, path);
        var loaded = ShopSession.LoadFromPath(catalog, path, out _);
        check(loaded != null && Snapshot(loaded.layout, loaded.economy) == Snapshot(layout, economy) &&
              loaded.economy.NextGrowthTask == null &&
              !loaded.economy.TryClaimGrowthTask(catalog, loaded.layout, "second_expansion", out _),
            "Paired save/load retains both expanded dimensions and the complete one-time claim ledger without replay");
        loaded.economy.AssignStaff(catalog, "restock-1", "", out _);
        check(loaded.economy.Validate(catalog, loaded.layout, out _) && loaded.economy.NextGrowthTask == null,
            "After a reward was earned, later staffing changes never revoke the permanent completed ledger");

        var test = Ready(catalog, out var testLayout);
        test.EnableTestResources();
        test.cash = 0;
        before = Snapshot(testLayout, test);
        check(!test.TryExpandShop(catalog, testLayout, out _) && Snapshot(testLayout, test) == before,
            "Unlimited-resource mode does not bypass the popularity requirement");
        test.SetTestPopularity(10, out _);
        check(test.TryExpandShop(catalog, testLayout, out _) && test.cash == 0 && testLayout.width == 10 &&
              test.TryClaimGrowthTask(catalog, testLayout, "staff_ready", out _) && test.cash == 0 &&
              test.TryClaimGrowthTask(catalog, testLayout, "first_contract", out _) && test.cash == 0,
            "Test-mode expansion is free and pre-existing contracts qualify, while claimed rewards never mint extra cash");
        var paid = Ready(catalog, out var paidLayout);
        paid.popularity = 10; paid.cash = 599;
        before = Snapshot(paidLayout, paid);
        check(!paid.TryExpandShop(catalog, paidLayout, out _) && Snapshot(paidLayout, paid) == before,
            "Insufficient normal funds fail atomically without moving the wall or changing the economy");
        paid.cash = 600; paid.shopOpen = true;
        before = Snapshot(paidLayout, paid);
        check(!paid.TryExpandShop(catalog, paidLayout, out _) && Snapshot(paidLayout, paid) == before,
            "An open shop cannot expand even if both funds and popularity are sufficient");
        paid.shopOpen = false;
        Supply(paid, "top-1", 1);
        paid.AddToBasket(catalog, "top-1", "still-paying", 1, out _);
        before = Snapshot(paidLayout, paid);
        check(!paid.TryExpandShop(catalog, paidLayout, out _) && Snapshot(paidLayout, paid) == before,
            "Closing with an unpaid basket cannot expand or lose the guest's inventory");
        paid.ReturnAllBaskets();
        check(paid.TryExpandShop(catalog, paidLayout, out _) && paid.cash == 0 && paidLayout.width == 10,
            "An exactly funded closed shop expands successfully after the remaining basket is resolved");
        var overflow = Ready(catalog, out var overflowLayout);
        overflow.cash = int.MaxValue;
        before = Snapshot(overflowLayout, overflow);
        check(!overflow.TryClaimGrowthTask(catalog, overflowLayout, "staff_ready", out _) &&
              Snapshot(overflowLayout, overflow) == before,
            "Reward overflow rejects the claim without marking it consumed or wrapping the cash balance");

        var offline = Ready(catalog, out var offlineLayout);
        offline.shopOpen = true;
        Supply(offline, "top-1", 10);
        offline.Stock("top_tshirt").purchased += 90;
        offline.Stock("top_tshirt").available += 90;
        check(offline.CaptureOfflineBaseline(catalog, offlineLayout, 1700000000, out _) &&
              offline.TryApplyOfflineIncome(catalog, offlineLayout, 1700036000, out var offlineReport, out _) &&
              offlineReport.unitsSold == 50 && offline.transactions == 0 && offline.OnlineUnitsSold == 0 &&
              !offline.GetGrowthTaskStates().Find(task => task.definition.id == "first_checkout").complete &&
              !offline.GetGrowthTaskStates().Find(task => task.definition.id == "sell_ten").complete,
            "Fifty offline sold units generate money but never impersonate a live first transaction or online sales task");

        var invalid = Clone(economy);
        invalid.claimedGrowthTaskIds.Add("staff_ready");
        check(!invalid.Validate(catalog, layout, out _), "Duplicate reward claims invalidate the saved growth ledger");
        invalid = Clone(economy); invalid.claimedGrowthTaskIds[1] = "unknown";
        check(!invalid.Validate(catalog, layout, out _), "Unknown reward IDs invalidate the saved growth ledger");
        invalid = Clone(economy); invalid.claimedGrowthTaskIds.RemoveAt(0);
        check(!invalid.Validate(catalog, layout, out _), "Out-of-sequence reward claims invalidate the saved growth ledger");
        invalid = Clone(economy); invalid.areaExpansionLevel = 1;
        check(!invalid.Validate(catalog, layout, out _), "Room dimensions must agree with the stored expansion stage");
        invalid = Clone(economy); invalid.growthVersion = 0;
        check(!invalid.Validate(catalog, layout, out _), "Pre-growth snapshots cannot silently contain later expansion or claims");
        invalid = Clone(economy); invalid.claimedGrowthTaskIds = null;
        check(!invalid.Validate(catalog, layout, out _), "A missing claim ledger is rejected in memory rather than repaired by granting rewards again");

        var old = Ready(catalog, out var oldLayout);
        string oldJson = Snapshot(oldLayout, old);
        foreach (string field in new[] { "\"growthVersion\":0", "\"areaExpansionLevel\":0", "\"claimedGrowthTaskIds\":[]" })
            oldJson = oldJson.Replace("," + field, "").Replace(field + ",", "");
        string oldPath = Path.Combine(output, "pre-growth.json");
        File.WriteAllText(oldPath, oldJson);
        var migrated = ShopSession.LoadFromPath(catalog, oldPath, out _);
        check(migrated != null && migrated.economy.ExpansionLevel == 0 &&
              migrated.economy.claimedGrowthTaskIds.Count == 0 &&
              JsonUtility.ToJson(migrated.layout) == JsonUtility.ToJson(oldLayout) && migrated.economy.cash == old.cash,
            "Old saves missing all growth fields load at original size with no charges, rewards or furniture relocation");
        string damagedPath = Path.Combine(output, "growth-missing-ledger.json");
        string damaged = Snapshot(layout, economy);
        int keyStart = damaged.IndexOf("\"claimedGrowthTaskIds\":", StringComparison.Ordinal);
        int arrayEnd = damaged.IndexOf(']', keyStart);
        damaged = damaged.Remove(keyStart, arrayEnd - keyStart + 1);
        damaged = damaged.Replace(",,", ",").Replace("{,", "{").Replace(",}", "}");
        File.WriteAllText(damagedPath, damaged);
        check(ShopSession.LoadFromPath(catalog, damagedPath, out _) == null,
            "A versioned growth save with its reward ledger missing is rejected, preventing automatic reward replay");

        File.WriteAllText(Path.Combine(output, "growth-verification.json"),
            JsonUtility.ToJson(new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("SHOP_GROWTH_CHECKS_OK " + results.Count);
    }

    static ShopEconomy Ready(ShopCatalog catalog, out ShopLayout layout)
    {
        layout = ShopModel.CreateDefault();
        layout.facilities.RemoveAll(item => item.definitionId == "head" || item.definitionId == "bottom" ||
            item.definitionId == "fitting" || item.definitionId == "stage");
        ShopSalesTestSetup.EnsureStaffPoints(layout, catalog);
        var economy = ShopEconomy.CreateDefault(catalog, layout);
        economy.InitializeFixtureStore(catalog, layout, out _);
        return economy;
    }

    static void Supply(ShopEconomy economy, string shelfId, int quantity)
    {
        var shelf = economy.Shelf(shelfId);
        var stock = economy.Stock(shelf.productId);
        stock.contracted = true; stock.purchased += quantity; shelf.quantity += quantity;
    }

    static void Sell(ShopCatalog catalog, ShopEconomy economy, int quantity)
    {
        while (quantity > 0)
        {
            int batch = Math.Min(quantity, 10);
            Supply(economy, "top-1", batch);
            string id = Guid.NewGuid().ToString("N");
            if (!economy.AddToBasket(catalog, "top-1", id, batch, out string reason) ||
                !economy.PayBasket(id, out reason)) throw new InvalidOperationException("GROWTH SALE SETUP: " + reason);
            quantity -= batch;
        }
    }

    static bool PreservedFurniture(ShopLayout before, ShopLayout after)
    {
        if (before.facilities.Count != after.facilities.Count) return false;
        foreach (var original in before.facilities)
        {
            var current = after.facilities.Find(item => item.instanceId == original.instanceId);
            if (current == null) return false;
            if (original.definitionId == "warehouse")
            {
                if (current.x != after.width || current.z != original.z || current.wallPosition != original.wallPosition ||
                    current.rotation != original.rotation) return false;
            }
            else if(current.x!=original.x+after.width-before.width || current.z!=original.z+after.depth-before.depth || current.rotation!=original.rotation || current.definitionId!=original.definitionId)return false;
        }
        return true;
    }

    static ShopEconomy Clone(ShopEconomy economy) => JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));
    static string Snapshot(ShopLayout layout, ShopEconomy economy) =>
        JsonUtility.ToJson(new ShopSession { layout = layout, economy = economy });
}
