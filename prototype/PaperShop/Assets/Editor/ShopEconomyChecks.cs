using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopEconomyChecks
{
    [Serializable] private sealed class Report { public bool passed; public int checks; public string[] results; }

    public static void Run(ShopCatalog catalog, string output)
    {
        catalog=ShopLedgerFixture.Catalog(catalog);
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (condition, name) =>
        {
            if (!condition) throw new InvalidOperationException("ECONOMY CHECK FAILED: " + name);
            results.Add(name);
        };
        var layout = ShopModel.CreateDefault();
        var economy = ShopEconomy.CreateDefault(catalog, layout);
        string reason;
        check(economy.Validate(catalog, layout, out reason) && economy.cash == 2500 && economy.Capacity == 200 &&
              economy.autoRestock && economy.autoRestockThreshold == .5f && economy.shelves.Count == 4,
              "Default state has 2500 cash, 200 warehouse capacity, four assigned empty shelves and automatic restocking");
        check(economy.stocks.TrueForAll(stock => !stock.contracted && stock.purchased == 0) && economy.WarehouseUsed == 0 && economy.InTransit == 0,
              "Default bindings do not grant a contract or free inventory");
        var idleCopy = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));
        check(idleCopy.jobs != null && idleCopy.jobs.Count == 0 && idleCopy.job == null && idleCopy.Validate(catalog, layout, out reason),
              "An empty job list stays empty after Unity JSON round trip without inventing a task");
        string before = JsonUtility.ToJson(economy);
        check(!economy.Order(catalog, "top_tshirt", out reason) && before == JsonUtility.ToJson(economy), "Unsigned products cannot be ordered and failure is atomic");
        check(economy.Contract(catalog, "top_tshirt", out reason) && economy.cash == 2420, "First contract deducts exactly 80 cash");
        before = JsonUtility.ToJson(economy);
        check(!economy.Contract(catalog, "top_tshirt", out reason) && before == JsonUtility.ToJson(economy), "Repeated signing never charges again");
        check(economy.Order(catalog, "top_tshirt", out reason) && economy.cash == 2320 && economy.InTransit == 20 && economy.WarehouseUsed == 0,
              "One order pays 100 for 20 units and reserves inbound capacity");
        var incomingCopy = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));
        incomingCopy.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        check(incomingCopy.Validate(catalog, layout, out reason) && incomingCopy.Stock("top_tshirt").available == 20 && economy.InTransit == 20 && economy.WarehouseUsed == 0,
              "In-transit JSON round trip delivers exactly once without mutating the original: " + reason);
        before = JsonUtility.ToJson(economy);
        economy.Tick(0f);
        economy.Tick(-1f);
        economy.Tick(float.NaN);
        check(before == JsonUtility.ToJson(economy), "Paused, negative and nonfinite delta time cannot advance delivery");
        economy.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds - 1f);
        check(economy.InTransit == 20 && economy.Stock("top_tshirt").available == 0, "Delivery remains in transit until the complete dispatch and transport interval elapses");
        economy.Tick(1f);
        economy.Tick(100f);
        check(economy.InTransit == 0 && economy.Stock("top_tshirt").available == 20 && economy.Validate(catalog, layout, out reason),
              "Delivery arrives once and preserves per-product conservation");
        check(economy.BeginRestock(catalog, layout, "top-1", out reason) && economy.Stock("top_tshirt").available == 10 &&
              economy.Stock("top_tshirt").reserved == 10 && economy.WarehouseUsed == 20 && economy.Carrying == 0,
              "Restocking reserves shelf space and moves available units into warehouse reservation");
        before = JsonUtility.ToJson(economy);
        check(!economy.BeginRestock(catalog, layout, "top-1", out reason) && before == JsonUtility.ToJson(economy), "A second task cannot duplicate reservations");
        check(economy.Pickup(out reason) && economy.Stock("top_tshirt").reserved == 0 && economy.Carrying == 10 && economy.WarehouseCommitted == 20,
              "Pickup transfers ownership to carrying while retaining return capacity");
        before = JsonUtility.ToJson(economy);
        check(!economy.Pickup(out reason) && before == JsonUtility.ToJson(economy), "Repeated pickup cannot duplicate merchandise");
        check(economy.CompleteRestock(catalog, layout, out reason) && economy.Shelf("top-1").quantity == 10 && economy.Carrying == 0 &&
              economy.WarehouseCommitted == 10 && economy.Validate(catalog, layout, out reason), "Shelving completes once, releases return capacity and conserves stock");
        before = JsonUtility.ToJson(economy);
        check(!economy.CompleteRestock(catalog, layout, out reason) && !economy.BeginRestock(catalog, layout, "top-1", out reason) &&
              reason.Contains("已满") && before == JsonUtility.ToJson(economy), "Completion is idempotent and a full shelf produces an explicit refusal");
        check(!economy.Assign(catalog, layout, "top-1", "shoes_canvas", out reason) && reason.Contains("类别"), "Category mismatch is rejected");
        check(!economy.Assign(catalog, layout, "top-1", "top_shirt", out reason), "A stocked shelf cannot silently change product");

        var empty = ShopEconomy.CreateDefault(catalog, layout);
        empty.Contract(catalog, "top_tshirt", out reason);
        check(!empty.BeginRestock(catalog, layout, "top-1", out reason) && reason.Contains("缺货"), "Missing inventory is reported separately from a full shelf");
        var lowCash = ShopEconomy.CreateDefault(catalog, layout);
        lowCash.cash = 79;
        before = JsonUtility.ToJson(lowCash);
        check(!lowCash.Contract(catalog, "top_tshirt", out reason) && !lowCash.Expand(out reason) && before == JsonUtility.ToJson(lowCash), "Insufficient cash cannot partially apply signing or upgrades");

        for (int state = 0; state < 3; state++)
        {
            var original = Stocked(catalog, layout);
            if (state >= 1) original.BeginRestock(catalog, layout, "top-1", out reason);
            if (state >= 2) original.Pickup(out reason);
            string json = JsonUtility.ToJson(original);
            var loaded = JsonUtility.FromJson<ShopEconomy>(json);
            check(loaded.Validate(catalog, layout, out reason) && json == JsonUtility.ToJson(loaded), "JSON round trip preserves warehouse/reserved/carrying state " + state);
            loaded.CancelRestock();
            loaded.CancelRestock();
            check(loaded.Validate(catalog, layout, out reason) && loaded.Stock("top_tshirt").available == 20 && loaded.Carrying == 0 &&
                  loaded.Stock("top_tshirt").reserved == 0 && json == JsonUtility.ToJson(original), "Cancellation after load is lossless, repeatable and independent of original state " + state);
        }

        var full = ShopEconomy.CreateDefault(catalog, layout);
        full.cash = 10000;
        full.Contract(catalog, "top_tshirt", out reason);
        for (int i = 0; i < 10; i++) if (!full.Order(catalog, "top_tshirt", out reason)) throw new InvalidOperationException(reason);
        before = JsonUtility.ToJson(full);
        check(full.InTransit == 200 && !full.Order(catalog, "top_tshirt", out reason) && before == JsonUtility.ToJson(full), "Ten inbound batches fill capacity before any delivery arrives");
        full.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        full.BeginRestock(catalog, layout, "top-1", out reason);
        full.Pickup(out reason);
        before = JsonUtility.ToJson(full);
        check(full.WarehouseUsed == 190 && full.Carrying == 10 && !full.Order(catalog, "top_tshirt", out reason) && before == JsonUtility.ToJson(full),
              "Carrying keeps ten return slots so new orders cannot overbook cancellation capacity");
        full.CancelRestock();
        check(full.WarehouseUsed == 200 && full.Validate(catalog, layout, out reason), "Cancellation into a committed full warehouse remains valid");
        full.BeginRestock(catalog, layout, "top-1", out reason);
        full.Pickup(out reason);
        full.CompleteRestock(catalog, layout, out reason);
        var fullLayout = ShopModel.Clone(layout);
        fullLayout.facilities.Add(new PlacedFacility { instanceId = "top-second", definitionId = "top", x = 0, z = 0 });
        if (!full.SyncShelves(catalog, fullLayout, out reason) || !full.BeginRestock(catalog, fullLayout, "top-second", out reason) || !full.Pickup(out reason))
            throw new InvalidOperationException(reason);
        before = JsonUtility.ToJson(full);
        check(full.WarehouseUsed == 180 && full.Carrying == 10 && !full.Order(catalog, "top_tshirt", out reason) && before == JsonUtility.ToJson(full),
              "A physically fitting 20-unit order is rejected when ten carried units reserve return slots");
        full.CancelRestock();
        check(full.WarehouseUsed == 190 && full.Shelf("top-1").quantity == 10 && full.Validate(catalog, fullLayout, out reason),
              "The rejected overbooking leaves enough room to restore all carried merchandise");
        int cashBefore = full.cash;
        check(full.Expand(out reason) && full.Capacity == 400 && full.cash == cashBefore - 400 && full.UpgradeCost == 800, "Level two warehouse costs 400 and holds 400 units");
        cashBefore = full.cash;
        check(full.Expand(out reason) && full.Capacity == 600 && full.cash == cashBefore - 800, "Level three warehouse costs 800 and holds 600 units");
        before = JsonUtility.ToJson(full);
        check(!full.Expand(out reason) && before == JsonUtility.ToJson(full), "Maximum warehouse level refuses further charges");

        var automatic = Stocked(catalog, layout);
        check(automatic.NextRestock(catalog, layout) == "top-1", "Automatic restocking finds an empty contracted shelf with available merchandise");
        automatic.autoRestock = false;
        check(automatic.NextRestock(catalog, layout) == null, "Automatic restocking toggle stops new tasks");
        automatic.autoRestock = true;
        automatic.Shelf("top-1").quantity = 5;
        automatic.Stock("top_tshirt").available = 15;
        check(automatic.Validate(catalog, layout, out reason) && automatic.NextRestock(catalog, layout) == null, "Exactly five of ten units does not trigger the strictly-below-50-percent rule");
        automatic.Shelf("top-1").quantity = 4;
        automatic.Stock("top_tshirt").available = 16;
        check(automatic.Validate(catalog, layout, out reason) && automatic.NextRestock(catalog, layout) == "top-1", "Four of ten units triggers automatic restocking below 50 percent");
        cashBefore = automatic.cash;
        check(automatic.BeginRestock(catalog, layout, "top-1", out reason) &&
              automatic.NextRestock(catalog, layout) == null && automatic.job.quantity == 6 &&
              automatic.Stock("top_tshirt").available == 10 && automatic.Stock("top_tshirt").reserved == 6,
              "An active task suppresses duplicates and reserves exactly the six missing units");
        check(automatic.Pickup(out reason) && automatic.CompleteRestock(catalog, layout, out reason) &&
              automatic.Shelf("top-1").quantity == 10 && automatic.Stock("top_tshirt").available == 10 &&
              automatic.Stock("top_tshirt").reserved == 0 && automatic.Carrying == 0 &&
              automatic.cash == cashBefore && automatic.Validate(catalog, layout, out reason),
              "A threshold-triggered trip preserves all twenty units and never changes cash");

        var changedLayout = ShopModel.Clone(layout);
        changedLayout.facilities.RemoveAll(item => item.instanceId == "top-1");
        before = JsonUtility.ToJson(economy);
        check(!economy.SyncShelves(catalog, changedLayout, out reason) && before == JsonUtility.ToJson(economy), "Removing a stocked shelf cannot discard merchandise");
        var interrupted = Stocked(catalog, layout);
        interrupted.BeginRestock(catalog, layout, "top-1", out reason);
        interrupted.Pickup(out reason);
        before = JsonUtility.ToJson(interrupted);
        check(!interrupted.CompleteRestock(catalog, changedLayout, out reason) && !interrupted.SyncShelves(catalog, changedLayout, out reason) &&
              before == JsonUtility.ToJson(interrupted), "Missing target cannot swallow carrying stock or remove a busy shelf");
        interrupted.CancelRestock();
        check(interrupted.SyncShelves(catalog, changedLayout, out reason) && interrupted.Validate(catalog, changedLayout, out reason) &&
              interrupted.Stock("top_tshirt").available == 20, "Interrupted task returns all stock before empty shelf removal");
        changedLayout.facilities.Add(new PlacedFacility { instanceId = "top-new", definitionId = "top", x = 0, z = 0 });
        check(interrupted.SyncShelves(catalog, changedLayout, out reason) && interrupted.Shelf("top-new").productId == "top_tshirt" &&
              interrupted.Shelf("top-new").quantity == 0 && interrupted.Validate(catalog, changedLayout, out reason), "New shelf receives a default binding without generating goods");

        var invalid = Stocked(catalog, layout);
        invalid.Stock("top_tshirt").available--;
        check(!invalid.Validate(catalog, layout, out reason) && reason.Contains("不守恒"), "Load validation catches a lost unit rather than silently accepting corruption");
        invalid = Stocked(catalog, layout);
        invalid.stocks.Add(invalid.stocks[0]);
        check(!invalid.Validate(catalog, layout, out reason), "Load validation rejects duplicated stock records");
        invalid = Stocked(catalog, layout);
        invalid.Stock("top_tshirt").reserved = 1;
        check(!invalid.Validate(catalog, layout, out reason), "Load validation rejects reservations without a matching job");
        invalid = Stocked(catalog, layout);
        invalid.Order(catalog, "top_tshirt", out reason);
        invalid.orders[0].remaining = float.NaN;
        check(!invalid.Validate(catalog, layout, out reason), "Load validation rejects nonfinite delivery timers");
        invalid = Stocked(catalog, layout);
        invalid.jobs = null;
        check(!invalid.Validate(catalog, layout, out reason), "Load validation rejects a missing job list instead of inventing an idle state");
        invalid = Stocked(catalog, layout);
        invalid.jobs.Add(null);
        check(!invalid.Validate(catalog, layout, out reason), "Load validation rejects a null entry inside the job list");
        invalid = Stocked(catalog, layout);
        invalid.BeginRestock(catalog, layout, "top-1", out reason);
        invalid.jobs.Add(invalid.job);
        check(!invalid.Validate(catalog, layout, out reason), "Load validation rejects multiple jobs for the single clerk");

        File.WriteAllText(Path.Combine(output, "economy-verification.json"), JsonUtility.ToJson(new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("SHOP_ECONOMY_CHECKS_OK " + results.Count);
    }

    private static ShopEconomy Stocked(ShopCatalog catalog, ShopLayout layout)
    {
        var economy = ShopEconomy.CreateDefault(catalog, layout);
        string reason;
        if (!economy.Contract(catalog, "top_tshirt", out reason) || !economy.Order(catalog, "top_tshirt", out reason))
            throw new InvalidOperationException(reason);
        economy.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        return economy;
    }
}
