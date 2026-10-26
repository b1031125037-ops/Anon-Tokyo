using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopMenuVerification
    {
        IEnumerator VerifyBalancedTraffic()
        {
            App.SetCustomerFlowStressTest(false);App.SetCustomerLimit(12);App.SetPaused(true);
            Check(App.Economy.shelves.Count==1&&App.Economy.cash==800,"V012 actual fresh scene has one display and 800 coins");
            Check(Resources.Load<Texture2D>("UI/World/decoration")!=null,"Decoration icon is imported in the player");
            App.Economy.autoRestock=false;
            var shelf=App.Economy.shelves[0];var stock=App.Economy.Stock(shelf.productId);stock.contracted=true;
            App.SetShopOpen(true);
            var tick=typeof(ShopApp).GetMethod("TickCustomers",BindingFlags.Instance|BindingFlags.NonPublic);
            int maximumBasket=0;int before=App.RoadSpawned;
            for(int i=0;i<3000;i++)
            {
                int topup=10-shelf.quantity;shelf.quantity+=topup;stock.purchased+=topup;
                tick.Invoke(App,new object[]{.1f});
                foreach(var basket in App.Economy.baskets)maximumBasket=Math.Max(maximumBasket,App.Economy.GetBasketQuantity(basket.transactionId));
                if(i%100==0)yield return null;
                if(i==1500){App.World.SyncProductDisplays(App.Economy);yield return Capture("90-v012-natural-road.png");}
            }
            Check(App.RoadSpawned-before>70&&App.LeftRoadSpawns>20&&App.RightRoadSpawns>20,"Natural traffic spawns frequently from both road ends");
            Check(App.RoadEntries>5&&App.WindowShoppers>0&&App.Economy.transactions>3,"Natural entry, window shopping and successful purchase branches all execute");
            Check(maximumBasket>=2&&maximumBasket<=3,"One display produces one SKU with one or two base units and at most one reception bonus");
            Check(App.PeakCustomers<=12&&App.CustomerStopConflicts==0,"Natural visitors respect the twelve-guest cap and distinct stopping cells");
            Check(App.DoorDepartures>5&&App.RoadPedestrianCount>0,"Crossing the door finishes visits while outdoor pedestrians continue");
            Check(App.Economy.Validate(App.Catalog,App.Layout,out string validation),"Traffic stock conservation: "+validation);
            App.SetShopOpen(false);
            for(int i=0;i<1200&&!App.CanRenovate;i++){tick.Invoke(App,new object[]{.1f});if(i%100==0)yield return null;}
            Check(App.CanRenovate&&App.CustomerCount==0&&App.RoadPedestrianCount>0,"Closing completes at the door without waiting for road pedestrians");
            yield return Capture("91-v012-door-departure.png");
            SetPresentationField("selectedId",shelf.instanceId);App.OpenPanel("shelf");yield return Capture("92-v012-shelf-compact.png");
            Check(App.CompactPanelRect.width<360,"Facility status is a compact side panel");
            SetPresentationField("panelKey","");App.OpenStaffPost("checkout-1");yield return Capture("93-v012-post-compact.png");
            SetPresentationField("profileId","anon");SetPresentationField("panelKey","profile");yield return Capture("94-v012-staff-compact.png");
            SetPresentationField("panelKey","warehouse");yield return Capture("95-v012-warehouse-compact.png");
            SetPresentationField("panelKey","");App.SetPaused(false);App.ApplyApplicationFocus(false);
            double elapsed=App.SimulationElapsedSeconds;long offline=App.Economy.offlineUnitsSold;
            yield return new WaitForSecondsRealtime(.4f);
            Check(Application.runInBackground&&App.SimulationElapsedSeconds>elapsed&&App.Economy.offlineUnitsSold==offline,"Unfocused frames continue live simulation without offline reward");
            App.OpenPanel("settings");elapsed=App.SimulationElapsedSeconds;yield return new WaitForSecondsRealtime(.3f);
            Check(App.IsSimulationPaused&&App.SimulationElapsedSeconds==elapsed,"Settings pauses the running scene even while unfocused");
            App.ApplyApplicationFocus(true);SetPresentationField("panelKey","");yield return new WaitForSecondsRealtime(.2f);
            Check(App.SimulationElapsedSeconds>elapsed,"Closing settings resumes simulation immediately");
            App.SetPaused(true);
            Debug.Log("V012_TRAFFIC_METRICS road="+App.RoadSpawned+" entered="+App.RoadEntries+" window="+App.WindowShoppers+" paid="+App.Economy.transactions+" peak="+App.PeakCustomers+" door="+App.DoorDepartures);
        }
    }
}
