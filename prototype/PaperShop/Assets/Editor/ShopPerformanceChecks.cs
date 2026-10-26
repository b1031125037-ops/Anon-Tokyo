using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopPerformanceChecks
{
    [Serializable] class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("PERFORMANCE CHECK FAILED: "+label);results.Add(label);};
        var layout=ShopModel.CreateDefault();
        layout.facilities.RemoveAll(f=>f.definitionId=="stage"||f.definitionId=="fitting"||f.definitionId=="head"||f.definitionId=="bottom");
        ShopSalesTestSetup.EnsureStaffPoints(layout,catalog);
        var e=ShopEconomy.CreateDefault(catalog,layout);
        e.InitializeFixtureStore(catalog,layout,out _);e.cash=20000;e.shopOpen=false;
        check(catalog.staff.Count==10&&Array.TrueForAll(ShopStaffProfiles.All,s=>s.cashier>=1&&s.cashier<=100&&s.restocking>=1&&s.restocking<=100&&s.hospitality>=1&&s.hospitality<=100),"Ten distinct profiles contain all three bounded abilities");
        check(e.IsStaffUnlocked("tomori")&&e.IsStaffUnlocked("rana")&&!e.IsStaffUnlocked("uika"),"Legacy MyGO availability is retained while Mujica starts locked");
        int money=e.cash;
        check(!e.ContractStaff("umiri",out _)&&e.cash==money,"Contract level gate rejects atomically");
        check(!e.AssignStaff(catalog,e.PostFor(FacilityKind.Restock).instanceId,"umiri",out _),"Locked members cannot bypass signing through post assignment");
        e.shopLevel=2;e.popularity=10;e.RefreshShopLevel();e.cash=499;
        check(!e.ContractStaff("umiri",out _)&&e.cash==499,"Insufficient contract funds leave membership unchanged");
        e.cash=20000;money=e.cash;
        check(e.ContractStaff("umiri",out _)&&e.cash==money-500&&e.IsStaffUnlocked("umiri"),"Normal contract charges exact cost and unlocks once");
        money=e.cash;
        check(!e.ContractStaff("umiri",out _)&&e.cash==money,"Duplicate signing cannot charge again");
        check(e.AssignStaff(catalog,e.PostFor(FacilityKind.Restock).instanceId,"umiri",out _)&&e.StaffAttribute(FacilityKind.Restock)==98,"New signed member can work with its actual restocking score");
        check(!e.AssignStaff(catalog,e.PostFor(FacilityKind.Checkout).instanceId,"umiri",out _),"Signed members still cannot occupy two work posts");
        int[] levels={10,20,30,40}, widths={10,12,12,12}, depths={8,8,10,12}, caps={10,14,21,28},costs={600,1200,1800,2400};
        check(e.FixtureLimit==10,"Level two permits two each of the first three categories and one each of the remaining four");
        var oldCheckout=layout.facilities.Find(f=>f.definitionId=="checkout");int oldX=oldCheckout.x,oldZ=oldCheckout.z;
        for(int i=0;i<4;i++)
        {
            e.popularity=levels[i];e.RefreshShopLevel();money=e.cash;
            check(e.TryExpandShop(catalog,layout,out _)&&e.cash==money-costs[i]&&layout.width==widths[i]&&layout.depth==depths[i]&&e.ShopLevel==i+2&&e.FixtureLimit==caps[i],"Area, level, cost and category-based ownership limits for expansion "+(i+1));
            check(ShopModel.FindPath(layout,catalog,ShopModel.RoadSpawnFor(layout),ShopModel.WarehousePickup(layout)).Count>0&&ShopModel.ValidateCheckoutAccess(layout,catalog,out _),"Road, entrance and checkout remain reachable after expansion "+(i+1));
        }
        check(layout.facilities.Find(f=>f.definitionId=="checkout").x==oldX+4&&layout.facilities.Find(f=>f.definitionId=="checkout").z==oldZ+4,"Depth expansion translates original arrangement together, retaining rear-wall relationship");
        string before=JsonUtility.ToJson(layout)+JsonUtility.ToJson(e);
        check(!e.TryExpandShop(catalog,layout,out _)&&before==JsonUtility.ToJson(layout)+JsonUtility.ToJson(e),"Fifth expansion rejects at 144 cells without altering assets");
        e.popularity=0;e.RefreshShopLevel();
        check(e.ShopLevel==5&&e.FixtureLimit==28&&e.NextShopLevelPopularity==0,"Five-level peak is permanent and permits four display cases in each of seven categories");
        for(int z=0;z<12;z++)
        {
            var candidate=ShopModel.Clone(layout);
            bool ok=ShopModel.TryPlace(candidate,catalog,new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=12,z=4,wallPosition=z+1},out _);
            check(ok&&ShopModel.WarehousePickup(candidate)==new Vector2Int(11,z),"Warehouse supports right wall cell "+z+" in the twelve-deep room");
        }
        for(int i=0;i<25;i++)e.RecordFeverSale();
        check(e.feverMask==31,"Five missing-letter guarantees complete all five independent slots within 25 paid visits");
        e.feverMask=(1<<1);
        for(int i=0;i<25;i++)e.RecordFeverSale();
        check((e.feverMask&(1<<3))!=0&&e.feverMask==31,"The second E has an independent collection slot");
        e.shopOpen=true;
        before=JsonUtility.ToJson(e);
        check(!e.StartPerformance(catalog,layout,"MyGO!!!!!",2,out _)&&before==JsonUtility.ToJson(e),"Missing stage does not consume letters");
        check(ShopModel.TryPlace(layout,catalog,new PlacedFacility{instanceId="stage-test",definitionId="stage",x=4,z=0},out _),"A stage fits inside the newly expanded foreground");
        before=JsonUtility.ToJson(e);
        check(!e.StartPerformance(catalog,layout,"Ave Mujica",2,out _)&&before==JsonUtility.ToJson(e),"Incomplete signed band cannot consume FEVER slots");
        check(!e.StartPerformance(catalog,layout,"MyGO!!!!!",2,out _)&&e.feverMask==31,"Six unlocked employees cannot start or consume letters");
        e.ContractStaff("mutsumi",out _);
        check(!e.StartPerformance(catalog,layout,"MyGO!!!!!",2,out _)&&e.feverMask==31,"Seven employees still cannot start");
        e.ContractStaff("uika",out _);
        check(e.StartPerformance(catalog,layout,"MyGO!!!!!",2,out _)&&e.IsPerforming&&e.feverMask==0&&e.performanceRemaining==ShopEconomy.PerformanceLength("MyGO!!!!!")&&e.performancePreviousSpeed==2,"Successful performance consumes slots once and records remaining time and prior speed");
        check(e.IsPerformingStaff("taki")&&!e.IsPerformingStaff("umiri")&&e.AssignedStaffId(FacilityKind.Checkout)=="umiri"&&e.AssignedStaffId(FacilityKind.Restock)=="mutsumi"&&e.AssignedStaffId(FacilityKind.Reception)=="uika","Eight employees cover three real work posts with the non-performing band");
        before=JsonUtility.ToJson(e);e.RecordFeverSale();e.TickPerformance(0);e.TickPerformance(float.NaN);
        check(before==JsonUtility.ToJson(e),"Paused or invalid time and in-performance purchases cannot advance or refill activity");
        e.TickPerformance(13);
        var path=Path.Combine(output,"active-performance.json");ShopSession.SaveToPath(layout,e,catalog,path);
        var saved=ShopSession.LoadFromPath(catalog,path,out string reason);
        check(saved!=null&&Mathf.Abs(saved.economy.performanceRemaining-(ShopEconomy.PerformanceLength("MyGO!!!!!")-13))<.001f&&saved.economy.IsStaffUnlocked("umiri")&&saved.layout.depth==12&&saved.economy.FixtureLimit==28,"Active performance, contracts and full-area state survive a real save/load");
        int completed=e.performancesCompleted;
        check(e.TickPerformance(ShopEconomy.PerformanceLength("MyGO!!!!!")-13)&&e.performancesCompleted==completed+1&&e.performanceCooldown==120&&!e.IsPerforming,"Completion grants exactly one event count and starts cooldown");
        int popularity=e.popularity;e.TickPerformance(0);
        check(e.popularity==popularity&&e.performancesCompleted==completed+1,"Completion cannot be awarded twice by another idle tick");
        before=JsonUtility.ToJson(e);e.RecordFeverSale();
        check(before==JsonUtility.ToJson(e)&&!e.StartPerformance(catalog,layout,"MyGO!!!!!",1,out _),"Cooldown blocks letter collection and restart");
        e.TickPerformance(120);
        check(e.performanceCooldown==0,"Cooldown expires through simulation time");
        e.testMode=true;money=e.cash;
        foreach(var s in ShopStaffProfiles.All)if(!e.IsStaffUnlocked(s.id))e.ContractStaff(s.id,out _);
        check(e.signedStaffIds.Count==5&&e.cash==money,"Test resources waive fees but require five individual signed records");
        e.FillFeverForTesting();
        check(e.StartPerformance(catalog,layout,"Ave Mujica",1,out _)&&e.IsPerformingStaff("umiri")&&!e.IsPerformingStaff("taki"),"The complete Mujica band can perform with correct temporary post substitution");
        check(e.Validate(catalog,layout,out reason),"Full progression passes inventory and save consistency: "+reason);
        var capacity=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(e));
        capacity.testMode=false;capacity.cash=20000;
        int bought=0,initialOwned=capacity.OwnedFixtureCount,initialCash=capacity.cash;
        foreach(string category in ShopProductSet.Categories)
        {
            while(capacity.OwnedCategoryCount(catalog,category)<4)
            {
                if(!capacity.BuyFixture(catalog,layout,category,out _,out reason))throw new Exception(reason);
                bought++;
            }
            before=JsonUtility.ToJson(capacity);
            check(capacity.OwnedCategoryCount(catalog,category)==4&&
                !capacity.BuyFixture(catalog,layout,category,out _,out _)&&before==JsonUtility.ToJson(capacity),
                "Level-five ownership, including stored cases, rejects a fifth "+category+" case atomically");
        }
        check(capacity.FixtureLimit==28&&capacity.OwnedFixtureCount==28&&bought==28-initialOwned&&
            capacity.cash==initialCash-bought*ShopEconomy.BasicFixtureCost&&capacity.Validate(catalog,layout,out reason),
            "Filling all seven categories reaches twenty-eight cases and deducts exactly one price per new empty case");
        check(capacity.stocks.TrueForAll(stock=>
            JsonUtility.ToJson(stock)==JsonUtility.ToJson(e.Stock(stock.productId))),
            "Buying all remaining display cases cannot create, consume or move any product inventory");
        string activeJson=File.ReadAllText(path);
        string missingTime=System.Text.RegularExpressions.Regex.Replace(activeJson,@"""performanceRemaining""\s*:\s*[\d.]+\s*,","");
        string broken=Path.Combine(output,"missing-performance-time.json");File.WriteAllText(broken,missingTime);
        check(ShopSession.LoadFromPath(catalog,broken,out _)==null,"Missing versioned event time cannot silently finish or reset an active saved performance");
        string missingRoster=System.Text.RegularExpressions.Regex.Replace(activeJson,@"""signedStaffIds""\s*:\s*\[[^\]]*\]\s*,","");
        broken=Path.Combine(output,"missing-signed-roster.json");File.WriteAllText(broken,missingRoster);
        check(ShopSession.LoadFromPath(catalog,broken,out _)==null,"Missing versioned signature ledger cannot silently revoke or regrant signed characters");
        var freshLayout=ShopModel.CreateDefault();
        var fresh=ShopEconomy.CreateDefault(catalog,freshLayout);
        string legacy=JsonUtility.ToJson(new ShopSession{layout=freshLayout,economy=fresh});
        foreach(var field in new[]{"rosterVersion","signedStaffIds","performanceVersion","feverMask","feverMisses","performancesCompleted",
            "feverRandomState","performanceRemaining","performanceCooldown","performingBand","performancePreviousSpeed"})
        {
            var match=System.Text.RegularExpressions.Regex.Match(legacy,"\""+field+"\":(?:\\[\\]|\"\"|-?[\\d.]+)");
            if(match.Success)legacy=legacy.Replace(","+match.Value,"").Replace(match.Value+",","");
        }
        string legacyPath=Path.Combine(output,"pre-performance.json");File.WriteAllText(legacyPath,legacy);
        var migrated=ShopSession.LoadFromPath(catalog,legacyPath,out reason);
        check(migrated!=null&&migrated.economy.IsStaffUnlocked("rana")&&!migrated.economy.IsStaffUnlocked("umiri")&&
            !migrated.economy.IsPerforming&&migrated.economy.feverMask==0,"Legacy saves without any roster or performance fields retain MyGO and initialize a clean event state: "+reason);
        var invalid=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(e));invalid.signedStaffIds.Add("umiri");
        check(!invalid.Validate(catalog,layout,out _),"Duplicate contract records are rejected");
        invalid=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(e));invalid.performanceRemaining=float.PositiveInfinity;
        check(!invalid.Validate(catalog,layout,out _),"Invalid serialized performance time is rejected");
        File.WriteAllText(Path.Combine(output,"performance-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_PERFORMANCE_CHECKS_OK "+results.Count);
    }
}
