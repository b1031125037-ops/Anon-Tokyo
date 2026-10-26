using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    [Serializable]
    public sealed class StaffPostState
    {
        public string instanceId, staffId;
        public FacilityKind role;
        public int level=1;
        // Zero follows the current upgraded maximum; otherwise a player-set limit.
        public int carryLimit;
        public bool restockFollowUp;
    }

    public sealed partial class ShopEconomy
    {
        public int staffSystemVersion;
        public bool staffPointsIntroduced;
        public List<StaffPostState> staffPosts=new List<StaffPostState>();
        public static bool IsStaffFacility(FacilityKind kind)=>ShopModel.IsStaffPoint(kind) || kind==FacilityKind.Checkout;
        public StaffPostState Post(string id)=>staffPosts==null?null:staffPosts.Find(p=>p!=null && p.instanceId==id);
        public StaffPostState PostFor(FacilityKind role)=>staffPosts==null?null:staffPosts.Find(p=>p!=null && p.role==role && (role!=FacilityKind.Restock || string.IsNullOrEmpty(activeRestockPostId) || p.instanceId==activeRestockPostId));
        public string AssignedStaffId(FacilityKind role)=>EffectiveStaffId(PostFor(role));
        public int PostLevel(string id)=>Post(id)?.level??1;
        public int CheckoutLevel=>PostFor(FacilityKind.Checkout)?.level??1;
        public int GuideBonusFor(StaffPostState post)=>Math.Min(35,WorkingAttribute(post)/4+5*Math.Max(0,(post?.level??1)-1));
        public int GuideQuantityBonus=>GuideBonusFor(PostFor(FacilityKind.Reception));
        public int CheckoutBonusFor(StaffPostState post)=>Math.Min(8,WorkingAttribute(post)/25+2*((post?.level??1)-1));
        public int CarryCapacity=>CarryMaximumFor(PostFor(FacilityKind.Restock),AssignedStaffId(FacilityKind.Restock));
        public int EffectiveCarryLimit
        {
            get {var p=PostFor(FacilityKind.Restock);return CarryLimitFor(p,EffectiveStaffId(p));}
        }
        public static int PostUpgradeCost(int level)=>level==1?300:level==2?600:0;
        public int CheckoutPrice(ShopCatalog catalog,string productId,string checkoutId=null)
        {
            var product=Product(catalog,productId);
            var post=checkoutId==null?PostFor(FacilityKind.Checkout):Post(checkoutId);
            return product==null?0:(int)(((long)product.sellPrice*(100+CheckoutBonusFor(post)))/100);
        }
        public bool SyncStaffPosts(ShopCatalog catalog,ShopLayout layout,out string reason)
        {
            if(catalog==null || layout?.facilities==null)return Fail("岗位布局不可用。",out reason);
            bool migrate=staffSystemVersion==0;
            int migratedLevel=1;
            if(migrate && shelves!=null)foreach(var shelf in shelves)if(shelf!=null)migratedLevel=Math.Max(migratedLevel,shelf.level);
            var proposed=new List<StaffPostState>();
            foreach(var facility in layout.facilities)
            {
                var definition=catalog.Find(facility.definitionId);
                if(definition==null)return Fail("岗位设施定义不存在。",out reason);
                if(!IsStaffFacility(definition.kind))continue;
                var previous=Post(facility.instanceId);
                if(previous!=null)proposed.Add(previous);
                else
                {
                    string initial=definition.kind==FacilityKind.Checkout?"taki":definition.kind==FacilityKind.Reception?"soyo":"anon";
                    bool free=staffPosts==null || !staffPosts.Exists(p=>p!=null && p.staffId==initial);
                    free &= !proposed.Exists(p=>p.staffId==initial);
                    proposed.Add(new StaffPostState{instanceId=facility.instanceId,role=definition.kind,
                        level=definition.kind==FacilityKind.Checkout && migrate?Math.Min(3,migratedLevel):1,
                        staffId=free?initial:""});
                }
            }
            var old=staffPosts;int oldVersion=staffSystemVersion;
            staffPosts=proposed;staffSystemVersion=1;
            if(!ValidateStaffPosts(catalog,layout,out reason)){staffPosts=old;staffSystemVersion=oldVersion;return false;}
            return true;
        }
        public bool ValidateStaffPosts(ShopCatalog catalog,ShopLayout layout,out string reason)
        {
            if(staffSystemVersion==0){reason="";return true;}
            if((extraRestockPurchased&&ShopLevel<4)||(extraReceptionPurchased&&ShopLevel<5))return Fail("额外岗位购买与店铺等级不符。",out reason);
            if(staffSystemVersion!=1 || staffPosts==null)return Fail("岗位数据版本无效。",out reason);
            var ids=new HashSet<string>();var assigned=new HashSet<string>();var roles=new Dictionary<FacilityKind,int>();
            foreach(var post in staffPosts)
            {
                if(post==null || string.IsNullOrEmpty(post.instanceId) || !ids.Add(post.instanceId) ||
                    !IsStaffFacility(post.role) || post.level<1 || post.level>3 ||
                    post.carryLimit<0 || (post.role==FacilityKind.Restock && post.carryLimit>300))
                    return Fail("岗位实例、等级或携带额度无效。",out reason);
                roles.TryGetValue(post.role,out int count);roles[post.role]=count+1;
                if(count+1>OwnedPostLimit(post.role))return Fail("岗位数量超过已购买额度。",out reason);
                var f=layout.facilities.Find(p=>p.instanceId==post.instanceId);
                if(f==null || catalog.Find(f.definitionId)?.kind!=post.role)return Fail("岗位数据与布局不一致。",out reason);
                if(!string.IsNullOrEmpty(post.staffId) &&
                    (catalog.staff.Find(s=>s.id==post.staffId)==null || !IsStaffUnlocked(post.staffId) || !assigned.Add(post.staffId)))
                    return Fail("员工不存在或同时绑定了多个岗位。",out reason);
            }
            foreach(var f in layout.facilities)
                if(IsStaffFacility(catalog.Find(f.definitionId).kind) && !ids.Contains(f.instanceId))
                    return Fail("岗位设施缺少绑定数据。",out reason);
            reason="";return true;
        }
        public bool AssignStaff(ShopCatalog catalog,string postId,string staffId,out string reason)
        {
            var post=Post(postId);
            if(post==null)return Fail("岗位不存在。",out reason);
            staffId=staffId??"";
            if(staffId.Length>0 && catalog.staff.Find(s=>s.id==staffId)==null)return Fail("员工不存在。",out reason);
            if(staffId.Length>0 && !IsStaffUnlocked(staffId))return Fail("请先在角色档案中签约解锁这位员工。",out reason);
            if(staffId.Length>0 && staffPosts.Exists(p=>p!=post && p.staffId==staffId))
                return Fail("该员工已绑定其他岗位，请先解除原岗位。",out reason);
            post.staffId=staffId;reason=staffId.Length==0?"岗位已设为空缺。":"员工岗位已更新。";return true;
        }
        public bool UpgradePost(string postId,out string reason)
        {
            var post=Post(postId);if(post==null || post.level>=3)return Fail("岗位不存在或已满级。",out reason);
            int cost=PostUpgradeCost(post.level);
            if(!testMode && cash<cost)return Fail("升级需要 "+cost+" 金币。",out reason);
            if(!testMode)cash-=cost;post.level++;
            reason="岗位已升级至 Lv."+post.level+"。";return true;
        }
        public bool SetCarryLimit(string postId,int value,out string reason)
        {
            var post=Post(postId);
            if(post==null || post.role!=FacilityKind.Restock || value<1 || value>post.level*10)
                return Fail("携带额度需为 1 至当前岗位容量的整数。",out reason);
            post.carryLimit=value;reason="携带额度已设为 "+value+" 件，下一趟生效。";return true;
        }
    }
}
