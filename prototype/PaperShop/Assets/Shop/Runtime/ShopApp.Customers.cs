using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public enum GuestPhase { ToReception, WaitingGuide, ShoppingQueue, Browsing, CheckoutQueue, Paying, Leaving, Entering, Wandering }
        sealed class Guest
        {
            public int number, ticket, bought, kinds, guideQuantityBonusSnapshot,plannedKinds=1,wanderStops;
            public PaperCharacterView view;
            public Vector2Int cell, destination;
            public bool hasDestination, guided, countedAutonomous, closingRedirect;
            public readonly List<Vector2Int> route=new List<Vector2Int>();
            public readonly List<string> itinerary=new List<string>();
            public string shelfId, transactionId, guidePostId, visitingStaffId;
            public GuestPhase phase;
            public float remaining, visitTime;
        }
        readonly List<Guest> guests=new List<Guest>();
        readonly System.Random customerRandom=new System.Random(731);
        PaperCharacterView cashier;
        float arrivalTimer=ShopCustomerFlow.CheckInterval;
        int visitors, queueTicket, customerLimit=ShopCustomerFlow.DefaultCustomerLimit;
        bool customerFlowStressTest;
        public string TradingStatus {get;private set;}="尚未营业";
        public bool HasCustomer => guests.Count>0;
        public int CustomerCount => guests.Count;
        public int CustomerLimit => customerLimit;
        public int PeakCustomers {get;private set;}
        public int DepartedCustomers {get;private set;}
        public int AutonomousVisits {get;private set;}
        public int DeclinedPurchases {get;private set;}
        public int MultiItemSales {get;private set;}
        public int MultiKindSales {get;private set;}
        public int QueueServicesCompleted {get;private set;}
        public int CustomerStopConflicts {get;private set;}
        public int PaymentsAtFront {get;private set;}
        public Vector2Int? PayingCustomerCell=>guests.Find(g=>g.phase==GuestPhase.Paying)?.cell;
        public bool CanCheckoutServe
        {
            get
            {
                var checkout=Layout.facilities.Find(p=>p.definitionId=="checkout");
                return HasCashier && ShopModel.CanUseCheckoutFront(Layout,catalog,checkout,out _);
            }
        }
        public int ArrivalAttempts {get;private set;}
        public int ArrivalsSpawned {get;private set;}
        public int ProbabilityRejectedArrivals {get;private set;}
        public int CrowdingHeldArrivals {get;private set;}
        public double LastArrivalProbability {get;private set;}
        public float SecondsUntilArrivalCheck => Mathf.Max(0,arrivalTimer);
        public bool CustomerFlowStressTest => customerFlowStressTest;
        public int ShelfQueueCount => guests.FindAll(g=>g.phase==GuestPhase.ShoppingQueue).Count;
        public int CheckoutQueueCount => guests.FindAll(g=>g.phase==GuestPhase.CheckoutQueue).Count;
        bool CashierBusy => guests.Exists(g=>g.phase==GuestPhase.Paying);
        bool guideEnabled=true;
        public bool GuideEnabled => guideEnabled;
        bool ArrivalEntranceCrowded => guests.FindAll(g=>g.phase!=GuestPhase.Leaving &&
            Mathf.Abs(g.cell.x-ShopModel.EntranceFor(Layout).x)+Mathf.Abs(g.cell.y-ShopModel.EntranceFor(Layout).y)<=1).Count>=2;
        public double CurrentArrivalProbability => !Economy.shopOpen?0:customerFlowStressTest
            ? (guests.Count<customerLimit && StopAvailable(null,ShopModel.RoadSpawnFor(Layout))?1:0)
            : Math.Min(1,Economy.PerformanceArrivalMultiplier*ShopCustomerFlow.ArrivalProbability(Economy.popularity,guests.Count,customerLimit,
                ShelfQueueCount,CheckoutQueueCount,ArrivalEntranceCrowded,StopAvailable(null,ShopModel.RoadSpawnFor(Layout))));

        public void ResetCustomerArrival()
        {
            arrivalTimer=customerFlowStressTest?ShopCustomerFlow.StressCheckInterval:ShopCustomerFlow.CheckInterval;
        }

        public bool SetCustomerFlowStressTest(bool enabled)
        {
            var args=Environment.GetCommandLineArgs();
            if(enabled && Array.IndexOf(args,"--verify-crowd")<0 && Array.IndexOf(args,"--verify-mechanics")<0 &&
                Array.IndexOf(args,"--verify-map")<0 && Array.IndexOf(args,"--verify-operations")<0 &&
                Array.IndexOf(args,"--verify-renovation")<0 && Array.IndexOf(args,"--verify-growth")<0 &&
                Array.IndexOf(args,"--verify-performance")<0 && Array.IndexOf(args,"--verify-menu")<0)return false;
            customerFlowStressTest=enabled;ResetCustomerArrival();return true;
        }

        public void SetTestPopularity(int value)
        {
            if(Economy.SetTestPopularity(value,out string reason))dirty=true;
            LastMessage=reason;
        }

        void CheckCustomerArrival()
        {
            ArrivalAttempts++;
            LastArrivalProbability=CurrentArrivalProbability;
            // Advance and save one random state per normal trial, even when congestion holds entry.
            bool accepted=customerFlowStressTest?LastArrivalProbability>0:Economy.RollCustomerArrival(LastArrivalProbability);
            if(!customerFlowStressTest)dirty=true;
            if(LastArrivalProbability<=0){CrowdingHeldArrivals++;return;}
            if(!accepted){ProbabilityRejectedArrivals++;return;}
            if(TrySpawnCustomer())ArrivalsSpawned++;
        }

        void EnsureCheckoutLayout()
        {
            if(ShopModel.ValidateCheckoutAccess(Layout,catalog,out _))return;
            var draft=ShopModel.Clone(Layout);
            try
            {
                ShopSalesTestSetup.EnsureCheckoutAccess(draft,catalog,draft.facilities.Find(f=>f.definitionId=="checkout"));
                Layout=draft;dirty=true;
            }
            catch(InvalidOperationException)
            {
                Economy.shopOpen=false;
                LastMessage="请为收银台后方员工位留出通路，再开店。";
            }
        }

        void InitializeTradingActors()
        {
            cashier=World.CreatePaperActor("Cashier placeholder",new Color(.73f,.82f,1f));
            cashier.transform.localScale=Vector3.one;
            InitializeGuideActor();
        }
        public void SetCustomerLimit(int limit)
        {
            customerLimit=Mathf.Clamp(limit,1,12);
            LastMessage="客流硬上限 "+customerLimit+" 人；来客仍受概率与拥挤程度控制。";
        }
        public void SetGuideEnabled(bool enabled)
        {
            guideEnabled=enabled;
            if(!enabled && !Economy.IsPerforming && extraGuideGuest!=null)ReleaseExtraGuide(false);
            if(!enabled && !Economy.IsPerforming && guideGuest!=null)
            {
                var g=guideGuest;ReleaseGuide(false);
                // Finish the current edge before redirecting from reception.
                if(g.phase==GuestPhase.ToReception || g.phase==GuestPhase.WaitingGuide)
                    g.phase=GuestPhase.WaitingGuide;
            }
            LastMessage=enabled?"导购已上岗，空闲时接待新客。":"导购休息，客人按自己的计划逛店。";
        }
        public void SetShopOpen(bool open)
        {
            if(open && IsEditingLayout)
            {BusinessFailure("请先结束装修，再开店营业。");return;}
            if(open && !ShopModel.ValidateCheckoutAccess(Layout,catalog,out string reason))
            {BusinessFailure(reason);return;}
            if(open && !CanCheckoutServe)
            {BusinessFailure("暂时不能开店："+CheckoutBlockReason);return;}
            if(Economy.shopOpen!=open)PlaySfx(open?"open":"close");
            businessErrorUntil=0;Economy.shopOpen=open;dirty=true;ResetCustomerArrival();
            if(!open)BeginClosingCustomers();
            TradingStatus=open?"营业中 · 每 6 游戏秒独立判定来客":ClosingStatusText;
            LastMessage=TradingStatus;
        }
        void ClearCustomers()
        {
            foreach(var g in guests)
            {
                Economy.ReturnBasket(g.transactionId);
                if(g.view!=null){g.view.gameObject.SetActive(false);Destroy(g.view.gameObject);}
            }
            guests.Clear();ClearRoadTraffic();
            if(cashier!=null)cashier.SetWorking(false);
            ResetGuide();
        }
        void RemoveGuest(Guest g)
        {
            ReleaseGuestGuide(g,false);
            Economy.ReturnBasket(g.transactionId);
            if(g.view!=null){g.view.gameObject.SetActive(false);Destroy(g.view.gameObject);}
            guests.Remove(g);
        }
        bool StopAvailable(Guest owner,Vector2Int cell)
        {
            if(ShopModel.IsBlocked(Layout,catalog,cell))return false;
            foreach(var other in guests)
            {
                if(other==owner)continue;
                if(other.hasDestination && other.destination==cell)return false;
                if(other.route.Count==0 && other.cell==cell)return false;
            }
            return true;
        }
        bool GuestRoute(Guest g,Vector2Int destination)
        {
            // Reserve stopping points only. Transit cells may be shared.
            if(g.route.Count>0 || !StopAvailable(g,destination))return false;
            var route=ShopModel.FindPath(Layout,catalog,g.cell,destination);
            if(route.Count==0)return false;
            if(route[0]==g.cell)route.RemoveAt(0);
            g.route.Clear();g.route.AddRange(route);
            g.destination=destination;g.hasDestination=true;return true;
        }
        bool IsServiceOrDoor(Vector2Int p)
        {
            if(p==ShopModel.EntranceFor(Layout) || p==new Vector2Int(1,Layout.depth-1) || p==ShopModel.WarehousePickup(Layout) ||
                p==ReceptionCell() || p==GuideHome())return true;
            foreach(var f in Layout.facilities)
            {
                var d=catalog.Find(f.definitionId);
                if(ShopModel.IsStaffPoint(d.kind) && p==new Vector2Int(f.x,f.z))return true;
                if(d.kind==FacilityKind.Shelf && ShopModel.ServiceCell(f,d)==p)return true;
                if(d.kind==FacilityKind.Checkout &&
                    (ShopModel.PaymentCell(Layout,catalog,f)==p || ShopModel.CheckoutStaffCell(f,d)==p))return true;
            }
            return false;
        }
        bool WaitingCell(Guest g,Vector2Int service,out Vector2Int result)
        {
            result=g.cell;int best=int.MaxValue;
            for(int z=0;z<Layout.depth;z++)
            for(int x=0;x<Layout.width;x++)
            {
                var p=new Vector2Int(x,z);
                if(IsServiceOrDoor(p) || !StopAvailable(g,p))continue;
                var queuePath=ShopModel.FindPath(Layout,catalog,p,service);
                if(queuePath.Count==0)continue;
                int score=queuePath.Count*20+Mathf.Abs(g.cell.x-x)+Mathf.Abs(g.cell.y-z);
                if(p==g.cell)score-=5;
                if(score>=best || ShopModel.FindPath(Layout,catalog,g.cell,p).Count==0)continue;
                best=score;result=p;
            }
            return best<int.MaxValue;
        }
        void JoinNextShelf(Guest g)
        {
            g.view.SetWorking(false);
            if(!Economy.shopOpen)g.itinerary.Clear();
            if(g.itinerary.Count>0)
            {
                g.shelfId=g.itinerary[0];g.itinerary.RemoveAt(0);
                g.phase=GuestPhase.ShoppingQueue;g.ticket=++queueTicket;g.hasDestination=false;
            }
            else if(g.bought>0)
            {
                var checkout=Layout.facilities.Find(p=>p.definitionId=="checkout");
                if(checkout==null || !Economy.QuoteBasketAtCheckout(catalog,g.transactionId,checkout.instanceId,out _))
                {
                    if(!Economy.shopOpen)
                    {g.phase=GuestPhase.CheckoutQueue;g.ticket=++queueTicket;g.hasDestination=false;TradingStatus=ClosingStatusText;return;}
                    LeaveGuest(g,"没有可用收银台，商品安全退回");return;
                }
                g.phase=GuestPhase.CheckoutQueue;g.ticket=++queueTicket;g.hasDestination=false;dirty=true;
            }
            else LeaveGuest(g,"顾客没有选到商品，沿道路离店");
        }
        void StartAutonomous(Guest g)
        {
            if(!g.countedAutonomous){g.countedAutonomous=true;AutonomousVisits++;}
            g.guided=false;JoinNextShelf(g);
        }
        void LeaveGuest(Guest g,string message,bool paid=false)
        {
            ReleaseGuestGuide(g,false);
            Economy.ReturnBasket(g.transactionId);
            if(!paid){g.bought=0;g.kinds=0;}
            g.phase=GuestPhase.Leaving;g.view.SetWorking(false);g.hasDestination=false;
            TradingStatus=message;
        }
        bool TrySpawnCustomer()=>CreateShopVisitor(ShopModel.RoadSpawnFor(Layout),null);
        void TickQueue(string shelfId,PlacedFacility facility,bool checkout)
        {
            var waiting=guests.FindAll(g=>checkout?g.phase==GuestPhase.CheckoutQueue:
                g.phase==GuestPhase.ShoppingQueue && g.shelfId==shelfId);
            waiting.Sort((a,b)=>a.ticket.CompareTo(b.ticket));
            var service=checkout?ShopModel.PaymentCell(Layout,catalog,facility):
                ShopModel.ServiceCell(facility,catalog.Find(facility.definitionId));
            bool serviceAvailable=!checkout || CanCheckoutServe;
            bool busy=!serviceAvailable || guests.Exists(g=>checkout?g.phase==GuestPhase.Paying:
                g.phase==GuestPhase.Browsing && g.shelfId==shelfId);
            for(int i=0;i<waiting.Count;i++)
            {
                var g=waiting[i];
                if(g.route.Count>0)continue;
                bool first=i==0 && !busy;
                if(first && g.cell==service && StopAvailable(g,service))
                {
                    if(checkout){g.phase=GuestPhase.Paying;g.remaining=Economy.CheckoutSeconds;g.view.SetWorking(true);}
                    else if(!g.guided || GuideReadyFor(g))
                    {
                        g.phase=GuestPhase.Browsing;g.remaining=2.0f+(float)customerRandom.NextDouble();
                        g.view.SetWorking(true);if(g.guided)StartGuestRecommendation(g);
                    }
                    continue;
                }
                if(first && GuestRoute(g,service))continue;
                if(g.hasDestination && !IsServiceOrDoor(g.destination))continue;
                if(WaitingCell(g,serviceAvailable?service:ShopModel.EntranceFor(Layout),out var wait))GuestRoute(g,wait);
            }
        }
        void TickCustomers(float dt)
        {
            var checkout=Layout.facilities.Find(p=>p.definitionId=="checkout");
            if(cashier!=null)
            {
                cashier.gameObject.SetActive(checkout!=null && HasCashier);
                if(checkout!=null)cashier.transform.position=World.CellCenter(CashierStandCell(checkout));
                cashier.SetWorking(CashierBusy);cashier.Tick(dt);
            }
            TickGuide(dt);TickExtraGuide(dt);
            if(dt<=0)return;
            TickRoadTraffic(dt);
            if(Economy.shopOpen&&customerFlowStressTest)
            {
                arrivalTimer-=dt;
                if(arrivalTimer<=0){ResetCustomerArrival();CheckCustomerArrival();}
            }
            else if(!Economy.shopOpen&&guests.Count==0){TradingStatus=ClosingStatusText;return;}
            foreach(var shelf in Economy.shelves)
            {
                var f=Layout.facilities.Find(p=>p.instanceId==shelf.instanceId);
                if(f!=null)TickQueue(shelf.instanceId,f,false);
            }
            if(checkout!=null)TickQueue(null,checkout,true);
            foreach(var g in new List<Guest>(guests))
            {
                g.visitTime+=dt;
                g.view.SetMoving(g.route.Count>0);UpdateGuestPose(g);g.view.Tick(dt);
                if(g.route.Count>0)
                {
                    var next=g.route[0];
                    if(ShopModel.IsBlocked(Layout,catalog,next))
                    {
                        g.route.Clear();g.hasDestination=false;
                        if(!Economy.shopOpen){g.closingRedirect=true;RedirectClosingGuest(g);}
                        else LeaveGuest(g,"布局变化，未付款商品安全退回");
                        continue;
                    }
                    if(g.route.Count==1 && guests.Exists(other=>other!=g && other.route.Count==0 && other.cell==next))continue;
                    g.view.transform.position=Vector3.MoveTowards(g.view.transform.position,World.CellCenter(next),dt*1.8f);
                    if(Vector3.SqrMagnitude(g.view.transform.position-World.CellCenter(next))<.0001f)
                    {g.cell=next;g.route.RemoveAt(0);
                     if(g.phase==GuestPhase.Leaving&&!ShopModel.IsShopCell(Layout,g.cell)){ExitThroughDoor(g);continue;}}
                    continue;
                }
                if(g.closingRedirect){RedirectClosingGuest(g);continue;}
                switch(g.phase)
                {
                    case GuestPhase.Entering:
                        if(ShopModel.IsShopCell(Layout,g.cell))BeginStoreVisit(g);
                        else RouteEnteringGuest(g);break;
                    case GuestPhase.Wandering:TickWindowShopper(g,dt);break;
                    case GuestPhase.ToReception:
                        g.phase=GuestPhase.WaitingGuide;break;
                    case GuestPhase.WaitingGuide:
                        if(!GuestGuideAvailable(g))StartAutonomous(g);
                        break;
                    case GuestPhase.ShoppingQueue:
                        if(!Layout.facilities.Exists(f=>f.instanceId==g.shelfId))
                        {ReleaseGuestGuide(g,false);g.guided=false;JoinNextShelf(g);}
                        break;
                    case GuestPhase.Browsing:
                        g.remaining-=dt;if(g.remaining>0)break;
                        var stock=Economy.Shelf(g.shelfId);
                        bool buy=g.kinds<g.plannedKinds&&customerRandom.NextDouble()<ShopBalance.PurchaseChance(Economy.popularity,g.guided,Economy.PerformancePurchaseBonus,g.kinds);
                        int wanted=customerRandom.NextDouble()<.30?2:1;
                        if(customerRandom.NextDouble()<ShopBalance.ExtraUnitChance(g.guideQuantityBonusSnapshot))wanted++;
                        int quantity=stock==null?0:Math.Min(wanted,stock.quantity);
                        if(buy && quantity>0 && Economy.AddToBasket(catalog,g.shelfId,g.transactionId,quantity,out _))
                        {g.bought+=quantity;g.kinds++;dirty=true;}
                        else DeclinedPurchases++;
                        QueueServicesCompleted++;
                        ReleaseGuestGuide(g,true);
                        g.guided=false;JoinNextShelf(g);break;
                    case GuestPhase.CheckoutQueue:
                        if(checkout==null && Economy.shopOpen)LeaveGuest(g,"没有可用收银台，商品退仓");
                        break;
                    case GuestPhase.Paying:
                        if(!CanCheckoutServe || g.cell!=ShopModel.PaymentCell(Layout,catalog,checkout))
                        {g.phase=GuestPhase.CheckoutQueue;g.hasDestination=false;g.view.SetWorking(false);break;}
                        g.remaining-=dt;if(g.remaining>0)break;
                        Economy.RefreshDecoration(Layout);
                        bool paid=Economy.PayBasket(g.transactionId,out string payment);
                        if(paid){if(Economy.lastDecorationBonus>0){lastDecorationPayout=Economy.lastDecorationBonus;lastDecorationPayoutUntil=Time.unscaledTime+2;}PlaySfx("income");PaymentsAtFront++;if(g.bought>1)MultiItemSales++;if(g.kinds>1)MultiKindSales++;}
                        dirty=true;LastMessage=payment;
                        if(!paid && !Economy.shopOpen)
                        {g.phase=GuestPhase.CheckoutQueue;g.hasDestination=false;g.view.SetWorking(false);TradingStatus=payment;}
                        else LeaveGuest(g,paid?"交易完成，顾客离店":payment,paid);
                        break;
                    case GuestPhase.Leaving:
                        if(!ShopModel.IsShopCell(Layout,g.cell)){ExitThroughDoor(g);}
                        else if(!GuestRoute(g,ShopModel.EntranceFor(Layout)+Vector2Int.up) && IsServiceOrDoor(g.cell) &&
                            WaitingCell(g,g.cell,out var departureWait))GuestRoute(g,departureWait);
                        break;
                }
                if(Economy.shopOpen && g.visitTime>180f && g.phase!=GuestPhase.Leaving)
                    LeaveGuest(g,"等候过久，商品安全退回");
            }
            for(int i=0;i<guests.Count;i++)
            for(int j=i+1;j<guests.Count;j++)
                if(guests[i].route.Count==0 && guests[j].route.Count==0 && guests[i].cell==guests[j].cell)
                {
                    CustomerStopConflicts++;
                    if(isMenuVerification&&CustomerStopConflicts<6)Debug.Log("STOP_CONFLICT "+guests[i].number+" "+guests[i].phase+" / "+guests[j].number+" "+guests[j].phase+" at "+guests[i].cell);
                }
            if(!Economy.shopOpen)
            {
                TradingStatus=ClosingStatusText;
                if(guests.Count==0 && closingWasPending){closingWasPending=false;LastMessage="客人已全部结账离店，可以装修。";}
                return;
            }
            TradingStatus="店内与道路 "+guests.Count+" / "+customerLimit+" 人 · 挑选等候 "+ShelfQueueCount+" · 付款等候 "+CheckoutQueueCount;
            if(!ShopModel.CanUseCheckoutFront(Layout,catalog,checkout,out string checkoutReason))
                TradingStatus+="\n收银暂停："+checkoutReason;
        }
        void DrawTrading(Rect box)
        {
            GUI.Label(new Rect(box.x+22,box.y+58,570,29),BusinessStateText,heading);
            GUI.Label(new Rect(box.x+22,box.y+110,570,60),"累计成交 "+Economy.transactions+" 单\n收入 "+Economy.revenue+" 金币",body);
            if(ArtButton(new Rect(box.x+22,box.y+210,570,38),Economy.shopOpen?"停止营业":IsClosing?"恢复营业":"开始营业",activeButton))SetShopOpen(!Economy.shopOpen);
            if(ArtButton(new Rect(box.x+22,box.y+270,570,38),"舞台与 FEVER",button))OpenPerformance();
        }
    }
}
