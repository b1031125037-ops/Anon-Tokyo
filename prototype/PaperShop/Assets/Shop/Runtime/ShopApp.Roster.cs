using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        Vector2 rosterScroll;
        string profileId="anon";
        public void OpenRoster(){if(panelKey!="roster")OpenPanel("roster");}
        public void OpenStaffProfile(string id){if(ShopStaffProfiles.Find(id)==null)return;profileId=id;panelKey="profile";CancelPlacement();}
        public bool SignStaff(string id)
        {
            var draft=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            if(!draft.ContractStaff(id,out string message)){LastMessage=message;return false;}
            if(!SaveGrowthDraft(Layout,draft,out string reason)){LastMessage=reason;return false;}
            Economy=draft;dirty=verifyMode;LastMessage=message;PlaySfx("upgrade");return true;
        }
        void DrawStaffTabs(Rect box)
        {
            if(ArtButton(new Rect(box.x+22,box.y+54,278,32),"工作岗位",panelKey=="staff"?activeButton:button)&&panelKey!="staff")OpenPanel("staff");
            if(ArtButton(new Rect(box.x+314,box.y+54,278,32),"角色档案",panelKey=="roster"?activeButton:button)&&panelKey!="roster")OpenRoster();
        }
        void DrawRoster(Rect box)
        {
            DrawStaffTabs(box);
            GUI.Label(new Rect(box.x+22,box.y+101,570,40),"10 位角色 · MyGO!!!!! 已开放 · Ave Mujica 需逐人签约",body);
            rosterScroll=GUI.BeginScrollView(new Rect(box.x+20,box.y+147,576,355),rosterScroll,new Rect(0,0,550,ShopStaffProfiles.All.Length*83));
            for(int i=0;i<ShopStaffProfiles.All.Length;i++)
            {
                var s=ShopStaffProfiles.All[i];float y=i*83;
                GUI.Label(new Rect(8,y,390,26),s.title+" · "+s.band,body);
                GUI.Label(new Rect(8,y+29,390,25),"收银 "+s.cashier+"  补货 "+s.restocking+"  招待 "+s.hospitality,small);
                GUI.Label(new Rect(8,y+54,390,23),Economy.IsStaffUnlocked(s.id)?"可使用":"条件：Lv."+s.contractLevel+" · "+s.contractCost+" 金币",small);
                GUI.enabled=!Economy.IsStaffUnlocked(s.id)&&Economy.ShopLevel>=s.contractLevel&&(Economy.testMode||Economy.cash>=s.contractCost);
                if(ArtButton(new Rect(409,y+15,130,39),Economy.IsStaffUnlocked(s.id)?"已签约":"签约",button))SignStaff(s.id);
                GUI.enabled=true;
            }
            GUI.EndScrollView();
        }
        void DrawStaffProfile(Rect box)
        {
            var s=ShopStaffProfiles.Find(profileId);if(s==null)return;
            GUI.Label(new Rect(box.x+22,box.y+62,570,30),s.title+" · "+s.band,heading);
            GUI.Label(new Rect(box.x+22,box.y+108,570,32),"收银 "+s.cashier+" / 100     补货 "+s.restocking+" / 100     招待 "+s.hospitality+" / 100",body);
            GUI.Label(new Rect(box.x+22,box.y+154,570,60),s.profile,body);
            GUI.Label(new Rect(box.x+22,box.y+226,570,92),
                "收银：提高商品售价。\n补货：一次能带更多商品。\n招待：让顾客愿意多买一些。",body);
            var assigned=Economy.staffPosts.Find(p=>p.staffId==s.id);
            GUI.Label(new Rect(box.x+22,box.y+330,570,42),!Economy.IsStaffUnlocked(s.id)?
                "签约条件：店铺 Lv."+s.contractLevel+" · "+(Economy.testMode?"免费":s.contractCost+" 金币"):
                "已解锁 · "+(Economy.IsPerformingStaff(s.id)?"舞台表演中":assigned==null?"待命，可配置岗位":"已安排工作岗位"),body);
            GUI.enabled=!Economy.IsStaffUnlocked(s.id)&&Economy.ShopLevel>=s.contractLevel&&(Economy.testMode||Economy.cash>=s.contractCost);
            if(ArtButton(new Rect(box.x+22,box.y+386,278,40),Economy.IsStaffUnlocked(s.id)?"已解锁":"签约这位员工",activeButton))SignStaff(s.id);
            GUI.enabled=true;
            if(ArtButton(new Rect(box.x+314,box.y+386,278,40),"返回角色列表",button))OpenRoster();
        }
    }
}
