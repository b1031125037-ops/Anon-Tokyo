using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        bool renovationCollapsed;
        public bool RenovationDrawerCollapsed=>renovationCollapsed;
        public void ShowRenovationDrawer()
        {
            if(!CanRenovate){OpenPanel("build");return;}
            renovationMode=true;renovationCollapsed=false;panelKey="build";World.SetEditing(true);
        }
        public void CollapseRenovationDrawer(){renovationCollapsed=true;if(panelKey=="build")panelKey="";}
        public void EndRenovation()
        {EndSurfacePainting();CancelPlacement();renovationMode=false;renovationCollapsed=false;Select(null);panelKey="";World.SetEditing(false);}
        void DrawRenovationDrawer(float width,float height)
        {
            if(renovationCollapsed)
            {
                var tab=new Rect(width-162,170,138,44);uiRegions.Add(tab);
                if(ArtButton(tab,"展开装修库存 ‹",activeButton))ShowRenovationDrawer();return;
            }
            var box=new Rect(width-324,166,300,height-294);
            TutorialFrame(box,"renovation-drawer");
            GUI.Label(new Rect(box.x+18,box.y+15,200,29),"装修库存",heading);
            if(ArtButton(new Rect(box.xMax-59,box.y+14,42,29),"收起",button)){CollapseRenovationDrawer();return;}
            string[] tabs={"展柜","装饰","设施","地板","墙纸"};
            for(int i=0;i<5;i++)
                if(ArtButton(new Rect(box.x+16+(i%3)*90,box.y+56+(i/3)*35,84,30),tabs[i],renovationCategory==i?activeButton:button))OpenRenovationInventory(i);
            float start=box.y+132;
            if(renovationCategory==0)
            {
                for(int i=-1;i<ShopProductSet.Categories.Length;i++)
                    if(ArtButton(new Rect(box.x+16+((i+1)%4)*67,start+((i+1)/4)*31,62,27),i<0?"全部":ShopProductSet.CategoryNames[i],renovationDisplayFilter==i?activeButton:button))
                    {renovationDisplayFilter=i;renovationScroll=Vector2.zero;}
                start+=68;
            }
            var labels=new List<string>();var actions=new List<Action>();
            if(renovationCategory==0)
                foreach(var stored in Economy.storedFixtures)
                {
                    if(renovationDisplayFilter>=0&&stored.definitionId!=ShopProductSet.Categories[renovationDisplayFilter])continue;
                    var f=stored;labels.Add(catalog.Find(f.definitionId).title+" · Lv."+f.shelf.level+"\n库存 1");
                    actions.Add(()=>BeginStoredCounterPlacement(f.instanceId));
                }
            else if(renovationCategory==2)
                foreach(string id in new[]{"checkout","reception","restock","stage","stage_mujica"})
                {
                    int count=Economy.StoredFacilityCount(Layout,id);if(count==0)continue;
                    string captured=id;labels.Add(catalog.Find(id).title+"\n库存 "+count);
                    actions.Add(()=>{panelKey="";BeginPlacement(captured);World.SetEditing(true);});
                }
            else if(renovationCategory==1)
                foreach(string id in ShopEnvironmentAssets.Decorations)
                {int count=Economy.StoredFacilityCount(Layout,id);if(count==0)continue;string captured=id;labels.Add(catalog.Find(id).title+"\n库存 "+count);actions.Add(()=>{panelKey="";BeginPlacement(captured);World.SetEditing(true);});}
            else if(renovationCategory>=3)
            {
                var ids=renovationCategory==3?ShopSurfaces.FloorIds:ShopSurfaces.WallIds;
                var names=renovationCategory==3?ShopSurfaces.FloorNames:ShopSurfaces.WallNames;
                for(int i=0;i<ids.Length;i++)
                {
                    int count=Economy.MaterialCount(ids[i]);if(count==0)continue;
                    string id=ids[i];labels.Add(names[i]+"\n库存 "+count);actions.Add(()=>BeginSurfacePainting(id));
                }
            }
            float footer=box.yMax-156,listHeight=Mathf.Max(76,footer-start-12);
            if(labels.Count==0)GUI.Label(new Rect(box.x+18,start+12,263,46),"该分类暂无库存",body);
            else
            {
                renovationScroll=GUI.BeginScrollView(new Rect(box.x+12,start,276,listHeight),renovationScroll,new Rect(0,0,254,labels.Count*66));
                for(int i=0;i<labels.Count;i++)
                {
                    GUI.Label(new Rect(5,i*66+4,174,52),labels[i],body);
                    if(TutorialStep==25&&renovationCategory==0&&i==0)RegisterTutorialTarget(new Rect(box.x+194,start+i*66+10-renovationScroll.y,66,35),new Rect(box.x+12,start,276,listHeight));
                    if(ArtButton(new Rect(182,i*66+10,66,35),renovationCategory>=3?"使用":"摆放",activeButton))actions[i]();
                }
                GUI.EndScrollView();
            }
            DrawRenovationTools(new Rect(box.x+16,footer,268,148));
        }
        void DrawRenovationTools(Rect box)
        {
            if(IsPaintingSurface)
            {
                var ids=surfaceStoreFloor?ShopSurfaces.FloorIds:ShopSurfaces.WallIds;
                var names=surfaceStoreFloor?ShopSurfaces.FloorNames:ShopSurfaces.WallNames;
                int i=Array.IndexOf(ids,paintStyle);
                GUI.Label(new Rect(box.x,box.y,268,45),(i>=0?names[i]:"材料")+" · 剩余 "+Economy.MaterialCount(paintStyle),body);
                if(ArtButton(new Rect(box.x,box.y+49,268,32),"结束铺设",button))EndSurfacePainting();
            }
            else if(!string.IsNullOrEmpty(placingDefinition))
            {
                GUI.Label(new Rect(box.x,box.y,268,44),candidateValid?"点击店内确认摆放":placementReason,small);
                GUI.enabled=placingDefinition!="warehouse";
                if(ArtButton(new Rect(box.x,box.y+49,130,32),"旋转 · R",button)){rotation=(rotation+1)%4;PlaySfx("rotate");UpdateCandidate();}
                GUI.enabled=true;
                if(ArtButton(new Rect(box.x+138,box.y+49,130,32),"取消摆放",button))CancelPlacement();
            }
            else
            {
                var item=Selected();
                GUI.Label(new Rect(box.x,box.y,268,31),item==null?"选择店内物件进行调整":catalog.Find(item.definitionId).title,body);
                GUI.enabled=item!=null;
                if(ArtButton(new Rect(box.x,box.y+39,84,32),"移动",button)){BeginPlacement(item.definitionId,item.instanceId);World.SetEditing(true);}
                GUI.enabled=item!=null&&item.definitionId!="warehouse";
                if(ArtButton(new Rect(box.x+92,box.y+39,84,32),"旋转",button))ApplyPlacement(new PlacedFacility{instanceId=item.instanceId,definitionId=item.definitionId,x=item.x,z=item.z,rotation=(item.rotation+1)%4},out _,"rotate");
                GUI.enabled=item!=null&&item.definitionId!="warehouse"&&item.definitionId!="checkout";
                if(ArtButton(new Rect(box.x+184,box.y+39,84,32),"收纳",button))RemoveFacility(item.instanceId);
                GUI.enabled=true;
                if(item!=null&&ArtButton(new Rect(box.x,box.y+78,268,25),"查看物件详情",button))
                {var kind=catalog.Find(item.definitionId).kind;if(ShopEconomy.IsStaffFacility(kind))OpenStaffPost(item.instanceId);else if(kind==FacilityKind.Shelf)panelKey="shelf";else if(kind==FacilityKind.Warehouse)panelKey="warehouse";else if(kind==FacilityKind.Stage)OpenPerformance();}
            }
            if(ArtButton(new Rect(box.x,box.y+109,268,34),"完成装修",activeButton))EndRenovation();
        }
    }
}
