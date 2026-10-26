using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    [DefaultExecutionOrder(-500)]
    public sealed class ShopVerification : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public bool passed, timedOut;
            public bool framesAdvanced, pathCompleted, pathStayedClear, pauseHeld, resumed, interacting;
            public bool placementAccepted, collisionRejected, visibleFacilitiesSelectable, warehouseDoorSelectable, retargetKeepsPosition;
            public bool productAssigned, contracted, purchaseOrdered, dispatchWaitObserved, deliveryPauseHeld, deliveryTimerAdvanced, deliveryArrivedAfter20GameSeconds;
            public bool returnedBeforeRestock, manualRestockStarted, stockReservedBeforePickup, reachedWarehouseDoor, pickupAtWarehouse;
            public bool carryingVisualObserved, carryingScreenshotCaptured, restockCompleted, inventoryConserved, warehouseExpanded;
            public bool customerSaleCompleted, saleConserved, testResourcesEnabled, testStockInfinite;
            public bool warehouseDoorMoved, upgradedShelfFilled, upgradedPriceApplied, noCarryProp;
            public bool exactlyTwoSalesCounters, threeStaffPresent, guideGreeted, guideRecommended, guidedSaleCompleted, customerDeparted, testRestockerWorked;
            public int frameCount, warehouseAfterDelivery, warehouseAfterRestock, shelfAfterRestock, purchasedTotal, capacityAfterExpansion, worldWarehouseLevel;
            public float realtimeSeconds, deliveryGameSecondsObserved, deliveryRealtimeSeconds;
            public string[] errors;
        }

        readonly List<string> errors = new List<string>();
        bool active, finished;
        string output;
        float startedReal, deadline, originalTimeScale;
        Report report;
        ShopApp app;

        void Awake()
        {
            active = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-map") >= 0;
            if (active) Application.logMessageReceived += Log;
        }

        void Update()
        {
            // This still runs if a coroutine throws or a stage stops progressing.
            if (!active || finished || report == null || Time.realtimeSinceStartup < deadline) return;
            report.timedOut = true;
            errors.Add("Runtime integration verification exceeded its 90-second real-time deadline.");
            Finish();
        }

        IEnumerator Start()
        {
            if (!active) yield break;
            originalTimeScale = Time.timeScale;
            startedReal = Time.realtimeSinceStartup;
            deadline = startedReal + 90f;
            report = new Report { pathStayedClear = true, inventoryConserved = true };
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--verify-map");
            if (index + 1 >= args.Length)
            {
                errors.Add("--verify-map requires an output directory.");
                Finish();
                yield break;
            }
            output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(output);
            Application.runInBackground = true;
            app = GetComponent<ShopApp>();
            yield return null;
            if (app == null || app.World == null || app.Economy == null)
            {
                errors.Add("ShopApp, world, or economy did not initialize.");
                Finish();
                yield break;
            }
            app.SetAutoRestock(false);
            if (!app.SetCustomerFlowStressTest(true)) errors.Add("Explicit legacy integration customer stress mode could not be enabled.");
            app.SetPaused(false);
            int frame = Time.frameCount;
            yield return new WaitForSecondsRealtime(1f);
            report.framesAdvanced = Time.frameCount > frame + 10;
            report.visibleFacilitiesSelectable = true;
            foreach (var item in app.Layout.facilities)
            {
                var definition = app.Catalog.Find(item.definitionId);
                var size = ShopModel.Size(definition, item.rotation);
                Vector3 target;
                if (definition.kind == FacilityKind.Warehouse)
                {
                    // The annex has an empty center. Its real door sign is a
                    // selectable renderer on the shop-facing side of the wall.
                    target = app.World.WarehouseDoorPosition;
                }
                else
                    target = app.World.CellCenter(new Vector2Int(item.x, item.z))
                        + new Vector3((size.x - 1) * .5f, ShopModel.IsStaffPoint(definition.kind) ? .02f : .15f, (size.y - 1) * .5f);
                Vector3 screen = app.World.WorldCamera.WorldToScreenPoint(target);
                bool selectable = app.World.PickFacility(screen, out string picked) && picked == item.instanceId;
                report.visibleFacilitiesSelectable &= selectable;
                if (definition.kind == FacilityKind.Warehouse) report.warehouseDoorSelectable = selectable;
                if (!selectable) errors.Add("Visible facility did not pick its instance: " + item.instanceId + "; picked=" + picked);
            }
            yield return Capture("01-shop-map.png");

            Vector2Int warehouseCell = ShopModel.WarehouseServiceCell;
            Vector3 start = app.World.CharacterRoot.position;
            app.MoveActor(warehouseCell, true);
            yield return new WaitForSecondsRealtime(.22f);
            Vector3 beforeRetarget = app.World.CharacterRoot.position;
            app.MoveActor(warehouseCell, true);
            report.retargetKeepsPosition = Vector3.Distance(beforeRetarget, app.World.CharacterRoot.position) < .0001f;
            yield return new WaitForSecondsRealtime(.23f);
            app.SetPaused(true);
            Vector3 paused = app.World.CharacterRoot.position;
            yield return new WaitForSecondsRealtime(.4f);
            report.pauseHeld = Vector3.Distance(paused, app.World.CharacterRoot.position) < .0001f;
            app.SetPaused(false);
            yield return new WaitForSecondsRealtime(.4f);
            report.resumed = Vector3.Distance(paused, app.World.CharacterRoot.position) > .05f;
            float stageDeadline = Mathf.Min(deadline, Time.realtimeSinceStartup + 12f);
            while (app.IsWalking && Time.realtimeSinceStartup < stageDeadline)
            {
                ObservePath();
                yield return null;
            }
            report.pathCompleted = !app.IsWalking && app.CompletedWalks > 0 && app.ActorCell == warehouseCell
                && Vector3.Distance(start, app.World.CharacterRoot.position) > .5f;
            yield return new WaitForSecondsRealtime(.35f);
            report.interacting = app.World.Character.IsWorking;
            yield return Capture("02-service-point.png");

            var placement = new PlacedFacility { instanceId = "verify-decor", definitionId = "decor", x = 0, z = 2, rotation = 1 };
            report.placementAccepted = app.ApplyPlacement(placement, out _);
            string snapshot = JsonUtility.ToJson(app.Layout);
            report.collisionRejected = !app.ApplyPlacement(new PlacedFacility { instanceId = "verify-overlap", definitionId = "decor", x = 0, z = 2 }, out _)
                && snapshot == JsonUtility.ToJson(app.Layout);
            yield return new WaitForSecondsRealtime(.3f);
            yield return Capture("03-layout-edited.png");

            // Use the app's commands and normal Update clock throughout. No direct
            // Economy.Tick, inventory edits, pickup, or completion calls are made.
            report.productAssigned = app.SetShelfProduct("top-1", "top_tshirt");
            report.contracted = app.ContractProduct("top_tshirt") && app.Economy.Stock("top_tshirt").contracted;
            report.purchaseOrdered = app.OrderProduct("top_tshirt") && app.Economy.orders.Count == 1
                && app.Economy.InTransit == 20 && app.Economy.Stock("top_tshirt").purchased == 20;
            CheckConservation("after purchase");
            var order = Delivery();
            if (!report.purchaseOrdered || order == null)
            {
                errors.Add("Could not create the expected 20-unit delivery through ShopApp commands.");
                Finish();
                yield break;
            }
            bool configuredFor20 = order.awaitingDispatch && Mathf.Abs(order.dispatchRemaining - ShopEconomy.DispatchSeconds) < .001f
                && Mathf.Abs(order.remaining - ShopEconomy.DeliverySeconds) < .001f
                && Mathf.Abs(ShopEconomy.DeliverySeconds - 20f) < .001f;
            app.SetPaused(true);
            float pausedRemaining = order.remaining;
            float pausedDispatch = order.dispatchRemaining;
            int pausedWarehouse = app.Economy.WarehouseUsed;
            app.OpenPanel("goods");
            yield return Capture("04-product-panel.png");
            yield return new WaitForSecondsRealtime(.5f);
            report.deliveryPauseHeld = Delivery() == order && Mathf.Abs(order.remaining - pausedRemaining) < .00001f
                && Mathf.Abs(order.dispatchRemaining - pausedDispatch) < .00001f
                && app.Economy.WarehouseUsed == pausedWarehouse;
            app.OpenPanel("goods");
            app.SetPaused(false);
            Time.timeScale = 4f;
            float deliveryStarted = Time.realtimeSinceStartup;
            float previousRemaining = order.remaining;
            stageDeadline = Mathf.Min(deadline, deliveryStarted + 25f);
            while (Delivery() != null && Time.realtimeSinceStartup < stageDeadline)
            {
                yield return null;
                var current = Delivery();
                if(current != null && current.awaitingDispatch && current.dispatchRemaining < pausedDispatch)
                    report.dispatchWaitObserved = Mathf.Abs(current.remaining - ShopEconomy.DeliverySeconds) < .001f;
                float remaining = current == null ? 0f : current.remaining;
                float advanced = previousRemaining - remaining;
                if (advanced > .00001f) report.deliveryTimerAdvanced = true;
                if (advanced < -.001f) errors.Add("Delivery countdown moved backwards.");
                report.deliveryGameSecondsObserved += Mathf.Max(0f, advanced);
                previousRemaining = remaining;
            }
            report.deliveryRealtimeSeconds = Time.realtimeSinceStartup - deliveryStarted;
            report.warehouseAfterDelivery = app.Economy.WarehouseUsed;
            report.deliveryArrivedAfter20GameSeconds = configuredFor20 && Delivery() == null
                && report.deliveryGameSecondsObserved >= 19.99f && app.Economy.Stock("top_tshirt").available == 20
                && report.warehouseAfterDelivery == 20 && !app.IsRestocking;
            CheckConservation("after delivery");
            if (!report.deliveryArrivedAfter20GameSeconds)
            {
                errors.Add("The real Update delivery did not arrive as 20 warehouse units within the stage deadline.");
                Finish();
                yield break;
            }

            // Start away from the door, so this proves the actual warehouse leg
            // rather than issuing a restock while already at the pickup point.
            bool returnRequested = app.MoveActor(app.RestockHomeCell);
            stageDeadline = Mathf.Min(deadline, Time.realtimeSinceStartup + 8f);
            while (app.IsWalking && Time.realtimeSinceStartup < stageDeadline)
            {
                ObservePath();
                yield return null;
            }
            report.returnedBeforeRestock = returnRequested && !app.IsWalking && app.ActorCell == app.RestockHomeCell;
            report.manualRestockStarted = app.RequestRestock("top-1") && app.IsRestocking;
            var stock = app.Economy.Stock("top_tshirt");
            report.stockReservedBeforePickup = app.Economy.job != null && !app.Economy.job.pickedUp
                && app.Economy.job.quantity == 10 && stock.available == 10 && stock.reserved == 10;
            CheckConservation("after reserve");
            bool previouslyPickedUp = false;
            stageDeadline = Mathf.Min(deadline, Time.realtimeSinceStartup + 14f);
            while (app.IsRestocking && Time.realtimeSinceStartup < stageDeadline)
            {
                ObservePath();
                bool atWarehouse = app.ActorCell == warehouseCell
                    && Vector3.Distance(app.World.CharacterRoot.position, app.World.CellCenter(warehouseCell)) < .06f;
                report.reachedWarehouseDoor |= atWarehouse;
                var job = app.Economy.job;
                if (job != null && job.pickedUp && !previouslyPickedUp)
                {
                    report.pickupAtWarehouse = atWarehouse;
                    CheckConservation("at pickup");
                }
                if (job != null && job.pickedUp)
                {
                    previouslyPickedUp = true;
                    report.carryingVisualObserved |= app.World.IsCarrying && app.Economy.Carrying == 10;
                    if (!report.carryingScreenshotCaptured && app.World.IsCarrying && app.IsWalking
                        && Vector3.Distance(app.World.CharacterRoot.position, app.World.CellCenter(warehouseCell)) > .2f)
                    {
                        yield return Capture("05-restock-carrying.png");
                        report.carryingScreenshotCaptured = File.Exists(Path.Combine(output, "05-restock-carrying.png"));
                    }
                }
                yield return null;
            }
            var shelf = app.Economy.Shelf("top-1");
            stock = app.Economy.Stock("top_tshirt");
            report.warehouseAfterRestock = app.Economy.WarehouseUsed;
            report.shelfAfterRestock = shelf == null ? -1 : shelf.quantity;
            report.purchasedTotal = stock == null ? -1 : stock.purchased;
            report.restockCompleted = !app.IsRestocking && !app.World.IsCarrying && app.Economy.Carrying == 0
                && report.shelfAfterRestock == 10 && report.warehouseAfterRestock == 10
                && stock != null && stock.available == 10 && stock.reserved == 0 && stock.purchased == 20;
            CheckConservation("after shelving");

            bool expanded = app.ExpandWarehouse();
            report.capacityAfterExpansion = app.Economy.Capacity;
            report.worldWarehouseLevel = app.World.WarehouseLevel;
            report.warehouseExpanded = expanded && report.capacityAfterExpansion == 400
                && app.Economy.warehouseLevel == 2 && report.worldWarehouseLevel == 2;
            CheckConservation("after expansion");
            app.OpenPanel("warehouse");
            yield return Capture("06-warehouse-panel.png");
            app.OpenPanel("warehouse");
            yield return Capture("07-expanded-warehouse.png");
            int cashBeforeSale=app.Economy.cash;
            long revenueBeforeSale=app.Economy.revenue;
            int frontPaymentsBeforeSale=app.PaymentsAtFront;
            app.SetShopOpen(true);
            stageDeadline=Mathf.Min(deadline,Time.realtimeSinceStartup+18f);
            while(app.Economy.transactions==0 && Time.realtimeSinceStartup<stageDeadline)yield return null;
            long saleRevenue=app.Economy.revenue-revenueBeforeSale;
            report.customerSaleCompleted=app.Economy.transactions>=1 && saleRevenue>0
                && (long)app.Economy.cash-cashBeforeSale==saleRevenue && app.PaymentsAtFront>frontPaymentsBeforeSale;
            app.ForceResetCustomersForVerification();
            report.saleConserved=app.Economy.Validate(app.Catalog,app.Layout,out _);
            app.Economy.cash=0;app.Economy.EnableTestResources();
            report.testResourcesEnabled=app.Economy.testMode && app.Economy.stocks.TrueForAll(s=>s.contracted);
            report.testStockInfinite=app.RequestRestock("shoes-1");
            stageDeadline=Mathf.Min(deadline,Time.realtimeSinceStartup+24f);
            while((app.IsRestocking || app.Economy.restockFollowUp) && Time.realtimeSinceStartup<stageDeadline)yield return null;
            report.testStockInfinite &= app.Economy.Shelf("shoes-1").quantity==10 && app.Economy.cash==0 && app.Economy.Validate(app.Catalog,app.Layout,out _);
            report.warehouseDoorMoved=app.MoveWarehouseDoor(6) && ShopModel.WarehousePickup(app.Layout)==new Vector2Int(7,6);
            var checkoutPost=app.Economy.PostFor(FacilityKind.Checkout);
            report.upgradedPriceApplied=checkoutPost!=null && app.UpgradeStaffPost(checkoutPost.instanceId)
                && app.UpgradeSalesCounter("shoes-1") && app.Economy.CheckoutPrice(app.Catalog,app.Economy.Shelf("shoes-1").productId)==12
                && app.Economy.ShelfPrice(app.Catalog,"shoes-1")==10 && app.Economy.ShelfCapacity(app.Catalog,app.Layout,"shoes-1")==20;
            bool refill=app.RequestRestock("shoes-1");
            stageDeadline=Mathf.Min(deadline,Time.realtimeSinceStartup+14f);
            while((app.IsRestocking || app.Economy.restockFollowUp) && Time.realtimeSinceStartup<stageDeadline)yield return null;
            report.upgradedShelfFilled=refill && app.Economy.Shelf("shoes-1").quantity==20 && app.Economy.Validate(app.Catalog,app.Layout,out _);
            report.noCarryProp=app.World.CharacterRoot.Find("Carried parcel placeholder")==null;
            app.OpenPanel("trade");
            yield return Capture("08-trading-panel.png");
            app.OpenPanel("trade");
            app.StartTwoCounterTest();
            report.exactlyTwoSalesCounters=app.Economy.shelves.Count==2;
            report.threeStaffPresent=app.HasAllStaff;
            int oldTransactions=app.Economy.transactions,oldDepartures=app.DepartedCustomers,oldRestocks=app.RestockJobsCompleted;
            int oldGreetings=app.GuideGreetings,oldRecommendations=app.GuideRecommendations,oldGuidedVisits=app.GuidedVisits;
            stageDeadline=Mathf.Min(deadline,Time.realtimeSinceStartup+26f);
            bool greetingShot=false,recommendationShot=false;
            while(!(app.Economy.transactions>oldTransactions && app.DepartedCustomers>oldDepartures
                && app.GuideGreetings>oldGreetings && app.GuideRecommendations>oldRecommendations
                && app.GuidedVisits>oldGuidedVisits && app.RestockJobsCompleted>oldRestocks)
                && Time.realtimeSinceStartup<stageDeadline)
            {
                if(!greetingShot && app.GuideGreetings>oldGreetings){yield return Capture("09-guide-escort.png");greetingShot=true;}
                if(!recommendationShot && app.GuideRecommendations>oldRecommendations){yield return Capture("10-guide-recommends.png");recommendationShot=true;}
                yield return null;
            }
            report.guideGreeted=app.GuideGreetings>oldGreetings;
            report.guideRecommended=app.GuideRecommendations>oldRecommendations;
            report.guidedSaleCompleted=app.Economy.transactions>oldTransactions && app.GuidedVisits>oldGuidedVisits && app.Economy.Validate(app.Catalog,app.Layout,out _);
            report.customerDeparted=app.DepartedCustomers>oldDepartures;
            report.testRestockerWorked=app.RestockJobsCompleted>oldRestocks;
            app.OpenPanel("staff");yield return Capture("11-three-staff.png");app.OpenPanel("staff");
            app.OpenPanel("trade");yield return Capture("12-sales-flow-results.png");app.OpenPanel("trade");
            Finish();
        }

        DeliveryOrder Delivery()
        {
            return app.Economy.orders.Find(item => item != null && item.productId == "top_tshirt");
        }

        void ObservePath()
        {
            Vector3 delta = app.World.CharacterRoot.position - app.World.CellCenter(Vector2Int.zero);
            var cell = new Vector2Int(Mathf.RoundToInt(delta.x), Mathf.RoundToInt(delta.z));
            report.pathStayedClear &= ShopModel.IsShopCell(app.Layout, cell) && !ShopModel.IsBlocked(app.Layout, app.Catalog, cell);
        }

        void CheckConservation(string stage)
        {
            if (app.Economy.Validate(app.Catalog, app.Layout, out string reason)) return;
            report.inventoryConserved = false;
            errors.Add("Inventory validation failed " + stage + ": " + reason);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            StopAllCoroutines();
            Time.timeScale = originalTimeScale;
            if (report == null) report = new Report();
            report.frameCount = Time.frameCount;
            report.realtimeSeconds = Time.realtimeSinceStartup - startedReal;
            report.passed = !report.timedOut && report.framesAdvanced && report.pathCompleted && report.pathStayedClear
                && report.pauseHeld && report.resumed && report.interacting && report.placementAccepted && report.collisionRejected
                && report.visibleFacilitiesSelectable && report.warehouseDoorSelectable && report.retargetKeepsPosition
                && report.productAssigned && report.contracted && report.purchaseOrdered && report.deliveryPauseHeld
                && report.dispatchWaitObserved && report.deliveryTimerAdvanced && report.deliveryArrivedAfter20GameSeconds && report.returnedBeforeRestock
                && report.manualRestockStarted && report.stockReservedBeforePickup && report.reachedWarehouseDoor
                && report.pickupAtWarehouse && report.carryingVisualObserved && report.carryingScreenshotCaptured
                && report.restockCompleted && report.inventoryConserved && report.warehouseExpanded && report.customerSaleCompleted
                && report.saleConserved && report.testResourcesEnabled && report.testStockInfinite && report.warehouseDoorMoved
                && report.upgradedShelfFilled && report.upgradedPriceApplied && report.noCarryProp && report.exactlyTwoSalesCounters
                && report.threeStaffPresent && report.guideGreeted && report.guideRecommended && report.guidedSaleCompleted
                && report.customerDeparted && report.testRestockerWorked && errors.Count == 0;
            report.errors = errors.ToArray();
            try
            {
                if (!string.IsNullOrEmpty(output))
                    File.WriteAllText(Path.Combine(output, "runtime-verification.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                report.passed = false;
                Debug.LogError("Could not write runtime verification report: " + exception.Message);
            }
            Debug.Log("SHOP_RUNTIME_CHECKS_OK " + report.passed);
            Application.logMessageReceived -= Log;
            Application.Quit(report.passed ? 0 : 1);
        }

        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image == null)
            {
                errors.Add("No rendered frame for " + filename + "; verification needs a visible graphics window.");
                yield break;
            }
            File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG());
            Destroy(image);
        }

        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message + "\n" + trace);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (active) Time.timeScale = originalTimeScale;
        }
    }
}

