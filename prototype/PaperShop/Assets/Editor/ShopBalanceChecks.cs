using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using AnonTokyo.Shop;
public static class ShopBalanceChecks
{
    [Serializable] class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("BALANCE CHECK FAILED: "+label);results.Add(label);};
        var session=ShopProfiles.NewGame(catalog,false);var e=session.economy;var map=session.layout;
        check(e.cash==800&&e.shelves.Count==1&&e.shelves[0].productId=="top_tshirt"&&!e.shopOpen,"New finite store starts with 800 coins, one empty top display and a closed door");
        check(e.DecorationValue(map)==0,"Free starting facilities have no decoration bonus");
        int[] costs={20,26,65,24,16,36,30},prices={36,46,110,44,30,64,54};
        for(int i=0;i<costs.Length;i++)
        {var p=catalog.products.Find(x=>x.id==ShopProductSet.ProductIds[i]);check(p.buyPrice==costs[i]&&p.sellPrice==prices[i]&&p.batchSize==5,"Live differentiated cost, price and five-unit order: "+p.id);}
        string before=JsonUtility.ToJson(e);
        check(!e.Contract(catalog,"shoes_canvas",out _)&&!e.Order(catalog,"shoes_canvas",out _)&&before==JsonUtility.ToJson(e),"Missing corresponding display blocks contract and purchase atomically");
        check(e.Contract(catalog,"top_tshirt",out _)&&e.Order(catalog,"top_tshirt",out _)&&e.cash==620&&e.InTransit==5,"First contract and order leave 620 working coins and reserve five units");
        e.Tick(4);check(e.Stock("top_tshirt").available==5,"First delivery arrives as five real units");
        check(e.EffectiveCarryLimit==5&&e.BeginRestockBatch(catalog,map,e.EffectiveCarryLimit,null,out _)&&e.Pickup(out _)&&e.CompleteRestock(catalog,map,out _),"Initial Anon carries the complete five-unit order in one trip");
        check(e.Shelf("top-1").quantity==5&&string.IsNullOrEmpty(e.NextRestock(catalog,map)),"Exactly half-full does not dispatch another trip");
        check(e.CheckoutPrice(catalog,"top_tshirt")==37&&Math.Abs(e.CheckoutSeconds-3.8625f)<.001,"Initial cashier adds one coin to a 36-coin item and takes about 3.86 seconds");
        check(e.AddToBasket(catalog,"top-1","first-sale",2,out _)&&e.QuoteBasketAtCheckout(catalog,"first-sale","checkout-1",out _)&&e.PayBasket("first-sale",out _)&&e.cash==694,"A two-item checkout settles 74 coins without a free decoration reward");
        check(e.Validate(catalog,map,out _),"New-economy first sale conserves all goods and cash");
        e.cash=10000;
        check(e.BuyFixture(catalog,map,"shoes",out _,out _)&&e.OwnsDisplayFor(catalog,"shoes_canvas")&&e.Contract(catalog,"shoes_canvas",out _)&&e.Order(catalog,"shoes_canvas",out _),"An owned stored display unlocks its own product without granting others");
        check(e.DecorationValue(map)==0,"Unplaced purchased furniture does not give a decoration reward");
        e.currentDecorationValue=10000;
        check(Math.Abs(e.DecorationRate-.05)<.000001,"Decoration reward has a hard five-percent cap");
        check(e.AddToBasket(catalog,"top-1","decor-sale",1,out _)&&e.QuoteBasketAtCheckout(catalog,"decor-sale","checkout-1",out _)&&e.PayBasket("decor-sale",out _)&&e.lastDecorationBonus==1&&Math.Abs(e.decorationRemainder-.85)<.000001,"Decoration yields one separate coin on a 37-coin sale and carries the fraction");
        string path=Path.Combine(output,"balance-roundtrip.json");ShopSession.SaveToPath(map,e,catalog,path);var loaded=ShopSession.LoadFromPath(catalog,path,out _);
        check(loaded!=null&&loaded.economy.decorationRevenue==1&&Math.Abs(loaded.economy.decorationRemainder-.85)<.000001&&loaded.economy.freeDecorationIds.Count==5,"Free gift flags and decoration remainder survive save/load");
        foreach(var staff in ShopStaffProfiles.All)
        {
            var post=e.Post("checkout-1");post.staffId=staff.id;
            for(int level=1;level<=3;level++){post.level=level;check(e.CheckoutPrice(catalog,"outfit_soyo")<=118&&e.CheckoutSeconds>=3.75&&e.CheckoutSeconds<=4.5,"Cashier bonus and speed remain bounded: "+staff.id+" level "+level);}
        }
        check(ShopBalance.EntryChance(100)>ShopBalance.EntryChance(0)&&ShopBalance.ShoppingChance(100)>ShopBalance.ShoppingChance(0),"Popularity independently raises entry and shopping intent");
        check(ShopBalance.PerformanceEntryChance(0)==.85&&Math.Abs(ShopBalance.PerformanceEntryChance(100)-.95)<.000001&&ShopBalance.PerformanceEntryChance(0)>ShopBalance.EntryChance(0)*2,"Performance strongly increases empty-store entry to 85–95 percent");
        check(ShopBalance.ExtraUnitChance(0)==0&&ShopBalance.ExtraUnitChance(35)==.35&&ShopBalance.ExtraKindChance(35)>.12,"Reception raises probabilities instead of guaranteeing extra units");
        check(ShopBalance.DecorationFor("decor_flamingo")==25&&ShopBalance.DecorationFor("decor_ottoman")==15&&ShopBalance.DecorationFor("top")==10&&ShopBalance.DecorationFor("stage")==0,"Paid furniture and free MyGO stage have the intended decoration values");
        var fresh=ShopProfiles.NewGame(catalog,false).economy;
        fresh.cash=0;
        check(fresh.IsFinanciallyStranded(catalog),"A fifty-coin task reward cannot indefinitely prevent bankruptcy when a full batch is unaffordable");
        fresh.cash=80;check(!fresh.IsFinanciallyStranded(catalog),"Ready reward plus reachable first-contract reward can finance an exact full batch");
        fresh.cash=79;check(fresh.IsFinanciallyStranded(catalog),"One coin short even after reachable rewards is insolvent");
        fresh.cash=0;
        foreach(var task in ShopEconomy.GrowthTasks)fresh.claimedGrowthTaskIds.Add(task.id);
        check(fresh.IsFinanciallyStranded(catalog),"No goods, no finance and no claimable reward is insolvent");
        fresh.cash=179;check(fresh.IsFinanciallyStranded(catalog),"First order must include unsigned-product contract cost");
        fresh.cash=180;check(!fresh.IsFinanciallyStranded(catalog),"Exactly affordable initial contract and batch prevents bankruptcy");
        fresh.Stock("top_tshirt").contracted=true;fresh.cash=99;check(fresh.IsFinanciallyStranded(catalog),"Signed product still requires full order cost");
        fresh.cash=100;check(!fresh.IsFinanciallyStranded(catalog),"Exact replenishment cost permits continued operation");
        fresh.cash=0;var fs=fresh.Stock("top_tshirt");fs.available=1;
        check(!fresh.IsFinanciallyStranded(catalog),"Warehouse stock can rescue the shop");fs.available=0;fs.returned=1;
        check(!fresh.IsFinanciallyStranded(catalog),"Returned stock can rescue the shop");fs.returned=0;
        fresh.shelves[0].quantity=1;check(!fresh.IsFinanciallyStranded(catalog),"Displayed stock prevents false bankruptcy");fresh.shelves[0].quantity=0;
        fresh.EnableTestResources();check(!fresh.IsFinanciallyStranded(catalog),"Unlimited resource mode never goes bankrupt");
        check(ShopApp.EmployeeVisitChance==.025f,"Off-duty special guest chance reduced to 2.5 percent");
        for(int level=1;level<=5;level++)
        {fresh.shopLevel=level;fresh.areaExpansionLevel=level-1;fresh.popularity=level*20;fresh.RecordSuccessfulVisit();
         check(fresh.PopularityCap==level*20&&fresh.popularity==level*20,"Level-specific popularity cap blocks extra sales: "+level);}
        fresh.shopLevel=1;fresh.areaExpansionLevel=0;fresh.popularity=95;fresh.MigrateManagementRules();
        check(fresh.popularity==20,"Existing low-level save clamps popularity without upgrading the shop");
        foreach(var post in fresh.staffPosts)
        {var preview=ShopApp.StaffEffectPreview(post,post.staffId);check(!string.IsNullOrEmpty(preview),"Every active role exposes numerical employee benefits: "+post.role);}
        foreach(int pop in new[]{0,20,40,60,80,100})
        {
            double first=ShopBalance.PurchaseChance(pop,false,0,0),second=ShopBalance.PurchaseChance(pop,false,0,1),third=ShopBalance.PurchaseChance(pop,false,0,2);
            check(first>second&&second>third&&Math.Abs(second/first-.65)<.000001,"Each additional purchased SKU reduces the next kind's probability: popularity "+pop);
            check(ShopBalance.PurchaseChance(pop,false,.15,1)>second,"Performance improves purchase chance without bypassing multi-kind decay: "+pop);
        }
        check(ShopApp.GuestCardPose(ShopApp.GuestPhase.WaitingGuide,false,0)=="think"&&ShopApp.GuestCardPose(ShopApp.GuestPhase.WaitingGuide,true,0)=="walk", "First reception uses thinking when stopped and walking while approaching");
        File.WriteAllText(Path.Combine(output,"balance-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_BALANCE_CHECKS_OK "+results.Count);
    }
}
