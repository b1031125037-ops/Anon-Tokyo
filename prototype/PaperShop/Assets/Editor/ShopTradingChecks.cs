using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopTradingChecks
{
    [Serializable] sealed class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        catalog=ShopLedgerFixture.Catalog(catalog);
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new InvalidOperationException("TRADING CHECK FAILED: "+label);results.Add(label);};
        var map=ShopModel.CreateDefault();var e=ShopEconomy.CreateDefault(catalog,map);
        e.Contract(catalog,"top_tshirt",out _);e.Order(catalog,"top_tshirt",out _);e.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        e.BeginRestock(catalog,map,"top-1",out _);e.Pickup(out _);e.CompleteRestock(catalog,map,out _);
        int cash=e.cash;
        check(e.TakeFromShelf(catalog,"top-1","tx-1",out _) && e.Shelf("top-1").quantity==9 && e.cash==cash && e.transactions==0,"Browsing transfers one unit to a basket without income");
        check(e.Validate(catalog,map,out _),"Basket stock remains in the per-product conservation equation");
        check(!e.TakeFromShelf(catalog,"top-1","tx-1",out _) && e.Shelf("top-1").quantity==9,"Duplicate active transaction cannot remove a second unit");
        var copy=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(e));
        check(copy.Validate(catalog,map,out _) && copy.baskets.Count==1,"A pending basket survives Unity JSON serialization");
        copy.ReturnAllBaskets();
        check(copy.Validate(catalog,map,out _) && copy.baskets.Count==0 && copy.Stock("top_tshirt").available==11,"Interrupted customers return unpaid stock without losing merchandise");
        check(e.PayBasket("tx-1",out _) && e.cash==cash+10 && e.Stock("top_tshirt").sold==1 && e.revenue==10 && e.transactions==1,"Payment settles income and sold units together");
        check(!e.PayBasket("tx-1",out _) && e.cash==cash+10 && e.transactions==1,"Repeating payment cannot duplicate income");
        check(e.Validate(catalog,map,out _),"Sold inventory remains conserved after payment");
        e.TakeFromShelf(catalog,"top-1","tx-2",out _);
        e.cash=10000;for(int i=0;i<9;i++)e.Order(catalog,"top_tshirt",out _);e.Tick(ShopEconomy.DispatchSeconds + ShopEconomy.DeliverySeconds);
        e.Stock("top_tshirt").available+=10;e.Stock("top_tshirt").purchased+=10;
        e.ReturnBasket("tx-2");
        check(e.Validate(catalog,map,out _) && e.Stock("top_tshirt").available==200 && e.Stock("top_tshirt").returned==1,"Full-warehouse returns stay in a conserved buffer instead of disappearing");
        var test=ShopEconomy.CreateDefault(catalog,map);test.cash=0;test.EnableTestResources();
        check(test.stocks.TrueForAll(s=>s.contracted),"Test mode unlocks every product");
        check(test.BeginRestock(catalog,map,"top-1",out _) && test.job.quantity==10 && test.Stock("top_tshirt").purchased==10,"Infinite supply creates only the units actually reserved");
        test.Pickup(out _);test.CompleteRestock(catalog,map,out _);
        for(int i=0;i<11;i++)check(test.Order(catalog,"top_tshirt",out _),"Free test order "+i+" bypasses money and warehouse limits");
        check(test.cash==0 && test.InTransit==220 && test.Validate(catalog,map,out _),"Unlimited test resources do not require huge balances or break conservation");
        test.Tick(ShopEconomy.DispatchSeconds + 1f);
        check(test.InTransit==0 && test.Stock("top_tshirt").available==220,"Test delivery completes after five dispatch plus one transport game seconds");
        check(test.Expand(out _) && test.Expand(out _) && test.cash==0 && test.warehouseLevel==3,"Test warehouse upgrades are free within the designed level range");
        test.TakeFromShelf(catalog,"top-1","test-sale",out _);test.PayBasket("test-sale",out _);
        check(test.cash==0 && test.transactions==1 && test.revenue==10 && test.Validate(catalog,map,out _),"Infinite money still records real sales statistics");
        string path=Path.Combine(output,"trading-session.json");ShopSession.SaveToPath(map,test,catalog,path);
        var loaded=ShopSession.LoadFromPath(catalog,path,out _);
        check(loaded!=null && loaded.economy.testMode && loaded.economy.transactions==1 && loaded.economy.revenue==10,"Combined save preserves test mode, baskets, sold quantities and revenue");
        File.WriteAllText(Path.Combine(output,"trading-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_TRADING_CHECKS_OK "+results.Count);
    }
}
