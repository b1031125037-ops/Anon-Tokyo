using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Exercises the production save-before-award boundary using isolated files.</summary>
    [DefaultExecutionOrder(-430)]
    public sealed class ShopOfflineVerification : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public bool passed, timedOut, finiteFixture, initialSnapshot, failedWriteAtomic;
            public bool fourHourSale, realInventoryDebited, persistedBeforeReward, saveLoadRoundTrip;
            public bool repeatedSettlementAtomic, reloadedIntervalNotRepeated, twentyFourHourCap;
            public bool exhaustedStockEarnsNothing, inventoryConserved, userFilesUntouched;
            public int checks, screenshots, initialStock, unitPrice, initialCash, cashAfterFourHours;
            public long fourHourUnits, fourHourIncome, cappedUnits, cappedIncome, cappedCreditedSeconds;
            public float realtimeSeconds;
            public string[] results, errors;
        }

        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();
        readonly Dictionary<string, byte[]> userFiles = new Dictionary<string, byte[]>();
        ShopApp app;
        Report report;
        string output;
        float started, deadline;
        bool active, finished;

        void Awake()
        {
            active = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-offline") >= 0;
            if (active) Application.logMessageReceived += OnLog;
        }

        void Update()
        {
            if (!active || finished || report == null || Time.realtimeSinceStartup < deadline) return;
            report.timedOut = true;
            errors.Add("Offline settlement verification exceeded 45 real-time seconds.");
            Finish();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message + "\n" + stack);
        }

        IEnumerator Start()
        {
            if (!active) yield break;
            report = new Report();
            started = Time.realtimeSinceStartup;
            deadline = started + 45f;
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--verify-offline");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            { errors.Add("--verify-offline requires an output directory."); Finish(); yield break; }
            try
            {
                output = Path.GetFullPath(args[index + 1]);
                string playerDirectory = Path.GetFullPath(Application.persistentDataPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if ((output.TrimEnd('\\', '/') + Path.DirectorySeparatorChar).StartsWith(playerDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Verification output must be outside the player's save directory.");
                Directory.CreateDirectory(output);
                foreach (string path in new[]
                {
                    ShopSession.NormalSavePath,
                    Path.Combine(Application.persistentDataPath, "shop-test-session-v1.json"),
                    ShopUserSettings.SavePath
                })
                {
                    userFiles[path] = ReadOptional(path);
                    userFiles[path + ".bak"] = ReadOptional(path + ".bak");
                }
            }
            catch (Exception ex) { output = null; errors.Add(ex.Message); Finish(); yield break; }
            Application.runInBackground = true;
            yield return null;
            app = GetComponent<ShopApp>();
            if (app == null || app.Economy == null || app.World == null)
            { errors.Add("Shop did not initialize for offline acceptance."); Finish(); yield break; }

            if (!PrepareFixture()) { Finish(); yield break; }
            long departure = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var economy = app.Economy;
            report.initialStock = 100;
            report.initialCash = economy.cash;
            string checkoutId = economy.PostFor(FacilityKind.Checkout).instanceId;
            report.unitPrice = economy.CheckoutPrice(app.Catalog, "top_tshirt", checkoutId);
            report.initialSnapshot = economy.CaptureOfflineBaseline(app.Catalog, app.Layout, departure, out string reason)
                && economy.offlineWasEligible && economy.offlineStockSnapshot.Count == 1
                && economy.offlineStockSnapshot[0].quantity == report.initialStock;
            Check(report.initialSnapshot, "Open departure snapshot contains exactly 100 physical units of the displayed product");
            if (!report.initialSnapshot) { errors.Add(reason); Finish(); yield break; }

            string originalEconomy = JsonUtility.ToJson(economy);
            string originalReport = app.LatestOfflineIncome == null ? null : JsonUtility.ToJson(app.LatestOfflineIncome);
            string blockedParent = Path.Combine(output, "write-failure-parent.txt");
            File.WriteAllText(blockedParent, "This file deliberately cannot be a save directory.");
            long fourHours = departure + 4 * 3600;
            report.failedWriteAtomic = !app.SettleOfflineForVerification(fourHours, Path.Combine(blockedParent, "session.json"))
                && JsonUtility.ToJson(app.Economy) == originalEconomy
                && (app.LatestOfflineIncome == null ? null : JsonUtility.ToJson(app.LatestOfflineIncome)) == originalReport
                && app.Economy.offlineLastCheckUtcSeconds == departure;
            Check(report.failedWriteAtomic, "A failed real save leaves stock, cash, earned income, last UTC time and the displayed award unchanged");

            string settledPath = Path.Combine(output, "offline-settled-session.json");
            bool settled = app.SettleOfflineForVerification(fourHours, settledPath);
            var award = app.LatestOfflineIncome;
            report.fourHourUnits = award == null ? 0 : award.unitsSold;
            report.fourHourIncome = award == null ? 0 : award.income;
            report.cashAfterFourHours = app.Economy.cash;
            report.fourHourSale = settled && award != null && award.wasEligible && award.elapsedSeconds == 4 * 3600
                && award.creditedSeconds == 4 * 3600 && award.unitsSold == 20
                && award.income == 20L * report.unitPrice && award.lines.Count == 1
                && award.lines[0].unitPrice == report.unitPrice;
            Check(report.fourHourSale, "Four offline hours sell 20 percent of departure stock at the actual checkout price");
            if (!report.fourHourSale) { Finish(); yield break; }
            economy = app.Economy;
            var stock = economy.Stock("top_tshirt");
            report.realInventoryDebited = economy.Shelf("top-1").quantity == 0 && stock.available == 80
                && stock.sold == 20 && stock.purchased == 100 && economy.cash == report.initialCash + award.income
                && economy.offlineUnitsSold == 20 && economy.offlineRevenue == award.income
                && economy.revenue == award.income && economy.transactions == 0 && economy.popularity == 0;
            Check(report.realInventoryDebited, "The award debits shelves before warehouse stock and credits money without inventing visits or stock");

            var loaded = ShopSession.LoadFromPath(app.Catalog, settledPath, out reason);
            string committedState = JsonUtility.ToJson(new ShopSession { layout = app.Layout, economy = economy });
            report.persistedBeforeReward = File.Exists(settledPath) && loaded != null
                && loaded.economy.cash == economy.cash && loaded.economy.Stock("top_tshirt").sold == 20
                && loaded.economy.offlineLastCheckUtcSeconds == fourHours;
            Check(report.persistedBeforeReward, "The file already contains both stock debits and reward before the live result is displayed");
            report.saveLoadRoundTrip = loaded != null && JsonUtility.ToJson(loaded) == committedState
                && loaded.economy.Validate(app.Catalog, loaded.layout, out _);
            Check(report.saveLoadRoundTrip, "The actual settlement file reloads the identical layout, ledger, snapshot and fractional progress");
            app.OpenOfflineSummary();
            yield return Capture("01-four-hour-offline-income.png");

            string beforeDuplicate = JsonUtility.ToJson(app.Economy);
            report.repeatedSettlementAtomic = app.SettleOfflineForVerification(fourHours, settledPath)
                && app.LatestOfflineIncome.elapsedSeconds == 0 && app.LatestOfflineIncome.unitsSold == 0
                && app.LatestOfflineIncome.income == 0 && JsonUtility.ToJson(app.Economy) == beforeDuplicate;
            Check(report.repeatedSettlementAtomic, "Calling the production settlement path again at the same time cannot duplicate the reward");
            string loadedBefore = loaded == null ? null : JsonUtility.ToJson(loaded.economy);
            report.reloadedIntervalNotRepeated = loaded != null
                && loaded.economy.TryApplyOfflineIncome(app.Catalog, loaded.layout, fourHours, out var repeated, out _)
                && repeated.income == 0 && repeated.unitsSold == 0
                && JsonUtility.ToJson(loaded.economy) == loadedBefore;
            Check(report.reloadedIntervalNotRepeated, "A restarted economy loaded from the real save also rejects a duplicate interval");

            long threeDaysLater = fourHours + 3 * 86400;
            bool capped = app.SettleOfflineForVerification(threeDaysLater, settledPath);
            award = app.LatestOfflineIncome;
            report.cappedUnits = award == null ? 0 : award.unitsSold;
            report.cappedIncome = award == null ? 0 : award.income;
            report.cappedCreditedSeconds = award == null ? 0 : award.creditedSeconds;
            report.twentyFourHourCap = capped && award != null && award.capped && award.elapsedSeconds == 3 * 86400
                && award.creditedSeconds == ShopEconomy.OfflineMaxSeconds && award.unitsSold == 80
                && award.income == 80L * report.unitPrice && app.Economy.Stock("top_tshirt").available == 0
                && app.Economy.Stock("top_tshirt").sold == 100 && app.Economy.cash == report.initialCash + 100 * report.unitPrice;
            Check(report.twentyFourHourCap, "A three-day absence credits at most 24 hours and exhausts only the 80 physical units remaining");
            long revenueAfterDepletion = app.Economy.revenue;
            report.exhaustedStockEarnsNothing = app.SettleOfflineForVerification(threeDaysLater + 86400, settledPath)
                && app.LatestOfflineIncome.unitsSold == 0 && app.LatestOfflineIncome.income == 0
                && app.Economy.revenue == revenueAfterDepletion && app.Economy.Stock("top_tshirt").purchased == 100;
            Check(report.exhaustedStockEarnsNothing, "A further day after stock is exhausted earns no money and never creates replacement goods");
            report.inventoryConserved = app.Economy.Validate(app.Catalog, app.Layout, out reason);
            Check(report.inventoryConserved, "Final offline inventory and money accounting pass the production state validator");
            if (!report.inventoryConserved) errors.Add(reason);
            report.userFilesUntouched = true;
            foreach (var file in userFiles) report.userFilesUntouched &= SameBytes(file.Value, ReadOptional(file.Key));
            Check(report.userFilesUntouched, "All isolated settlements leave normal/test player saves, backups and settings byte-for-byte unchanged");
            Finish();
        }

        bool PrepareFixture()
        {
            app.SetPaused(true);
            app.SetAutoRestock(false);
            if (!app.ForceResetCustomersForVerification()) return false;
            app.CancelRestockTask();
            var economy = app.Economy;
            economy.orders.Clear();
            economy.jobs.Clear();
            economy.baskets.Clear();
            foreach (var stock in economy.stocks)
            {
                stock.available = stock.reserved = stock.purchased = stock.sold = stock.returned = 0;
                stock.contracted = false;
            }
            foreach (var shelf in economy.shelves) shelf.quantity = 0;
            economy.revenue = 0;
            economy.transactions = 0;
            economy.popularity = 0;
            economy.cash = 2500;
            if (!app.SetShelfProduct("top-1", "top_tshirt")) return false;
            var top = economy.Stock("top_tshirt");
            top.contracted = true;
            top.purchased = 100;
            top.available = 90;
            economy.Shelf("top-1").quantity = 10;
            app.SetShopOpen(true);
            report.finiteFixture = !economy.testMode && economy.shopOpen && app.CanCheckoutServe
                && app.IsSimulationPaused && !economy.autoRestock && economy.Validate(app.Catalog, app.Layout, out _);
            Check(report.finiteFixture, "Isolated finite-stock shop has a usable staffed checkout while live simulation and replenishment are held");
            return report.finiteFixture;
        }

        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            if (finished) yield break;
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) { errors.Add("Missing screenshot " + filename); yield break; }
            try { File.WriteAllBytes(Path.Combine(output, filename), texture.EncodeToPNG()); report.screenshots++; }
            catch (Exception ex) { errors.Add(ex.Message); }
            finally { Destroy(texture); }
        }

        static byte[] ReadOptional(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        void Check(bool value, string label)
        {
            results.Add(label);
            if (!value) errors.Add(label);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            if (report == null) report = new Report();
            report.realtimeSeconds = Time.realtimeSinceStartup - started;
            report.checks = results.Count;
            report.results = results.ToArray();
            report.errors = errors.ToArray();
            report.passed = errors.Count == 0 && !report.timedOut && report.screenshots == 1 && report.userFilesUntouched;
            if (!string.IsNullOrEmpty(output))
                File.WriteAllText(Path.Combine(output, "offline-runtime-verification.json"), JsonUtility.ToJson(report, true));
            Debug.Log("SHOP_OFFLINE_RUNTIME_" + (report.passed ? "OK" : "FAILED"));
            Application.Quit(report.passed ? 0 : 1);
        }
        void OnDestroy() { if (active) Application.logMessageReceived -= OnLog; }
    }
}
