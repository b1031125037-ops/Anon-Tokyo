using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    [Serializable] public sealed class RenovationMaterialStock
    {
        public string styleId;
        public int quantity;
    }
    public sealed partial class ShopEconomy
    {
        public int renovationInventoryVersion;
        public bool stageOwned=true;
        public List<RenovationMaterialStock> renovationMaterials=new List<RenovationMaterialStock>();
        public void InitializeRenovationInventory(ShopLayout layout)
        {
            InitializeEnvironment();
            if(renovationInventoryVersion!=0)return;
            stageOwned=true;
            renovationInventoryVersion=1;
        }
        public int MaterialCount(string id)=>renovationMaterials.Find(s=>s.styleId==id)?.quantity??0;
        static bool IsMaterial(string id)=>Array.IndexOf(ShopSurfaces.WallIds,id)>=0||Array.IndexOf(ShopSurfaces.FloorIds,id)>=0;
        void AddMaterial(string id,int quantity)
        {
            var stock=renovationMaterials.Find(s=>s.styleId==id);
            if(stock==null){stock=new RenovationMaterialStock{styleId=id};renovationMaterials.Add(stock);}
            stock.quantity+=quantity;
        }
        public int MaterialLimit(ShopLayout layout,string id)=>Array.IndexOf(ShopSurfaces.FloorIds,id)>=0?layout.width*layout.depth:layout.width+layout.depth;
        public int OwnedMaterialCount(ShopLayout layout,string id)
        {
            int count=MaterialCount(id);
            if(Array.IndexOf(ShopSurfaces.FloorIds,id)>=0)
            {for(int x=0;x<layout.width;x++)for(int z=0;z<layout.depth;z++)if(ShopSurfaces.Get(layout,"floor",x,z)==id)count++;}
            else
            {for(int x=0;x<layout.width;x++)if(ShopSurfaces.Get(layout,"north",x,0)==id)count++;
             for(int z=0;z<layout.depth;z++)if(ShopSurfaces.Get(layout,"east",0,z)==id)count++;}
            return count;
        }
        public int MaterialRoom(ShopLayout layout,string id)=>Math.Max(0,MaterialLimit(layout,id)-OwnedMaterialCount(layout,id));
        public bool BuyRenovationMaterial(ShopLayout layout,string id,int quantity,out string reason)
        {
            if(!IsMaterial(id)||quantity<1||quantity>144||quantity>MaterialRoom(layout,id))
                return Fail("材料或数量无效，已达到当前店铺的持有上限。",out reason);
            // Keep the existing free material trial until the user sets prices.
            AddMaterial(id,quantity);reason="已放入装修库存。";return true;
        }
        public bool BuyStage(out string reason)
        {
            if(stageOwned)return Fail("已经拥有舞台。",out reason);
            if(ShopLevel<3)return Fail("舞台在店铺 Lv.3 解锁。",out reason);
            stageOwned=true;reason="舞台已放入装修库存。";return true;
        }
        public int OwnedFacilityCount(string id)=>id=="checkout"?1:id=="stage"?(stageOwned?1:0):
            id=="stage_mujica"?(mujicaStageOwned?1:0):ShopEnvironmentAssets.IsDecoration(id)?DecorationOwned(id):
            id=="restock"?OwnedPostLimit(FacilityKind.Restock):id=="reception"?OwnedPostLimit(FacilityKind.Reception):0;
        public int StoredFacilityCount(ShopLayout layout,string id)=>Math.Max(0,OwnedFacilityCount(id)-layout.facilities.FindAll(f=>f.definitionId==id).Count);
        public bool UseRenovationMaterial(ShopLayout layout,string kind,int x,int z,string style,out string reason)
        {
            if(!ShopSurfaces.ValidCell(layout,kind,x,z)||!ShopSurfaces.ValidStyle(kind,style))return Fail("请选择有效墙面或地板。",out reason);
            string old=ShopSurfaces.Get(layout,kind,x,z);
            if(old==style)return Fail("这里已经使用这种材料。",out reason);
            if(MaterialCount(style)<1)return Fail("装修库存不足，请先在商店购买材料。",out reason);
            if(MaterialCount(old)>=9999)return Fail("替换材料的收纳已满。",out reason);
            if(!ShopSurfaces.Apply(layout,kind,x,z,style,out reason))return false;
            AddMaterial(style,-1);AddMaterial(old,1);
            return true;
        }
        bool ValidateRenovationInventory(ShopLayout layout,out string reason)
        {
            if(renovationInventoryVersion<0||renovationInventoryVersion>1||renovationMaterials==null)return Fail("装修库存版本无效。",out reason);
            var ids=new HashSet<string>();
            foreach(var stock in renovationMaterials)
                if(stock==null||!IsMaterial(stock.styleId)||!ids.Add(stock.styleId)||stock.quantity<0||stock.quantity>9999||OwnedMaterialCount(layout,stock.styleId)>MaterialLimit(layout,stock.styleId))
                    return Fail("装修材料库存无效。",out reason);
            reason="";return true;
        }
    }
}
