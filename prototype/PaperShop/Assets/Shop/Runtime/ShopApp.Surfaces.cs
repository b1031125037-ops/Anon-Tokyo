using System;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        string paintStyle="";
        bool surfaceStoreFloor;
        public bool IsPaintingSurface=>!string.IsNullOrEmpty(paintStyle);
        public bool BeginSurfacePainting(string style)
        {
            if(!CanRenovate){LastMessage="请先关店，等顾客全部付款离开。";return false;}
            bool floor=Array.IndexOf(ShopSurfaces.FloorIds,style)>=0;
            if(!floor&&Array.IndexOf(ShopSurfaces.WallIds,style)<0)return false;
            if(Economy.MaterialCount(style)<1){LastMessage="装修库存不足，请先在商店购买材料。";return false;}
            renovationMode=true;CancelPlacement();Select(null);panelKey="";paintStyle=style;surfaceStoreFloor=floor;
            World.SetEditing(true);LastMessage="点击目标"+(floor?"地板格":"墙面列")+"应用；右键结束施工。";
            return true;
        }
        public void EndSurfacePainting()
        {
            paintStyle="";
            if(World!=null)World.SetEditing(renovationMode);
        }
        public bool ApplySurface(string kind,int x,int z,string style)
        {
            if(!CanRenovate){LastMessage="营业或结账期间不能更换装修材料。";return false;}
            var draft=ShopModel.Clone(Layout);
            var state=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            if(!state.UseRenovationMaterial(draft,kind,x,z,style,out string reason)||
               !ShopModel.Validate(draft,catalog,out reason)){LastMessage=reason;return false;}
            Layout=draft;Economy=state;World.Rebuild(Layout,catalog);World.SetWarehouseLevel(Economy.warehouseLevel);
            World.SetEditing(IsEditingLayout);dirty=true;LastMessage=reason;PlaySfx("place");return true;
        }
        void PaintSurfaceAt(Vector2 screen)
        {
            if(surfaceStoreFloor)
            {
                if(World.PickCell(screen,out var cell))ApplySurface("floor",cell.x,cell.y,paintStyle);
                else LastMessage="请点击店内的地板格。";
            }
            else if(World.PickWallColumn(screen,out string kind,out int column))
                ApplySurface(kind,kind=="north"?column:0,kind=="east"?column:0,paintStyle);
            else LastMessage="请点击完整墙面或门上方墙面；门保持原样。";
        }
        void DrawSurfaceStore(Rect box)
        {
            DrawStoreTabs(box);
            GUI.Label(new Rect(box.x+22,box.y+108,570,30),surfaceStoreFloor?"地板":"墙纸",heading);
            var ids=surfaceStoreFloor?ShopSurfaces.FloorIds:ShopSurfaces.WallIds;
            var names=surfaceStoreFloor?ShopSurfaces.FloorNames:ShopSurfaces.WallNames;
            for(int i=0;i<ids.Length;i++)
            {
                float y=box.y+168+i*48;Color old=GUI.color;
                GUI.color=ShopSurfaces.ColorFor(ids[i]);GUI.DrawTexture(new Rect(box.x+22,y,32,32),Texture2D.whiteTexture);GUI.color=old;
                GUI.Label(new Rect(box.x+66,y+5,310,26),names[i]+" · "+Economy.OwnedMaterialCount(Layout,ids[i])+"/"+Economy.MaterialLimit(Layout,ids[i]),body);
                int amount=Mathf.Min(10,Economy.MaterialRoom(Layout,ids[i]));
                GUI.enabled=amount>0;
                if(ArtButton(new Rect(box.x+390,y,204,33),amount>0?"领取 "+amount+" 份 · 免费":"持有已达上限",activeButton))BuyMaterial(ids[i],amount);
                GUI.enabled=true;
            }

        }
        void DrawSurfaceHint(float width,float height)
        {
            var ids=surfaceStoreFloor?ShopSurfaces.FloorIds:ShopSurfaces.WallIds;
            var names=surfaceStoreFloor?ShopSurfaces.FloorNames:ShopSurfaces.WallNames;
            int index=Array.IndexOf(ids,paintStyle);
            Rect box=new Rect(width*.5f-360,height-204,720,77);PanelBox(box);
            GUI.Label(new Rect(box.x+16,box.y+10,505,26),(index>=0?names[index]:paintStyle)+" · "+(surfaceStoreFloor?"点击一格地板":"点击一列墙面")+" · 右键结束",body);
            GUI.Label(new Rect(box.x+16,box.y+40,505,26),LastMessage,small);
            if(ArtButton(new Rect(box.x+543,box.y+17,158,40),"更换材料",button))OpenRenovationInventory(surfaceStoreFloor?3:4);
        }
    }
}
