using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Exercises real close/pay/exit, renovation guards and owned fixture storage.</summary>
    [DefaultExecutionOrder(-430)]
    public sealed class ShopRenovationVerification : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public bool passed, timedOut, inventoryConserved=true;
            public bool closingRetainedBaskets, closingRetainedActors, stoppedArrivals, renovationBlockedWhileOpen;
            public bool renovationBlockedWhileClosing, resetBlockedWhileClosing, cashierRemovalRejected;
            public bool cashierFailurePreservesBaskets, cashierRecoveryPreservesBaskets, closingSaveIsClosed;
            public bool allClosingGoodsPaid, allCustomersWalkedOut, renovationAvailableAfterExit;
            public bool renovationKeepsSimulationRunning;
            public bool purchaseToStorage, purchaseLimitIncludesStorage, failedPurchaseAtomic, placementConsumesOne;
            public bool previewCancellationPreservesOwnedCounter;
            public bool storeRetainsOwnership, storePreservesUpgradeAndThreshold, storedFixtureSaveRoundTrip;
            public bool levelKeepsAreaLimit, expansionRaisesLimit, levelNeverFalls, openingBlockedDuringRenovation;
            public int customersBeforeClose, unpaidBeforeClose, transactionsBeforeClose, transactionsAfterClose;
            public int soldDuringClosing, screenshots, shopLevel, ownedFixtures, fixtureLimit;
            public float realtimeSeconds;
            public string[] errors;
        }
        ShopApp app;
        Report report;
        string output;
        bool active, finished;
        float started, deadline, originalScale, nextValidate;
        readonly List<string> errors=new List<string>();
        readonly HashSet<string> uniqueErrors=new HashSet<string>(StringComparer.Ordinal);

        void Awake()
        {
            active=Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-renovation")>=0;
            if(active)Application.logMessageReceived+=Log;
        }
        void Update()
        {
            if(!active || finished || report==null)return;
            if(app!=null && app.Economy!=null && Time.realtimeSinceStartup>=nextValidate)
            {nextValidate=Time.realtimeSinceStartup+.5f;Validate("live");}
            if(Time.realtimeSinceStartup>=deadline)
            {report.timedOut=true;Error("Renovation verification exceeded 150 seconds.");Finish();}
        }
        IEnumerator Start()
        {
            if(!active)yield break;
            originalScale=Time.timeScale;started=Time.realtimeSinceStartup;deadline=started+150f;report=new Report();
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--verify-renovation");
            if(index+1>=args.Length || args[index+1].StartsWith("--",StringComparison.Ordinal))
            {Error("--verify-renovation needs an output directory.");Finish();yield break;}
            try{output=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(output);}
            catch(Exception ex){Error(ex.Message);Finish();yield break;}
            Application.runInBackground=true;app=GetComponent<ShopApp>();yield return null;
            if(app==null || app.Economy==null || app.World==null)
            {Error("Shop did not initialize.");Finish();yield break;}
            app.Economy.EnableTestResources();
            Check(app.ForceResetCustomersForVerification(),"Verification reset unavailable.");
            app.StartTwoCounterTest();app.ForceResetCustomersForVerification();
            app.SetPaused(false);app.ApplyApplicationFocus(true);app.ApplyApplicationPause(false);
            app.SetGameSpeed(2);Time.timeScale=4f;app.SetGuideEnabled(false);app.SetAutoRestock(true);
            float stage=Mathf.Min(deadline-105f,Time.realtimeSinceStartup+30f);
            while((app.Economy.shelves.Exists(s=>s.quantity<10) || app.IsRestocking) && Time.realtimeSinceStartup<stage)yield return null;
            Check(app.Economy.shelves.TrueForAll(s=>s.quantity>0),"Counters were not stocked for real shopping.");
            Check(app.SetCustomerFlowStressTest(true),"Renovation verification arrival mode unavailable.");
            app.SetCustomerLimit(4);app.SetShopOpen(true);
            var checkout=app.Layout.facilities.Find(f=>f.definitionId=="checkout");
            var unchangedCheckout=new PlacedFacility{instanceId=checkout.instanceId,definitionId="checkout",x=checkout.x,z=checkout.z,rotation=checkout.rotation};
            string layoutBefore=JsonUtility.ToJson(app.Layout);
            report.renovationBlockedWhileOpen=!app.CanRenovate && !app.ApplyPlacement(unchangedCheckout,out _)
                && JsonUtility.ToJson(app.Layout)==layoutBefore;
            stage=Mathf.Min(deadline-75f,Time.realtimeSinceStartup+35f);
            while((BasketUnits()==0 || app.CustomerCount<2) && Time.realtimeSinceStartup<stage)yield return null;
            app.SetPaused(true);
            report.unpaidBeforeClose=BasketUnits();report.customersBeforeClose=app.CustomerCount;
            report.transactionsBeforeClose=app.Economy.transactions;
            int soldBefore=SoldUnits(), arrivalsBefore=app.ArrivalsSpawned, departuresBefore=app.DepartedCustomers;
            int attemptsBefore=app.ArrivalAttempts;
            app.RequestCloseShop(out _);
            report.closingRetainedBaskets=report.unpaidBeforeClose>0 && BasketUnits()==report.unpaidBeforeClose;
            report.closingRetainedActors=report.customersBeforeClose>=2 && app.CustomerCount==report.customersBeforeClose && app.IsClosing;
            report.renovationBlockedWhileClosing=!app.CanRenovate && !app.ApplyPlacement(unchangedCheckout,out _)
                && JsonUtility.ToJson(app.Layout)==layoutBefore;
            app.StartTwoCounterTest();
            report.resetBlockedWhileClosing=app.IsClosing && app.CustomerCount==report.customersBeforeClose
                && BasketUnits()==report.unpaidBeforeClose && JsonUtility.ToJson(app.Layout)==layoutBefore;
            string cashierId=app.Economy.AssignedStaffId(FacilityKind.Checkout);
            report.cashierRemovalRejected=!app.AssignStaffToPost(checkout.instanceId,"")
                && app.Economy.AssignedStaffId(FacilityKind.Checkout)==cashierId;
            report.closingSaveIsClosed=SaveRoundTrip("closing-session.json",true);
            app.OpenPanel("trade");yield return Capture("01-closing-keeps-real-customers.png");app.OpenPanel("trade");
            // Simulate an externally interrupted employee assignment. Gameplay's
            // removal command above is guarded; recovery must keep real baskets.
            Check(app.Economy.AssignStaff(app.Catalog,checkout.instanceId,"",out _),"Could not stage missing cashier fixture.");
            app.SetPaused(false);
            yield return new WaitForSecondsRealtime(2f);
            report.cashierFailurePreservesBaskets=app.IsClosing && !app.CanRenovate && !app.CanCheckoutServe
                && BasketUnits()==report.unpaidBeforeClose && app.Economy.transactions==report.transactionsBeforeClose;
            app.SetPaused(true);
            report.cashierRecoveryPreservesBaskets=app.AssignStaffToPost(checkout.instanceId,cashierId)
                && BasketUnits()==report.unpaidBeforeClose && app.CustomerCount>0;
            yield return Capture("02-closing-payment-queue.png");
            app.SetPaused(false);
            stage=Mathf.Min(deadline-45f,Time.realtimeSinceStartup+30f);
            while(app.IsClosing && Time.realtimeSinceStartup<stage)yield return null;
            report.transactionsAfterClose=app.Economy.transactions;report.soldDuringClosing=SoldUnits()-soldBefore;
            report.allClosingGoodsPaid=BasketUnits()==0 && report.soldDuringClosing==report.unpaidBeforeClose
                && report.transactionsAfterClose>report.transactionsBeforeClose;
            report.allCustomersWalkedOut=app.CustomerCount==0 && app.DepartedCustomers-departuresBefore==report.customersBeforeClose;
            report.stoppedArrivals=app.ArrivalsSpawned==arrivalsBefore && app.ArrivalAttempts==attemptsBefore;
            report.renovationAvailableAfterExit=app.CanRenovate && !app.IsClosing;
            Check(report.renovationAvailableAfterExit,"Closing did not reach an empty closed store.");
            if(!app.CanRenovate){Finish();yield break;}
            app.SetAutoRestock(false);app.CancelRestockTask();app.OpenPanel("build");
            report.renovationAvailableAfterExit &= app.IsEditingLayout && !app.IsSimulationPaused;
            double renovationStarted = app.SimulationElapsedSeconds;
            yield return new WaitForSecondsRealtime(.4f);
            report.renovationKeepsSimulationRunning=app.IsEditingLayout && !app.IsSimulationPaused
                && app.SimulationElapsedSeconds>renovationStarted+.2 && !app.Economy.shopOpen && app.CustomerCount==0;
            yield return Capture("03-closed-renovation-available.png");
            app.SetShopOpen(true);
            // Opening a shop may close the editor instead of rejecting. In both
            // cases it must never leave an open shop in editable placement mode.
            report.openingBlockedDuringRenovation=!app.Economy.shopOpen || !app.IsEditingLayout;
            if(app.Economy.shopOpen)app.SetShopOpen(false);
            if(app.IsEditingLayout)app.OpenPanel("build");
            int ownedBefore=app.Economy.OwnedFixtureCount, storedBefore=app.Economy.StoredFixtureCount;
            bool firstPurchase=app.BuyCounter("top");
            var stored=app.Economy.FirstStoredFixture("top");
            report.purchaseToStorage=firstPurchase && stored!=null && app.Economy.OwnedFixtureCount==ownedBefore+1
                && app.Economy.StoredFixtureCount==storedBefore+1 && !app.Layout.facilities.Exists(f=>f.instanceId==stored.instanceId);
            string purchasedId=stored?.instanceId;
            string beforePreview=JsonUtility.ToJson(app.Economy);
            bool previewStarted=!string.IsNullOrEmpty(purchasedId) && app.BeginStoredCounterPlacement(purchasedId);
            app.OpenPanel("store");
            report.previewCancellationPreservesOwnedCounter=previewStarted && app.Economy.StoredFixture(purchasedId)!=null
                && beforePreview==JsonUtility.ToJson(app.Economy);
            while(app.Economy.OwnedFixtureCount<app.Economy.FixtureLimit)
                if(!app.BuyCounter("shoes")){Error("Purchase before fixture limit failed.");break;}
            string beforeRejected=JsonUtility.ToJson(app.Economy);
            report.purchaseLimitIncludesStorage=app.Economy.OwnedFixtureCount==app.Economy.FixtureLimit && !app.BuyCounter("top");
            report.failedPurchaseAtomic=beforeRejected==JsonUtility.ToJson(app.Economy);
            int beforePlace=app.Economy.StoredFixtureCount;
            bool placed=false;
            if(!string.IsNullOrEmpty(purchasedId))
            {
                for(int z=0;z<app.Layout.depth && !placed;z++)
                    for(int x=0;x<app.Layout.width && !placed;x++)
                        for(int rotation=0;rotation<4 && !placed;rotation++)
                        {
                            var proposed=new PlacedFacility{instanceId=purchasedId,definitionId="top",x=x,z=z,rotation=rotation};
                            var draft=ShopModel.Clone(app.Layout);
                            if(!ShopModel.TryPlace(draft,app.Catalog,proposed,out _) || !ShopModel.ValidateCheckoutAccess(draft,app.Catalog,out _))continue;
                            placed=app.ApplyPlacement(proposed,out _);
                        }
            }
            report.placementConsumesOne=placed && app.Economy.StoredFixtureCount==beforePlace-1
                && app.Economy.StoredFixture(purchasedId)==null && app.Economy.OwnedFixtureCount==app.Economy.FixtureLimit;
            if(placed)
            {
                Check(app.UpgradeSalesCounter(purchasedId),"Purchased fixture upgrade failed.");
                Check(app.SetShelfRestockThreshold(purchasedId,.75f),"Purchased fixture threshold failed.");
                int owned=app.Economy.OwnedFixtureCount;
                bool storedBack=app.StoreSalesCounter(purchasedId);
                var restored=app.Economy.StoredFixture(purchasedId);
                report.storeRetainsOwnership=storedBack && app.Economy.OwnedFixtureCount==owned
                    && !app.Layout.facilities.Exists(f=>f.instanceId==purchasedId);
                report.storePreservesUpgradeAndThreshold=restored!=null && restored.shelf.level==2
                    && restored.shelf.overrideRestockThreshold && Mathf.Abs(restored.shelf.restockThreshold-.75f)<.0001f;
            }
            report.storedFixtureSaveRoundTrip=SaveRoundTrip("owned-fixtures-session.json",false);
            int oldLimit=app.Economy.FixtureLimit;
            app.SetTestPopularity(20);
            report.levelKeepsAreaLimit=app.Economy.ShopLevel>=2 && app.Economy.FixtureLimit==oldLimit;
            report.expansionRaisesLimit=app.ExpandShop() && app.Economy.FixtureLimit>oldLimit && app.BuyCounter("head");
            int levelBefore=app.Economy.ShopLevel;
            app.SetTestPopularity(0);
            report.levelNeverFalls=app.Economy.ShopLevel==levelBefore;
            report.shopLevel=app.Economy.ShopLevel;report.ownedFixtures=app.Economy.OwnedFixtureCount;report.fixtureLimit=app.Economy.FixtureLimit;
            app.OpenCounterStore();yield return Capture("04-owned-fixture-shop.png");
            app.OpenPanel("settings");yield return Capture("05-settings-after-renovation.png");
            Validate("completed ownership and closing");
            Finish();
        }
        [Serializable] sealed class FixtureSnapshot {public List<StoredFixture> items;}
        int BasketUnits(){int n=0;foreach(var basket in app.Economy.baskets)n+=app.Economy.GetBasketQuantity(basket.transactionId);return n;}
        int SoldUnits(){int n=0;foreach(var stock in app.Economy.stocks)n+=stock.sold;return n;}
        bool SaveRoundTrip(string filename,bool closing)
        {
            try
            {
                string path=Path.Combine(output,filename);
                ShopSession.SaveToPath(app.Layout,app.Economy,app.Catalog,path);
                var loaded=ShopSession.LoadFromPath(app.Catalog,path,out string reason);
                if(loaded==null){Error("Save load failed: "+reason);return false;}
                return !loaded.economy.shopOpen && loaded.economy.Validate(app.Catalog,loaded.layout,out _)
                    && (closing?loaded.economy.baskets.Count==app.Economy.baskets.Count:
                        JsonUtility.ToJson(new FixtureSnapshot{items=loaded.economy.storedFixtures})==
                            JsonUtility.ToJson(new FixtureSnapshot{items=app.Economy.storedFixtures})
                        && loaded.economy.StoredFixtureCount==app.Economy.StoredFixtureCount
                        && loaded.economy.OwnedFixtureCount==app.Economy.OwnedFixtureCount);
            }
            catch(Exception ex){Error(ex.Message);return false;}
        }
        void Validate(string phase)
        {
            if(app.Economy.Validate(app.Catalog,app.Layout,out string reason))return;
            report.inventoryConserved=false;Error("State validation "+phase+": "+reason);
        }
        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();if(finished)yield break;
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            if(image==null){Error("Missing screenshot "+filename);yield break;}
            try{File.WriteAllBytes(Path.Combine(output,filename),image.EncodeToPNG());report.screenshots++;}
            catch(Exception ex){Error(ex.Message);}
            finally{Destroy(image);}
        }
        void Check(bool condition,string error){if(!condition)Error(error);}
        void Error(string message){if(uniqueErrors.Add(message))errors.Add(message);}
        void Log(string message,string trace,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)Error(message+"\n"+trace);}
        void Finish()
        {
            if(finished)return;finished=true;StopAllCoroutines();
            foreach(var field in typeof(Report).GetFields())
                if(field.FieldType==typeof(bool) && field.Name!="passed" && field.Name!="timedOut")
                    Check((bool)field.GetValue(report),"Acceptance failed: "+field.Name);
            report.realtimeSeconds=Time.realtimeSinceStartup-started;report.passed=errors.Count==0 && !report.timedOut;report.errors=errors.ToArray();
            if(!string.IsNullOrEmpty(output))File.WriteAllText(Path.Combine(output,"renovation-verification.json"),JsonUtility.ToJson(report,true));
            Time.timeScale=originalScale;Application.logMessageReceived-=Log;
            Debug.Log("SHOP_RENOVATION_CHECKS_OK "+report.passed);Application.Quit(report.passed?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;if(active)Time.timeScale=originalScale;}
    }
}
