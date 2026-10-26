using System;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public void OpenCounterStore()
        {
            if(panelKey!="store")OpenPanel("store");
        }

        public bool BuyCounter(string definitionId)
        {
            bool ok=Economy.BuyFixture(catalog,Layout,definitionId,out _,out string reason);
            LastMessage=reason;dirty|=ok;PlaySfx(ok?"purchase":"error");return ok;
        }

        public bool BeginStoredCounterPlacement(string instanceId)
        {
            if(!CanRenovate){OpenPanel("build");return false;}
            var stored=Economy.StoredFixture(instanceId);
            if(stored==null){LastMessage="收纳中没有这座柜台。";return false;}
            panelKey="";
            BeginPlacement(stored.definitionId,stored.instanceId);
            World.SetEditing(true);
            return true;
        }

        public bool StoreSalesCounter(string instanceId)
        {
            if(!CanRenovate){LastMessage="请先关店，等待顾客全部结清离店后再收纳柜台。";return false;}
            var draft=ShopModel.Clone(Layout);
            var state=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            state.CancelRestock();
            if(!state.StoreFixture(catalog,draft,instanceId,out string reason)){LastMessage=reason;return false;}
            CancelRestockTask();StopAtCurrentCell();
            Layout=draft;Economy=state;dirty=true;Select(null);
            World.Rebuild(Layout,catalog);ResetGuide();LastMessage=reason;
            return true;
        }

        void DrawCounterStore(Rect box)
        {
            DrawStoreTabs(box);
            GUI.Label(new Rect(box.x+22,box.y+100,570,27),
                "店铺 Lv."+Economy.ShopLevel+" · 柜台持有 "+Economy.OwnedFixtureCount+" / "+Economy.FixtureLimit+" · 收纳 "+Economy.StoredFixtureCount,heading);

            string[] ids=ShopProductSet.Categories;
            for(int i=-1;i<ids.Length;i++)
                if(ArtButton(new Rect(box.x+22+(i+1)*72,box.y+174,66,29),i<0?"全部":ShopProductSet.CategoryNames[i],fixtureCategory==i?activeButton:button))
                {fixtureCategory=i;fixtureScroll=Vector2.zero;}
            int rows=fixtureCategory<0?ids.Length:1;
            fixtureScroll=GUI.BeginScrollView(new Rect(box.x+20,box.y+218,576,228),fixtureScroll,new Rect(0,0,553,rows*76));
            for(int row=0;row<rows;row++)
            {
                int i=fixtureCategory<0?row:fixtureCategory;
                var d=catalog.Find(ids[i]);float y=row*76;
                if(TutorialStep==23&&tutorialDynamicTarget.width==0&&Economy.OwnedCategoryCount(catalog,ids[i])==0)RegisterTutorialTarget(new Rect(box.x+385,box.y+228+y-fixtureScroll.y,177,40),new Rect(box.x+20,box.y+218,576,228));
                GUI.Label(new Rect(8,y,335,25),d.title+" · "+Economy.OwnedCategoryCount(catalog,ids[i])+" / "+Economy.FixtureCategoryLimit(ids[i]),body);
                GUI.Label(new Rect(8,y+25,335,22),d.width+" × "+d.depth+" 格 · 容量 "+d.capacity,small);
                DrawProductIcon(new Rect(8,y+46,26,26),ShopProductSet.ProductIds[i]);
                GUI.Label(new Rect(42,y+47,301,24),ShopProductSet.ProductNames[i],small);
                GUI.enabled=Economy.OwnedCategoryCount(catalog,ids[i])<Economy.FixtureCategoryLimit(ids[i]) &&
                    (Economy.testMode || Economy.cash>=ShopEconomy.BasicFixtureCost);
                if(ArtButton(new Rect(365,y+10,177,40),
                    Economy.OwnedCategoryCount(catalog,ids[i])>=Economy.FixtureCategoryLimit(ids[i])?"此类已达上限":
                    Economy.testMode?"购买 · 免费":"购买 · "+ShopEconomy.BasicFixtureCost,activeButton))BuyCounter(ids[i]);
                GUI.enabled=true;
            }
            GUI.EndScrollView();

        }

        int fixtureCategory=-1;
        Vector2 fixtureScroll;

        void DrawRenovationGate(Rect box)
        {
            GUI.Label(new Rect(box.x+22,box.y+60,570,28),BusinessStateText,heading);
            GUI.Label(new Rect(box.x+22,box.y+104,570,65),
                CanRenovate?"顾客已全部离开，可以摆放、移动、旋转和收纳柜台。":
                IsClosing?"已停止迎客。现有顾客会付款后离开；空手顾客直接离店。"+
                    (IsPaused?"\n当前手动暂停，点击下方继续结账。":"\n剩余 "+CustomerCount+" 位客人，请等待结账离店。"):
                    "装修前必须关店。关店只停止迎客，已拿货客人仍需完成付款。",
                body);
            GUI.enabled=!IsClosing || IsPaused;
            string action=CanRenovate?"进入装修":IsClosing?(IsPaused?"继续结账":"等待客人离店…"):"关店并等待结账";
            if(ArtButton(new Rect(box.x+22,box.y+183,278,40),action,activeButton))
            {
                if(CanRenovate)OpenPanel("build");
                else if(IsClosing)SetPaused(false);
                else RequestCloseShop(out _);
            }
            GUI.enabled=true;
            if(ArtButton(new Rect(box.x+314,box.y+183,280,40),"返回店铺",button))OpenPanel("renovation_gate");
        }

        void DrawRenovation(Rect box){DrawRenovationInventory(box);}
    }
}
