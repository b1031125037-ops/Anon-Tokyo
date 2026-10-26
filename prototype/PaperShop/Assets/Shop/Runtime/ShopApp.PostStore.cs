using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public bool BuyAdditionalStaffPost(FacilityKind role)
        {
            var state=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            if(!state.BuyExtraPost(role,out string message)){LastMessage=message;return false;}
            if(!SaveGrowthDraft(Layout,state,out string reason)){LastMessage=reason;return false;}
            Economy=state;dirty=verifyMode;LastMessage=message;PlaySfx("purchase");return true;
        }
        void DrawPostStore(Rect box)
        {
            DrawStoreTabs(box);
            GUI.Label(new Rect(box.x+22,box.y+102,570,40),"额外员工岗位 · 店铺 Lv."+Economy.ShopLevel+"\n每类最多两处；不占展柜额度，购买后在关店装修中摆放。",body);
            int i=0;
            foreach(var role in new[]{FacilityKind.Restock,FacilityKind.Reception})
            {
                float y=box.y+160+i*92;bool owned=Economy.OwnedPostLimit(role)==2;
                int level=ShopEconomy.ExtraPostLevel(role),cost=ShopEconomy.ExtraPostCost(role);
                int placed=Economy.staffPosts.FindAll(p=>p.role==role).Count;
                GUI.Label(new Rect(box.x+22,y,335,28),(role==FacilityKind.Restock?"第二补货位":"第二招待位")+" · 1 格",heading);
                GUI.Label(new Rect(box.x+22,y+35,335,50),"Lv."+level+" 解锁 · 已放 "+placed+" / "+Economy.OwnedPostLimit(role)+
                    "\n"+(owned?"已购买，移除后保留摆放额度":cost+" 金币 · 每类限购一次"),small);
                GUI.enabled=!owned&&Economy.ShopLevel>=level&&(Economy.testMode||Economy.cash>=cost);
                if(ArtButton(new Rect(box.x+390,y+11,204,42),owned?"已购买":Economy.ShopLevel<level?"Lv."+level+" 解锁":
                    Economy.testMode?"购买 · 免费":"购买 · "+cost,activeButton))BuyAdditionalStaffPost(role);
                GUI.enabled=true;i++;
            }
            GUI.Label(new Rect(box.x+22,box.y+354,335,28),"MyGO!!!!! 舞台",heading);
            GUI.Label(new Rect(box.x+22,box.y+389,570,28),"已拥有 · Lv.3 可在装修库存摆放",small);
            GUI.Label(new Rect(box.x+22,box.y+442,335,28),"Ave Mujica 舞台",heading);
            GUI.Label(new Rect(box.x+22,box.y+478,335,28),"Lv.3 解锁 · 1,500 金币",small);
            GUI.enabled=!Economy.mujicaStageOwned&&Economy.ShopLevel>=3&&(Economy.testMode||Economy.cash>=1500);
            if(ArtButton(new Rect(box.x+390,box.y+451,204,42),Economy.mujicaStageOwned?"已拥有":Economy.ShopLevel<3?"Lv.3 解锁":"购买 · 1,500",activeButton))BuyEnvironmentItem("stage_mujica");
            GUI.enabled=true;
        }
    }
}
