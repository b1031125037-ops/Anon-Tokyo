using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public static class ShopProductSet
    {
        public static readonly string[] Categories={"top","bottom","outfit","shoes","head","bag","accessory"};
        public static readonly string[] CategoryNames={"上装","下装","套装","鞋袜","头饰","箱包","配件"};
        public static readonly string[] ProductIds={"top_tshirt","bottom_skirt","outfit_soyo","shoes_canvas","head_ribbon","bag_basic","accessory_necklace"};
        public static readonly string[] ProductNames={"AT制服AW[上衣]","AT制服AW[下装]","爽世[日常套装]","AT制服AW[鞋袜]","AT制服AW[头饰]","基础手提包","基础项链"};
        public static bool IsDisplayCategory(string id)=>Array.IndexOf(Categories,id)>=0;
        public static bool IsCurrentProduct(string id)=>Array.IndexOf(ProductIds,id)>=0;
        public static List<ProductDefinition> CurrentProducts(ShopCatalog catalog)
        {
            var result=new List<ProductDefinition>();
            foreach(var id in ProductIds){var p=catalog.products.Find(x=>x.id==id);if(p!=null)result.Add(p);}
            return result;
        }
        public static void Ensure(ShopCatalog catalog)
        {
            for(int i=0;i<Categories.Length;i++)
            {
                string id=Categories[i];
                var f=catalog.Find(id);
                if(f==null)
                {
                    f=new FacilityDefinition{id=id,kind=FacilityKind.Shelf,width=id=="outfit"?2:1,depth=1,capacity=10,
                        serviceOffset=new Vector2Int(0,-1),color=id=="outfit"?new Color(.73f,.63f,.81f):
                        id=="bag"?new Color(.74f,.61f,.47f):new Color(.62f,.74f,.77f)};
                    catalog.facilities.Add(f);
                }
                f.title=id=="bag"?"包架":id=="accessory"?"配件架":CategoryNames[i]+"架";
                var p=catalog.products.Find(x=>x.id==ProductIds[i]);
                if(p==null && (id=="outfit"||id=="bag"||id=="accessory"))
                {
                    p=new ProductDefinition{id=ProductIds[i],category=id,buyPrice=5,sellPrice=10,batchSize=20};
                    catalog.products.Add(p);
                }
                if(p!=null){p.title=ProductNames[i];p.buyPrice=ShopBalance.Costs[i];p.sellPrice=ShopBalance.Prices[i];p.batchSize=5;}
            }
        }
        // Only newly introduced product ledgers may be absent from an old save.
        // Existing merchandise, contracts, deliveries and fractional offline income stay intact.
        public static bool MigrateStocks(ShopEconomy economy,out string reason)
        {
            if(economy.contentVersion<0||economy.contentVersion>1||economy.stocks==null)
            {reason="商品目录版本或库存列表无效。";return false;}
            if(economy.contentVersion==0)
            {
                foreach(var id in new[]{"outfit_soyo","bag_basic","accessory_necklace"})
                    if(economy.Stock(id)==null)economy.stocks.Add(new ProductStock{productId=id,contracted=economy.testMode});
                economy.contentVersion=1;
            }
            reason="";return true;
        }
    }
}
