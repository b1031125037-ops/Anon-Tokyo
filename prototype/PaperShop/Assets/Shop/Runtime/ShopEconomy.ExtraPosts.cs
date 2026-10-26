using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        public bool extraRestockPurchased, extraReceptionPurchased;
        public int UnlockedStaffCount {get {int n=0;foreach(var s in ShopStaffProfiles.All)if(IsStaffUnlocked(s.id))n++;return n;}}
        public int OwnedPostLimit(FacilityKind role)=>role==FacilityKind.Restock?(extraRestockPurchased?2:1):
            role==FacilityKind.Reception?(extraReceptionPurchased?2:1):1;
        public static int ExtraPostLevel(FacilityKind role)=>role==FacilityKind.Restock?4:5;
        public static int ExtraPostCost(FacilityKind role)=>role==FacilityKind.Restock?800:1000;
        public static int BaseCarryFor(StaffPostState post,string staffId=null)=>3+Ability(ShopStaffProfiles.Find(staffId??post?.staffId),FacilityKind.Restock)/25;
        public static int CarryMaximumFor(StaffPostState post,string staffId=null)=>BaseCarryFor(post,staffId)+2*((post?.level??1)-1);
        public static int CarryLimitFor(StaffPostState post,string staffId=null)=>CarryMaximumFor(post,staffId);
        public static int Ability(StaffDefinition staff,FacilityKind role)=>staff==null?50:
            role==FacilityKind.Checkout?staff.cashier:role==FacilityKind.Restock?staff.restocking:staff.hospitality;
        public bool BuyExtraPost(FacilityKind role,out string reason)
        {
            if(!ShopModel.IsStaffPoint(role))return Fail("仅可购买补货和招待员工点。",out reason);
            if(OwnedPostLimit(role)==2)return Fail("这一额外岗位已购买，最多两处。",out reason);
            if(ShopLevel<ExtraPostLevel(role))return Fail("需要店铺 Lv."+ExtraPostLevel(role)+"。",out reason);
            int cost=ExtraPostCost(role);
            if(!testMode&&cash<cost)return Fail("购买岗位需要 "+cost+" 金币。",out reason);
            if(!testMode)cash-=cost;
            if(role==FacilityKind.Restock)extraRestockPurchased=true;else extraReceptionPurchased=true;
            reason="已购买额外岗位额度，请关店后在装修中摆放。移除后可重新摆放，不必再次购买。";return true;
        }
        public List<StaffPostState> PerformanceWorkPosts()
        {
            var result=new List<StaffPostState>();
            foreach(var role in new[]{FacilityKind.Checkout,FacilityKind.Restock,FacilityKind.Reception})
            {var first=staffPosts.Find(p=>p.role==role);if(first!=null)result.Add(first);}
            foreach(var p in staffPosts)if(!result.Contains(p))result.Add(p);
            return result;
        }
        // Permanent assignments remain intact. An event overlay is rebuilt from persisted
        // roster and posts, so save/load and event completion cannot lose manual bindings.
        public Dictionary<string,string> PerformanceAssignments()
        {
            var result=new Dictionary<string,string>();
            if(!IsPerforming)return result;
            var remaining=new List<StaffDefinition>();
            foreach(var s in ShopStaffProfiles.All)if(IsStaffUnlocked(s.id)&&s.band!=performingBand)remaining.Add(s);
            foreach(var p in PerformanceWorkPosts())
            {
                StaffDefinition best=null;
                foreach(var s in remaining)if(best==null||Ability(s,p.role)>Ability(best,p.role))best=s;
                if(best==null)break;
                result[p.instanceId]=best.id;remaining.Remove(best);
            }
            return result;
        }
        public string EffectiveStaffId(StaffPostState post)
        {
            if(post==null)return "";
            if(!IsPerforming)return post.staffId??"";
            var bindings=PerformanceAssignments();
            return bindings.TryGetValue(post.instanceId,out string id)?id:"";
        }
    }
}
