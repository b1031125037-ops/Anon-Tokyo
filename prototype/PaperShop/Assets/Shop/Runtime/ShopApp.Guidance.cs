using UnityEngine;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        enum GuidePhase { Idle, Approaching, Greeting, Leading, Recommending, Returning }
        PaperCharacterView guide;
        Guest guideGuest;
        Vector2Int guideCell;
        readonly List<Vector2Int> guideRoute=new List<Vector2Int>();
        GuidePhase guidePhase;
        float guideTimer;
        GUIStyle roleLabel;
        readonly List<Rect> roleRects=new List<Rect>();
        public int GuideGreetings {get;private set;}
        public int GuidedVisits {get;private set;}
        public int GuideRecommendations {get;private set;}
        public int RestockJobsCompleted {get;private set;}
        public bool HasAllStaff => guide!=null && cashier!=null && World.Character!=null;
        public bool GuideReadyToRecommend => guidePhase==GuidePhase.Leading && guideRoute.Count==0;
        bool GuideReadyFor(Guest g)=>(guideGuest==g && GuideReadyToRecommend) || (extraGuideGuest==g && extraGuidePhase==GuidePhase.Leading && extraGuideRoute.Count==0);
        public string GuideStatus => guidePhase==GuidePhase.Idle?"待岗":guidePhase==GuidePhase.Approaching?"迎客":guidePhase==GuidePhase.Greeting?"接待":guidePhase==GuidePhase.Leading?"带客":guidePhase==GuidePhase.Recommending?"推荐":"返回接待点";

        Vector2Int Neighbour(Vector2Int origin,Vector2Int avoid)
        {
            foreach(var d in new[]{Vector2Int.right,Vector2Int.left,Vector2Int.down,Vector2Int.up})
            {var p=origin+d;if(p!=avoid && ShopModel.IsShopCell(Layout,p) && !ShopModel.IsBlocked(Layout,catalog,p) && ShopModel.FindPath(Layout,catalog,ShopModel.EntranceFor(Layout),p).Count>0)return p;}
            return origin;
        }
        Vector2Int ReceptionCell()
        {
            var point=Layout.facilities.Find(p=>catalog.Find(p.definitionId).kind==FacilityKind.Reception);
            if(point!=null)return Neighbour(new Vector2Int(point.x,point.z),GuideHome());
            var preferred=new Vector2Int(1,6);
            return !ShopModel.IsBlocked(Layout,catalog,preferred) && ShopModel.FindPath(Layout,catalog,ShopModel.EntranceFor(Layout),preferred).Count>0?preferred:ShopModel.EntranceFor(Layout);
        }
        Vector2Int GuideHome()
        {
            var point=Layout.facilities.Find(p=>catalog.Find(p.definitionId).kind==FacilityKind.Reception);
            if(point!=null)return new Vector2Int(point.x,point.z);
            return Neighbour(new Vector2Int(1,6),ShopModel.SpawnCell);
        }
        Vector2Int CashierStandCell(PlacedFacility checkout)=>ShopModel.CheckoutStaffCell(checkout,catalog.Find("checkout"));
        void InitializeGuideActor()
        {
            guide=World.CreatePaperActor("Guide placeholder",new Color(.72f,1f,.78f));
            guide.transform.localScale=Vector3.one;ResetGuide();
        }
        void ResetGuide()
        {
            ResetExtraGuide();
            if(guide==null)return;
            guide.gameObject.SetActive(HasGuideStaff);
            if(guideGuest!=null)guideGuest.guided=false;
            guideGuest=null;
            guideRoute.Clear();guidePhase=GuidePhase.Idle;guide.SetWorking(false);guide.SetMoving(false);
            guideCell=GuideHome();guide.transform.position=World.CellCenter(guideCell);
        }
        bool GuideGo(Vector2Int destination)
        {
            bool midStep=guideRoute.Count>0;
            var start=midStep?guideRoute[0]:guideCell;
            var next=ShopModel.FindPath(Layout,catalog,start,destination);
            if(guideGuest!=null && guideGuest.cell!=guideCell && guideGuest.cell!=destination && ShopModel.IsShopCell(Layout,guideGuest.cell) && !ShopModel.IsBlocked(Layout,catalog,guideGuest.cell))
            {
                var draft=ShopModel.Clone(Layout);
                draft.facilities.Add(new PlacedFacility{instanceId="guide-visitor-reservation",definitionId="decor",x=guideGuest.cell.x,z=guideGuest.cell.y});
                var separated=ShopModel.FindPath(draft,catalog,start,destination);
                if(separated.Count>0)next=separated;
            }
            if(next.Count==0)return false;
            guideRoute.Clear();guideRoute.AddRange(next);if(!midStep && guideRoute.Count>0 && guideRoute[0]==guideCell)guideRoute.RemoveAt(0);
            return true;
        }
        void BeginRecommendation()
        {guidePhase=GuidePhase.Recommending;guide.SetWorking(true);GuideRecommendations++;}
        void ReleaseGuide(bool completed)
        {
            if(guide==null)return;
            if(completed && guidePhase==GuidePhase.Recommending)GuidedVisits++;
            if(guideGuest!=null)guideGuest.guided=false;
            guideGuest=null;
            guide.SetWorking(false);guidePhase=GuidePhase.Returning;
            if(!GuideGo(GuideHome()))ResetGuide();
        }
        void TickGuide(float dt)
        {
            if(guide==null)return;
            guide.Tick(dt);
            if(dt<=0)return;
            if(!HasGuideStaff){if(guideGuest!=null)ReleaseGuide(false);return;}
            if(ShopModel.IsBlocked(Layout,catalog,guideCell))ResetGuide();
            if(guidePhase==GuidePhase.Idle && guideGuest==null && guideRoute.Count==0 && guideCell!=GuideHome())
            {if(GuideGo(GuideHome()))guidePhase=GuidePhase.Returning;}
            if(Economy.shopOpen && GuideAvailable && guideGuest!=null && guideGuest.phase==GuestPhase.WaitingGuide && guidePhase==GuidePhase.Idle)
            {
                guidePhase=GuidePhase.Approaching;
                var meeting=Mathf.Abs(guideCell.x-guideGuest.cell.x)+Mathf.Abs(guideCell.y-guideGuest.cell.y)==1?guideCell:Neighbour(guideGuest.cell,guideGuest.cell);
                if(!GuideGo(meeting)){var waiting=guideGuest;ReleaseGuide(false);StartAutonomous(waiting);return;}
            }
            guide.SetMoving(guideRoute.Count>0);
            if(guideRoute.Count>0)
            {
                var next=guideRoute[0];
                if(ShopModel.IsBlocked(Layout,catalog,next)){ResetGuide();return;}
                guide.transform.position=Vector3.MoveTowards(guide.transform.position,World.CellCenter(next),dt*2.45f);
                if(Vector3.SqrMagnitude(guide.transform.position-World.CellCenter(next))<.0001f){guideCell=next;guideRoute.RemoveAt(0);}
                return;
            }
            if(guidePhase==GuidePhase.Approaching)
            {guidePhase=GuidePhase.Greeting;guideTimer=.9f;guide.SetWorking(true);TradingStatus="导购接待客人并介绍柜台";}
            else if(guidePhase==GuidePhase.Greeting)
            {
                guideTimer-=dt*Economy.WorkEfficiency(FacilityKind.Reception);if(guideTimer>0)return;
                if(guideGuest==null){ResetGuide();return;}
                var customer=guideGuest;
                JoinNextShelf(customer);
                var shelf=Layout.facilities.Find(p=>p.instanceId==customer.shelfId);
                if(shelf==null){LeaveGuest(customer,"目标柜台已变更");return;}
                var target=ShopModel.ServiceCell(shelf,catalog.Find(shelf.definitionId));
                if(!GuideGo(Neighbour(target,target))){ReleaseGuide(false);return;}
                GuideGreetings++;guide.SetWorking(false);guidePhase=GuidePhase.Leading;
                TradingStatus="导购带客前往 "+catalog.Find(shelf.definitionId).title;
            }
            else if(guidePhase==GuidePhase.Returning)guidePhase=GuidePhase.Idle;
        }

        public void StartTwoCounterTest()
        {
            if(!verifyMode){LastMessage="请通过商店购买柜台，在关店后的装修中调整布局。";return;}
            if(!CanRenovate){LastMessage="请先关店，等待所有客人结账离开后再重跑店铺。";return;}
            CancelRestockTask();ClearCustomers();CancelPlacement();StopAtCurrentCell();
            var prepared=ShopSalesTestSetup.Prepare(Layout,Economy,catalog);
            Layout=prepared.layout;Economy=prepared.economy;restockPhase=RestockPhase.None;
            ActorCell=RestockHomeCell;
            World.Rebuild(Layout,catalog);World.SetWarehouseLevel(Economy.warehouseLevel);World.SetCarrying(false);
            World.CharacterRoot.position=World.CellCenter(ActorCell);ResetGuide();Select(null);autoDelay=0;ResetCustomerArrival();dirty=true;
            InitializeBusinessState();
            TradingStatus=Economy.shopOpen?"精简店铺已开始多人营业，补货员准备上货":BusinessStateText;LastMessage=TradingStatus;
        }

        void DrawStaffRoles()
        {
            roleRects.Clear();expandedRoleCount=0;
            if(roleLabel==null){roleLabel=new GUIStyle(chip){fontSize=13,padding=new RectOffset(9,9,2,2)};roleLabel.normal.textColor=ink;roleLabel.normal.background=null;roleLabel.border=new RectOffset();}
            DrawStockAlerts();
            if(hideWorldText)return;
            // FEVER HUD presents stage activity; do not cover performers with repeated bubbles.
            if(extraRestocker!=null&&extraRestocker.gameObject.activeSelf)DrawCompactRole(extraRestocker.transform,ExtraRestockStatus,extraRestockPhase!=RestockPhase.None?"restock":"idle",extraRestockPhase==RestockPhase.None&&extraRestockRoute.Count==0);
            if(extraGuide!=null&&extraGuide.gameObject.activeSelf)DrawCompactRole(extraGuide.transform,"欢迎光临","welcome");
            if(HasRestockStaff)DrawCompactRole(World.CharacterRoot,IsRestocking?"补货中":"待岗",IsRestocking?"restock":"idle",!IsRestocking);
            if(cashier!=null&&HasCashier)DrawCompactRole(cashier.transform,CashierBusy?"收银中":"待岗",CashierBusy?"checkout":"idle",!CashierBusy);
            if(guide!=null&&HasGuideStaff)DrawCompactRole(guide.transform,guideEnabled?"欢迎光临":"休息",guideEnabled?"welcome":"idle",guideGuest==null);
            foreach(var g in guests)
            {
                string state=g.phase==GuestPhase.WaitingGuide?"等候接待":g.phase==GuestPhase.Browsing?"看看衣服":g.phase==GuestPhase.ShoppingQueue?"等候选购":g.phase==GuestPhase.CheckoutQueue?"等候付款":g.phase==GuestPhase.Paying?"付款":g.phase==GuestPhase.Leaving?"离店":g.phase==GuestPhase.Wandering?"闲逛":"进店";
                string icon=g.phase==GuestPhase.Browsing?"browse":g.phase==GuestPhase.Paying?"checkout":g.phase==GuestPhase.Leaving?"leave":g.phase==GuestPhase.WaitingGuide||g.phase==GuestPhase.CheckoutQueue||g.phase==GuestPhase.ShoppingQueue?"queue":"welcome";
                string identity=string.IsNullOrEmpty(g.visitingStaffId)?"":ShopStaffProfiles.Find(g.visitingStaffId).title+" · ";
                DrawCompactRole(g.view.transform,identity+state+" · "+g.bought+"件",icon,false,true);
            }
            if(roleStates.Count>128){roleStates.Clear();roleChanged.Clear();}
            foreach(var p in passers)if(p.items>0)
                DrawCompactRole(p.view.transform,(string.IsNullOrEmpty(p.visitingStaffId)?"":ShopStaffProfiles.Find(p.visitingStaffId).title+" · ")+"已购 "+p.items+"件","leave",false,true);
        }

        void DrawWorkingStaff(Rect box)
        {
            DrawStaffTabs(box);
            GUI.Label(new Rect(box.x+22,box.y+101,570,38),"已解锁 "+Economy.UnlockedStaffCount+" / 10 人 · 当前 "+Economy.staffPosts.Count+" 个岗位",body);
            int i=0;
            foreach(var post in Economy.PerformanceWorkPosts())
            {
                float y=box.y+150+i*60;
                GUI.Label(new Rect(box.x+22,y,430,48),catalog.Find(Layout.facilities.Find(f=>f.instanceId==post.instanceId).definitionId).title+" · Lv."+post.level+"\n"+StaffName(post),body);
                if(ArtButton(new Rect(box.x+466,y+3,126,38),"配置岗位",button))OpenStaffPost(post.instanceId);
                i++;
            }
            GUI.Label(new Rect(box.x+22,box.y+462,570,45),"Lv.4 解锁第二补货位；Lv.5 解锁第二招待位。",small);
        }
    }
}
