using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Visible, isolated acceptance run for staffing, mixed replenishment and normal arrivals.</summary>
    [DefaultExecutionOrder(-450)]
    public sealed class ShopMechanicsVerification : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public bool passed, timedOut, inventoryConserved = true;
            public bool wallCheckoutPlacementAccepted, wallCheckoutPaymentReachable, wallCheckoutServed;
            public bool checkoutMarkersMatch, blockedWallNoPayment, blockedFurnitureNoPayment;
            public bool sixUsefulFacilities, staffPointsOneCell, staffIdleAtPosts, employeeAssignment;
            public bool duplicateAssignmentRejected, carryLimitConfigurable, upgradeResponsibilities;
            public bool mixedTrip, fairMixedAllocation, carryingWithinLimit = true, replenishmentCompleted;
            public bool capacityRemainderFetched, carriedSaveRoundTrip, loadedMixedCancellation;
            public bool configuredSaveRoundTrip, arrivalStateRoundTrip;
            public bool independentShelfThresholds, shelfThresholdInheritance, shelfThresholdPersistence, shelfSettingsUiOpened;
            public bool probabilityRisesWithPopularity, crowdingSuppressesArrivals, reproducibleSampling;
            public bool normalFlowUsed, initialArrivalWait, sparseLowPopularity, popularityEarned;
            public bool oneArrivalPerTrial = true, noArrivalBurst = true, noStoppedCustomerOverlap = true;
            public int warehousePickups, shelvesReplenished, maximumCarrying, mixedSkuCount;
            public int lowSampleArrivals, highSampleArrivals, lowAttempts, lowArrivals, highAttempts, highArrivals;
            public int probabilityRejections, crowdingHolds, peakCustomers, sales, initialPopularity, finalPopularity;
            public int screenshots;
            public double lowProbability, highProbability;
            public float minimumArrivalInterval = -1f, realtimeSeconds;
            public string[] errors;
        }

        readonly List<string> errors = new List<string>();
        readonly HashSet<string> seenErrors = new HashSet<string>(StringComparer.Ordinal);
        ShopApp app;
        Report report;
        string output;
        bool active, finished, observingArrivals;
        float started, deadline, originalScale, nextValidate, flowClock, lastArrivalAt = -1f;
        int observedArrivals, observedAttempts;

        void Awake()
        {
            active = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-mechanics") >= 0;
            if (active) Application.logMessageReceived += Log;
        }

        void Update()
        {
            if (!active || finished || report == null) return;
            if (app != null && app.Economy != null)
            {
                report.maximumCarrying = Math.Max(report.maximumCarrying, app.Economy.Carrying);
                report.carryingWithinLimit &= app.Economy.Carrying <= app.Economy.EffectiveCarryLimit;
                report.noStoppedCustomerOverlap &= app.CustomerStopConflicts == 0;
                report.peakCustomers = Math.Max(report.peakCustomers, app.CustomerCount);
                if (Time.realtimeSinceStartup >= nextValidate)
                {
                    nextValidate = Time.realtimeSinceStartup + .4f;
                    Validate("live simulation");
                }
                if (observingArrivals) ObserveArrivals();
            }
            if (Time.realtimeSinceStartup >= deadline)
            {
                report.timedOut = true;
                Error("Mechanics verification exceeded 150 real-time seconds.");
                Finish();
            }
        }

        IEnumerator Start()
        {
            if (!active) yield break;
            originalScale = Time.timeScale;
            started = Time.realtimeSinceStartup;
            deadline = started + 150f;
            report = new Report();
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--verify-mechanics");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Error("--verify-mechanics requires an output directory.");
                Finish();
                yield break;
            }
            try { output = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(output); }
            catch (Exception exception) { Error(exception.Message); Finish(); yield break; }
            Application.runInBackground = true;
            app = GetComponent<ShopApp>();
            yield return null;
            if (app == null || app.World == null || app.Economy == null)
            {
                Error("ShopApp/world/economy initialization failed.");
                Finish();
                yield break;
            }
            app.Economy.EnableTestResources();
            app.ForceResetCustomersForVerification();
            app.StartTwoCounterTest();
            app.ForceResetCustomersForVerification();
            app.SetAutoRestock(false);
            app.CancelRestockTask();
            app.SetPaused(false);
            app.SetCustomerLimit(ShopCustomerFlow.DefaultCustomerLimit);
            Time.timeScale = 4f;
            CheckFlowModel();

            var reception = app.Layout.facilities.Find(p => p.definitionId == "reception");
            var restock = app.Layout.facilities.Find(p => p.definitionId == "restock");
            var checkout = app.Layout.facilities.Find(p => p.definitionId == "checkout");
            var tops = app.Layout.facilities.Find(p => p.definitionId == "top");
            var shoes = app.Layout.facilities.Find(p => p.definitionId == "shoes");
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "reception", "restock", "checkout", "top", "shoes", "warehouse" };
            report.sixUsefulFacilities = app.Layout.facilities.Count == 6;
            foreach (var item in app.Layout.facilities) report.sixUsefulFacilities &= allowed.Remove(item.definitionId);
            report.sixUsefulFacilities &= allowed.Count == 0;
            if (reception == null || restock == null || checkout == null || tops == null || shoes == null)
            {
                Error("The six-facility mechanics layout is incomplete.");
                Finish();
                yield break;
            }
            report.wallCheckoutPlacementAccepted = app.ApplyPlacement(new PlacedFacility
            {
                instanceId = checkout.instanceId, definitionId = "checkout", x = 3, z = 7, rotation = 3
            }, out string wallPlacementReason);
            if (!report.wallCheckoutPlacementAccepted) Error("Door-side wall checkout placement rejected: " + wallPlacementReason);
            checkout = app.Layout.facilities.Find(p => p.definitionId == "checkout");
            var payment = ShopModel.PaymentCell(app.Layout, app.Catalog, checkout);
            var cashierCell = ShopModel.CheckoutStaffCell(checkout, app.Catalog.Find("checkout"));
            report.wallCheckoutPaymentReachable = checkout.x == 3 && checkout.z == 7 && checkout.rotation == 3
                && payment == new Vector2Int(2, 7) && payment == ShopModel.CheckoutCustomerCell(checkout, app.Catalog.Find("checkout"))
                && payment != cashierCell && ShopModel.CanUseCheckoutFront(app.Layout, app.Catalog, checkout, out _)
                && ShopModel.IsShopCell(app.Layout, payment) && !ShopModel.IsBlocked(app.Layout, app.Catalog, payment)
                && ShopModel.FindPath(app.Layout, app.Catalog, ShopModel.EntranceCell, payment).Count > 0
                && ShopModel.FindPath(app.Layout, app.Catalog, ShopModel.EntranceCell, cashierCell).Count > 0;
            report.staffPointsOneCell = ShopModel.Size(app.Catalog.Find("reception"), reception.rotation) == Vector2Int.one
                && ShopModel.Size(app.Catalog.Find("restock"), restock.rotation) == Vector2Int.one;
            float idleDeadline = Time.realtimeSinceStartup + 8f;
            while (!StaffAtPosts(reception, restock) && Time.realtimeSinceStartup < idleDeadline) yield return null;
            report.staffIdleAtPosts = StaffAtPosts(reception, restock);
            yield return Capture("01-staff-at-posts.png");
            app.World.SelectFacility(checkout.instanceId);
            app.World.ShowPlacement(checkout, true);
            report.checkoutMarkersMatch = HorizontalMatch(app.World.CheckoutPaymentMarkerPosition(checkout.instanceId), app.World.CellCenter(payment))
                && HorizontalMatch(app.World.CheckoutStaffMarkerPosition(checkout.instanceId), app.World.CellCenter(cashierCell))
                && HorizontalMatch(app.World.PlacementPaymentMarkerPosition, app.World.CellCenter(payment))
                && HorizontalMatch(app.World.PlacementStaffMarkerPosition, app.World.CellCenter(cashierCell));
            yield return Capture("08-checkout-front-rear-markers.png");
            app.World.HidePlacement();
            app.World.SelectFacility(null);

            report.employeeAssignment = app.AssignStaffToPost(reception.instanceId, "tomori")
                && app.Economy.Post(reception.instanceId).staffId == "tomori"
                && app.AssignStaffToPost(reception.instanceId, "soyo")
                && app.Economy.Post(reception.instanceId).staffId == "soyo";
            string beforeDuplicate = JsonUtility.ToJson(app.Economy);
            report.duplicateAssignmentRejected = !app.AssignStaffToPost(restock.instanceId, "soyo")
                && JsonUtility.ToJson(app.Economy) == beforeDuplicate;
            report.carryLimitConfigurable = app.SetStaffCarryLimit(restock.instanceId, 7)
                && app.Economy.EffectiveCarryLimit == 7
                && app.SetStaffCarryLimit(restock.instanceId, 10)
                && app.Economy.EffectiveCarryLimit == 10;
            Validate("after staff configuration");

            int pickupsBefore = app.WarehousePickups;
            int shelvesBefore = app.RestockJobsCompleted;
            int totalBefore = app.Economy.Shelf(tops.instanceId).quantity + app.Economy.Shelf(shoes.instanceId).quantity;
            Check(totalBefore == 0, "The replenishment acceptance scenario must start with empty sales counters.");
            Check(app.RequestRestockBatch(), "The mixed replenishment command was rejected.");
            bool carryCaptured = false;
            float replenishmentDeadline = Mathf.Min(deadline - 75f, Time.realtimeSinceStartup + 35f);
            while (Time.realtimeSinceStartup < replenishmentDeadline)
            {
                if (app.Economy.Carrying > 0 && !carryCaptured)
                {
                    var skus = new HashSet<string>(StringComparer.Ordinal);
                    bool allFive = app.Economy.jobs.Count == 2;
                    foreach (var task in app.Economy.jobs)
                    {
                        if (task.pickedUp) skus.Add(task.productId);
                        allFive &= task.pickedUp && task.quantity == 5;
                    }
                    report.mixedSkuCount = skus.Count;
                    report.mixedTrip = app.WarehousePickups == pickupsBefore + 1
                        && app.Economy.Carrying == 10 && skus.Count == 2;
                    report.fairMixedAllocation = report.mixedTrip && allFive;
                    app.SetPaused(true);
                    report.carriedSaveRoundTrip = SaveRoundTrip("mixed-carry-session.json", out var loaded);
                    if (loaded != null)
                    {
                        var ownedBefore = new Dictionary<string, int>();
                        foreach (var stock in loaded.economy.stocks) ownedBefore[stock.productId] = stock.available;
                        var carryingBySku = new Dictionary<string, int>();
                        foreach (var task in loaded.economy.jobs)
                        {
                            carryingBySku.TryGetValue(task.productId, out int quantity);
                            carryingBySku[task.productId] = quantity + task.quantity;
                        }
                        loaded.economy.CancelRestock();
                        report.loadedMixedCancellation = loaded.economy.jobs.Count == 0
                            && loaded.economy.Carrying == 0 && loaded.economy.Validate(app.Catalog, loaded.layout, out _);
                        foreach (var pair in carryingBySku)
                            report.loadedMixedCancellation &= loaded.economy.Stock(pair.Key).available == ownedBefore[pair.Key] + pair.Value;
                    }
                    yield return Capture("02-mixed-replenishment.png");
                    app.SetPaused(false);
                    carryCaptured = true;
                }
                bool full = app.Economy.Shelf(tops.instanceId).quantity == app.Economy.ShelfCapacity(app.Catalog, app.Layout, tops.instanceId)
                    && app.Economy.Shelf(shoes.instanceId).quantity == app.Economy.ShelfCapacity(app.Catalog, app.Layout, shoes.instanceId);
                if (full && !app.IsRestocking) break;
                yield return null;
            }
            report.warehousePickups = app.WarehousePickups - pickupsBefore;
            report.shelvesReplenished = app.RestockJobsCompleted - shelvesBefore;
            report.replenishmentCompleted = app.Economy.Shelf(tops.instanceId).quantity == 10
                && app.Economy.Shelf(shoes.instanceId).quantity == 10 && !app.IsRestocking;
            report.capacityRemainderFetched = report.replenishmentCompleted && report.warehousePickups == 2
                && report.shelvesReplenished == 4;
            Validate("after both mixed trips");
            yield return Capture("03-both-counters-stocked.png");

            string topProduct = app.Economy.Shelf(tops.instanceId).productId;
            int originalPrice = app.Economy.CheckoutPrice(app.Catalog, topProduct);
            int originalCapacity = app.Economy.ShelfCapacity(app.Catalog, app.Layout, tops.instanceId);
            int guideBonus = app.Economy.GuideQuantityBonus, carryCapacity = app.Economy.CarryCapacity;
            bool guideUpgrade = app.UpgradeStaffPost(reception.instanceId)
                && app.Economy.GuideQuantityBonus > guideBonus && app.Economy.CarryCapacity == carryCapacity
                && app.Economy.CheckoutPrice(app.Catalog, topProduct) == originalPrice;
            bool restockUpgrade = app.UpgradeStaffPost(restock.instanceId)
                && app.Economy.CarryCapacity > carryCapacity && app.Economy.EffectiveCarryLimit == 10
                && app.Economy.CheckoutPrice(app.Catalog, topProduct) == originalPrice;
            bool checkoutUpgrade = app.UpgradeStaffPost(checkout.instanceId)
                && app.Economy.CheckoutPrice(app.Catalog, topProduct) > originalPrice
                && app.Economy.ShelfCapacity(app.Catalog, app.Layout, tops.instanceId) == originalCapacity;
            int checkoutPrice = app.Economy.CheckoutPrice(app.Catalog, topProduct);
            bool shelfUpgrade = app.UpgradeSalesCounter(tops.instanceId)
                && app.Economy.ShelfCapacity(app.Catalog, app.Layout, tops.instanceId) > originalCapacity
                && app.Economy.CheckoutPrice(app.Catalog, topProduct) == checkoutPrice
                && app.Economy.ShelfPrice(app.Catalog, tops.instanceId) == originalPrice;
            report.upgradeResponsibilities = guideUpgrade && restockUpgrade && checkoutUpgrade && shelfUpgrade;
            report.carryLimitConfigurable &= app.SetStaffCarryLimit(restock.instanceId, app.Economy.CarryCapacity)
                && app.Economy.EffectiveCarryLimit == app.Economy.CarryCapacity;
            app.SetRestockThreshold(.5f);
            report.independentShelfThresholds = app.SetShelfRestockThreshold(tops.instanceId, 0f)
                && app.SetShelfRestockThreshold(shoes.instanceId, .5f);
            app.SetRestockThreshold(.75f);
            report.independentShelfThresholds &= Mathf.Abs(app.Economy.EffectiveRestockThreshold(tops.instanceId)) < .00001f
                && Mathf.Abs(app.Economy.EffectiveRestockThreshold(shoes.instanceId) - .5f) < .00001f;
            report.shelfThresholdInheritance = app.SetShelfRestockThreshold(tops.instanceId, null)
                && !app.Economy.Shelf(tops.instanceId).overrideRestockThreshold
                && Mathf.Abs(app.Economy.EffectiveRestockThreshold(tops.instanceId) - .75f) < .00001f
                && app.SetShelfRestockThreshold(tops.instanceId, .25f);
            report.configuredSaveRoundTrip = SaveRoundTrip("configured-posts-session.json", out var configured);
            report.shelfThresholdPersistence = configured != null
                && configured.economy.Shelf(tops.instanceId).overrideRestockThreshold
                && configured.economy.Shelf(shoes.instanceId).overrideRestockThreshold
                && Mathf.Abs(configured.economy.EffectiveRestockThreshold(tops.instanceId) - .25f) < .00001f
                && Mathf.Abs(configured.economy.EffectiveRestockThreshold(shoes.instanceId) - .5f) < .00001f
                && Mathf.Abs(configured.economy.autoRestockThreshold - .75f) < .00001f;
            yield return Capture("04-upgraded-posts.png");
            report.shelfSettingsUiOpened = app.OpenShelfSettings(tops.instanceId);
            yield return Capture("11-shelf-restock-settings.png");
            app.OpenPanel("shelf");
            yield return VerifyBlockedCheckout(checkout.instanceId, false);
            yield return VerifyBlockedCheckout(checkout.instanceId, true);
            checkout = app.Layout.facilities.Find(p => p.definitionId == "checkout");

            app.SetAutoRestock(true);
            app.SetGuideEnabled(true);
            app.SetCustomerFlowStressTest(false);
            report.normalFlowUsed = !app.CustomerFlowStressTest;
            Check(app.Economy.SetTestPopularity(0, out _), "Could not set the low-popularity test profile.");
            Check(app.Economy.SetTestCustomerFlowSeed(ShopCustomerFlow.DefaultSeed, out _), "Could not set the reproducible normal arrival seed.");
            report.initialPopularity = app.Economy.popularity;
            int transactionsBefore = app.Economy.transactions;
            app.SetShopOpen(true);
            BeginArrivalObservation();
            report.lowProbability = app.CurrentArrivalProbability;
            int lowAttemptStart = app.ArrivalAttempts, lowArrivalStart = app.ArrivalsSpawned;
            float initialWaitUntil = flowClock + ShopCustomerFlow.CheckInterval - .25f;
            while (flowClock < initialWaitUntil && Time.realtimeSinceStartup < deadline - 30f) yield return null;
            report.initialArrivalWait = app.ArrivalsSpawned == lowArrivalStart && app.ArrivalAttempts == lowAttemptStart;
            float flowDeadline = Mathf.Min(deadline - 35f, Time.realtimeSinceStartup + 28f);
            bool paymentCaptured = false;
            while (app.ArrivalAttempts - lowAttemptStart < 8 && Time.realtimeSinceStartup < flowDeadline)
            {
                if (!paymentCaptured && CustomerPayingAtWallCheckout(checkout))
                {
                    report.wallCheckoutServed = true;
                    yield return Capture("07-door-side-wall-checkout.png");
                    paymentCaptured = true;
                }
                yield return null;
            }
            report.lowAttempts = app.ArrivalAttempts - lowAttemptStart;
            report.lowArrivals = app.ArrivalsSpawned - lowArrivalStart;
            report.sparseLowPopularity = report.lowAttempts >= 8 && report.lowArrivals > 0
                && report.lowArrivals < report.lowAttempts;
            report.popularityEarned = app.Economy.transactions > transactionsBefore
                && app.Economy.popularity > report.initialPopularity;
            yield return Capture("05-normal-probability-customers.png");

            observingArrivals = false;
            app.ForceResetCustomersForVerification();
            Check(app.Economy.SetTestPopularity(90, out _), "Could not set the high-popularity test profile.");
            Check(app.Economy.SetTestCustomerFlowSeed(ShopCustomerFlow.DefaultSeed, out _), "Could not reset high-popularity arrival seed.");
            app.SetShopOpen(true);
            BeginArrivalObservation();
            report.highProbability = app.CurrentArrivalProbability;
            int highAttemptStart = app.ArrivalAttempts, highArrivalStart = app.ArrivalsSpawned;
            int highSalesBefore = app.Economy.transactions;
            flowDeadline = Mathf.Min(deadline - 5f, Time.realtimeSinceStartup + 28f);
            while (app.ArrivalAttempts - highAttemptStart < 8 && Time.realtimeSinceStartup < flowDeadline)
            {
                if (!paymentCaptured && CustomerPayingAtWallCheckout(checkout))
                {
                    report.wallCheckoutServed = true;
                    yield return Capture("07-door-side-wall-checkout.png");
                    paymentCaptured = true;
                }
                yield return null;
            }
            report.highAttempts = app.ArrivalAttempts - highAttemptStart;
            report.highArrivals = app.ArrivalsSpawned - highArrivalStart;
            report.probabilityRisesWithPopularity &= report.highProbability > report.lowProbability;
            report.popularityEarned |= app.Economy.transactions > highSalesBefore && app.Economy.popularity > 90;
            report.probabilityRejections = app.ProbabilityRejectedArrivals;
            report.crowdingHolds = app.CrowdingHeldArrivals;
            report.sales = app.Economy.transactions - transactionsBefore;
            report.finalPopularity = app.Economy.popularity;
            yield return Capture("06-popular-but-spaced-customers.png");
            observingArrivals = false;
            app.SetPaused(true);
            report.arrivalStateRoundTrip = SaveRoundTrip("arrival-state-session.json", out var restored);
            if (restored != null)
            {
                int liveState = app.Economy.customerArrivalRandomState, storedState = restored.economy.customerArrivalRandomState;
                for (int i = 0; i < 12; i++)
                    report.arrivalStateRoundTrip &= ShopCustomerFlow.NextSample(ref liveState) == ShopCustomerFlow.NextSample(ref storedState);
            }
            app.ForceResetCustomersForVerification();
            Validate("after ending the mechanics test");
            Finish();
        }

        void CheckFlowModel()
        {
            double low = ShopCustomerFlow.ArrivalProbability(0, 0, 3, 0, 0, false, true);
            double high = ShopCustomerFlow.ArrivalProbability(100, 0, 3, 0, 0, false, true);
            double one = ShopCustomerFlow.ArrivalProbability(100, 1, 3, 0, 0, false, true);
            double two = ShopCustomerFlow.ArrivalProbability(100, 2, 3, 0, 0, false, true);
            report.probabilityRisesWithPopularity = low > 0 && low < high && high < 1;
            report.crowdingSuppressesArrivals = one < high && two < one
                && ShopCustomerFlow.ArrivalProbability(100, 3, 3, 0, 0, false, true) == 0
                && ShopCustomerFlow.ArrivalProbability(100, 0, 3, 2, 0, false, true) == 0
                && ShopCustomerFlow.ArrivalProbability(100, 0, 3, 0, 2, false, true) == 0
                && ShopCustomerFlow.ArrivalProbability(100, 0, 3, 0, 0, true, true) == 0
                && ShopCustomerFlow.ArrivalProbability(100, 0, 3, 0, 0, false, false) == 0;
            int lowState = ShopCustomerFlow.DefaultSeed, highState = lowState, replayState = lowState;
            report.reproducibleSampling = true;
            for (int i = 0; i < 600; i++)
            {
                double sample = ShopCustomerFlow.NextSample(ref lowState);
                double highSample = ShopCustomerFlow.NextSample(ref highState);
                double replaySample = ShopCustomerFlow.NextSample(ref replayState);
                report.reproducibleSampling &= sample == highSample && sample == replaySample && sample >= 0 && sample < 1;
                if (sample < low) report.lowSampleArrivals++;
                if (highSample < high) report.highSampleArrivals++;
            }
            report.probabilityRisesWithPopularity &= report.lowSampleArrivals > 0
                && report.highSampleArrivals > report.lowSampleArrivals && report.highSampleArrivals < 600;
        }

        bool StaffAtPosts(PlacedFacility reception, PlacedFacility restock)
        {
            var guide = GameObject.Find("Guide placeholder");
            return guide != null && !app.IsRestocking && !app.IsWalking
                && Vector3.SqrMagnitude(guide.transform.position - app.World.CellCenter(new Vector2Int(reception.x, reception.z))) < .0001f
                && Vector3.SqrMagnitude(app.World.CharacterRoot.position - app.World.CellCenter(new Vector2Int(restock.x, restock.z))) < .0001f;
        }

        bool CustomerPayingAtWallCheckout(PlacedFacility checkout)
        {
            var cashierObject = GameObject.Find("Cashier placeholder");
            var cashier = cashierObject == null ? null : cashierObject.GetComponent<PaperCharacterView>();
            if (cashier == null || !cashier.IsWorking) return false;
            Vector3 staff = app.World.CellCenter(ShopModel.CheckoutStaffCell(checkout, app.Catalog.Find("checkout")));
            if (Vector3.SqrMagnitude(cashier.transform.position - staff) > .0001f) return false;
            var front = ShopModel.CheckoutCustomerCell(checkout, app.Catalog.Find("checkout"));
            if (!app.PayingCustomerCell.HasValue || app.PayingCustomerCell.Value != front) return false;
            Vector3 customer = app.World.CellCenter(front);
            foreach (var actor in app.World.GetComponentsInChildren<PaperCharacterView>())
                if (actor.name.StartsWith("Customer ", StringComparison.Ordinal) && actor.IsWorking
                    && Vector3.SqrMagnitude(actor.transform.position - customer) < .0001f) return true;
            return false;
        }

        static bool HorizontalMatch(Vector3 actual, Vector3 expected)
        {
            return Mathf.Abs(actual.x - expected.x) < .001f && Mathf.Abs(actual.z - expected.z) < .001f;
        }

        IEnumerator VerifyBlockedCheckout(string checkoutId, bool furniture)
        {
            var original = ShopModel.Clone(app.Layout);
            app.ForceResetCustomersForVerification();
            app.CancelRestockTask();
            var blockedCheckout = new PlacedFacility
            {
                instanceId = checkoutId, definitionId = "checkout",
                x = furniture ? 4 : 3, z = furniture ? 4 : 7, rotation = 0
            };
            bool placed = app.ApplyPlacement(blockedCheckout, out string reason);
            if (!placed) Error("Blocked-front checkout body should remain placeable: " + reason);
            if (furniture)
            {
                placed &= app.ApplyPlacement(new PlacedFacility
                {
                    instanceId = "verification-front-obstruction", definitionId = "decor", x = 4, z = 5
                }, out reason);
                if (!placed) Error("The interior checkout-front obstruction could not be placed: " + reason);
            }
            var current = app.Layout.facilities.Find(p => p.instanceId == checkoutId);
            bool blocked = !ShopModel.CanUseCheckoutFront(app.Layout, app.Catalog, current, out _)
                && !app.CanCheckoutServe
                && ShopModel.PaymentCell(app.Layout, app.Catalog, current) == ShopModel.CheckoutCustomerCell(current, app.Catalog.Find("checkout"));
            int transactionsBefore = app.Economy.transactions;
            long revenueBefore = app.Economy.revenue;
            app.SetCustomerFlowStressTest(true);
            app.SetCustomerLimit(3);
            app.SetGuideEnabled(false);
            app.SetAutoRestock(true);
            app.SetShopOpen(true);
            // A checkout with no usable payment cell must not admit shoppers,
            // otherwise waiting baskets would prevent the required renovation.
            bool openingRejected = !app.Economy.shopOpen && app.CanRenovate && app.CustomerCount == 0;
            float observeUntil = Mathf.Min(deadline - 60f, Time.realtimeSinceStartup + .5f);
            bool neverPaying = true;
            while (Time.realtimeSinceStartup < observeUntil)
            {
                neverPaying &= !app.PayingCustomerCell.HasValue;
                yield return null;
            }
            bool result = placed && blocked && openingRejected && neverPaying
                && app.Economy.transactions == transactionsBefore && app.Economy.revenue == revenueBefore;
            if (furniture) report.blockedFurnitureNoPayment = result;
            else report.blockedWallNoPayment = result;
            app.World.SelectFacility(checkoutId);
            yield return Capture(furniture ? "10-front-furniture-no-payment.png" : "09-front-wall-no-payment.png");
            app.ForceResetCustomersForVerification();
            app.CancelRestockTask();
            app.SetAutoRestock(false);
            app.SetCustomerFlowStressTest(false);
            app.ReplaceLayout(original);
            Validate(furniture ? "after furniture-blocked checkout" : "after wall-blocked checkout");
        }

        bool SaveRoundTrip(string filename, out ShopSession loaded)
        {
            loaded = null;
            try
            {
                string path = Path.Combine(output, filename);
                string before = JsonUtility.ToJson(new ShopSession { layout = app.Layout, economy = app.Economy });
                ShopSession.SaveToPath(app.Layout, app.Economy, app.Catalog, path);
                loaded = ShopSession.LoadFromPath(app.Catalog, path, out string reason);
                if (loaded == null) { Error("Save load failed: " + reason); return false; }
                bool same = JsonUtility.ToJson(loaded) == before && loaded.economy.Validate(app.Catalog, loaded.layout, out _);
                if (!same) Error("Save round-trip changed data: " + filename);
                return same;
            }
            catch (Exception exception) { Error("Save round-trip failed: " + filename + ": " + exception.Message); return false; }
        }

        void BeginArrivalObservation()
        {
            flowClock = 0;
            lastArrivalAt = -1f;
            observedArrivals = app.ArrivalsSpawned;
            observedAttempts = app.ArrivalAttempts;
            observingArrivals = true;
        }

        void ObserveArrivals()
        {
            if (!app.IsPaused) flowClock += Mathf.Min(Time.deltaTime, .1f);
            int arrived = app.ArrivalsSpawned - observedArrivals;
            int trials = app.ArrivalAttempts - observedAttempts;
            report.oneArrivalPerTrial &= arrived >= 0 && arrived <= 1 && trials >= 0 && trials <= 1 && arrived <= trials;
            if (arrived > 0)
            {
                if (lastArrivalAt >= 0)
                {
                    float interval = flowClock - lastArrivalAt;
                    report.minimumArrivalInterval = report.minimumArrivalInterval < 0 ? interval : Mathf.Min(report.minimumArrivalInterval, interval);
                    report.noArrivalBurst &= interval >= ShopCustomerFlow.CheckInterval - .15f;
                }
                lastArrivalAt = flowClock;
            }
            observedArrivals = app.ArrivalsSpawned;
            observedAttempts = app.ArrivalAttempts;
        }

        void Validate(string phase)
        {
            if (app.Economy.Validate(app.Catalog, app.Layout, out string reason)) return;
            report.inventoryConserved = false;
            Error("Inventory/state validation failed " + phase + ": " + reason);
        }

        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            if (finished) yield break;
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image == null) { Error("No rendered frame: " + filename); yield break; }
            try { File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG()); report.screenshots++; }
            catch (Exception exception) { Error(exception.Message); }
            finally { Destroy(image); }
        }

        void Check(bool condition, string error) { if (!condition) Error(error); }
        void Error(string error) { if (seenErrors.Add(error)) errors.Add(error); }
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
            Check(report.wallCheckoutPlacementAccepted && report.wallCheckoutPaymentReachable && report.wallCheckoutServed, "Door-side wall checkout did not place and serve a real payment.");
            Check(report.checkoutMarkersMatch, "Checkout placement/selected payment and cashier markers disagree with actual front/rear cells.");
            Check(report.blockedWallNoPayment && report.blockedFurnitureNoPayment, "A blocked checkout paid through a wall or used a side/remote fallback.");
            Check(report.sixUsefulFacilities && report.staffPointsOneCell && report.staffIdleAtPosts, "Useful facilities or default staff positions failed.");
            Check(report.employeeAssignment && report.duplicateAssignmentRejected, "Employee assignment or exclusive binding failed.");
            Check(report.carryLimitConfigurable && report.upgradeResponsibilities, "Carry settings or upgrade responsibilities failed.");
            Check(report.mixedTrip && report.fairMixedAllocation && report.carryingWithinLimit, "Mixed load or carrying capacity failed.");
            Check(report.replenishmentCompleted && report.capacityRemainderFetched, "The accepted manual batch did not fetch and shelve all remaining goods.");
            Check(report.carriedSaveRoundTrip && report.loadedMixedCancellation && report.configuredSaveRoundTrip && report.arrivalStateRoundTrip, "New mechanics did not survive save/load safely.");
            Check(report.independentShelfThresholds && report.shelfThresholdInheritance && report.shelfThresholdPersistence
                && report.shelfSettingsUiOpened, "Per-counter replenishment settings, inheritance, persistence, or actual shelf settings UI failed.");
            Check(report.probabilityRisesWithPopularity && report.crowdingSuppressesArrivals && report.reproducibleSampling, "Arrival probability/pressure/random-state checks failed.");
            Check(report.normalFlowUsed && report.initialArrivalWait && report.sparseLowPopularity, "Normal sparse probabilistic arrivals were not observed.");
            Check(report.highAttempts >= 8 && report.highArrivals > 0 && report.popularityEarned, "High-popularity visits or earned popularity were not observed.");
            Check(report.oneArrivalPerTrial && report.noArrivalBurst && report.noStoppedCustomerOverlap, "Arrivals burst or stopped customers overlapped.");
            Check(report.inventoryConserved, "Inventory conservation failed.");
            Check(report.screenshots >= 6, "Rendered acceptance evidence is incomplete.");
            report.passed = errors.Count == 0 && !report.timedOut;
            report.errors = errors.ToArray();
            try
            {
                if (!string.IsNullOrEmpty(output))
                    File.WriteAllText(Path.Combine(output, "mechanics-verification.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception) { report.passed = false; Debug.LogError(exception.Message); }
            Time.timeScale = originalScale;
            Application.logMessageReceived -= Log;
            Debug.Log("SHOP_MECHANICS_CHECKS_OK " + report.passed);
            Application.Quit(report.passed ? 0 : 1);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (active) Time.timeScale = originalScale;
        }
    }
}
