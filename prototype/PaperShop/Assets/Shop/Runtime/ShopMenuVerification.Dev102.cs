using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopMenuVerification
    {
        object InvokeApp(string method,params object[] values)=>typeof(ShopApp).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(App,values);
        IEnumerator VerifyDev102()
        {
            App.ChooseTutorial(true);App.SetPaused(true);
            App.TutorialAction();yield return new WaitForSecondsRealtime(.8f);
            yield return Capture("06-tutorial-circle.png");
            Check(App.LastTutorialCircle.width>0&&Mathf.Abs(App.LastTutorialCircle.width-App.LastTutorialCircle.height)<.01f,"Facility tutorial draws a real circle around the model");
            var ring=ReadPresentationField<Texture2D>("tutorialRing");
            Check(ring!=null&&ring.GetPixel(128,128).a==0&&ring.GetPixel(128,250).a>.5f,"Tutorial ring has a transparent center and a visible round edge");
            App.Economy.tutorialStep=-1;
            yield return VerifyDepartureClearance();
            Check(App.ReturnToTitle(),"Save finite test and return");yield return WaitForScene(true);
            Check(App.StartSlot(4),"Start isolated maximum-level store for second worker");yield return WaitForScene(false);
            App.SetPaused(true);App.Economy.shopOpen=false;InvokeApp("ClearCustomers");
            Check(App.BuyCounter("bottom"),"Buy a second display so each restocker has a cabinet in its stable assignment");
            var stored=App.Economy.storedFixtures.Find(f=>f.definitionId=="bottom");bool displayPlaced=false;
            for(int z=1;z<App.Layout.depth-1&&!displayPlaced;z++)for(int x=1;x<App.Layout.width-1&&!displayPlaced;x++)
                displayPlaced=App.ApplyPlacement(new PlacedFacility{instanceId=stored.instanceId,definitionId="bottom",x=x,z=z},out _);
            Check(displayPlaced,"Place the second worker's display with a reachable service point");
            Check(App.BuyAdditionalStaffPost(FacilityKind.Restock),"Buy unlocked second restock point");
            bool placed=false;
            for(int z=1;z<App.Layout.depth-1&&!placed;z++)for(int x=1;x<App.Layout.width-1&&!placed;x++)
                placed=App.ApplyPlacement(new PlacedFacility{instanceId="dev-second",definitionId="restock",x=x,z=z},out _);
            Check(placed&&App.AssignStaffToPost("dev-second","rana"),"Place second point and assign Rana");
            App.EndRenovation();SetPresentationField("panelKey","");
            App.Economy.CancelRestock();
            foreach(var shelf in App.Economy.shelves){App.Economy.Stock(shelf.productId).available+=shelf.quantity;shelf.quantity=0;}
            InvokeApp("TickExtraRestocker",0f);InvokeApp("SyncStaffCards");
            var actor=ReadPresentationField<PaperCharacterView>("extraRestocker");
            Check(actor!=null&&actor.CharacterId=="rana"&&App.ExtraRestockStatus=="待岗","New second worker starts with correct card and idle status");
            int pickups=App.ExtraWarehousePickups,completed=App.ExtraRestocksCompleted;
            Check(App.Economy.BeginRestockBatch(App.Catalog,App.Layout,App.Economy.EffectiveCarryLimit,null,out _),"Primary worker reserves its own display while second starts independently");
            string primaryShelf=App.Economy.job.shelfId;
            var states=new HashSet<string>();var poses=new HashSet<string>();
            bool captured=false;
            for(int step=0;step<2400;step++)
            {
                InvokeApp("TickExtraRestocker",.05f);InvokeApp("SyncStaffCards");
                states.Add(App.ExtraRestockStatus);poses.Add(actor.CurrentPose);
                if(!captured&&App.ExtraRestockStatus=="运送货物")
                {
                    actor.Tick(.3f);yield return Capture("07-second-worker-carrying.png");captured=true;
                    var card=actor.GetComponentInChildren<MeshRenderer>();
                    Check(card.bounds.max.x<=App.Layout.width*.5f+.03f,"Upright stock-carrying card stays inside the warehouse wall plane");
                }
                if(App.ExtraRestocksCompleted>completed&&App.ExtraRestockStatus=="待岗")break;
                if(step%200==0)yield return null;
            }
            foreach(var state in new[]{"前往仓库","取货中","运送货物","补货中","返回岗位","待岗"})
                Check(states.Contains(state),"Second restocker reaches actual state: "+state);
            Check(poses.Contains("walk")&&poses.Contains("restock")&&poses.Contains("idle"),"Second worker switches walk, restock and idle cards");
            Check(App.ExtraWarehousePickups>pickups&&App.ExtraRestocksCompleted>completed&&App.Economy.shelves[1].quantity>0,"Second worker really picks up stock and fills its own display");
            Check(App.Economy.job!=null&&App.Economy.job.shelfId==primaryShelf&&!App.Economy.job.pickedUp,"Second worker never consumes or clears the primary worker's reservation");
            var point=App.Layout.facilities.Find(f=>f.instanceId=="dev-second");
            Check((actor.transform.position-App.World.CellCenter(new Vector2Int(point.x,point.z))).sqrMagnitude<.001f,"Second worker returns to its own point after finishing");
            Check(App.Economy.Validate(App.Catalog,App.Layout,out _),"Second restock cycle preserves inventory accounting");
            yield return Capture("07-second-worker-idle.png");
            var ownShelf=App.Economy.shelves[1];var ownStock=App.Economy.Stock(ownShelf.productId);
            ownStock.available+=ownShelf.quantity-5;ownShelf.quantity=5;completed=App.ExtraRestocksCompleted;
            for(int step=0;step<80;step++)InvokeApp("TickExtraRestocker",.05f);
            Check(App.ExtraRestocksCompleted==completed&&App.ExtraRestockStatus=="待岗","Second worker waits at exactly half-full capacity");
            ownShelf.quantity=4;ownStock.available++;
            for(int step=0;step<1600&&App.ExtraRestocksCompleted==completed;step++)InvokeApp("TickExtraRestocker",.05f);
            Check(App.ExtraRestocksCompleted>completed&&ownShelf.quantity>=5,"Second worker resumes after stock falls below half");
            App.Economy.CancelRestock();
            Check(App.AssignStaffToPost("dev-second",""),"Second worker can be unassigned while closed");InvokeApp("TickExtraRestocker",.05f);
            Check(App.ExtraRestockStatus=="未上岗"&&!actor.gameObject.activeSelf,"Unassigned second worker has no stale working visual");
            yield return VerifyDepartureClearance();CheckPreferencesUntouched();
        }
        IEnumerator VerifyDepartureClearance()
        {
            for(int column=0;column<2;column++)for(int direction=-1;direction<=1;direction+=2)
            {
                InvokeApp("ClearCustomers");SetPresentationField("roadTimer",10000f);
                var outside=ShopModel.EntranceFor(App.Layout)+Vector2Int.up+Vector2Int.left*column;
                Check((bool)InvokeApp("CreateShopVisitor",outside,null),"Prepare door departure on side "+column+" / "+direction);
                var guests=ReadPresentationField<IList>("guests");var guest=guests[0];var gt=guest.GetType();
                gt.GetField("cell").SetValue(guest,outside);var actor=(PaperCharacterView)gt.GetField("view").GetValue(guest);
                var start=actor.transform.position;int left=App.DoorDepartures;InvokeApp("ExitThroughDoor",guest);
                Check(App.CustomerCount==0&&App.DoorDepartures==left+1&&actor.transform.position==start,"Door crossing frees indoor occupancy once without teleporting");
                var road=ReadPresentationField<IList>("passers");var passer=road[0];var pt=passer.GetType();pt.GetField("direction").SetValue(passer,direction);
                bool straight=true,smooth=true;Vector3 previous=start;
                for(int i=0;i<90&&(bool)pt.GetField("clearingDoor").GetValue(passer);i++)
                {
                    InvokeApp("TickRoadTraffic",.025f);var position=actor.transform.position;
                    straight&=Mathf.Abs(position.x-start.x)<.001f;smooth&=Vector3.Distance(previous,position)<=.046f;previous=position;
                }
                Check(straight&&smooth&&!(bool)pt.GetField("clearingDoor").GetValue(passer),"Actor moves straight clear of open leaves with no horizontal jump");
                Check(actor.transform.position.z>=App.Layout.depth*.5f+2.49f,"Actor clears the door by 2.5 metres before turning");
                float x=actor.transform.position.x;InvokeApp("TickRoadTraffic",.1f);
                Check((actor.transform.position.x-x)*direction>0&&App.DoorDepartures==left+1,"Actor then walks toward its chosen road end, without counting departure twice");
                if(column==0&&direction==1)yield return Capture("08-door-clearance-"+App.Layout.width+".png");
            }
            InvokeApp("ClearCustomers");
        }
    }
}
