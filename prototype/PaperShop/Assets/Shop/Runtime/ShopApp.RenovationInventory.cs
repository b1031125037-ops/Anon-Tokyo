using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        int renovationCategory,renovationDisplayFilter=-1;
        Vector2 renovationScroll;
        public int RenovationCategory=>renovationCategory;
        public void OpenRenovationInventory(int category)
        {
            if(category<0||category>4)return;
            renovationCategory=category;renovationScroll=Vector2.zero;
            EndSurfacePainting();CancelPlacement();ShowRenovationDrawer();
        }
        public bool BuyMaterial(string id,int quantity)
        {
            bool ok=Economy.BuyRenovationMaterial(Layout,id,quantity,out string reason);
            if(ok&&Economy.tutorialVersion>=1&&TutorialStep>0)
            {if(System.Array.IndexOf(ShopSurfaces.WallIds,id)>=0)Economy.tutorialWallClaimed=true;else Economy.tutorialFloorClaimed=true;}
            LastMessage=reason;dirty|=ok;PlaySfx(ok?"purchase":"error");return ok;
        }
        public bool BuyEnvironmentItem(string id)
        {
            bool ok=Economy.BuyEnvironment(id,out string reason);
            LastMessage=reason;dirty|=ok;PlaySfx(ok?"purchase":"error");return ok;
        }
        public bool BuyStageFixture()
        {
            bool ok=Economy.BuyStage(out string reason);
            LastMessage=reason;dirty|=ok;PlaySfx(ok?"purchase":"error");return ok;
        }
        void DrawRenovationInventory(Rect box)
        {
            string[] tabs={"展柜","装饰","设施","地板","墙纸"};
            for(int i=0;i<tabs.Length;i++)
                if(ArtButton(new Rect(box.x+22+i*116,box.y+54,106,32),tabs[i],renovationCategory==i?activeButton:button))OpenRenovationInventory(i);
            if(renovationCategory==0)
            {
                for(int i=-1;i<ShopProductSet.Categories.Length;i++)
                    if(ArtButton(new Rect(box.x+22+(i+1)*72,box.y+110,66,29),i<0?"全部":ShopProductSet.CategoryNames[i],renovationDisplayFilter==i?activeButton:button))
                    {renovationDisplayFilter=i;renovationScroll=Vector2.zero;}
                var owned=Economy.storedFixtures.FindAll(f=>renovationDisplayFilter<0||f.definitionId==ShopProductSet.Categories[renovationDisplayFilter]);
                if(owned.Count==0){GUI.Label(new Rect(box.x+22,box.y+174,570,35),"该分类暂无库存",body);return;}
                renovationScroll=GUI.BeginScrollView(new Rect(box.x+20,box.y+166,576,318),renovationScroll,new Rect(0,0,552,owned.Count*67));
                for(int i=0;i<owned.Count;i++)
                {
                    var f=owned[i];float y=i*67;
                    GUI.Label(new Rect(8,y+3,335,25),catalog.Find(f.definitionId).title+" · Lv."+f.shelf.level+" · 1 座",body);
                    DrawProductIcon(new Rect(8,y+30,28,28),f.shelf.productId);
                    GUI.Label(new Rect(44,y+31,299,25),ProductName(f.shelf.productId),small);
                    if(ArtButton(new Rect(365,y+10,177,38),"摆放",activeButton))BeginStoredCounterPlacement(f.instanceId);
                }
                GUI.EndScrollView();return;
            }
            int row=0;
            if(renovationCategory==2)
            {
                foreach(string id in new[]{"checkout","reception","restock","stage","stage_mujica"})
                {
                    int count=Economy.StoredFacilityCount(Layout,id);if(count==0)continue;
                    float y=box.y+122+row++*67;
                    GUI.Label(new Rect(box.x+22,y+6,350,29),catalog.Find(id).title+" · 库存 "+count,body);
                    if(ArtButton(new Rect(box.x+390,y,204,38),"摆放",activeButton))
                    {panelKey="";BeginPlacement(id);World.SetEditing(true);}
                }
            }
            else if(renovationCategory>=3)
            {
                bool floor=renovationCategory==3;
                var ids=floor?ShopSurfaces.FloorIds:ShopSurfaces.WallIds;
                var names=floor?ShopSurfaces.FloorNames:ShopSurfaces.WallNames;
                for(int i=0;i<ids.Length;i++)
                {
                    int count=Economy.MaterialCount(ids[i]);if(count==0)continue;
                    float y=box.y+122+row++*67;
                    GUI.Label(new Rect(box.x+22,y+6,350,29),names[i]+" · 库存 "+count,body);
                    if(ArtButton(new Rect(box.x+390,y,204,38),"使用",activeButton))BeginSurfacePainting(ids[i]);
                }
            }
            if(row==0)GUI.Label(new Rect(box.x+22,box.y+132,570,35),"该分类暂无库存",body);
        }
    }
}
