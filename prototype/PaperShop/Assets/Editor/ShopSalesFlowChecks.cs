using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopSalesFlowChecks
{
    [Serializable] sealed class Report{public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException("SALES FLOW CHECK FAILED: "+name);results.Add(name);};
        var layout=ShopModel.CreateDefault();var economy=ShopEconomy.CreateDefault(catalog,layout);
        economy.EnableTestResources();economy.UpgradeShelf(catalog,layout,"top-1",out _);
        foreach(var id in new[]{"top-1","shoes-1","head-1","bottom-1"})
        {economy.BeginRestock(catalog,layout,id,out _);economy.Pickup(out _);economy.CompleteRestock(catalog,layout,out _);}
        economy.TakeFromShelf(catalog,"head-1","unfinished-customer",out _);
        var warehouse=layout.facilities.Find(p=>p.definitionId=="warehouse");warehouse.wallPosition=7;
        string originalLayout=JsonUtility.ToJson(layout),originalEconomy=JsonUtility.ToJson(economy);
        var prepared=ShopSalesTestSetup.Prepare(layout,economy,catalog);
        check(prepared.economy.shelves.Count==2 && prepared.layout.facilities.FindAll(p=>catalog.Find(p.definitionId).kind==FacilityKind.Shelf).Count==2,"Prepared layout contains exactly two selling counters");
        check(prepared.layout.facilities.Count==6 && prepared.layout.facilities.TrueForAll(p=>
            p.definitionId=="top" || p.definitionId=="shoes" || p.definitionId=="checkout" || p.definitionId=="warehouse" || p.definitionId=="reception" || p.definitionId=="restock"),
            "Test keeps only selling, checkout, warehouse and the two necessary staff points");
        check(prepared.layout.facilities.Exists(p=>p.definitionId=="top") && prepared.layout.facilities.Exists(p=>p.definitionId=="shoes"),"The two retained test categories are tops and shoes");
        check(prepared.economy.Shelf("top-1").level==2 && prepared.economy.shelves.TrueForAll(s=>s.quantity==0),"Retained counters preserve upgrade levels and start empty");
        check(prepared.economy.baskets.Count==0 && prepared.economy.job==null && prepared.economy.Validate(catalog,prepared.layout,out _),"Removed-counter goods and unpaid baskets are safely conserved");
        check(prepared.economy.Stock("head_ribbon").returned+prepared.economy.Stock("head_ribbon").available==10 &&
              prepared.economy.Stock("bottom_skirt").returned+prepared.economy.Stock("bottom_skirt").available==10,"Deleted counters return every physical unit to tracked warehouse stock");
        check(prepared.economy.shopOpen && prepared.economy.autoRestock && prepared.economy.salesTestVersion==ShopSalesTestSetup.CurrentVersion,"Full-flow test starts open with auto-restocking enabled and the current setup marker");
        check(ShopModel.WarehousePickup(prepared.layout)==new Vector2Int(7,6),"Test setup preserves the chosen warehouse wall position");
        var checkout=prepared.layout.facilities.Find(p=>p.definitionId=="checkout");
        var checkoutDefinition=catalog.Find("checkout");
        var customerCell=ShopModel.PaymentCell(prepared.layout,catalog,checkout);
        var staffCell=ShopModel.CheckoutStaffCell(checkout,checkoutDefinition);
        check(ShopModel.ValidateCheckoutAccess(prepared.layout,catalog,out _) && customerCell!=staffCell &&
            ShopModel.FindPath(prepared.layout,catalog,ShopModel.EntranceCell,customerCell).Count>0 &&
            ShopModel.FindPath(prepared.layout,catalog,ShopModel.EntranceCell,staffCell).Count>0,
            "Checkout has separate reachable customer payment and rear cashier cells");
        check(checkout.instanceId=="checkout-1" &&
            JsonUtility.ToJson(prepared.layout.facilities.Find(p=>p.instanceId=="top-1"))==JsonUtility.ToJson(layout.facilities.Find(p=>p.instanceId=="top-1")) &&
            JsonUtility.ToJson(prepared.layout.facilities.Find(p=>p.instanceId=="shoes-1"))==JsonUtility.ToJson(layout.facilities.Find(p=>p.instanceId=="shoes-1")),
            "Checkout identity and both selling-counter positions survive test cleanup");
        check(checkout.x==3 && checkout.z==7 && checkout.rotation==3 &&
            ShopModel.CheckoutCustomerCell(checkout,checkoutDefinition)==new Vector2Int(2,7) &&
            customerCell==ShopModel.CheckoutCustomerCell(checkout,checkoutDefinition) &&
            ShopModel.CanUseCheckoutFront(prepared.layout,catalog,checkout,out _),
            "The legal wall-adjacent checkout keeps its rotation and pays exactly at its entrance-facing front");
        check(JsonUtility.ToJson(layout)==originalLayout && JsonUtility.ToJson(economy)==originalEconomy,"Preparing the test never mutates the source progress");
        var again=ShopSalesTestSetup.Prepare(prepared.layout,prepared.economy,catalog);
        check(JsonUtility.ToJson(again)==JsonUtility.ToJson(prepared),"Repeating setup before stocking cannot duplicate returned goods");
        prepared.economy.BeginRestock(catalog,prepared.layout,"top-1",out _);prepared.economy.Pickup(out _);
        var interrupted=ShopSalesTestSetup.Prepare(prepared.layout,prepared.economy,catalog);
        check(interrupted.economy.Validate(catalog,interrupted.layout,out _) && interrupted.economy.job==null,"Restarting during a carrying task returns the reservation without loss");
        string path=Path.Combine(output,"two-counter-session.json");ShopSession.SaveToPath(prepared.layout,prepared.economy,catalog,path);
        var loaded=ShopSession.LoadFromPath(catalog,path,out _);
        check(loaded!=null && loaded.economy.salesTestVersion==ShopSalesTestSetup.CurrentVersion && loaded.economy.shelves.Count==2 && loaded.economy.Shelf("top-1").level==2,"Two-counter setup marker, stock and upgrade levels survive combined save/load");
        var frontOffsets=new[]{new Vector2Int(0,1),new Vector2Int(1,0),new Vector2Int(0,-1),new Vector2Int(-1,0)};
        for(int turn=0;turn<4;turn++)
        {
            var placement=new PlacedFacility{instanceId="rotated-checkout",definitionId="checkout",x=3,z=3,rotation=turn};
            check(ShopModel.CheckoutCustomerCell(placement,checkoutDefinition)==new Vector2Int(3,3)+frontOffsets[turn] &&
                ShopModel.CheckoutStaffCell(placement,checkoutDefinition)==new Vector2Int(3,3)-frontOffsets[turn],
                "Checkout front and rear rotate to opposite cells at quarter-turn "+turn);
        }
        var interiorCheckout=new PlacedFacility{instanceId="checkout-1",definitionId="checkout",x=3,z=3,rotation=0};
        var blocked=new ShopLayout{facilities=new List<PlacedFacility>{
            new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=8,z=4},
            interiorCheckout,new PlacedFacility{instanceId="front-blocker",definitionId="decor",x=3,z=4}}};
        check(ShopModel.ValidateCheckoutAccess(blocked,catalog,out _) &&
            !ShopModel.CanUseCheckoutFront(blocked,catalog,interiorCheckout,out _) &&
            ShopModel.PaymentCell(blocked,catalog,interiorCheckout)==new Vector2Int(3,4),
            "Blocked payment space keeps placement valid but disables checkout without a side or remote fallback");
        var blockedStaff=ShopModel.Clone(prepared.layout);
        blockedStaff.facilities.Add(new PlacedFacility{instanceId="staff-blocker",definitionId="decor",x=staffCell.x,z=staffCell.y});
        check(!ShopModel.ValidateCheckoutAccess(blockedStaff,catalog,out _),"A blocked rear employee cell still rejects checkout placement");
        var extra=ShopModel.Clone(layout);
        extra.facilities.Add(new PlacedFacility{instanceId="extra-decor",definitionId="decor",x=0,z=0});
        var extraEconomy=ShopEconomy.CreateDefault(catalog,extra);
        extraEconomy.salesTestVersion=1;
        var cleaned=ShopSalesTestSetup.Prepare(extra,extraEconomy,catalog);
        check(cleaned.layout.facilities.Count==6 && cleaned.economy.salesTestVersion==ShopSalesTestSetup.CurrentVersion &&
            cleaned.economy.cash==extraEconomy.cash && cleaned.economy.warehouseLevel==extraEconomy.warehouseLevel,
            "A version-one test profile migrates to the clean crowd layout without changing money or warehouse level");
        var versionTwo=ShopModel.Clone(prepared.layout);
        versionTwo.facilities.RemoveAll(p=>p.definitionId=="reception" || p.definitionId=="restock");
        versionTwo.facilities.Add(new PlacedFacility{instanceId="user-kept-decoration",definitionId="decor",x=0,z=0});
        var v2state=ShopEconomy.CreateDefault(catalog,versionTwo);v2state.salesTestVersion=2;
        var migrated=ShopSalesTestSetup.Prepare(versionTwo,v2state,catalog);
        check(migrated.layout.facilities.Exists(p=>p.instanceId=="user-kept-decoration") &&
            migrated.layout.facilities.Exists(p=>p.definitionId=="reception") &&
            migrated.layout.facilities.Exists(p=>p.definitionId=="restock"),"Version two migration adds staff points without clearing the user's existing layout");
        foreach(string id in new[]{"reception","restock"})
        {
            var point=migrated.layout.facilities.Find(p=>p.definitionId==id);
            check(ShopModel.Size(catalog.Find(id),0)==Vector2Int.one && !ShopModel.IsBlocked(migrated.layout,catalog,new Vector2Int(point.x,point.z)) &&
                ShopModel.FindPath(migrated.layout,catalog,ShopModel.EntranceCell,new Vector2Int(point.x,point.z)).Count>0,
                "The "+id+" marker occupies one furniture cell while allowing staff to stand on it");
        }
        File.WriteAllText(Path.Combine(output,"sales-flow-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_SALES_FLOW_CHECKS_OK "+results.Count);
    }
}
