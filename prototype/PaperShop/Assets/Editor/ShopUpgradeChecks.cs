using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopUpgradeChecks
{
    [Serializable] sealed class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        catalog=ShopLedgerFixture.Catalog(catalog);
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException("UPGRADE CHECK FAILED: "+name);results.Add(name);};
        var map=ShopModel.CreateDefault();var e=ShopEconomy.CreateDefault(catalog,map);
        e.Contract(catalog,"top_tshirt",out _);e.Order(catalog,"top_tshirt",out _);e.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        e.BeginRestock(catalog,map,"top-1",out _);e.Pickup(out _);e.CompleteRestock(catalog,map,out _);
        e.TakeFromShelf(catalog,"top-1","old-price",out _);int cash=e.cash;
        check(e.UpgradeShelf(catalog,map,"top-1",out _) && e.cash==cash-300 && e.Shelf("top-1").level==2,"The first per-counter upgrade costs 300 once");
        check(e.ShelfCapacity(catalog,map,"top-1")==20 && e.ShelfPrice(catalog,"top-1")==10 && e.Shelf("top-1").quantity==9,"Shelf upgrade increases capacity without creating stock or changing sale price");
        check(e.baskets[0].unitPrice==10 && e.PayBasket("old-price",out _) && e.cash==cash-300+10,"An already picked basket keeps its pre-upgrade quoted price");
        e.TakeFromShelf(catalog,"top-1","new-price",out _);
        check(e.baskets[0].unitPrice==10 && e.PayBasket("new-price",out _),"A shelf upgrade cannot create a price bonus for new customers");
        check(e.UpgradeShelf(catalog,map,"top-1",out _) && e.ShelfCapacity(catalog,map,"top-1")==30 && e.ShelfPrice(catalog,"top-1")==10,"The second shelf upgrade yields capacity 30 while retaining the base price");
        string snapshot=JsonUtility.ToJson(e);
        check(!e.UpgradeShelf(catalog,map,"top-1",out _) && snapshot==JsonUtility.ToJson(e),"Maximum level rejects another charge atomically");
        check(e.Shelf("head-1").level==1 && e.ShelfPrice(catalog,"head-1")==10,"Upgrades apply to one counter, not every product or counter");
        var poor=ShopEconomy.CreateDefault(catalog,map);poor.cash=299;snapshot=JsonUtility.ToJson(poor);
        check(!poor.UpgradeShelf(catalog,map,"top-1",out _) && snapshot==JsonUtility.ToJson(poor),"Insufficient money leaves counter level and cash unchanged");
        poor.EnableTestResources();poor.cash=0;
        check(poor.UpgradeShelf(catalog,map,"top-1",out _) && poor.UpgradeShelf(catalog,map,"top-1",out _) && poor.cash==0,"Infinite-resource mode allows free counter upgrades");
        check(poor.BeginRestock(catalog,map,"top-1",out _) && poor.job.quantity==30,"Restock reservation uses the upgraded capacity");
        poor.Pickup(out _);poor.CompleteRestock(catalog,map,out _);
        check(poor.Shelf("top-1").quantity==30 && poor.Validate(catalog,map,out _),"Full upgraded test stock remains conserved");
        string path=Path.Combine(output,"upgraded.json");ShopSession.SaveToPath(map,e,catalog,path);
        var loaded=ShopSession.LoadFromPath(catalog,path,out _);
        check(loaded!=null && loaded.economy.Shelf("top-1").level==3 && loaded.economy.revenue==20,"Counter capacity levels and base-price sales survive combined save/load");
        var legacy=new ShopSession{layout=map,economy=ShopEconomy.CreateDefault(catalog,map)};
        string oldJson=JsonUtility.ToJson(legacy).Replace(",\"level\":1","");
        File.WriteAllText(Path.Combine(output,"legacy-level.json"),oldJson);
        var old=ShopSession.LoadFromPath(catalog,Path.Combine(output,"legacy-level.json"),out _);
        check(old!=null && old.economy.shelves.TrueForAll(s=>s.level==1),"Old snapshots without counter levels load as level one");
        var empty=ShopModel.CreateDefault();empty.facilities.RemoveAll(p=>p.definitionId!="warehouse" && p.definitionId!="checkout");
        for(int wall=0;wall<8;wall++)
        {
            check(ShopModel.TryPlace(empty,catalog,new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=8,z=4,wallPosition=wall+1},out _) &&
                ShopModel.WarehousePickup(empty)==new Vector2Int(7,wall),"Warehouse door accepts clear right-wall cell "+wall);
        }
        snapshot=JsonUtility.ToJson(empty);
        check(!ShopModel.TryPlace(empty,catalog,new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=8,z=4,wallPosition=9},out _) && snapshot==JsonUtility.ToJson(empty),"Out-of-range wall position is rejected atomically");
        check(!ShopModel.TryPlace(empty,catalog,new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=0,z=4,wallPosition=5},out _),"The warehouse cannot be moved to the road-facing left wall");
        var blocked=ShopModel.CreateDefault();blocked.facilities.Add(new PlacedFacility{instanceId="door-obstacle",definitionId="decor",x=7,z=6});snapshot=JsonUtility.ToJson(blocked);
        check(!ShopModel.TryPlace(blocked,catalog,new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=8,z=4,wallPosition=7},out _) && snapshot==JsonUtility.ToJson(blocked),"An obstructed door candidate leaves the old doorway in place");
        check(ShopModel.FindPath(map,catalog,ShopModel.RoadSpawnCell,ShopModel.EntranceCell).Count>0 &&
            ShopModel.FindPath(map,catalog,ShopModel.EntranceCell,ShopModel.RoadExitCell).Count>0,"Customers can enter and leave via the left-wall road");
        var movedEconomy=ShopEconomy.CreateDefault(catalog,empty);ShopSession.SaveToPath(empty,movedEconomy,catalog,Path.Combine(output,"moved-door.json"));
        var moved=ShopSession.LoadFromPath(catalog,Path.Combine(output,"moved-door.json"),out _);
        check(moved!=null && moved.layout.facilities.Find(p=>p.definitionId=="warehouse").wallPosition==8,"Warehouse wall position persists in combined saves");
        CheckStaff(catalog,output,check);
        File.WriteAllText(Path.Combine(output,"upgrade-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_UPGRADE_CHECKS_OK "+results.Count);
    }

    static void CheckStaff(ShopCatalog catalog,string output,Action<bool,string> check)
    {
        var setup=ShopSalesTestSetup.Prepare(ShopModel.CreateDefault(),ShopEconomy.CreateDefault(catalog,ShopModel.CreateDefault()),catalog);
        ShopLedgerFixture.Reset(catalog);
        var map=setup.layout;var e=setup.economy;e.EnableTestResources();e.testMode=false;e.cash=3000;
        check(e.AssignedStaffId(FacilityKind.Reception)=="soyo" && e.AssignedStaffId(FacilityKind.Restock)=="anon" &&
            e.AssignedStaffId(FacilityKind.Checkout)=="taki","Initial staff are assigned to three distinct posts");
        string snapshot=JsonUtility.ToJson(e);
        check(!e.AssignStaff(catalog,"reception-1","anon",out _) && snapshot==JsonUtility.ToJson(e),"A worker cannot occupy two posts and a rejected assignment changes nothing");
        check(e.AssignStaff(catalog,"reception-1","tomori",out _) && e.AssignedStaffId(FacilityKind.Reception)=="tomori","Any defined staff member can replace the guide");
        int money=e.cash;
        check(e.UpgradePost("reception-1",out _) && e.cash==money-300 && e.GuideQuantityBonus==21 &&
            e.CarryCapacity==5 && e.CheckoutPrice(catalog,"top_tshirt")==10,
            "Reception upgrade charges 300 and adds five percentage points to the extra-item chance without modifying carry capacity or checkout price");
        money=e.cash;
        check(e.UpgradePost("restock-1",out _) && e.cash==money-300 && e.CarryCapacity==7 && e.EffectiveCarryLimit==7,
            "Restock level two charges 300 and adds two to Anon's five-unit capacity");
        e.Post("restock-1").carryLimit=7;
        check(e.CarryCapacity==7 && e.EffectiveCarryLimit==7,
            "A legacy saved carry limit cannot reduce the new fixed attribute-and-level capacity");
        money=e.cash;
        check(e.AssignStaff(catalog,"restock-1","rana",out _) && e.CarryCapacity==8 && e.EffectiveCarryLimit==8 &&
            e.cash==money,"Changing to Rana recalculates the carrying attribute without changing post level or cash");
        check(e.AssignStaff(catalog,"restock-1","anon",out _) && e.CarryCapacity==7 && e.EffectiveCarryLimit==7,
            "Returning Anon restores her own capacity rather than retaining the previous worker's bonus");
        money=e.cash;
        check(e.UpgradePost("restock-1",out _) && e.cash==money-600 && e.CarryCapacity==9 && e.EffectiveCarryLimit==9,
            "Restock level three charges 600 and uses its full nine-unit capacity despite obsolete saved limits");
        money=e.cash;
        check(e.UpgradePost("checkout-1",out _) && e.cash==money-300 &&
            e.CheckoutPrice(catalog,"top_tshirt")==10 && e.ShelfPrice(catalog,"top-1")==10,
            "Checkout level two charges 300 and adds two percentage points to Taki's cashier-attribute bonus");
        money=e.cash;
        check(e.UpgradePost("checkout-1",out _) && e.cash==money-600 && e.CheckoutPrice(catalog,"top_tshirt")==10,
            "Checkout level three charges 600 and adds four percentage points to the cashier-attribute bonus");
        string save=Path.Combine(output,"staff-posts.json");ShopSession.SaveToPath(map,e,catalog,save);
        var loaded=ShopSession.LoadFromPath(catalog,save,out _);
        check(loaded!=null && loaded.economy.AssignedStaffId(FacilityKind.Reception)=="tomori" &&
            loaded.economy.CheckoutLevel==3 && loaded.economy.EffectiveCarryLimit==9 && loaded.economy.cash==e.cash,
            "Staff bindings, upgraded capacity and paid upgrade balance survive save/load while obsolete carry limits remain ineffective");
        var legacy=ShopEconomy.CreateDefault(catalog,map);legacy.EnableTestResources();
        legacy.Shelf("top-1").level=3;legacy.staffSystemVersion=0;legacy.staffPosts.Clear();
        check(legacy.SyncStaffPosts(catalog,map,out _) && legacy.CheckoutLevel==3 &&
            legacy.Shelf("top-1").level==3 && legacy.CheckoutPrice(catalog,"top_tshirt")==10 && legacy.ShelfPrice(catalog,"top-1")==10,
            "Legacy highest shelf level migrates once to checkout without losing capacity or stacking price bonuses");
        snapshot=JsonUtility.ToJson(legacy);
        check(legacy.SyncStaffPosts(catalog,map,out _) && snapshot==JsonUtility.ToJson(legacy),"Repeating post migration cannot duplicate upgrades or bindings");
    }
}
