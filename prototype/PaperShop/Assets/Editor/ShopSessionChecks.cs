using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopSessionChecks
{
    [Serializable] private sealed class Report { public bool passed; public int checks; public string[] results; }

    public static void Run(ShopCatalog catalog, string output)
    {
        catalog=ShopLedgerFixture.Catalog(catalog);
        Directory.CreateDirectory(output);
        string directory = Path.Combine(output, "session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        Action<bool, string> check = (condition, name) =>
        {
            if (!condition) throw new InvalidOperationException("SESSION CHECK FAILED: " + name);
            results.Add(name);
        };
        string reason;
        var fresh=ShopProfiles.NewGame(catalog,false);
        ShopLedgerFixture.Reset(catalog);
        fresh.economy.MigrateManagementRules();
        check(fresh.economy.CaptureOfflineBaseline(catalog,fresh.layout,1792512000,out reason),"Fresh migrated player profile captures an offline baseline");
        string freshPath=Path.Combine(directory,"fresh-current-rules.json");
        ShopSession.SaveToPath(fresh.layout,fresh.economy,catalog,freshPath);
        var freshLoaded=ShopSession.LoadFromPath(catalog,freshPath,out reason);
        check(freshLoaded!=null&&freshLoaded.economy.managementRulesVersion==1&&JsonUtility.ToJson(freshLoaded)==JsonUtility.ToJson(fresh),
            "Fresh current-rule profile round-trips every version and gameplay field");
        freshLoaded.economy.shopLevel=3;freshLoaded.economy.popularity=20;freshLoaded.economy.RefreshShopLevel();
        freshLoaded.economy.MigrateManagementRules();
        check(freshLoaded.economy.popularity==20&&freshLoaded.economy.ShopLevel==3,"Reloading current rules cannot migrate popularity twice");
        var layout = ShopModel.CreateDefault();
        layout.facilities.Add(new PlacedFacility { instanceId = "session-user-decor", definitionId = "decor", x = 0, z = 0, rotation = 2 });
        var economy = Stocked(catalog, layout);
        Require(economy.BeginRestock(catalog, layout, "top-1", out reason), reason);
        Require(economy.Pickup(out reason), reason);
        Require(economy.CompleteRestock(catalog, layout, out reason), reason);
        Require(economy.Contract(catalog, "shoes_canvas", out reason), reason);
        Require(economy.Order(catalog, "shoes_canvas", out reason), reason);
        economy.Tick(ShopEconomy.DispatchSeconds + .75f);
        Require(economy.Expand(out reason), reason);
        Require(economy.Expand(out reason), reason);
        economy.autoRestock = false;
        economy.autoRestockThreshold = 0.5f;
        var original = new ShopSession { layout = layout, economy = economy };
        string originalJson = JsonUtility.ToJson(original);
        string path = Path.Combine(directory, "shop-session-v1.json");
        ShopSession.SaveToPath(layout, economy, catalog, path);
        var loaded = ShopSession.LoadFromPath(catalog, path, out string message);
        check(loaded != null && JsonUtility.ToJson(loaded) == originalJson, "A single file round-trips furniture and economy as one snapshot");
        check(loaded.economy.jobs != null && loaded.economy.jobs.Count == 0 && loaded.economy.job == null,
              "An idle task list stays empty after JSON round-trip instead of creating a phantom restock task");
        check(loaded.economy.warehouseLevel == 3 && loaded.economy.Capacity == 600 && loaded.economy.cash == economy.cash &&
              loaded.economy.Stock("top_tshirt").contracted && loaded.economy.Stock("shoes_canvas").contracted &&
              loaded.economy.Shelf("top-1").quantity == 10 && loaded.economy.orders.Count == 1 &&
              Math.Abs(loaded.economy.orders[0].remaining - 2.25f) < 0.001f && !loaded.economy.autoRestock && loaded.economy.autoRestockThreshold == 0.5f,
              "Orders retain remaining time, contracts, upgrades, shelf stock and automatic-restock settings");
        loaded.economy.cash--;
        loaded.layout.facilities.Find(item => item.instanceId == "session-user-decor").rotation = 1;
        check(JsonUtility.ToJson(original) == originalJson, "The loaded snapshot shares neither mutable economy nor placements with the original");

        string primaryBefore = File.ReadAllText(path);
        string backupBefore = File.ReadAllText(path + ".bak");
        var mismatchedLayout = ShopModel.Clone(layout);
        mismatchedLayout.facilities.RemoveAll(item => item.instanceId == "top-1");
        bool rejected = RejectSave(mismatchedLayout, economy, catalog, path);
        check(rejected && primaryBefore == File.ReadAllText(path) && backupBefore == File.ReadAllText(path + ".bak"),
              "A layout/economy shelf mismatch is rejected before either committed file changes");
        var invalidEconomy = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));
        invalidEconomy.Stock("top_tshirt").available--;
        check(RejectSave(layout, invalidEconomy, catalog, path) && RejectSave(layout, null, catalog, path) &&
              primaryBefore == File.ReadAllText(path) && backupBefore == File.ReadAllText(path + ".bak"),
              "Lost merchandise or a null economy cannot overwrite a valid primary or backup");

        for (int state = 0; state < 2; state++)
        {
            var taskEconomy = Stocked(catalog, layout);
            Require(taskEconomy.BeginRestock(catalog, layout, "top-1", out reason), reason);
            if (state == 1) Require(taskEconomy.Pickup(out reason), reason);
            string untouched = JsonUtility.ToJson(taskEconomy);
            string taskPath = Path.Combine(directory, "restock-" + state + ".json");
            ShopSession.SaveToPath(layout, taskEconomy, catalog, taskPath);
            var resumed = ShopSession.LoadFromPath(catalog, taskPath, out message);
            check(resumed != null && resumed.economy.job != null && resumed.economy.job.pickedUp == (state == 1) &&
                  JsonUtility.ToJson(resumed.economy) == untouched,
                  "Load preserves the active " + (state == 0 ? "reserved" : "carrying") + " task without cancelling it");
            resumed.economy.CancelRestock();
            resumed.economy.CancelRestock();
            check(resumed.economy.Validate(catalog, resumed.layout, out reason) && resumed.economy.job == null &&
                  resumed.economy.Stock("top_tshirt").available == 20 && resumed.economy.Stock("top_tshirt").reserved == 0 &&
                  resumed.economy.Carrying == 0 && resumed.economy.Shelf("top-1").quantity == 0 && JsonUtility.ToJson(taskEconomy) == untouched,
                  "The caller can cancel the loaded task twice without losing, duplicating or mutating original merchandise: " + state);

            // Remove only the task record; keep purchased, available and reserved untouched.
            // These malformed fixtures must fail for both pending pickup and carried goods.
            var lostTask = JsonUtility.FromJson<ShopEconomy>(untouched);
            lostTask.jobs.Clear();
            string lostTaskJson = JsonUtility.ToJson(new ShopSession { layout = layout, economy = lostTask });
            CheckRejectedJson(catalog, directory, "lost-active-task-missing-" + state, WithoutTaskList(lostTaskJson, false), check);
            CheckRejectedJson(catalog, directory, "lost-active-task-null-" + state, WithoutTaskList(lostTaskJson, true), check);
        }

        CheckRejectedJson(catalog, directory, "missing-objects", "{\"version\":1}", check);
        CheckRejectedJson(catalog, directory, "null-objects", "{\"version\":1,\"layout\":null,\"economy\":null}", check);
        CheckRejectedJson(catalog, directory, "empty-objects", "{\"version\":1,\"layout\":{},\"economy\":{}}", check);
        var missingList = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));
        missingList.stocks = null;
        CheckRejectedJson(catalog, directory, "null-stock-list", JsonUtility.ToJson(new ShopSession { layout = layout, economy = missingList }), check);
        CheckIdleCompatibility(catalog, directory, "missing-idle-task-list", WithoutTaskList(originalJson, false), originalJson, check);
        CheckIdleCompatibility(catalog, directory, "null-idle-task-list", WithoutTaskList(originalJson, true), originalJson, check);
        CheckRejectedJson(catalog, directory, "future-version", JsonUtility.ToJson(new ShopSession { version = 2, layout = layout, economy = economy }), check);

        layout.facilities.Add(new PlacedFacility { instanceId = "second-revision-decor", definitionId = "decor", x = 0, z = 2 });
        Require(economy.Contract(catalog, "head_ribbon", out reason), reason);
        Require(economy.Order(catalog, "head_ribbon", out reason), reason);
        ShopSession.SaveToPath(layout, economy, catalog, path);
        File.WriteAllText(path, "{truncated");
        var recovered = ShopSession.LoadFromPath(catalog, path, out message);
        check(recovered != null && JsonUtility.ToJson(recovered) == originalJson &&
              recovered.layout.facilities.Find(item => item.instanceId == "second-revision-decor") == null &&
              !recovered.economy.Stock("head_ribbon").contracted,
              "Backup recovery restores the matching earlier furniture and economy revision together");
        var repaired = ShopSession.LoadFromPath(catalog, path, out message);
        check(repaired != null && JsonUtility.ToJson(repaired) == originalJson, "Recovered primary is readable on the next load");
        File.WriteAllText(path, "broken-primary");
        File.WriteAllText(path + ".bak", "broken-backup");
        check(ShopSession.LoadFromPath(catalog, path, out message) == null && File.ReadAllText(path) == "broken-primary" &&
              File.ReadAllText(path + ".bak") == "broken-backup", "Two invalid session files stay untouched and never become default progress");
        check(ShopSession.SavePath==ShopProfiles.PathFor(1), "Combined progress uses the selected normal slot filename");
        File.WriteAllText(Path.Combine(output, "session-verification.json"), JsonUtility.ToJson(new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("SHOP_SESSION_CHECKS_OK " + results.Count);
    }

    private static void CheckRejectedJson(ShopCatalog catalog, string directory, string name, string json, Action<bool, string> check)
    {
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, json);
        string message;
        check(ShopSession.LoadFromPath(catalog, path, out message) == null && File.ReadAllText(path) == json && !File.Exists(path + ".bak"),
              "Malformed or quantity-inconsistent session is rejected without overwriting files: " + name);
    }

    private static void CheckIdleCompatibility(ShopCatalog catalog, string directory, string name, string json, string expectedJson, Action<bool, string> check)
    {
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, json);
        var loaded = ShopSession.LoadFromPath(catalog, path, out string message);
        check(loaded != null && loaded.economy.jobs != null && loaded.economy.jobs.Count == 0 && loaded.economy.job == null &&
              JsonUtility.ToJson(loaded) == expectedJson && File.ReadAllText(path) == json && !File.Exists(path + ".bak"),
              "An absent idle task list is compatible only when reservations and merchandise still balance: " + name);
    }

    // Fixture mutation of a known JsonUtility output, not a parser used on loaded saves.
    private static string WithoutTaskList(string json, bool nullField)
    {
        const string emptyJobs = "\"jobs\":[]";
        Require(json.Contains(emptyJobs), "Fixture must contain an explicit empty restock task list.");
        return nullField ? json.Replace(emptyJobs, "\"jobs\":null") :
            json.Replace("," + emptyJobs, "").Replace(emptyJobs + ",", "");
    }

    private static bool RejectSave(ShopLayout layout, ShopEconomy economy, ShopCatalog catalog, string path)
    {
        try { ShopSession.SaveToPath(layout, economy, catalog, path); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private static ShopEconomy Stocked(ShopCatalog catalog, ShopLayout layout)
    {
        var economy = ShopEconomy.CreateDefault(catalog, layout);
        string reason;
        Require(economy.Contract(catalog, "top_tshirt", out reason), reason);
        Require(economy.Order(catalog, "top_tshirt", out reason), reason);
        economy.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        return economy;
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("Session fixture failed: " + reason);
    }
}
