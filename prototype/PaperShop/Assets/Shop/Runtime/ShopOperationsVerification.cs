using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Isolated acceptance of real Update delivery, clock, pause and finite-stock checkout.</summary>
    [DefaultExecutionOrder(-440)]
    public sealed class ShopOperationsVerification : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public bool passed, timedOut, inventoryConserved = true;
            public bool finiteEconomy, pendingOrderUi, pendingRefund, cancellationIdempotent, capacityReleased;
            public bool pendingSaveRoundTrip, transportSaveRoundTrip, transportCancellationRejected;
            public bool dispatchStageObserved, deliveryArrivedOnce, orderRemovedAfterArrival;
            public bool speedInitiallyOne, speedRatioCorrect, speedRestoredToOne, invalidSpeedRejected;
            public bool pendingPauseHeld, transportPauseHeld, nestedPauseHeld, resumeWithoutCatchup;
            public bool pendingRenovationProgress, transportRenovationProgress, focusPauseHeld, systemPauseHeld, characterPauseHeld, arrivalsPauseHeld;
            public bool restockCompleted, finiteSaleCompleted, paymentAtExactFront, saleConserved;
            public int pauseCases, screenshots, purchased, delivered, sold, transactions;
            public float oneRate, twoRate, speedRatio, restoredRate, dispatchGameSeconds, transportGameSeconds, realtimeSeconds;
            public string[] errors;
        }

        sealed class Snapshot
        {
            public string economy;
            public double elapsed;
            public float arrivalTimer;
            public int arrived, attempts, departed;
            public readonly Dictionary<Transform, Matrix4x4> poses = new Dictionary<Transform, Matrix4x4>();
        }

        readonly List<string> errors = new List<string>();
        readonly HashSet<string> knownErrors = new HashSet<string>(StringComparer.Ordinal);
        ShopApp app;
        Report report;
        string output;
        bool active, finished;
        float started, deadline, originalScale, nextValidate;
        float sampleRate;

        void Awake()
        {
            active = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-operations") >= 0;
            if (active) Application.logMessageReceived += Log;
        }

        void Update()
        {
            if (!active || finished || report == null) return;
            if (app != null && app.Economy != null && Time.realtimeSinceStartup >= nextValidate)
            {
                nextValidate = Time.realtimeSinceStartup + .5f;
                Validate("live");
            }
            if (Time.realtimeSinceStartup >= deadline)
            {
                report.timedOut = true;
                Error("Operations verification exceeded 150 real-time seconds.");
                Finish();
            }
        }

        IEnumerator Start()
        {
            if (!active) yield break;
            originalScale = Time.timeScale;
            Time.timeScale = 1f;
            started = Time.realtimeSinceStartup;
            deadline = started + 150f;
            report = new Report();
            int argument = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-operations");
            var args = Environment.GetCommandLineArgs();
            if (argument + 1 >= args.Length || args[argument + 1].StartsWith("--", StringComparison.Ordinal))
            { Error("--verify-operations requires an output directory."); Finish(); yield break; }
            try { output = Path.GetFullPath(args[argument + 1]); Directory.CreateDirectory(output); }
            catch (Exception exception) { Error(exception.Message); Finish(); yield break; }
            Application.runInBackground = true;
            app = GetComponent<ShopApp>();
            yield return null;
            if (app == null || app.Economy == null || app.World == null)
            { Error("Shop app did not initialize."); Finish(); yield break; }
            app.ApplyApplicationFocus(true);
            app.ApplyApplicationPause(false);
            app.SetPaused(false);
            app.SetAutoRestock(false);
            app.ForceResetCustomersForVerification();
            app.CancelRestockTask();
            report.finiteEconomy = !app.Economy.testMode;
            report.speedInitiallyOne = app.GameSpeed == 1;
            // Warm the renderer before timing, and finish speed measurement before
            // screenshot readback can introduce a long frame into either sample.
            yield return new WaitForSecondsRealtime(1.2f);
            yield return SampleRate(1, 1.2f);
            report.oneRate = sampleRate;
            yield return SampleRate(2, 1.2f);
            report.twoRate = sampleRate;
            report.speedRatio = report.twoRate / Math.Max(.001f, report.oneRate);
            report.speedRatioCorrect = report.oneRate > .8f && report.oneRate < 1.15f
                && report.twoRate > 1.6f && report.twoRate < 2.3f && report.speedRatio > 1.75f && report.speedRatio < 2.25f;
            Check(app.SetShelfProduct("top-1", "top_tshirt") && app.ContractProduct("top_tshirt"), "Finite merchandise setup failed.");
            int cashBefore = app.Economy.cash;
            int committedBefore = app.Economy.WarehouseCommitted;
            int purchasedBefore = app.Economy.Stock("top_tshirt").purchased;
            Check(app.OrderProduct("top_tshirt"), "Pending order could not be created.");
            var cancelled = Delivery();
            if (cancelled == null) { Finish(); yield break; }
            int refund = cancelled.paidAmount;
            report.pendingOrderUi = cancelled.awaitingDispatch && refund > 0
                && Math.Abs(cancelled.dispatchRemaining - ShopEconomy.DispatchSeconds) < .01f
                && app.Economy.cash == cashBefore - refund;
            app.SetPaused(true);
            app.OpenPanel("orders");
            yield return Capture("01-pending-order-cancellable.png");
            report.pendingRefund = app.CancelDeliveryOrder(cancelled.orderId) && app.Economy.cash == cashBefore
                && app.Economy.Stock("top_tshirt").purchased == purchasedBefore && app.Economy.orders.Count == 0;
            report.capacityReleased = app.Economy.WarehouseCommitted == committedBefore;
            string afterCancel = JsonUtility.ToJson(app.Economy);
            report.cancellationIdempotent = !app.CancelDeliveryOrder(cancelled.orderId) && JsonUtility.ToJson(app.Economy) == afterCancel;
            Check(app.OrderProduct("top_tshirt"), "Replacement pending order could not be created.");
            var order = Delivery();
            double orderStarted = app.SimulationElapsedSeconds;
            report.pendingSaveRoundTrip = SaveRoundTrip("pending-session.json", true);
            app.OpenPanel("orders");
            app.SetPaused(false);
            app.SetShopOpen(true);
            Check(app.MoveActor(ShopModel.WarehousePickup(app.Layout), true), "Could not start moving clerk for pause evidence.");
            Check(order != null && order.awaitingDispatch, "Order left pending state before pending pause checks.");
            yield return PauseCases(true);
            report.pendingPauseHeld = order != null && order.awaitingDispatch;

            double dispatchEnd = app.SimulationElapsedSeconds + (order == null ? 0 : order.dispatchRemaining);
            while (Delivery() != null && Delivery().awaitingDispatch && Time.realtimeSinceStartup < deadline - 60f) yield return null;
            order = Delivery();
            report.dispatchStageObserved = order != null && !order.awaitingDispatch && order.dispatchRemaining <= .0001f
                && order.remaining > ShopEconomy.DeliverySeconds - .3f
                && app.SimulationElapsedSeconds >= dispatchEnd - .03;
            report.dispatchGameSeconds = order == null ? 0f
                : (float)(app.SimulationElapsedSeconds - orderStarted) - (ShopEconomy.DeliverySeconds - order.remaining);
            report.dispatchStageObserved &= Mathf.Abs(report.dispatchGameSeconds - ShopEconomy.DispatchSeconds) < .05f;
            if (order == null) { Error("Delivery skipped the transport observation window."); Finish(); yield break; }
            app.SetPaused(true);
            report.transportSaveRoundTrip = SaveRoundTrip("transport-session.json", false);
            string beforeTransportCancel = JsonUtility.ToJson(app.Economy);
            report.transportCancellationRejected = !app.CancelDeliveryOrder(order.orderId)
                && JsonUtility.ToJson(app.Economy) == beforeTransportCancel;
            app.OpenPanel("orders");
            yield return Capture("02-transport-order-locked.png");
            app.OpenPanel("orders");
            app.SetPaused(false);
            Check(app.MoveActor(FarthestReachableCell(), true), "Could not start movement during transport.");
            yield return PauseCases(false);
            report.transportPauseHeld = Delivery() != null && !Delivery().awaitingDispatch;
            app.SetShopOpen(false);
            float remainingBefore = order.remaining;
            double elapsedBefore = app.SimulationElapsedSeconds;
            while (Delivery() != null && Time.realtimeSinceStartup < deadline - 45f) yield return null;
            report.transportGameSeconds = (float)(app.SimulationElapsedSeconds - elapsedBefore) + ShopEconomy.DeliverySeconds - remainingBefore;
            var stock = app.Economy.Stock("top_tshirt");
            report.purchased = stock.purchased;
            report.delivered = stock.available;
            report.orderRemovedAfterArrival = app.Economy.orders.Count == 0 && app.Economy.InTransit == 0;
            report.deliveryArrivedOnce = stock.available == 20 && stock.purchased == 20 && report.transportGameSeconds >= 19.98f;
            int warehouseAfter = app.Economy.WarehouseUsed;
            yield return new WaitForSecondsRealtime(.6f);
            report.deliveryArrivedOnce &= app.Economy.WarehouseUsed == warehouseAfter && stock.available == 20;
            Validate("after one delivery");
            app.OpenPanel("orders");
            yield return Capture("03-delivery-complete.png");
            app.OpenPanel("orders");

            Check(app.RequestRestock("top-1"), "Finite stock replenishment could not start.");
            float stageDeadline = Mathf.Min(deadline - 30f, Time.realtimeSinceStartup + 22f);
            while (app.IsRestocking && Time.realtimeSinceStartup < stageDeadline) yield return null;
            report.restockCompleted = !app.IsRestocking && app.Economy.Shelf("top-1").quantity == 10;
            int transactionsBefore = app.Economy.transactions;
            int paymentsBefore = app.PaymentsAtFront;
            int saleCashBefore = app.Economy.cash;
            long revenueBefore = app.Economy.revenue;
            Check(app.SetCustomerFlowStressTest(true), "Isolated integration arrival mode unavailable.");
            app.SetCustomerLimit(3);
            app.SetShopOpen(true);
            stageDeadline = Mathf.Min(deadline - 5f, Time.realtimeSinceStartup + 55f);
            bool saleCaptured = false;
            while (app.Economy.transactions == transactionsBefore && Time.realtimeSinceStartup < stageDeadline)
            {
                if (app.PayingCustomerCell.HasValue && !saleCaptured)
                {
                    var checkout = app.Layout.facilities.Find(f => f.definitionId == "checkout");
                    report.paymentAtExactFront = app.PayingCustomerCell.Value == ShopModel.CheckoutCustomerCell(checkout, app.Catalog.Find("checkout"));
                    app.SetPaused(true);
                    var snapshot = SnapshotState();
                    yield return new WaitForSecondsRealtime(.4f);
                    Check(SameSnapshot(snapshot, "active customer payment pause"), "Active shopping/payment changed while paused.");
                    yield return Capture("04-finite-stock-payment.png");
                    app.SetPaused(false);
                    saleCaptured = true;
                }
                yield return null;
            }
            report.finiteSaleCompleted = app.Economy.transactions > transactionsBefore && app.PaymentsAtFront > paymentsBefore;
            report.saleConserved = app.Economy.cash > saleCashBefore
                && app.Economy.cash - saleCashBefore == app.Economy.revenue - revenueBefore;
            report.sold = app.Economy.Stock("top_tshirt").sold;
            report.transactions = app.Economy.transactions;
            Validate("after actual finite-stock payment");
            yield return SampleRate(1, .5f);
            report.restoredRate = sampleRate;
            report.speedRestoredToOne = app.GameSpeed == 1 && sampleRate > .8f && sampleRate < 1.15f;
            report.invalidSpeedRejected = !app.SetGameSpeed(0) && !app.SetGameSpeed(3) && app.GameSpeed == 1;
            Finish();
        }

        IEnumerator SampleRate(int speed, float seconds)
        {
            Check(app.SetGameSpeed(speed), "Valid game speed was rejected.");
            yield return null;
            double initial = app.SimulationElapsedSeconds;
            // Both samples refer to this frame's completed Update. A fresh
            // realtimeSinceStartup read instead samples midway through the frame;
            // the next deltaTime would then also charge work before that read.
            // The unscaled frame timestamp aligns the two boundaries while still
            // measuring game seconds per actual unscaled second.
            double real = Time.unscaledTimeAsDouble;
            yield return new WaitForSecondsRealtime(seconds);
            sampleRate = (float)((app.SimulationElapsedSeconds - initial) / (Time.unscaledTimeAsDouble - real));
        }

        IEnumerator PauseCases(bool pending)
        {
            foreach (string kind in new[] { "manual", "focus", "system" })
            {
                if (kind == "manual") app.SetPaused(true);
                else if (kind == "focus") app.ApplyApplicationFocus(false);
                else app.ApplyApplicationPause(true);
                yield return null;
                Check(app.IsSimulationPaused, kind + " did not set the unified paused state.");
                var snapshot = SnapshotState();
                yield return new WaitForSecondsRealtime(.36f);
                bool frozen = SameSnapshot(snapshot, (pending ? "pending " : "transport ") + kind);
                if (kind == "manual") app.SetPaused(false);
                else if (kind == "focus") { report.focusPauseHeld |= frozen; app.ApplyApplicationFocus(true); }
                else { report.systemPauseHeld |= frozen; app.ApplyApplicationPause(false); }
                report.pauseCases++;
                Check(frozen, kind + " failed to freeze all simulation owners.");
                double beforeResume = app.SimulationElapsedSeconds;
                yield return null;
                // A foreground resume may discard its first frame; any advance must remain
                // bounded by one capped frame, never the time spent unfocused/suspended.
                bool bounded = app.SimulationElapsedSeconds - beforeResume <= .2001;
                if (report.pauseCases == 1) report.resumeWithoutCatchup = bounded;
                else report.resumeWithoutCatchup &= bounded;
            }
            app.SetPaused(true);
            app.ApplyApplicationFocus(false);
            app.ApplyApplicationPause(true);
            app.ApplyApplicationFocus(true);
            app.ApplyApplicationPause(false);
            yield return null;
            var nested = SnapshotState();
            yield return new WaitForSecondsRealtime(.25f);
            bool nestedHeld = app.IsPaused && app.IsSimulationPaused && SameSnapshot(nested, "nested pause");
            if (pending) report.nestedPauseHeld = nestedHeld;
            else report.nestedPauseHeld &= nestedHeld;
            app.SetPaused(false);
            // Live closing and physical departure are covered by the renovation
            // run. This isolated fixture checks the clock while an editor is open.
            Check(app.ForceResetCustomersForVerification(), "Could not prepare a closed shop for renovation timing.");
            app.OpenPanel("build");
            yield return null;
            double renovationStart = app.SimulationElapsedSeconds;
            var order = Delivery();
            float deliveryBefore = order == null ? 0 : order.dispatchRemaining + order.remaining;
            int arrivalsBefore = app.ArrivalsSpawned;
            yield return new WaitForSecondsRealtime(.36f);
            order = Delivery();
            float deliveryAfter = order == null ? 0 : order.dispatchRemaining + order.remaining;
            double advanced = app.SimulationElapsedSeconds - renovationStart;
            bool continued = app.IsEditingLayout && !app.IsSimulationPaused && !app.Economy.shopOpen
                && advanced > .25 && deliveryBefore - deliveryAfter > .25f
                && Math.Abs(deliveryBefore - deliveryAfter - advanced) < .05
                && app.ArrivalsSpawned == arrivalsBefore;
            if (pending) report.pendingRenovationProgress = continued;
            else report.transportRenovationProgress = continued;
            Check(continued, (pending ? "Pending dispatch" : "Transport") + " must progress during closed-shop renovation without admitting customers.");
            app.OpenPanel("build");
            app.SetShopOpen(true);
        }

        Snapshot SnapshotState()
        {
            var value = new Snapshot
            {
                economy = JsonUtility.ToJson(app.Economy), elapsed = app.SimulationElapsedSeconds,
                arrivalTimer = app.SecondsUntilArrivalCheck, arrived = app.ArrivalsSpawned,
                attempts = app.ArrivalAttempts, departed = app.DepartedCustomers
            };
            foreach (var character in app.GetComponentsInChildren<PaperCharacterView>())
                foreach (var part in character.GetComponentsInChildren<Transform>())
                    value.poses[part] = part.localToWorldMatrix;
            return value;
        }

        bool SameSnapshot(Snapshot value, string phase)
        {
            bool characters = true;
            foreach (var pose in value.poses)
                characters &= pose.Key != null && pose.Key.localToWorldMatrix == pose.Value;
            bool arrivals = value.arrived == app.ArrivalsSpawned && value.attempts == app.ArrivalAttempts
                && value.departed == app.DepartedCustomers && Mathf.Abs(value.arrivalTimer - app.SecondsUntilArrivalCheck) < .000001f;
            if (report.pauseCases == 0) { report.characterPauseHeld = characters; report.arrivalsPauseHeld = arrivals; }
            else { report.characterPauseHeld &= characters; report.arrivalsPauseHeld &= arrivals; }
            bool same = characters && arrivals && Math.Abs(value.elapsed - app.SimulationElapsedSeconds) < .0000001
                && value.economy == JsonUtility.ToJson(app.Economy);
            if (!same) Error("Pause snapshot changed during " + phase + "; actors=" + characters + ", arrivals=" + arrivals);
            return same;
        }

        Vector2Int FarthestReachableCell()
        {
            Vector2Int farthest = app.ActorCell;
            int longest = 0;
            for (int x = 0; x < app.Layout.width; x++)
                for (int z = 0; z < app.Layout.depth; z++)
                {
                    var cell = new Vector2Int(x, z);
                    var route = ShopModel.FindPath(app.Layout, app.Catalog, app.ActorCell, cell);
                    if (route.Count > longest) { longest = route.Count; farthest = cell; }
                }
            return farthest;
        }

        DeliveryOrder Delivery() => app.Economy.orders.Find(o => o.productId == "top_tshirt");

        bool SaveRoundTrip(string file, bool pending)
        {
            try
            {
                string before = JsonUtility.ToJson(new ShopSession { layout = app.Layout, economy = app.Economy });
                string path = Path.Combine(output, file);
                ShopSession.SaveToPath(app.Layout, app.Economy, app.Catalog, path);
                var restored = ShopSession.LoadFromPath(app.Catalog, path, out string reason);
                if (restored == null) { Error("Delivery session load failed: " + reason); return false; }
                bool same = JsonUtility.ToJson(restored) == before && restored.economy.orders.Count == 1
                    && restored.economy.orders[0].awaitingDispatch == pending
                    && restored.economy.Validate(app.Catalog, restored.layout, out _);
                if (!same) Error("Delivery stage did not round-trip: " + file);
                return same;
            }
            catch (Exception exception) { Error("Delivery save/load failed: " + exception.Message); return false; }
        }

        void Validate(string phase)
        {
            if (app.Economy.Validate(app.Catalog, app.Layout, out string reason)) return;
            report.inventoryConserved = false;
            Error("Conservation failed " + phase + ": " + reason);
        }

        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            if (finished) yield break;
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) { Error("No rendered evidence: " + filename); yield break; }
            try { File.WriteAllBytes(Path.Combine(output, filename), texture.EncodeToPNG()); report.screenshots++; }
            catch (Exception exception) { Error(exception.Message); }
            finally { Destroy(texture); }
        }

        void Check(bool value, string message) { if (!value) Error(message); }
        void Error(string message) { if (knownErrors.Add(message)) errors.Add(message); }
        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Error(message + "\n" + trace);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            StopAllCoroutines();
            report.realtimeSeconds = Time.realtimeSinceStartup - started;
            Check(report.finiteEconomy && report.pendingOrderUi && report.pendingRefund && report.cancellationIdempotent && report.capacityReleased,
                "Pending order, finite refund, repeat-cancel protection or capacity release failed.");
            Check(report.pendingSaveRoundTrip && report.transportSaveRoundTrip && report.transportCancellationRejected,
                "Delivery stages did not persist or dispatched order could be cancelled.");
            Check(report.dispatchStageObserved && report.deliveryArrivedOnce && report.orderRemovedAfterArrival,
                "Dispatch/transport stages or exactly-once warehouse arrival failed.");
            Check(report.speedInitiallyOne && report.speedRatioCorrect && report.speedRestoredToOne && report.invalidSpeedRejected,
                "Actual 1x/2x Update clock or return to 1x failed.");
            Check(report.pendingPauseHeld && report.transportPauseHeld && report.nestedPauseHeld && report.resumeWithoutCatchup
                && report.focusPauseHeld && report.systemPauseHeld && report.characterPauseHeld
                && report.arrivalsPauseHeld && report.pauseCases == 6, "Manual/inactivity pause and resume coverage failed.");
            Check(report.pendingRenovationProgress && report.transportRenovationProgress,
                "Renovation must continue real-time dispatch and transport at the selected simulation speed.");
            Check(report.restockCompleted && report.finiteSaleCompleted && report.paymentAtExactFront && report.saleConserved
                && report.inventoryConserved, "Delivered finite inventory did not replenish and pay correctly at checkout front.");
            Check(report.screenshots >= 4, "Rendered operations evidence incomplete.");
            report.passed = errors.Count == 0 && !report.timedOut;
            report.errors = errors.ToArray();
            try
            {
                if (!string.IsNullOrEmpty(output))
                    File.WriteAllText(Path.Combine(output, "operations-verification.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception) { report.passed = false; Debug.LogError(exception.Message); }
            Time.timeScale = originalScale;
            Application.logMessageReceived -= Log;
            Debug.Log("SHOP_OPERATIONS_CHECKS_OK " + report.passed);
            Application.Quit(report.passed ? 0 : 1);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (active) Time.timeScale = originalScale;
        }
    }
}
