using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopStaffExpansionChecks
{
    [Serializable] class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("STAFF EXPANSION CHECK FAILED: "+label);results.Add(label);};
        var layout=ShopModel.CreateDefault();
        layout.facilities.RemoveAll(f=>f.definitionId=="stage"||f.definitionId=="fitting"||f.definitionId=="head"||f.definitionId=="bottom");
        ShopSalesTestSetup.EnsureStaffPoints(layout,catalog);
        var e=ShopEconomy.CreateDefault(catalog,layout);e.cash=20000;e.shopOpen=false;
        e.InitializeFixtureStore(catalog,layout,out _);
        string before=JsonUtility.ToJson(e);
        check(!e.BuyExtraPost(FacilityKind.Restock,out _)&&!e.BuyExtraPost(FacilityKind.Reception,out _)&&before==JsonUtility.ToJson(e),"Both purchase gates reject at level one atomically");
        e.shopLevel=4;e.popularity=30;e.RefreshShopLevel();int money=e.cash;
        check(e.BuyExtraPost(FacilityKind.Restock,out _)&&e.cash==money-800&&e.OwnedPostLimit(FacilityKind.Restock)==2,"Level four buys exactly one extra restocking license for 800");
        before=JsonUtility.ToJson(e);
        check(!e.BuyExtraPost(FacilityKind.Restock,out _)&&!e.BuyExtraPost(FacilityKind.Reception,out _)&&before==JsonUtility.ToJson(e),"Duplicate restocker and early receptionist cannot charge");
        e.shopLevel=5;e.popularity=40;e.RefreshShopLevel();e.cash=999;
        check(!e.BuyExtraPost(FacilityKind.Reception,out _)&&e.cash==999,"Reception requires its full normal price");
        e.cash=20000;money=e.cash;
        check(e.BuyExtraPost(FacilityKind.Reception,out _)&&e.cash==money-1000,"Level five buys one extra reception license for 1000");
        before=JsonUtility.ToJson(e);
        check(!e.BuyExtraPost(FacilityKind.Reception,out _)&&!e.BuyExtraPost(FacilityKind.Checkout,out _)&&before==JsonUtility.ToJson(e),"No duplicate receptionist or extra checkout purchase");
        int oldCap=e.FixtureLimit;
        check(PlacePoint(layout,catalog,"restock","stock-second")&&PlacePoint(layout,catalog,"reception","guide-second")&&e.SyncStaffPosts(catalog,layout,out _),"Purchased second points can be placed");
        check(e.staffPosts.Count==5&&e.FixtureLimit==oldCap,"Five actual posts do not consume or raise display case capacity");
        var third=ShopModel.Clone(layout);
        check(!PlacePoint(third,catalog,"restock","stock-third")&&!PlacePoint(third,catalog,"reception","guide-third"),"A third point of either role is rejected");
        check(e.AssignStaff(catalog,"stock-second","rana",out _)&&e.AssignStaff(catalog,"guide-second","tomori",out _),"Two additional employees can be independently assigned");
        check(!e.AssignStaff(catalog,"stock-second","tomori",out _),"One person cannot bind two posts");
        e.SetCarryLimit("stock-second",4,out _);
        check(ShopEconomy.CarryLimitFor(e.Post("stock-second"))==6&&e.EffectiveCarryLimit==5,"Independent worker carrying settings");
        e.warehouseLevel=3;
        foreach(var stock in e.stocks){stock.contracted=true;stock.available=20;stock.purchased=20;}
        check(e.BeginRestockBatch(catalog,layout,e.EffectiveCarryLimit,null,out _)&&e.jobs.TrueForAll(j=>string.IsNullOrEmpty(j.postId)),"Primary worker reserves its cabinet share");
        var primaryShelf=e.job.shelfId;
        e.activeRestockPostId="stock-second";
        check(e.BeginRestockBatch(catalog,layout,4,null,out _)&&e.job.shelfId!=primaryShelf&&e.CurrentRestockJobs.Count>0,"Second worker independently reserves other deficits");
        check(e.Pickup(out _)&&e.Validate(catalog,layout,out _),"One worker can carry stock while the other still has warehouse reservations");
        int carrying=e.Carrying;
        string save=Path.Combine(output,"two-worker-inventory.json");ShopSession.SaveToPath(layout,e,catalog,save);
        var loaded=ShopSession.LoadFromPath(catalog,save,out string reason);
        check(loaded!=null&&loaded.economy.extraReceptionPurchased&&loaded.economy.extraRestockPurchased&&
            loaded.economy.Carrying==carrying&&loaded.economy.jobs.Exists(j=>j.postId=="stock-second"),"Post purchases and separate job ownership survive save/load: "+reason);
        e.activeRestockPostId="";
        check(e.Pickup(out _)&&e.Validate(catalog,layout,out _),"Both workers can carry real finite stock concurrently");
        check(e.CompleteRestock(catalog,layout,out _)&&e.Validate(catalog,layout,out _),"Primary shelving preserves second worker inventory");
        e.activeRestockPostId="stock-second";
        check(e.CompleteRestock(catalog,layout,out _)&&e.Validate(catalog,layout,out _),"Second shelving preserves inventory conservation");
        e.CancelRestock();e.activeRestockPostId="";
        check(e.jobs.Count==0&&e.Validate(catalog,layout,out _),"Cancelling all remaining trips returns all stock without duplication");
        var licensed=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(e));licensed.extraRestockPurchased=false;
        check(!licensed.Validate(catalog,layout,out _),"A save cannot keep a second post without its purchased license");
        check(ShopStaffProfiles.Find("uika").hospitality>ShopStaffProfiles.Find("soyo").hospitality&&
            ShopStaffProfiles.Find("mutsumi").restocking>ShopStaffProfiles.Find("rana").restocking&&
            ShopStaffProfiles.Find("umiri").restocking>ShopStaffProfiles.Find("rana").restocking&&
            ShopStaffProfiles.Find("nyamu").hospitality>ShopStaffProfiles.Find("anon").hospitality&&
            ShopStaffProfiles.Find("sakiko").cashier>ShopStaffProfiles.Find("taki").cashier,"Every contracted member has a specialist advantage over initial staff");
        e.ContractStaff("uika",out _);e.ContractStaff("mutsumi",out _);e.ContractStaff("umiri",out _);
        e.performanceVersion=1;e.performingBand="MyGO!!!!!";e.performanceRemaining=45;
        var bindings=e.PerformanceAssignments();
        check(e.UnlockedStaffCount==8&&bindings.Count==3&&new HashSet<string>(bindings.Values).Count==3,"Eight employees provide exactly three distinct real workers");
        check(!string.IsNullOrEmpty(e.AssignedStaffId(FacilityKind.Checkout))&&!string.IsNullOrEmpty(e.AssignedStaffId(FacilityKind.Reception))&&
            !string.IsNullOrEmpty(e.AssignedStaffId(FacilityKind.Restock))&&!bindings.ContainsKey("stock-second")&&!bindings.ContainsKey("guide-second"),"Eight-person priority covers all core roles before extras");
        e.ContractStaff("nyamu",out _);e.ContractStaff("sakiko",out _);bindings=e.PerformanceAssignments();
        check(bindings.Count==5&&new HashSet<string>(bindings.Values).Count==5,"Ten employees cover every post during the five-person performance");
        check(e.Post("stock-second").staffId=="rana"&&e.Post("guide-second").staffId=="tomori","Automatic performance assignment never rewrites permanent bindings");
        e.TickPerformance(45);
        check(e.EffectiveStaffId(e.Post("stock-second"))=="rana"&&e.EffectiveStaffId(e.Post("guide-second"))=="tomori","Permanent assignments restore on performance completion");
        foreach(string band in new[]{"MyGO!!!!!","Ave Mujica"})
        {
            check(ShopStageFormation.Members(band)[1]==(band=="MyGO!!!!!"?"tomori":"uika"),"Lead vocalist is front center: "+band);
            for(int rotation=0;rotation<4;rotation++)
            {
                var stage=new PlacedFacility{definitionId="stage",x=2,z=2,rotation=rotation};
                var d=catalog.Find("stage");Vector2 center=ShopStageFormation.GridPosition(stage,d,1);
                Vector2 a=ShopStageFormation.GridPosition(stage,d,0),b=ShopStageFormation.GridPosition(stage,d,2);
                check((center-(a+b)*.5f).sqrMagnitude<.0001f&&Mathf.Abs(Vector2.Distance(a,b)-3)<.001f,
                    "Three front performers span four-cell edge after rotation "+rotation+" for "+band);
            }
        }
        File.WriteAllText(Path.Combine(output,"staff-expansion-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_STAFF_EXPANSION_CHECKS_OK "+results.Count);
    }
    static bool PlacePoint(ShopLayout layout,ShopCatalog catalog,string definition,string id)
    {
        for(int z=0;z<layout.depth;z++)for(int x=0;x<layout.width;x++)
        {
            var draft=ShopModel.Clone(layout);
            if(!ShopModel.TryPlace(draft,catalog,new PlacedFacility{instanceId=id,definitionId=definition,x=x,z=z},out _)||
                !ShopModel.ValidateCheckoutAccess(draft,catalog,out _))continue;
            layout.facilities=draft.facilities;return true;
        }
        return false;
    }
}
