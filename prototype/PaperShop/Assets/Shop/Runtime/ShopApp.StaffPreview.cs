using System;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public static string StaffEffectPreview(StaffPostState post,string id)
        {
            var staff=ShopStaffProfiles.Find(id);if(staff==null)return "选用后：岗位空置";
            if(post.role==FacilityKind.Reception)
            {
                int bonus=Math.Min(35,staff.hospitality/4+5*(post.level-1));
                return "选用后：多拿 1 件概率 "+bonus+"%\n多买种类概率 "+(ShopBalance.ExtraKindChance(bonus)*100).ToString("0.#")+"%";
            }
            if(post.role==FacilityKind.Restock)return "选用后：每趟携带 "+ShopEconomy.CarryMaximumFor(post,id)+" 件";
            return "选用后：售价 +"+Math.Min(8,staff.cashier/25+2*(post.level-1))+"%\n每位客人结账约 "+(4.5-staff.cashier*.0075).ToString("0.00")+" 秒";
        }
    }
}
