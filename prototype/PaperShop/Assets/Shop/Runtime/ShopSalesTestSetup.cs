using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public static class ShopSalesTestSetup
    {
        public const int CurrentVersion = 3;

        public static ShopSession Prepare(ShopLayout source,ShopEconomy state,ShopCatalog catalog)
        {
            if(!ShopModel.Validate(source,catalog,out string reason) || !state.Validate(catalog,source,out reason))
                throw new InvalidOperationException(reason);
            var layout=ShopModel.Clone(source);
            var economy=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(state));
            economy.CancelRestock();economy.ReturnAllBaskets();
            // Every existing physical item is returned, never deleted with a counter.
            foreach(var shelf in economy.shelves)
            {economy.Stock(shelf.productId).returned+=shelf.quantity;shelf.quantity=0;}
            var keep=new HashSet<string>(StringComparer.Ordinal);
            foreach(string category in new[]{"top","shoes"})
            {
                var item=layout.facilities.Find(p=>p.instanceId==category+"-1" && p.definitionId==category)
                    ?? layout.facilities.Find(p=>p.definitionId==category);
                if(item!=null)keep.Add(item.instanceId);
            }
            var checkout=layout.facilities.Find(p=>p.instanceId=="checkout-1" && p.definitionId=="checkout")
                ?? layout.facilities.Find(p=>p.definitionId=="checkout");
            if(checkout!=null)keep.Add(checkout.instanceId);
            // The crowd test needs only two product displays, one checkout and its warehouse.
            if(state.salesTestVersion<2)
                layout.facilities.RemoveAll(p=>p.definitionId!="warehouse" && !keep.Contains(p.instanceId));
            foreach(string category in new[]{"top","shoes"})
            {
                var item=layout.facilities.Find(p=>p.definitionId==category);
                if(item==null)
                {
                    bool placed=false;
                    for(int z=2;z<layout.depth-1 && !placed;z++)
                    for(int x=1;x<layout.width-1 && !placed;x++)
                    {
                        var candidate=new PlacedFacility{instanceId=Guid.NewGuid().ToString("N"),definitionId=category,x=x,z=z};
                        if(ShopModel.TryPlace(layout,catalog,candidate,out _)){item=layout.facilities.Find(p=>p.instanceId==candidate.instanceId);placed=true;}
                    }
                    if(!placed)throw new InvalidOperationException("没有足够通路放置双柜台测试设施。");
                }
            }
            EnsureCheckoutAccess(layout,catalog,checkout);
            EnsureStaffPoints(layout,catalog);
            economy.staffPointsIntroduced=true;
            if(!economy.SyncShelves(catalog,layout,out reason))throw new InvalidOperationException(reason);
            if(!economy.SyncStaffPosts(catalog,layout,out reason))throw new InvalidOperationException(reason);
            economy.salesTestVersion=CurrentVersion;economy.shopOpen=true;economy.autoRestock=true;
            if(!ShopModel.ValidateCheckoutAccess(layout,catalog,out reason) || !economy.Validate(catalog,layout,out reason))
                throw new InvalidOperationException(reason);
            return new ShopSession{layout=layout,economy=economy};
        }

        public static void EnsureStaffPoints(ShopLayout layout,ShopCatalog catalog)
        {
            ShopCatalog.EnsureStaffDefinitions(catalog);
            foreach(string id in new[]{"reception","restock"})
            {
                if(layout.facilities.Exists(p=>p.definitionId==id))continue;
                var preferred=id=="reception"?new Vector2Int(1,5):new Vector2Int(6,4);
                var cells=new List<Vector2Int>();
                for(int z=0;z<layout.depth;z++)for(int x=0;x<layout.width;x++)cells.Add(new Vector2Int(x,z));
                cells.Sort((a,b)=>(Math.Abs(a.x-preferred.x)+Math.Abs(a.y-preferred.y)).CompareTo(Math.Abs(b.x-preferred.x)+Math.Abs(b.y-preferred.y)));
                bool placed=false;
                foreach(var cell in cells)
                {
                    if(layout.facilities.Exists(p=>
                    {
                        var d=catalog.Find(p.definitionId);
                        return d.kind==FacilityKind.Checkout?
                            ShopModel.PaymentCell(layout,catalog,p)==cell || ShopModel.CheckoutStaffCell(p,d)==cell:
                            (d.kind==FacilityKind.Shelf || d.kind==FacilityKind.Warehouse || ShopModel.IsStaffPoint(d.kind)) && ShopModel.ServiceCell(p,d)==cell;
                    }))continue;
                    var proposed=ShopModel.Clone(layout);
                    if(!ShopModel.TryPlace(proposed,catalog,new PlacedFacility{instanceId=id+"-1",definitionId=id,x=cell.x,z=cell.y},out _) ||
                        !ShopModel.ValidateCheckoutAccess(proposed,catalog,out _))continue;
                    layout.facilities=proposed.facilities;placed=true;break;
                }
                if(!placed)throw new InvalidOperationException("没有可达空格放置"+catalog.Find(id).title+"。");
            }
        }

        public static void EnsureCheckoutAccess(ShopLayout layout,ShopCatalog catalog,PlacedFacility original)
        {
            if(original!=null && ShopModel.ValidateCheckoutAccess(layout,catalog,out _))return;
            var candidates=new List<PlacedFacility>();
            int originX=original!=null?original.x:4,originZ=original!=null?original.z:6;
            int rotation=original!=null?original.rotation:3;
            for(int turn=0;turn<4;turn++)
            for(int z=1;z<layout.depth-1;z++)
            for(int x=1;x<layout.width-1;x++)
                candidates.Add(new PlacedFacility
                {
                    instanceId=original!=null?original.instanceId:"checkout-1",definitionId="checkout",
                    x=x,z=z,rotation=(rotation+turn)%4
                });
            candidates.Sort((a,b)=>
            {
                int aScore=(Math.Abs(a.x-originX)+Math.Abs(a.z-originZ))*4+(a.rotation==rotation?0:1);
                int bScore=(Math.Abs(b.x-originX)+Math.Abs(b.z-originZ))*4+(b.rotation==rotation?0:1);
                int order=aScore.CompareTo(bScore);
                if(order!=0)return order;
                order=b.x.CompareTo(a.x);
                if(order!=0)return order;
                order=b.z.CompareTo(a.z);
                return order!=0?order:a.rotation.CompareTo(b.rotation);
            });
            foreach(var candidate in candidates)
            {
                var proposed=ShopModel.Clone(layout);
                if(!ShopModel.TryPlace(proposed,catalog,candidate,out _) ||
                    !ShopModel.ValidateCheckoutAccess(proposed,catalog,out _) ||
                    !ShopModel.CanUseCheckoutFront(proposed,catalog,candidate,out _))continue;
                layout.facilities=proposed.facilities;
                return;
            }
            throw new InvalidOperationException("没有空间为收银台保留前方付款位与后方收银员位。");
        }
    }
}
