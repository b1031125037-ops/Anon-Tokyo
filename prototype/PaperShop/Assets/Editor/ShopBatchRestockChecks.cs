using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopBatchRestockChecks
{
    [Serializable] sealed class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        catalog=ShopLedgerFixture.Catalog(catalog);
        Directory.CreateDirectory(output);
        var checks=new List<string>();
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException("BATCH CHECK FAILED: "+name);checks.Add(name);};
        var initial=ShopModel.CreateDefault();var initialState=ShopEconomy.CreateDefault(catalog,initial);
        var setup=ShopSalesTestSetup.Prepare(initial,initialState,catalog);
        ShopLedgerFixture.Reset(catalog);
        var map=setup.layout;var e=setup.economy;
        e.testMode=false;e.autoRestock=false;e.cash=2500;
        foreach(var stock in e.stocks){stock.available=0;stock.reserved=0;stock.returned=0;stock.purchased=0;stock.sold=0;}
        foreach(var shelf in e.shelves)
        {shelf.quantity=0;e.Contract(catalog,shelf.productId,out _);e.Order(catalog,shelf.productId,out _);}
        e.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        check(e.Validate(catalog,map,out _),"Finite mixed-product starting stock is conserved");
        string before=JsonUtility.ToJson(e);
        check(!e.BeginRestockBatch(catalog,map,0,null,out _) && before==JsonUtility.ToJson(e),"Invalid carry limit leaves all stock unchanged");
        check(e.BeginRestockBatch(catalog,map,10,null,out _) && e.jobs.Count==2 &&
            e.jobs.TrueForAll(j=>j.quantity==5) && e.jobs[0].productId!=e.jobs[1].productId,"Ten carrying slots are shared across two five-unit SKU lines");
        check(e.Validate(catalog,map,out _) && e.restockFollowUp,"Reservations preserve both ledgers and remember remaining shortages");
        var first=map.facilities.Find(p=>p.instanceId==e.jobs[0].shelfId);
        var second=map.facilities.Find(p=>p.instanceId==e.jobs[1].shelfId);
        var pickup=ShopModel.WarehousePickup(map);
        var a=ShopModel.ServiceCell(first,catalog.Find(first.definitionId));
        var b=ShopModel.ServiceCell(second,catalog.Find(second.definitionId));
        int route=ShopModel.FindPath(map,catalog,pickup,a).Count+ShopModel.FindPath(map,catalog,a,b).Count;
        int other=ShopModel.FindPath(map,catalog,pickup,b).Count+ShopModel.FindPath(map,catalog,b,a).Count;
        check(route<=other,"Two-stop itinerary follows a shortest reachable grid route");
        before=JsonUtility.ToJson(e);
        check(!e.BeginRestockBatch(catalog,map,10,null,out _) && before==JsonUtility.ToJson(e),"An active batch cannot be reserved twice");
        check(!e.Assign(catalog,map,e.jobs[1].shelfId,
            e.jobs[1].productId=="top_tshirt"?"top_shirt":"shoes_loafers",out _) && before==JsonUtility.ToJson(e),
            "Product reassignment is blocked for later batch targets as well as the first");
        check(e.Pickup(out _) && e.Carrying==10 && e.jobs.TrueForAll(j=>j.pickedUp) && e.Validate(catalog,map,out _),
            "One pickup transfers all SKU lines and preserves total warehouse commitment");
        before=JsonUtility.ToJson(e);
        check(!e.Pickup(out _) && before==JsonUtility.ToJson(e),"A second pickup is rejected without duplication");
        string savePath=Path.Combine(output,"mixed-carry.json");
        ShopSession.SaveToPath(map,e,catalog,savePath);
        var loaded=ShopSession.LoadFromPath(catalog,savePath,out _);
        check(loaded!=null && JsonUtility.ToJson(loaded.economy)==before,"Mixed carrying goods and follow-up intent survive combined storage");
        loaded.economy.CancelRestock();
        check(loaded.economy.Carrying==0 && loaded.economy.job==null && loaded.economy.Validate(catalog,map,out _),
            "Cancellation returns every SKU in a loaded mixed batch");
        int shelvesBefore=e.shelves[0].quantity+e.shelves[1].quantity;
        check(e.CompleteRestock(catalog,map,out _) && e.jobs.Count==1 && e.Carrying==5 &&
            e.shelves[0].quantity+e.shelves[1].quantity==shelvesBefore+5 && e.Validate(catalog,map,out _),
            "First shelving removes only its own line, retaining the second SKU");
        check(e.CompleteRestock(catalog,map,out _) && e.Carrying==0 && e.jobs.Count==0 &&
            e.shelves.TrueForAll(s=>s.quantity==5) && e.Validate(catalog,map,out _),"Second shelving completes the first mixed trip");
        check(!e.BeginRestockBatch(catalog,map,10,null,out _) && e.shelves.TrueForAll(s=>s.quantity==5),"Exactly half-full shelves do not begin a new trip");
        check(e.Validate(catalog,map,out _),"Threshold stopping preserves stock conservation");

        var malformed=JsonUtility.FromJson<ShopEconomy>(before);
        malformed.jobs.Add(malformed.jobs[0]);
        check(!malformed.Validate(catalog,map,out _),"Duplicate target lines cannot pass the inventory validator");
        malformed=JsonUtility.FromJson<ShopEconomy>(before);malformed.jobs[1].pickedUp=false;
        check(!malformed.Validate(catalog,map,out _),"Partially picked batches are rejected as corrupt");
        File.WriteAllText(Path.Combine(output,"batch-restock-verification.json"),
            JsonUtility.ToJson(new Report{passed=true,checks=checks.Count,results=checks.ToArray()},true));
        Debug.Log("SHOP_BATCH_RESTOCK_CHECKS_OK "+checks.Count);
    }
}
