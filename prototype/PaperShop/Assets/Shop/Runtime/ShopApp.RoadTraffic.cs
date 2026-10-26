using System;
using System.Collections.Generic;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        sealed class Passerby
        {
            public PaperCharacterView view;
            public int lane,direction,items;
            public float x;
            public bool entryChecked;
            public bool clearingDoor;
            public string visitingStaffId;
        }
        readonly List<Passerby> passers=new List<Passerby>();
        float roadTimer=1;
        public int RoadPedestrianCount=>passers.Count;
        public int RoadSpawned {get;private set;}
        public int RoadEntries {get;private set;}
        public int WindowShoppers {get;private set;}
        public int DoorDepartures {get;private set;}
        public int LeftRoadSpawns {get;private set;}
        public int RightRoadSpawns {get;private set;}
        public double RoadEntryProbability=>!Economy.shopOpen||guests.Count>=customerLimit||ArrivalEntranceCrowded?0:
            (Economy.IsPerforming?ShopBalance.PerformanceEntryChance(Economy.popularity):ShopBalance.EntryChance(Economy.popularity))*(1-guests.Count/16.0);
        void ClearRoadTraffic()
        {foreach(var p in passers)if(p.view!=null)Destroy(p.view.gameObject);passers.Clear();roadTimer=1;}
        void TickRoadTraffic(float dt)
        {
            if(dt<=0)return;
            roadTimer-=dt;
            if(roadTimer<=0)
            {
                roadTimer=1.8f;
                if(passers.Count<16&&customerRandom.NextDouble()<.78)
                {
                    int direction=customerRandom.Next(2)==0?1:-1,lane=Layout.depth+customerRandom.Next(1,4);
                    float start=direction==1?-1:Layout.width;
                    var view=World.CreatePaperActor("Road visitor",Color.white);
                    string[] skins={"guest01","guest02","guest03","guest04","guest05"};
                    view.SetCharacter(skins[appearanceRandom.Next(skins.Length)]);view.SetPose("walk");
                    view.transform.position=World.CellCenter(new Vector2Int((int)start,lane));
                    passers.Add(new Passerby{view=view,x=start,lane=lane,direction=direction});
                    RoadSpawned++;if(direction==1)LeftRoadSpawns++;else RightRoadSpawns++;
                }
            }
            foreach(var p in new List<Passerby>(passers))
            {
                // Crossing the threshold counts as leaving the shop, but the
                // visual actor must clear the outward door leaves before turning.
                if(p.clearingDoor)
                {
                    var clear=World.CellCenter(new Vector2Int((int)p.x,p.lane));
                    p.view.transform.position=Vector3.MoveTowards(p.view.transform.position,clear,dt*1.8f);
                    p.view.SetMoving(true);p.view.SetPose(p.items>0?"carry":"walk");p.view.Tick(dt);
                    if((p.view.transform.position-clear).sqrMagnitude<.0001f)p.clearingDoor=false;
                    continue;
                }
                float before=p.x;p.x+=p.direction*dt*1.45f;
                float decision=ShopModel.EntranceColumn(Layout);
                if(!p.entryChecked&&(before-decision)*p.direction<=0&&(p.x-decision)*p.direction>=0)
                {
                    p.entryChecked=true;
                    if(!customerFlowStressTest&&Economy.RollCustomerArrival(RoadEntryProbability))
                    {
                        p.x=decision;p.view.transform.position=World.CellCenter(new Vector2Int((int)decision,p.lane));
                        if(CreateShopVisitor(new Vector2Int((int)decision,p.lane),p.view))
                        {passers.Remove(p);RoadEntries++;continue;}
                    }
                }
                if(p.x< -1.3f||p.x>Layout.width+.3f){Destroy(p.view.gameObject);passers.Remove(p);continue;}
                var origin=World.CellCenter(new Vector2Int(0,p.lane));p.view.transform.position=origin+Vector3.right*p.x;
                p.view.SetMoving(true);p.view.SetPose(p.items>0?"carry":"walk");p.view.Tick(dt);
            }
        }
        bool CreateShopVisitor(Vector2Int start,PaperCharacterView incoming)
        {
            if(guests.Count>=customerLimit||!StopAvailable(null,start))return false;
            var g=new Guest{number=++visitors,cell=start,transactionId=Guid.NewGuid().ToString("N"),phase=GuestPhase.Entering};
            g.view=incoming??World.CreatePaperActor("Shop visitor",Color.white);g.view.transform.position=World.CellCenter(start);
            ConfigureGuestAppearance(g);guests.Add(g);
            RouteEnteringGuest(g);PeakCustomers=Mathf.Max(PeakCustomers,guests.Count);return true;
        }
        bool RouteEnteringGuest(Guest g)
        {
            // The doorway can also be the checkout front: it is a transit tile, never an arrival reservation.
            var entrance=ShopModel.EntranceFor(Layout);var cells=new List<Vector2Int>();
            for(int z=0;z<Layout.depth;z++)for(int x=0;x<Layout.width;x++)
            {var cell=new Vector2Int(x,z);if(!IsServiceOrDoor(cell)&&StopAvailable(g,cell))cells.Add(cell);}
            cells.Sort((a,b)=>(Mathf.Abs(a.x-entrance.x)+Mathf.Abs(a.y-entrance.y)).CompareTo(Mathf.Abs(b.x-entrance.x)+Mathf.Abs(b.y-entrance.y)));
            foreach(var cell in cells)if(GuestRoute(g,cell))return true;
            return false;
        }
        void PrepareShoppingPlan(Guest g)
        {
            g.plannedKinds=1;
            if(customerRandom.NextDouble()<ShopBalance.ExtraKindChance(g.guideQuantityBonusSnapshot))g.plannedKinds++;
            if(g.plannedKinds>1&&customerRandom.NextDouble()<.08+g.guideQuantityBonusSnapshot*.002)g.plannedKinds++;
            var choices=new List<string>();var kinds=new HashSet<string>();
            foreach(var s in Economy.shelves)
            {var f=Layout.facilities.Find(p=>p.instanceId==s.instanceId);if(f!=null&&!kinds.Contains(s.productId)&&ShopModel.FindPath(Layout,catalog,g.cell,ShopModel.ServiceCell(f,catalog.Find(f.definitionId))).Count>0){choices.Add(s.instanceId);kinds.Add(s.productId);}}
            for(int i=choices.Count-1;i>0;i--){int j=customerRandom.Next(i+1);var id=choices[i];choices[i]=choices[j];choices[j]=id;}
            int visits=Math.Min(choices.Count,Math.Max(g.plannedKinds,customerRandom.Next(1,4)));
            g.itinerary.Clear();g.itinerary.AddRange(choices.GetRange(0,visits));
        }
        public double VisitShoppingProbability=>Math.Min(.95,ShopBalance.ShoppingChance(Economy.popularity)+Economy.PerformancePurchaseBonus);
        void BeginStoreVisit(Guest g)
        {
            g.hasDestination=false;
            if(!Economy.shopOpen){LeaveGuest(g,"已停止迎客");return;}
            if(!customerFlowStressTest&&customerRandom.NextDouble()>=VisitShoppingProbability)
            {WindowShoppers++;g.phase=GuestPhase.Wandering;g.wanderStops=customerRandom.Next(2,5);g.remaining=0;return;}
            PrepareShoppingPlan(g);
            if(!TryAssignGuestGuide(g))StartAutonomous(g);
        }
        void TickWindowShopper(Guest g,float dt)
        {
            g.remaining-=dt;if(g.remaining>0)return;
            if(g.wanderStops--<=0){LeaveGuest(g,"闲逛结束");return;}
            var cells=new List<Vector2Int>();
            for(int z=0;z<Layout.depth;z++)for(int x=0;x<Layout.width;x++)
            {var cell=new Vector2Int(x,z);if(!IsServiceOrDoor(cell)&&StopAvailable(g,cell))cells.Add(cell);}
            for(int i=0;i<Math.Min(20,cells.Count);i++)if(GuestRoute(g,cells[customerRandom.Next(cells.Count)])){g.remaining=1.5f+(float)customerRandom.NextDouble()*1.5f;return;}
            LeaveGuest(g,"闲逛结束");
        }
        void ExitThroughDoor(Guest g)
        {
            ReleaseGuestGuide(g,false);Economy.ReturnBasket(g.transactionId);guests.Remove(g);
            DepartedCustomers++;DoorDepartures++;
            passers.Add(new Passerby{view=g.view,x=g.cell.x,lane=Layout.depth+2,clearingDoor=true,direction=customerRandom.Next(2)==0?1:-1,items=g.bought,entryChecked=true,visitingStaffId=g.visitingStaffId});
        }
    }
}
