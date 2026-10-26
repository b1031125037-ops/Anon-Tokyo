using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Exercises the visible customer simulation through normal ShopApp commands.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class ShopCrowdVerification : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public bool passed, timedOut, framesAdvanced, pauseHeld, resumed;
            public bool twoSalesCounters, unusedFacilitiesRemoved, threeStaffPresent, cashierBehindCounter, customerPaysInFront;
            public bool restockThresholdAdjustable;
            public bool sixCustomersConcurrent, shelfQueueObserved, checkoutQueueObserved;
            public bool guidedShopping, autonomousShopping, randomDeclineObserved;
            public bool multiItemSale, multiKindSale, severalSalesAndDepartures;
            public bool noCustomerStopOverlap, inventoryConserved, closePaidBaskets;
            public bool closeWaitedForDepartures, closeStoppedArrivals, renderedScreenshots;
            public int frameCount, peakCustomers, maxShelfQueue, maxCheckoutQueue;
            public int guideGreetings, guidedVisits, autonomousVisits, declinedPurchases;
            public int transactions, departures, multiItemSales, multiKindSales;
            public int queueServicesCompleted, customerStopConflicts, unpaidUnitsBeforeClose;
            public int screenshots;
            public long revenue;
            public float realtimeSeconds;
            public string[] errors;
        }

        readonly List<string> errors = new List<string>();
        readonly HashSet<string> reportedErrors = new HashSet<string>(StringComparer.Ordinal);
        bool active, finished;
        string output;
        float startedReal, deadline, originalTimeScale, nextConservationCheck;
        int startingTransactions, startingDepartures;
        Report report;
        ShopApp app;

        void Awake()
        {
            active = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-crowd") >= 0;
            if (active) Application.logMessageReceived += Log;
        }

        void Update()
        {
            if (!active || finished || report == null) return;
            if (app != null && app.Economy != null)
            {
                Observe();
                if (Time.realtimeSinceStartup >= nextConservationCheck)
                {
                    nextConservationCheck = Time.realtimeSinceStartup + .5f;
                    CheckConservation("during live customer simulation");
                }
            }
            // A coroutine exception must still produce a bounded, failed verification.
            if (Time.realtimeSinceStartup < deadline) return;
            report.timedOut = true;
            Error("Customer integration verification exceeded its 120-second real-time deadline.");
            Finish();
        }

        IEnumerator Start()
        {
            if (!active) yield break;
            originalTimeScale = Time.timeScale;
            startedReal = Time.realtimeSinceStartup;
            deadline = startedReal + 120f;
            report = new Report { inventoryConserved = true, noCustomerStopOverlap = true };
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--verify-crowd");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Error("--verify-crowd requires an output directory.");
                Finish();
                yield break;
            }
            try
            {
                output = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(output);
            }
            catch (Exception exception)
            {
                Error("Could not prepare verification output: " + exception.Message);
                Finish();
                yield break;
            }
            Application.runInBackground = true;
            app = GetComponent<ShopApp>();
            yield return null;
            if (app == null || app.World == null || app.Economy == null)
            {
                Error("ShopApp, world, or economy did not initialize.");
                Finish();
                yield break;
            }

            app.Economy.EnableTestResources();
            app.ForceResetCustomersForVerification();
            app.StartTwoCounterTest();
            if (!app.SetCustomerFlowStressTest(true)) Error("The explicit crowd stress mode could not be enabled.");
            app.SetCustomerLimit(6);
            app.SetGuideEnabled(true);
            app.SetAutoRestock(true);
            report.restockThresholdAdjustable = true;
            foreach (float threshold in new[] { 0f, .5f, 1f, .25f })
            {
                app.SetRestockThreshold(threshold);
                report.restockThresholdAdjustable &= Mathf.Abs(app.Economy.autoRestockThreshold - threshold) < .00001f;
            }
            app.SetPaused(false);
            app.SetShopOpen(true);
            Time.timeScale = 4f;
            startingTransactions = app.Economy.transactions;
            startingDepartures = app.DepartedCustomers;
            int firstFrame = Time.frameCount;
            report.threeStaffPresent = app.HasAllStaff;
            int salesCounters = 0;
            report.unusedFacilitiesRemoved = app.Layout.facilities.Count == 6;
            var allowedDefinitions = new HashSet<string>(StringComparer.Ordinal) { "top", "shoes", "checkout", "warehouse", "reception", "restock" };
            foreach (var item in app.Layout.facilities)
            {
                var definition = app.Catalog.Find(item.definitionId);
                if (definition == null)
                {
                    report.unusedFacilitiesRemoved = false;
                    continue;
                }
                if (definition.kind == FacilityKind.Shelf) salesCounters++;
                report.unusedFacilitiesRemoved &= allowedDefinitions.Remove(definition.id);
            }
            report.unusedFacilitiesRemoved &= allowedDefinitions.Count == 0;
            report.twoSalesCounters = salesCounters == 2;
            CheckConservation("after preparing six-customer test");
            yield return Capture("01-crowd-start.png");

            bool pauseTested = false, guideDisabled = false;
            bool crowdCaptured = false, shelfCaptured = false, checkoutCaptured = false, autonomousCaptured = false;
            int autonomousBeforeDisablingGuide = 0;
            int resumedTransactionBaseline = startingTransactions;
            while (!finished && Time.realtimeSinceStartup < deadline - 30f && errors.Count == 0)
            {
                Observe();
                report.framesAdvanced |= Time.frameCount > firstFrame + 10;
                if (!pauseTested && app.CustomerCount >= 3)
                {
                    app.SetPaused(true);
                    string economyBefore = JsonUtility.ToJson(app.Economy);
                    int customersBefore = app.CustomerCount;
                    int greetingsBefore = app.GuideGreetings;
                    int departuresBefore = app.DepartedCustomers;
                    var positionsBefore = ActorPositions();
                    int pausedFrame = Time.frameCount;
                    yield return new WaitForSecondsRealtime(.45f);
                    report.pauseHeld = app.IsPaused && economyBefore == JsonUtility.ToJson(app.Economy)
                        && customersBefore == app.CustomerCount && greetingsBefore == app.GuideGreetings
                        && departuresBefore == app.DepartedCustomers && PositionsUnchanged(positionsBefore)
                        && Time.frameCount > pausedFrame;
                    app.SetPaused(false);
                    resumedTransactionBaseline = app.Economy.transactions;
                    pauseTested = true;
                    if (!report.pauseHeld) Error("Pause changed customer/staff positions, economy, or customer progress.");
                }
                if (pauseTested)
                    report.resumed |= app.Economy.transactions > resumedTransactionBaseline;
                if (!crowdCaptured && app.CustomerCount >= 6)
                {
                    crowdCaptured = true;
                    yield return Capture("02-six-customers.png");
                }
                if (!shelfCaptured && report.shelfQueueObserved)
                {
                    shelfCaptured = true;
                    yield return Capture("03-shelf-queue.png");
                }
                if (!checkoutCaptured && report.checkoutQueueObserved)
                {
                    checkoutCaptured = true;
                    yield return Capture("04-checkout-queue.png");
                }
                if (!guideDisabled && report.guidedShopping && app.Economy.transactions > startingTransactions)
                {
                    yield return Capture("05-guided-shopping.png");
                    autonomousBeforeDisablingGuide = app.AutonomousVisits;
                    app.SetGuideEnabled(false);
                    guideDisabled = true;
                }
                if (guideDisabled && app.AutonomousVisits > autonomousBeforeDisablingGuide)
                {
                    report.autonomousShopping = true;
                    if (!autonomousCaptured)
                    {
                        autonomousCaptured = true;
                        yield return Capture("06-autonomous-shopping.png");
                    }
                }
                if (MainFlowSatisfied() && pauseTested) break;
                yield return null;
            }

            if (finished) yield break;
            // Real closure stops arrivals but retains purchased goods until
            // every shopper has paid and physically reached the road exit.
            float basketDeadline = Mathf.Min(deadline - 24f, Time.realtimeSinceStartup + 8f);
            while (BasketUnits() == 0 && Time.realtimeSinceStartup < basketDeadline && errors.Count == 0)
                yield return null;
            report.unpaidUnitsBeforeClose = BasketUnits();
            CheckConservation("before closing with unpaid baskets");
            int transactionsBeforeClose = app.Economy.transactions;
            long revenueBeforeClose = app.Economy.revenue;
            int arrivalsBeforeClose = app.ArrivalsSpawned;
            int customersBeforeClose = app.CustomerCount;
            app.SetShopOpen(false);
            bool closeInitiallyWaited=app.IsClosing && !app.CanRenovate && app.CustomerCount==customersBeforeClose
                && BasketUnits()==report.unpaidUnitsBeforeClose;
            while(app.IsClosing && Time.realtimeSinceStartup<deadline-1f)yield return null;
            report.closePaidBaskets = report.unpaidUnitsBeforeClose > 0 && BasketUnits() == 0
                && app.Economy.transactions > transactionsBeforeClose && app.Economy.revenue > revenueBeforeClose;
            report.closeWaitedForDepartures = closeInitiallyWaited && app.CanRenovate && app.CustomerCount == 0
                && app.ShelfQueueCount == 0 && app.CheckoutQueueCount == 0;
            report.closeStoppedArrivals=app.ArrivalsSpawned==arrivalsBeforeClose;
            CheckConservation("after closing guests paid and departed");
            yield return Capture("07-closed-shop.png");
            Finish();
        }

        void Observe()
        {
            report.peakCustomers = Mathf.Max(report.peakCustomers, app.PeakCustomers, app.CustomerCount);
            report.maxShelfQueue = Mathf.Max(report.maxShelfQueue, app.ShelfQueueCount);
            report.maxCheckoutQueue = Mathf.Max(report.maxCheckoutQueue, app.CheckoutQueueCount);
            report.guideGreetings = app.GuideGreetings;
            report.guidedVisits = app.GuidedVisits;
            report.autonomousVisits = app.AutonomousVisits;
            report.declinedPurchases = app.DeclinedPurchases;
            report.transactions = app.Economy.transactions - startingTransactions;
            report.departures = app.DepartedCustomers - startingDepartures;
            report.multiItemSales = app.MultiItemSales;
            report.multiKindSales = app.MultiKindSales;
            report.queueServicesCompleted = app.QueueServicesCompleted;
            report.customerStopConflicts = Mathf.Max(report.customerStopConflicts, app.CustomerStopConflicts);
            report.revenue = app.Economy.revenue;
            report.sixCustomersConcurrent |= app.CustomerCount >= 6;
            // Two queued shelf visitors prove there is competition for service.
            // For checkout, require an actual cashier transaction plus a waiter.
            report.shelfQueueObserved |= app.ShelfQueueCount >= 2;
            report.guidedShopping |= app.GuideGreetings > 0 && app.GuidedVisits > 0;
            report.randomDeclineObserved |= app.DeclinedPurchases > 0;
            report.multiItemSale |= app.MultiItemSales > 0;
            report.multiKindSale |= app.MultiKindSales > 0;
            report.severalSalesAndDepartures |= report.transactions >= 6 && report.departures >= 6;
            report.noCustomerStopOverlap &= app.CustomerStopConflicts == 0;
            var checkout = app.Layout.facilities.Find(p => p.definitionId == "checkout");
            var cashierObject = GameObject.Find("Cashier placeholder");
            if (checkout != null && cashierObject != null)
            {
                var definition = app.Catalog.Find(checkout.definitionId);
                Vector3 staffPosition = app.World.CellCenter(ShopModel.CheckoutStaffCell(checkout, definition));
                var exactFront = ShopModel.CheckoutCustomerCell(checkout, definition);
                Vector3 customerPosition = app.World.CellCenter(exactFront);
                var cashierView = cashierObject.GetComponent<PaperCharacterView>();
                report.checkoutQueueObserved |= app.CheckoutQueueCount > 0 && cashierView != null && cashierView.IsWorking;
                bool cashierBehind = Vector3.SqrMagnitude(cashierObject.transform.position - staffPosition) < .0001f;
                report.cashierBehindCounter |= cashierBehind;
                if (cashierBehind && cashierView != null && cashierView.IsWorking
                    && app.PayingCustomerCell.HasValue && app.PayingCustomerCell.Value == exactFront)
                    foreach (var actor in app.World.GetComponentsInChildren<PaperCharacterView>())
                        if (actor.name.StartsWith("Customer ", StringComparison.Ordinal) && actor.IsWorking
                            && Vector3.SqrMagnitude(actor.transform.position - customerPosition) < .0001f)
                            report.customerPaysInFront = true;
            }
        }

        bool MainFlowSatisfied()
        {
            return report.sixCustomersConcurrent && report.shelfQueueObserved && report.checkoutQueueObserved
                && report.guidedShopping && report.autonomousShopping && report.randomDeclineObserved
                && report.multiItemSale && report.multiKindSale && report.severalSalesAndDepartures
                && report.queueServicesCompleted > 0 && report.cashierBehindCounter && report.customerPaysInFront;
        }

        int BasketUnits()
        {
            int units = 0;
            foreach (var basket in app.Economy.baskets) units += app.Economy.GetBasketQuantity(basket.transactionId);
            return units;
        }

        Dictionary<Transform, Vector3> ActorPositions()
        {
            var result = new Dictionary<Transform, Vector3>();
            foreach (var actor in app.World.GetComponentsInChildren<PaperCharacterView>())
                result[actor.transform] = actor.transform.position;
            return result;
        }

        bool PositionsUnchanged(Dictionary<Transform, Vector3> before)
        {
            var after = ActorPositions();
            if (before.Count != after.Count) return false;
            foreach (var pair in before)
                if (pair.Key == null || !after.TryGetValue(pair.Key, out var position)
                    || Vector3.SqrMagnitude(pair.Value - position) > .000001f) return false;
            return true;
        }

        void CheckConservation(string stage)
        {
            if (app.Economy.Validate(app.Catalog, app.Layout, out string reason)) return;
            report.inventoryConserved = false;
            Error("Inventory validation failed " + stage + ": " + reason);
        }

        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            if (finished) yield break;
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            if (screenshot == null || screenshot.width < 320 || screenshot.height < 200)
            {
                if (screenshot != null) Destroy(screenshot);
                Error("No usable rendered frame for " + filename + "; a visible graphics window is required.");
                yield break;
            }
            try
            {
                File.WriteAllBytes(Path.Combine(output, filename), screenshot.EncodeToPNG());
                report.screenshots++;
            }
            catch (Exception exception) { Error("Screenshot write failed: " + exception.Message); }
            finally { Destroy(screenshot); }
        }

        void Require(bool condition, string reason)
        {
            if (!condition) Error(reason);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            report.realtimeSeconds = Time.realtimeSinceStartup - startedReal;
            report.frameCount = Time.frameCount;
            report.renderedScreenshots = report.screenshots >= 6;
            Require(report.framesAdvanced, "The visible game did not advance enough frames.");
            Require(report.pauseHeld && report.resumed, "Pause/resume verification was incomplete.");
            Require(report.twoSalesCounters && report.unusedFacilitiesRemoved, "Test layout must keep two sales counters and remove unused facilities.");
            Require(report.threeStaffPresent, "Three active staff roles were not present.");
            Require(report.restockThresholdAdjustable, "Automatic replenishment threshold changes were not retained.");
            Require(report.cashierBehindCounter && report.customerPaysInFront, "Cashier-behind/customer-in-front checkout positions were not observed.");
            Require(report.sixCustomersConcurrent, "Six customers were not simultaneously present.");
            Require(report.shelfQueueObserved, "No waiting line at a sales counter was observed.");
            Require(report.checkoutQueueObserved, "No waiting line at checkout was observed.");
            Require(report.guidedShopping, "No guide greeting and completed recommendation were observed.");
            Require(report.autonomousShopping, "No autonomous counter visit after disabling the guide was observed.");
            Require(report.randomDeclineObserved, "No probabilistic purchase refusal was observed.");
            Require(report.multiItemSale && report.multiKindSale, "Completed multi-item and multi-kind purchases were not both observed.");
            Require(report.severalSalesAndDepartures && report.queueServicesCompleted > 0, "Insufficient completed purchases, departures, or queue services.");
            Require(report.noCustomerStopOverlap, "Customers shared a stopped/queued grid cell.");
            Require(report.inventoryConserved, "Stock conservation failed.");
            Require(report.closePaidBaskets && report.closeWaitedForDepartures && report.closeStoppedArrivals,
                "Closing did not retain baskets for payment, stop arrivals, and wait for actual departures.");
            Require(report.renderedScreenshots, "Required rendered evidence was incomplete.");
            report.passed = errors.Count == 0 && !report.timedOut;
            report.errors = errors.ToArray();
            try
            {
                if (!string.IsNullOrEmpty(output))
                    File.WriteAllText(Path.Combine(output, "crowd-verification.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                report.passed = false;
                Debug.LogError("Could not write crowd verification report: " + exception.Message);
            }
            Debug.Log("SHOP_CROWD_CHECKS_OK " + report.passed);
            Application.logMessageReceived -= Log;
            Application.Quit(report.passed ? 0 : 1);
        }

        void Error(string message)
        {
            if (reportedErrors.Add(message)) errors.Add(message);
        }

        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Error(message + "\n" + trace);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (active) Time.timeScale = originalTimeScale;
        }
    }
}
