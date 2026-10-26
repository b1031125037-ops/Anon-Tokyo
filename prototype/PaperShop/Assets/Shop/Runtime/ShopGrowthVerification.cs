using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Isolated acceptance of both expansions, physical selling and the ordered growth rewards.</summary>
    [DefaultExecutionOrder(-420)]
    public sealed class ShopGrowthVerification : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool passed, timedOut, inventoryConserved = true, uniqueCustomerStops = true;
            public bool insufficientPopularityAtomic, insufficientFundsAtomic, openShopExpansionRejected;
            public bool firstExpansion, secondExpansion, maximumExpansionAtomic;
            public bool firstExpansionPreservesFixtures, secondExpansionPreservesFixtures;
            public bool firstExpansionPreservesGoods, secondExpansionPreservesGoods;
            public bool expandedWorldCellsMatch, warehouseDoorMovesOnExpandedWall;
            public bool actualExpandedWarehousePickup, actualExpandedRestock, expandedRoadReachable;
            public bool checkoutFrontAndRearCorrect, salesAfterFirstExpansion, salesAfterSecondExpansion;
            public bool taskOrderEnforced, rewardsCreditedOnce = true, tenTasksClaimed;
            public bool firstExpansionSaveRoundTrip, finalGrowthSaveRoundTrip, loadedRewardCannotRepeat;
            public int firstWidth, finalWidth, claimedTasks, paidRewards, sales, onlineUnitsSold;
            public int warehousePickups, restockJobs, screenshots;
            public float realtimeSeconds;
            public string[] errors;
        }
        [Serializable] sealed class GoodsSnapshot
        {
            public List<ProductStock> stocks;
            public List<ShelfStock> shelves;
            public List<StaffPostState> posts;
            public List<StoredFixture> stored;
        }
        ShopApp app;
        Report report;
        string output;
        bool active, finished, observeWarehouse;
        float started, deadline, originalScale, nextValidate;
        int pickupsBefore, restocksBefore;
        readonly List<string> errors = new List<string>();
        readonly HashSet<string> uniqueErrors = new HashSet<string>(StringComparer.Ordinal);

        void Awake()
        {
            active = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-growth") >= 0;
            if (active) Application.logMessageReceived += Log;
        }
        void Update()
        {
            if (!active || finished || report == null) return;
            if (app != null && app.Economy != null)
            {
                report.uniqueCustomerStops &= app.CustomerStopConflicts == 0;
                if (observeWarehouse && app.WarehousePickups > pickupsBefore &&
                    app.ActorCell == ShopModel.WarehousePickup(app.Layout) &&
                    app.ActorCell.x == app.Layout.width - 1 && app.Economy.Carrying > 0)
                    report.actualExpandedWarehousePickup = true;
                if (Time.realtimeSinceStartup >= nextValidate)
                {
                    nextValidate = Time.realtimeSinceStartup + .4f;
                    Validate("live growth simulation");
                }
            }
            if (Time.realtimeSinceStartup >= deadline)
            {
                report.timedOut = true;
                Error("Growth verification exceeded 100 seconds.");
                Finish();
            }
        }
        IEnumerator Start()
        {
            if (!active) yield break;
            originalScale = Time.timeScale;
            started = Time.realtimeSinceStartup;
            deadline = started + 100f;
            report = new Report();
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--verify-growth");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            { Error("--verify-growth needs an output directory."); Finish(); yield break; }
            try { output = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(output); }
            catch (Exception ex) { Error(ex.Message); Finish(); yield break; }
            Application.runInBackground = true;
            app = GetComponent<ShopApp>();
            yield return null;
            if (app == null || app.World == null || app.Economy == null)
            { Error("Shop did not initialize."); Finish(); yield break; }
            app.Economy.EnableTestResources();
            Check(app.ForceResetCustomersForVerification(), "Isolated verification reset unavailable.");
            app.StartTwoCounterTest();
            app.ForceResetCustomersForVerification();
            app.SetPaused(false);
            app.ApplyApplicationFocus(true);
            app.ApplyApplicationPause(false);
            app.SetGameSpeed(2);
            Time.timeScale = 6f;
            app.SetGuideEnabled(false);
            app.SetAutoRestock(false);
            app.CancelRestockTask();
            var firstShelf = app.Layout.facilities.Find(f => f.definitionId == "top");
            var secondShelf = app.Layout.facilities.Find(f => f.definitionId == "shoes");
            var checkout = app.Layout.facilities.Find(f => f.definitionId == "checkout");
            var restock = app.Layout.facilities.Find(f => f.definitionId == "restock");
            if (firstShelf == null || secondShelf == null || checkout == null || restock == null)
            { Error("Required growth test facilities are missing."); Finish(); yield break; }
            Check(app.UpgradeSalesCounter(firstShelf.instanceId), "Initial shelf upgrade failed.");
            Check(app.UpgradeSalesCounter(secondShelf.instanceId), "Second shelf upgrade failed.");
            Check(app.UpgradeStaffPost(checkout.instanceId), "Initial checkout upgrade failed.");
            Check(app.UpgradeStaffPost(restock.instanceId), "Initial restocker upgrade failed.");
            Check(app.SetShelfRestockThreshold(firstShelf.instanceId, .75f), "Shelf threshold setup failed.");
            app.SetRestockThreshold(.75f);
            app.SetTestPopularity(0);
            report.insufficientPopularityAtomic = RejectExpansionWithoutMutation();
            app.SetTestPopularity(20);
            app.Economy.testMode = false;
            app.Economy.cash = 0;
            report.insufficientFundsAtomic = RejectExpansionWithoutMutation();
            app.Economy.cash = 8000;
            app.SetShopOpen(true);
            report.openShopExpansionRejected = app.Economy.shopOpen && RejectExpansionWithoutMutation();
            app.RequestCloseShop(out _);
            Check(app.CanRenovate, "Empty shop did not close for expansion.");
            string beforeOutOfOrder = JsonUtility.ToJson(app.Economy);
            report.taskOrderEnforced = !app.ClaimGrowthTask("first_contract") &&
                JsonUtility.ToJson(app.Economy) == beforeOutOfOrder;
            Claim("staff_ready");
            Claim("first_contract");
            // Supply is obtained through real purchase/delivery APIs. Neither
            // the sales counters nor the sold statistics are manually seeded.
            for (int i = 0; i < 4; i++)
            {
                Check(app.OrderProduct(app.Economy.Shelf(firstShelf.instanceId).productId), "Top stock order failed.");
                Check(app.OrderProduct(app.Economy.Shelf(secondShelf.instanceId).productId), "Shoe stock order failed.");
            }
            app.SetAutoRestock(true);
            float stage = Mathf.Min(deadline - 65f, Time.realtimeSinceStartup + 25f);
            while ((app.Economy.shelves.Exists(s => s.quantity < 20) || app.IsRestocking ||
                    app.Economy.orders.Count > 0) && Time.realtimeSinceStartup < stage) yield return null;
            Check(app.Economy.shelves.TrueForAll(s => s.quantity > 0), "Real delivery/replenishment did not stock the shop.");
            app.SetAutoRestock(false);
            app.CancelRestockTask();
            Claim("shelf_ready");
            Check(app.BuyCounter("head"), "Third owned fixture purchase failed.");
            string fixtures = FixtureSnapshot();
            string goods = Goods();
            int cashBefore = app.Economy.cash;
            int cost = app.Economy.NextExpansionCost;
            report.firstExpansion = app.ExpandShop() && app.Layout.width == 10 && app.Layout.depth == 8 &&
                app.Economy.ExpansionLevel == 1 && app.Economy.cash == cashBefore - cost;
            report.firstWidth = app.Layout.width;
            report.firstExpansionPreservesFixtures = fixtures == FixtureSnapshot();
            report.firstExpansionPreservesGoods = goods == Goods();
            Check(report.firstExpansion, "First expansion failed: " + app.LastMessage);
            if (!report.firstExpansion) { Finish(); yield break; }
            report.firstExpansionSaveRoundTrip = SaveRoundTrip("first-expansion-session.json", false);
            report.warehouseDoorMovesOnExpandedWall = app.MoveWarehouseDoor(5) &&
                ShopModel.WarehousePickup(app.Layout) == new Vector2Int(9, 5) &&
                app.Layout.facilities.Find(f => f.definitionId == "warehouse").x == 10 &&
                Mathf.Abs(app.World.WarehouseDoorPosition.x - (app.Layout.width * .5f - .1f)) < .01f;
            Check(report.warehouseDoorMovesOnExpandedWall, "Expanded warehouse door did not move along the current right wall.");
            app.World.ResetCamera();
            yield return Capture("01-first-expansion-10x8.png");
            app.OpenGrowth();
            yield return Capture("02-growth-tasks-in-progress.png");
            app.OpenPanel("growth");
            var payment = ShopModel.PaymentCell(app.Layout, app.Catalog, checkout);
            var cashier = ShopModel.CheckoutStaffCell(checkout, app.Catalog.Find("checkout"));
            report.checkoutFrontAndRearCorrect = payment != cashier &&
                ShopModel.CanUseCheckoutFront(app.Layout, app.Catalog, checkout, out _) &&
                HorizontalMatch(app.World.CheckoutPaymentMarkerPosition(checkout.instanceId), app.World.CellCenter(payment)) &&
                HorizontalMatch(app.World.CheckoutStaffMarkerPosition(checkout.instanceId), app.World.CellCenter(cashier));
            report.expandedRoadReachable = ShopModel.FindPath(app.Layout, app.Catalog,
                new Vector2Int(app.Layout.width, 10), ShopModel.EntranceCell).Count > 0;
            pickupsBefore = app.WarehousePickups;
            restocksBefore = app.RestockJobsCompleted;
            observeWarehouse = true;
            int initialSales = app.Economy.transactions;
            Check(app.SetCustomerFlowStressTest(true), "Growth verification arrival fixture unavailable.");
            app.SetCustomerLimit(5);
            app.SetAutoRestock(true);
            app.SetShopOpen(true);
            stage = Mathf.Min(deadline - 25f, Time.realtimeSinceStartup + 45f);
            while (app.Economy.OnlineUnitsSold < 50 && Time.realtimeSinceStartup < stage) yield return null;
            report.salesAfterFirstExpansion = app.Economy.transactions > initialSales &&
                app.PaymentsAtFront > 0 && app.ArrivalsSpawned > 0 && app.DepartedCustomers > 0;
            report.actualExpandedRestock = app.RestockJobsCompleted > restocksBefore && app.WarehousePickups > pickupsBefore;
            Claim("first_checkout");
            Claim("sell_ten");
            Claim("three_counters");
            Claim("upgraded_post");
            Claim("first_expansion");
            Claim("assortment_growth");
            app.RequestCloseShop(out _);
            stage = Mathf.Min(deadline - 15f, Time.realtimeSinceStartup + 12f);
            while (app.IsClosing && Time.realtimeSinceStartup < stage) yield return null;
            Check(app.CanRenovate, "Customers did not finish payment and leave before the second expansion.");
            if (!app.CanRenovate) { Finish(); yield break; }
            app.SetAutoRestock(false);
            app.CancelRestockTask();
            // The progression gate itself is tested in ordinary economy mode.
            // Set only its popularity fixture through the existing test command.
            app.Economy.testMode = true;
            app.SetTestPopularity(50);
            app.Economy.testMode = false;
            fixtures = FixtureSnapshot();
            goods = Goods();
            cashBefore = app.Economy.cash;
            cost = app.Economy.NextExpansionCost;
            report.secondExpansion = app.ExpandShop() && app.Layout.width == 12 && app.Layout.depth == 8 &&
                app.Economy.ExpansionLevel == 2 && app.Economy.cash == cashBefore - cost;
            report.secondExpansionPreservesFixtures = fixtures == FixtureSnapshot();
            report.secondExpansionPreservesGoods = goods == Goods();
            report.finalWidth = app.Layout.width;
            Check(report.secondExpansion, "Second expansion failed: " + app.LastMessage);
            Claim("second_expansion");
            report.tenTasksClaimed = app.Economy.GetGrowthTaskStates().Count == 10 &&
                app.Economy.GetGrowthTaskStates().TrueForAll(t => t.claimed);
            report.claimedTasks = app.Economy.GetGrowthTaskStates().FindAll(t => t.claimed).Count;
            report.maximumExpansionAtomic = RejectExpansionWithoutMutation();
            report.finalGrowthSaveRoundTrip = SaveRoundTrip("fully-grown-session.json", true);
            report.expandedWorldCellsMatch = ShopModel.WarehousePickup(app.Layout) == new Vector2Int(11, 5) &&
                Mathf.Abs(app.World.CellCenter(new Vector2Int(11, 0)).x - 5.5f) < .01f &&
                Mathf.Abs(app.World.WarehouseDoorPosition.x - 5.9f) < .01f;
            report.expandedRoadReachable &= ShopModel.FindPath(app.Layout, app.Catalog,
                new Vector2Int(app.Layout.width, 10), ShopModel.EntranceCell).Count > 0;
            app.World.ResetCamera();
            yield return Capture("03-second-expansion-12x8.png");
            app.OpenGrowth();
            yield return Capture("04-all-growth-rewards-claimed.png");
            app.OpenPanel("growth");
            initialSales = app.Economy.transactions;
            app.SetAutoRestock(true);
            app.SetShopOpen(true);
            stage = deadline - 1f;
            while (app.Economy.transactions == initialSales && Time.realtimeSinceStartup < stage) yield return null;
            report.salesAfterSecondExpansion = app.Economy.transactions > initialSales;
            yield return Capture("05-expanded-shop-real-selling.png");
            Validate("completed growth");
            Finish();
        }
        bool RejectExpansionWithoutMutation()
        {
            string layout = JsonUtility.ToJson(app.Layout), economy = JsonUtility.ToJson(app.Economy);
            return !app.ExpandShop() && layout == JsonUtility.ToJson(app.Layout) && economy == JsonUtility.ToJson(app.Economy);
        }
        void Claim(string id)
        {
            var state = app.Economy.GetGrowthTaskStates().Find(t => t.definition.id == id);
            if (state == null) { Error("Missing task " + id); report.rewardsCreditedOnce = false; return; }
            int oldCash = app.Economy.cash;
            bool claimed = app.ClaimGrowthTask(id);
            bool paid = claimed && app.Economy.cash == oldCash + state.definition.reward;
            string after = JsonUtility.ToJson(app.Economy);
            bool duplicateRejected = !app.ClaimGrowthTask(id) && after == JsonUtility.ToJson(app.Economy);
            report.rewardsCreditedOnce &= paid && duplicateRejected;
            if (paid) report.paidRewards += state.definition.reward;
            Check(paid, "Task could not be claimed once with the exact reward: " + id + " (" + app.LastMessage + ")");
            Check(duplicateRejected, "Task duplicate mutated progress or cash: " + id);
        }
        string FixtureSnapshot()
        {
            var snapshot = new ShopLayout { width = 8, depth = 8 };
            foreach (var f in app.Layout.facilities)
                if (f.definitionId != "warehouse")
                    snapshot.facilities.Add(new PlacedFacility
                    { instanceId = f.instanceId, definitionId = f.definitionId, x = f.x, z = f.z, rotation = f.rotation, wallPosition = f.wallPosition });
            return JsonUtility.ToJson(snapshot);
        }
        string Goods() => JsonUtility.ToJson(new GoodsSnapshot
        {
            stocks = app.Economy.stocks, shelves = app.Economy.shelves,
            posts = app.Economy.staffPosts, stored = app.Economy.storedFixtures
        });
        bool SaveRoundTrip(string filename, bool allTasks)
        {
            try
            {
                string path = Path.Combine(output, filename);
                ShopSession.SaveToPath(app.Layout, app.Economy, app.Catalog, path);
                var loaded = ShopSession.LoadFromPath(app.Catalog, path, out string reason);
                if (loaded == null) { Error("Save load failed: " + reason); return false; }
                bool same = loaded.layout.width == app.Layout.width && loaded.layout.depth == app.Layout.depth &&
                    loaded.economy.ExpansionLevel == app.Economy.ExpansionLevel &&
                    loaded.economy.cash == app.Economy.cash && loaded.economy.Validate(app.Catalog, loaded.layout, out _) &&
                    JsonUtility.ToJson(loaded.layout) == JsonUtility.ToJson(app.Layout) &&
                    loaded.economy.GetGrowthTaskStates().FindAll(t => t.claimed).Count ==
                        app.Economy.GetGrowthTaskStates().FindAll(t => t.claimed).Count;
                if (allTasks)
                {
                    string before = JsonUtility.ToJson(loaded.economy);
                    report.loadedRewardCannotRepeat = !loaded.economy.TryClaimGrowthTask(app.Catalog,
                        loaded.layout, "second_expansion", out _) && before == JsonUtility.ToJson(loaded.economy);
                }
                return same;
            }
            catch (Exception ex) { Error(ex.Message); return false; }
        }
        static bool HorizontalMatch(Vector3? a, Vector3 b) =>
            a.HasValue && Mathf.Abs(a.Value.x - b.x) < .01f && Mathf.Abs(a.Value.z - b.z) < .01f;
        void Validate(string phase)
        {
            if (app.Economy.Validate(app.Catalog, app.Layout, out string reason)) return;
            report.inventoryConserved = false;
            Error("State validation " + phase + ": " + reason);
        }
        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            if (finished) yield break;
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image == null) { Error("Missing screenshot " + filename); yield break; }
            try { File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG()); report.screenshots++; }
            catch (Exception ex) { Error(ex.Message); }
            finally { Destroy(image); }
        }
        void Check(bool condition, string error) { if (!condition) Error(error); }
        void Error(string message) { if (uniqueErrors.Add(message)) errors.Add(message); }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Error(message + "\n" + trace); }
        void Finish()
        {
            if (finished) return;
            finished = true;
            StopAllCoroutines();
            foreach (var field in typeof(Report).GetFields())
                if (field.FieldType == typeof(bool) && field.Name != "passed" && field.Name != "timedOut")
                    Check((bool)field.GetValue(report), "Acceptance failed: " + field.Name);
            report.sales = app?.Economy?.transactions ?? 0;
            report.onlineUnitsSold = (int)(app?.Economy?.OnlineUnitsSold ?? 0);
            report.warehousePickups = app?.WarehousePickups ?? 0;
            report.restockJobs = app?.RestockJobsCompleted ?? 0;
            report.realtimeSeconds = Time.realtimeSinceStartup - started;
            report.passed = errors.Count == 0 && !report.timedOut;
            report.errors = errors.ToArray();
            if (!string.IsNullOrEmpty(output))
                File.WriteAllText(Path.Combine(output, "growth-verification.json"), JsonUtility.ToJson(report, true));
            Time.timeScale = originalScale;
            Application.logMessageReceived -= Log;
            Debug.Log("SHOP_GROWTH_CHECKS_OK " + report.passed);
            Application.Quit(report.passed ? 0 : 1);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; if (active) Time.timeScale = originalScale; }
    }
}
