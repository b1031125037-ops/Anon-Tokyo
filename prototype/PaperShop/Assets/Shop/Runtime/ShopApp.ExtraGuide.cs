using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        PaperCharacterView extraGuide;
        Guest extraGuideGuest;
        Vector2Int extraGuideCell;
        readonly List<Vector2Int> extraGuideRoute=new List<Vector2Int>();
        GuidePhase extraGuidePhase;
        float extraGuideTimer;
        public int ExtraGuideGreetings {get;private set;}
        public int ExtraGuideRecommendations {get;private set;}
        Vector2Int ExtraGuideHome()
        {var p=SecondPost(FacilityKind.Reception);var f=p==null?null:Layout.facilities.Find(x=>x.instanceId==p.instanceId);
            return f==null?GuideHome():new Vector2Int(f.x,f.z);}
        bool ExtraGuideAvailable=>(guideEnabled || Economy.IsPerforming) && SecondPost(FacilityKind.Reception)!=null &&
            !string.IsNullOrEmpty(Economy.EffectiveStaffId(SecondPost(FacilityKind.Reception)));
        bool TryAssignGuestGuide(Guest g)
        {
            if(GuideAvailable&&guideGuest==null&&guidePhase==GuidePhase.Idle&&GuestRoute(g,ReceptionCell()))
            {guideGuest=g;g.guidePostId=Economy.PostFor(FacilityKind.Reception).instanceId;}
            else if(ExtraGuideAvailable&&extraGuide!=null&&extraGuideGuest==null&&extraGuidePhase==GuidePhase.Idle&&
                GuestRoute(g,Neighbour(ExtraGuideHome(),ExtraGuideHome())))
            {extraGuideGuest=g;g.guidePostId=SecondPost(FacilityKind.Reception).instanceId;}
            else return false;
            g.guided=true;g.guideQuantityBonusSnapshot=Economy.GuideBonusFor(Economy.Post(g.guidePostId));PrepareShoppingPlan(g);
            g.phase=GuestPhase.ToReception;return true;
        }
        bool GuestGuideAvailable(Guest g)=>(guideGuest==g&&GuideAvailable)||(extraGuideGuest==g&&ExtraGuideAvailable);
        void ReleaseGuestGuide(Guest g,bool completed)
        {if(guideGuest==g)ReleaseGuide(completed);if(extraGuideGuest==g)ReleaseExtraGuide(completed);}
        void StartGuestRecommendation(Guest g)
        {
            if(extraGuideGuest==g){extraGuidePhase=GuidePhase.Recommending;extraGuide.SetWorking(true);ExtraGuideRecommendations++;GuideRecommendations++;}
            else BeginRecommendation();
        }
        void ResetExtraGuide()
        {
            if(extraGuideGuest!=null)extraGuideGuest.guided=false;
            extraGuideGuest=null;extraGuideRoute.Clear();extraGuidePhase=GuidePhase.Idle;
            if(extraGuide==null)return;
            extraGuide.SetMoving(false);extraGuide.SetWorking(false);extraGuideCell=ExtraGuideHome();
            extraGuide.transform.position=World.CellCenter(extraGuideCell);
        }
        bool ExtraGuideGo(Vector2Int target)
        {
            bool moving=extraGuideRoute.Count>0;
            var start=moving?extraGuideRoute[0]:extraGuideCell;
            var route=ShopModel.FindPath(Layout,catalog,start,target);
            if(route.Count==0)return false;
            extraGuideRoute.Clear();extraGuideRoute.AddRange(route);
            if(!moving)extraGuideRoute.RemoveAt(0);return true;
        }
        void ReleaseExtraGuide(bool completed)
        {
            if(completed&&extraGuidePhase==GuidePhase.Recommending)GuidedVisits++;
            if(extraGuideGuest!=null)extraGuideGuest.guided=false;
            extraGuideGuest=null;
            if(extraGuide==null)return;
            extraGuide.SetWorking(false);extraGuidePhase=GuidePhase.Returning;
            if(!ExtraGuideGo(ExtraGuideHome()))ResetExtraGuide();
        }
        void TickExtraGuide(float dt)
        {
            var post=SecondPost(FacilityKind.Reception);
            if(post==null)
            {ResetExtraGuide();if(extraGuide!=null)Destroy(extraGuide.gameObject);extraGuide=null;return;}
            if(extraGuide==null){extraGuide=World.CreatePaperActor("Second guide",new Color(.77f,1f,.9f));ResetExtraGuide();}
            bool staffed=!string.IsNullOrEmpty(Economy.EffectiveStaffId(post));
            extraGuide.gameObject.SetActive(staffed);extraGuide.Tick(dt);
            if(dt<=0)return;
            if(!staffed||(!guideEnabled&&!Economy.IsPerforming)){if(extraGuideGuest!=null)ReleaseExtraGuide(false);return;}
            if(ShopModel.IsBlocked(Layout,catalog,extraGuideCell)){ResetExtraGuide();return;}
            if(extraGuidePhase==GuidePhase.Idle&&extraGuideGuest==null&&extraGuideCell!=ExtraGuideHome())
            {if(ExtraGuideGo(ExtraGuideHome()))extraGuidePhase=GuidePhase.Returning;}
            if(Economy.shopOpen&&extraGuideGuest!=null&&extraGuideGuest.phase==GuestPhase.WaitingGuide&&extraGuidePhase==GuidePhase.Idle)
            {
                extraGuidePhase=GuidePhase.Approaching;
                if(!ExtraGuideGo(Neighbour(extraGuideGuest.cell,extraGuideGuest.cell)))
                {var g=extraGuideGuest;ReleaseExtraGuide(false);StartAutonomous(g);return;}
            }
            extraGuide.SetMoving(extraGuideRoute.Count>0);
            if(extraGuideRoute.Count>0)
            {
                var next=extraGuideRoute[0];
                if(ShopModel.IsBlocked(Layout,catalog,next)){ResetExtraGuide();return;}
                extraGuide.transform.position=Vector3.MoveTowards(extraGuide.transform.position,World.CellCenter(next),dt*2.45f);
                if((extraGuide.transform.position-World.CellCenter(next)).sqrMagnitude<.0001f)
                {extraGuideCell=next;extraGuideRoute.RemoveAt(0);}
                return;
            }
            if(extraGuidePhase==GuidePhase.Approaching)
            {extraGuidePhase=GuidePhase.Greeting;extraGuideTimer=.9f;extraGuide.SetWorking(true);}
            else if(extraGuidePhase==GuidePhase.Greeting)
            {
                extraGuideTimer-=dt*Economy.WorkEfficiency(post);if(extraGuideTimer>0)return;
                if(extraGuideGuest==null){ResetExtraGuide();return;}
                var g=extraGuideGuest;JoinNextShelf(g);
                var shelf=Layout.facilities.Find(f=>f.instanceId==g.shelfId);
                if(shelf==null){LeaveGuest(g,"目标柜台已变更");return;}
                var target=ShopModel.ServiceCell(shelf,catalog.Find(shelf.definitionId));
                if(!ExtraGuideGo(Neighbour(target,target))){ReleaseExtraGuide(false);return;}
                ExtraGuideGreetings++;GuideGreetings++;extraGuide.SetWorking(false);extraGuidePhase=GuidePhase.Leading;
            }
            else if(extraGuidePhase==GuidePhase.Returning)extraGuidePhase=GuidePhase.Idle;
        }
    }
}
