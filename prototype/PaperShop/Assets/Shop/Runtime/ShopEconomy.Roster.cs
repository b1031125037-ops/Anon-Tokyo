using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        public int rosterVersion;
        public List<string> signedStaffIds=new List<string>();
        public bool IsStaffUnlocked(string id)
        {
            var staff=ShopStaffProfiles.Find(id);
            return staff!=null&&(staff.initiallyAvailable || signedStaffIds!=null&&signedStaffIds.Contains(id));
        }
        public bool ContractStaff(string id,out string reason)
        {
            var staff=ShopStaffProfiles.Find(id);
            if(staff==null)return Fail("角色不存在。",out reason);
            if(IsStaffUnlocked(id))return Fail("角色已解锁，无需重复签约。",out reason);
            if(ShopLevel<staff.contractLevel)return Fail("需要店铺 Lv."+staff.contractLevel+" 才能签约。",out reason);
            if(!testMode&&cash<staff.contractCost)return Fail("签约需要 "+staff.contractCost+" 金币。",out reason);
            if(!testMode)cash-=staff.contractCost;
            signedStaffIds.Add(id);rosterVersion=1;
            reason="已签约 "+staff.title+"，现在可以配置岗位或参加演出。";return true;
        }
        public int StaffAttribute(FacilityKind role)
        {
            var staff=ShopStaffProfiles.Find(AssignedStaffId(role));
            return Ability(staff,role);
        }
        public float StaffEfficiency(FacilityKind role) => 1f;
        bool ValidateRoster(out string reason)
        {
            if(rosterVersion<0||rosterVersion>1||signedStaffIds==null||signedStaffIds.Count>5)
                return Fail("员工签约数据无效。",out reason);
            var unique=new HashSet<string>();
            foreach(var id in signedStaffIds)
            {
                var s=ShopStaffProfiles.Find(id);
                if(s==null||s.initiallyAvailable||!unique.Add(id)||rosterVersion!=1)
                    return Fail("员工签约记录包含未知、重复或基础成员。",out reason);
            }
            reason="";return true;
        }
    }
}
