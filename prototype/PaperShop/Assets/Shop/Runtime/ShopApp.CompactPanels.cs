using System;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        Vector2 compactScroll;
        float lastDecorationPayoutUntil;
        int lastDecorationPayout;
        bool IsCompactPanel=>panelKey=="shelf"||panelKey=="post"||panelKey=="profile"||panelKey=="warehouse"||panelKey=="object";
        public Rect CompactPanelRect=>new Rect(Screen.width/uiScale-372,164,348,Mathf.Min(540,Screen.height/uiScale-296));
        void DrawCompactPanel()
        {
            var box=CompactPanelRect;PanelBox(box);
            string title=panelKey=="shelf"?"展柜":panelKey=="post"?"岗位":panelKey=="warehouse"?"仓库":panelKey=="profile"?"员工属性":"物件";
            GUI.Label(new Rect(box.x+18,box.y+15,230,30),title,heading);
            if(ArtButton(new Rect(box.xMax-52,box.y+14,36,29),"×",button)){panelKey="";return;}
            float x=box.x+18,y=box.y+60,w=box.width-36;
            if(panelKey=="shelf")
            {
                var f=Selected();var stock=f==null?null:Economy.Shelf(f.instanceId);if(stock==null)return;
                GUI.Label(new Rect(x,y,w,29),catalog.Find(f.definitionId).title+" · Lv."+stock.level,heading);
                DrawProductIcon(new Rect(x,y+40,52,52),stock.productId);GUI.Label(new Rect(x+62,y+40,w-62,52),ProductName(stock.productId),body);
                GUI.Label(new Rect(x,y+110,w,66),"售价 "+Economy.CheckoutPrice(catalog,stock.productId)+" 金币/件\n库存 "+stock.quantity+" / "+Economy.ShelfCapacity(catalog,Layout,f.instanceId),body);
                GUI.Label(new Rect(x,y+185,w,25),"装饰值 +"+Economy.DecorationForFacility(f),small);
                GUI.enabled=stock.level<3&&(Economy.testMode||Economy.cash>=ShopEconomy.ShelfUpgradeCost(stock.level));
                if(ArtButton(new Rect(x,y+224,w,38),stock.level<3?"升级 · "+ShopEconomy.ShelfUpgradeCost(stock.level)+" 金币":"已满级",activeButton))UpgradeSalesCounter(f.instanceId);
                GUI.enabled=true;
                if(stock.level<3)GUI.Label(new Rect(x,y+271,w,30),"升级后容量 "+catalog.Find(f.definitionId).capacity*(stock.level+1),small);
            }
            else if(panelKey=="profile")
            {
                var s=ShopStaffProfiles.Find(profileId);if(s==null)return;
                var portrait=Resources.Load<Texture2D>("Characters/"+s.id+"/idle");if(portrait!=null)GUI.DrawTexture(new Rect(x,y,86,128),portrait,ScaleMode.ScaleToFit);
                GUI.Label(new Rect(x+98,y+5,w-98,55),s.title+"\n"+s.band,body);
                GUI.Label(new Rect(x+98,y+68,w-98,66),"收银 "+s.cashier+"\n补货 "+s.restocking+"   招待 "+s.hospitality,body);
                GUI.Label(new Rect(x,y+150,w,78),"收银：小幅提高售价与结账速度\n补货：提高每趟携带量\n招待：提高加购件数和种类的机会",small);
                var post=Economy.staffPosts.Find(p=>Economy.EffectiveStaffId(p)==s.id);
                GUI.Label(new Rect(x,y+246,w,60),post==null?"当前未上岗":PostEffect(post),body);
                if(post!=null&&ArtButton(new Rect(x,y+321,w,36),"调整岗位",button))OpenStaffPost(post.instanceId);
            }
            else if(panelKey=="post")
            {
                var f=Selected();var post=f==null?null:Economy.Post(f.instanceId);if(post==null)return;
                if(staffDraftPost!=post.instanceId){staffDraftPost=post.instanceId;staffDraftId=post.staffId;staffDraftError="";}
                GUI.Label(new Rect(x,y,w,26),catalog.Find(f.definitionId).title+" · Lv."+post.level,heading);
                GUI.Label(new Rect(x,y+33,w,52),PostEffect(post),small);
                float listHeight=Mathf.Max(76,box.height-374);
                compactScroll=GUI.BeginScrollView(new Rect(x,y+92,w,listHeight),compactScroll,new Rect(0,0,w-22,34*11));
                for(int i=-1;i<ShopStaffProfiles.All.Length;i++)
                {
                    string id=i<0?"":ShopStaffProfiles.All[i].id;
                    bool eligible=id==""||Economy.IsStaffUnlocked(id)&&!Economy.staffPosts.Exists(p=>p.instanceId!=post.instanceId&&p.staffId==id);
                    GUI.enabled=eligible&&!Economy.IsPerforming;
                    if(ArtButton(new Rect(0,(i+1)*34,w-24,30),i<0?"空置":ShopStaffProfiles.All[i].title+(IsVisitingEmployee(id)?" · 店内闲逛":""),staffDraftId==id?activeButton:button))SelectStaffDraft(id);
                }
                GUI.enabled=true;GUI.EndScrollView();float bottom=box.yMax-211;
                GUI.Label(new Rect(x,bottom,w,35),staffDraftError!=""?staffDraftError:"待确认："+(staffDraftId==""?"空置":ShopStaffProfiles.Find(staffDraftId)?.title),small);
                GUI.Label(new Rect(x,bottom+36,w,46),StaffEffectPreview(post,staffDraftId),small);
                GUI.enabled=!Economy.IsPerforming&&!IsVisitingEmployee(staffDraftId)&&!staffDraftError.Contains("正在店内闲逛");if(ArtButton(new Rect(x,bottom+86,w,34),"确认安排",activeButton))ConfirmStaffSelection();GUI.enabled=true;
                GUI.Label(new Rect(x,bottom+126,w,38),post.level<3?"升级后："+PostEffect(post,post.level+1):"已满级",small);
                GUI.enabled=post.level<3&&(Economy.testMode||Economy.cash>=ShopEconomy.PostUpgradeCost(post.level));if(ArtButton(new Rect(x,bottom+167,w,30),"升级 · "+ShopEconomy.PostUpgradeCost(post.level)+" 金币",button))UpgradeStaffPost(post.instanceId);GUI.enabled=true;
            }
            else if(panelKey=="warehouse")
            {
                GUI.Label(new Rect(x,y,w,27),"仓库 Lv."+Economy.warehouseLevel,heading);
                GUI.Label(new Rect(x,y+35,w,54),"已占 "+(Economy.testMode?"∞":Economy.WarehouseCommitted+" / "+Economy.Capacity)+"\n现货 "+Economy.WarehouseUsed+" · 在途 "+Economy.InTransit,body);
                var stocks=Economy.stocks.FindAll(s=>ShopProductSet.IsCurrentProduct(s.productId)||s.available>0);
                float height=Mathf.Max(110,box.height-260);
                compactScroll=GUI.BeginScrollView(new Rect(x,y+103,w,height),compactScroll,new Rect(0,0,w-22,stocks.Count*53));
                for(int i=0;i<stocks.Count;i++)
                {var s=stocks[i];DrawProductIcon(new Rect(0,i*53,40,40),s.productId);GUI.Label(new Rect(49,i*53,w-74,23),ProductName(s.productId),small);GUI.Label(new Rect(49,i*53+24,w-74,23),"可用 "+(Economy.testMode?"∞":s.available.ToString()),small);}
                GUI.EndScrollView();float bottom=box.yMax-93;
                GUI.enabled=Economy.warehouseLevel<ShopEconomy.MaxWarehouseLevel&&(Economy.testMode||Economy.cash>=Economy.UpgradeCost);
                if(ArtButton(new Rect(x,bottom,w,34),"扩容 +200 · "+Economy.UpgradeCost+" 金币",activeButton))ExpandWarehouse();GUI.enabled=true;
                if(ArtButton(new Rect(x,bottom+43,w,32),"配送订单",button))OpenPanel("orders");
            }
            else
            {
                var f=Selected();if(f==null)return;
                GUI.Label(new Rect(x,y,w,35),catalog.Find(f.definitionId).title,heading);
                GUI.Label(new Rect(x,y+52,w,34),"装饰值 +"+Economy.DecorationForFacility(f),body);
            }
        }
        int warehouseBadgeUsed=-1,warehouseBadgeCapacity=-1;float warehouseBadgePulse;
        void DrawWarehouseBadge()
        {
            if(hideWorldText||panelKey=="warehouse")return;
            var p=World.WorldCamera.WorldToScreenPoint(World.WarehouseDoorPosition+Vector3.up*.14f);if(p.z<=0)return;
            var rect=new Rect(p.x/uiScale-72,(Screen.height-p.y)/uiScale-35,144,36);
            if(p.x<0||p.x>Screen.width||p.y<0||p.y>Screen.height)return;
            rect.x=Mathf.Clamp(rect.x,8,Screen.width/uiScale-152);
            rect.y=Mathf.Clamp(rect.y,154,Screen.height/uiScale-161);
            if(warehouseBadgeUsed!=Economy.WarehouseCommitted||warehouseBadgeCapacity!=Economy.Capacity)
            {if(warehouseBadgeUsed>=0)warehouseBadgePulse=Time.unscaledTime+.5f;warehouseBadgeUsed=Economy.WarehouseCommitted;warehouseBadgeCapacity=Economy.Capacity;}
            uiRegions.Add(rect);Color old=GUI.color;if(Time.unscaledTime<warehouseBadgePulse)GUI.color=new Color(1,.88f,.72f);
            if(ArtButton(rect,"仓库 "+(Economy.testMode?"∞":Economy.WarehouseCommitted+" / "+Economy.Capacity),button))OpenPanel("warehouse");GUI.color=old;
        }
    }
}
