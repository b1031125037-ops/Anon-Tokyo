using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        PaperCharacterView extraRestocker;
        string extraRestockId="";
        Vector2Int extraRestockCell;
        readonly List<Vector2Int> extraRestockRoute=new List<Vector2Int>();
        RestockPhase extraRestockPhase;
        float extraRestockTimer, extraRestockDelay;
        public int ExtraWarehousePickups {get;private set;}
        public int ExtraRestocksCompleted {get;private set;}
        public string ExtraRestockStatus=>extraRestocker==null||!extraRestocker.gameObject.activeSelf?"未上岗":
            extraRestockPhase==RestockPhase.ToWarehouse?"前往仓库":
            extraRestockPhase==RestockPhase.Picking?"取货中":
            extraRestockPhase==RestockPhase.ToShelf?"运送货物":
            extraRestockPhase==RestockPhase.Stocking?"补货中":extraRestockRoute.Count>0?"返回岗位":"待岗";
        StaffPostState SecondPost(FacilityKind role)
        {var list=Economy.staffPosts.FindAll(p=>p.role==role);return list.Count>1?list[1]:null;}
        bool QueueAdditionalRestock()
        {
            var post=SecondPost(FacilityKind.Restock);
            if(post==null||string.IsNullOrEmpty(Economy.EffectiveStaffId(post)))return false;
            string previous=Economy.activeRestockPostId;
            Economy.activeRestockPostId=post.instanceId;
            try
            {
                post.restockFollowUp=true;extraRestockDelay=0;
                return Economy.job!=null||Economy.NextRestock(catalog,Layout)!=null;
            }
            finally {Economy.activeRestockPostId=previous;}
        }
        bool ExtraRestockGo(Vector2Int target)
        {
            var next=ShopModel.FindPath(Layout,catalog,extraRestockCell,target);
            extraRestockRoute.Clear();if(next.Count==0)return false;
            extraRestockRoute.AddRange(next);extraRestockRoute.RemoveAt(0);return true;
        }
        void TickExtraRestocker(float dt)
        {
            var post=SecondPost(FacilityKind.Restock);
            var facility=post==null?null:Layout.facilities.Find(f=>f.instanceId==post.instanceId);
            if(facility==null)
            {
                if(extraRestocker!=null)Destroy(extraRestocker.gameObject);
                extraRestocker=null;extraRestockId="";extraRestockRoute.Clear();return;
            }
            if(extraRestocker==null||extraRestockId!=post.instanceId)
            {
                if(extraRestocker!=null)Destroy(extraRestocker.gameObject);
                extraRestocker=World.CreatePaperActor("Second restocker",new Color(.82f,.76f,1f));
                extraRestockId=post.instanceId;extraRestockCell=new Vector2Int(facility.x,facility.z);
                extraRestocker.transform.position=World.CellCenter(extraRestockCell);
                extraRestockRoute.Clear();extraRestockPhase=RestockPhase.None;
            }
            bool staffed=!string.IsNullOrEmpty(Economy.EffectiveStaffId(post));
            extraRestocker.gameObject.SetActive(staffed);
            if(!staffed)
            {
                string old=Economy.activeRestockPostId;Economy.activeRestockPostId=post.instanceId;Economy.CancelCurrentRestock();Economy.activeRestockPostId=old;
                extraRestockRoute.Clear();extraRestockPhase=RestockPhase.None;extraRestocker.SetWorking(false);extraRestocker.SetMoving(false);return;
            }
            if(dt<=0)return;
            string previous=Economy.activeRestockPostId;
            Economy.activeRestockPostId=post.instanceId;
            try
            {
                if(Economy.job==null && extraRestockPhase!=RestockPhase.None)
                {extraRestockRoute.Clear();extraRestockPhase=RestockPhase.None;extraRestocker.SetWorking(false);}
                if(ShopModel.IsBlocked(Layout,catalog,extraRestockCell) ||
                    (extraRestockRoute.Count>0&&ShopModel.IsBlocked(Layout,catalog,extraRestockRoute[0])))
                {Economy.CancelCurrentRestock();extraRestockRoute.Clear();extraRestockPhase=RestockPhase.None;
                    extraRestockCell=new Vector2Int(facility.x,facility.z);extraRestocker.transform.position=World.CellCenter(extraRestockCell);}
                extraRestockDelay=Mathf.Max(0,extraRestockDelay-dt);
                if(extraRestockRoute.Count>0)
                {
                    var target=World.CellCenter(extraRestockRoute[0]);
                    extraRestocker.transform.position=Vector3.MoveTowards(extraRestocker.transform.position,target,dt*2.35f*Economy.WorkEfficiency(post));
                    if((extraRestocker.transform.position-target).sqrMagnitude<.0001f)
                    {extraRestockCell=extraRestockRoute[0];extraRestockRoute.RemoveAt(0);}
                }
                else if(extraRestockPhase==RestockPhase.ToWarehouse)
                {extraRestockPhase=RestockPhase.Picking;extraRestockTimer=1.2f;extraRestocker.SetWorking(true);}
                else if(extraRestockPhase==RestockPhase.Picking)
                {
                    extraRestockTimer-=dt*Economy.WorkEfficiency(post);
                    if(extraRestockTimer<=0)
                    {
                        string preferred=Economy.job?.shelfId;
                        Economy.CancelCurrentRestock();
                        if(Economy.BeginRestockBatch(catalog,Layout,ShopEconomy.CarryLimitFor(post,Economy.EffectiveStaffId(post)),preferred,out _)&&Economy.Pickup(out _))
                        {ExtraWarehousePickups++;WarehousePickups++;PlaySfx("pickup");extraRestockPhase=RestockPhase.ToShelf;RouteExtraShelf();}
                        else extraRestockPhase=RestockPhase.None;
                        extraRestocker.SetWorking(false);dirty=true;
                    }
                }
                else if(extraRestockPhase==RestockPhase.ToShelf)
                {extraRestockPhase=RestockPhase.Stocking;extraRestockTimer=1.4f;extraRestocker.SetWorking(true);}
                else if(extraRestockPhase==RestockPhase.Stocking)
                {
                    extraRestockTimer-=dt*Economy.WorkEfficiency(post);
                    if(extraRestockTimer<=0)
                    {
                        if(Economy.CompleteRestock(catalog,Layout,out _)){ExtraRestocksCompleted++;RestockJobsCompleted++;PlaySfx("stock");}
                        else Economy.CancelCurrentRestock();
                        extraRestocker.SetWorking(false);dirty=true;
                        if(Economy.job!=null){extraRestockPhase=RestockPhase.ToShelf;RouteExtraShelf();}
                        else
                        {
                            extraRestockPhase=RestockPhase.None;extraRestockDelay=.6f;
                            ExtraRestockGo(new Vector2Int(facility.x,facility.z));
                        }
                    }
                }
                else if(string.IsNullOrEmpty(placingDefinition)&&extraRestockDelay<=0)
                {
                    string shelf=Economy.NextRestock(catalog,Layout);
                    if(shelf!=null&&Economy.BeginRestockBatch(catalog,Layout,ShopEconomy.CarryLimitFor(post,Economy.EffectiveStaffId(post)),shelf,out _))
                    {
                        if(ExtraRestockGo(ShopModel.WarehousePickup(Layout)))extraRestockPhase=RestockPhase.ToWarehouse;
                        else Economy.CancelCurrentRestock();
                        dirty=true;
                    }
                    else {var home=new Vector2Int(facility.x,facility.z);if(home!=extraRestockCell)ExtraRestockGo(home);extraRestockDelay=1f;}
                }
                extraRestocker.SetMoving(extraRestockRoute.Count>0);extraRestocker.Tick(dt);
            }
            finally {Economy.activeRestockPostId=previous;}
        }
        void RouteExtraShelf()
        {
            var f=Layout.facilities.Find(p=>p.instanceId==Economy.job?.shelfId);
            if(f==null||!ExtraRestockGo(ShopModel.ServiceCell(f,catalog.Find(f.definitionId))))
            {Economy.CancelCurrentRestock();extraRestockPhase=RestockPhase.None;}
        }
    }
}
