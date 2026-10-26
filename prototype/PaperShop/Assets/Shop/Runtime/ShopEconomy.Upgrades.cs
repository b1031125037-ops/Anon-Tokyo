using System;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        public const int MaxShelfLevel=3;
        public static int ShelfUpgradeCost(int level)=>level==1?300:level==2?600:0;
        public static int ShelfCapacity(FacilityDefinition definition,ShelfStock shelf)=>definition.capacity*shelf.level;
        public int ShelfCapacity(ShopCatalog catalog,ShopLayout layout,string id)
        { var shelf=Shelf(id);var definition=ShelfDefinition(catalog,layout,id);return shelf==null||definition==null?0:ShelfCapacity(definition,shelf); }
        public int ShelfPrice(ShopCatalog catalog,string id)
        {
            var shelf=Shelf(id);var product=shelf==null?null:Product(catalog,shelf.productId);
            return product==null?0:product.sellPrice;
        }
        public bool UpgradeShelf(ShopCatalog catalog,ShopLayout layout,string id,out string reason)
        {
            var shelf=Shelf(id);var definition=ShelfDefinition(catalog,layout,id);
            if(shelf==null||definition==null)return Fail("销售柜台不存在。",out reason);
            if(shelf.level<1||shelf.level>=MaxShelfLevel)return Fail("销售柜台已达最高等级。",out reason);
            int cost=ShelfUpgradeCost(shelf.level);
            if(!testMode&&cash<cost)return Fail("升级需要 "+cost+" 金币，余额不足。",out reason);
            if((long)definition.capacity*(shelf.level+1)>int.MaxValue)return Fail("容量超出支持范围。",out reason);
            if(!testMode)cash-=cost;
            shelf.level++;
            reason="柜台已升级至 Lv."+shelf.level+" · 容量 "+ShelfCapacity(definition,shelf)+"；售价由收银台等级决定";
            return true;
        }
    }
}
