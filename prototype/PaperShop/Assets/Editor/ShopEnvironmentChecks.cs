using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using AnonTokyo.Shop;
public static class ShopEnvironmentChecks
{
    [Serializable] class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("ENVIRONMENT CHECK FAILED: "+label);results.Add(label);};
        var fresh=ShopProfiles.NewGame(catalog,false);var state=fresh.economy;
        check(state.stageOwned&&!state.mujicaStageOwned&&state.StoredFacilityCount(fresh.layout,"stage")==1,"New normal slot owns exactly one MyGO stage");
        state.cash=10000;long before=state.cash;
        check(!state.BuyEnvironment("stage_mujica",out _)&&state.cash==before,"Mujica stage requires level 3 without charging");
        state.shopLevel=3;state.cash=1499;
        check(!state.BuyEnvironment("stage_mujica",out _)&&state.cash==1499&&!state.mujicaStageOwned,"Insufficient funds cannot acquire Mujica stage");
        state.cash=2000;check(state.BuyEnvironment("stage_mujica",out _)&&state.cash==500&&state.mujicaStageOwned,"Mujica stage costs 1500");
        check(!state.BuyEnvironment("stage_mujica",out _)&&state.cash==500,"Repeated stage purchase is rejected without charging");
        check(state.BuyEnvironment("decor_flamingo",out _)&&state.cash==200&&state.DecorationOwned("decor_flamingo")==1,"Flamingo costs 300 into inventory");
        check(state.BuyEnvironment("decor_ottoman",out _)&&state.cash==0&&state.DecorationOwned("decor_ottoman")==1,"Ottoman costs 200 into inventory");
        check(!state.BuyEnvironment("decor_ottoman",out _)&&state.DecorationOwned("decor_ottoman")==1,"No negative coins or free decorations");
        state.testMode=true;check(state.BuyEnvironment("decor_ottoman",out _)&&state.cash==0&&state.DecorationOwned("decor_ottoman")==2,"Infinite mode buys inventory without coin deduction");
        state.testMode=false;
        check(state.Validate(catalog,fresh.layout,out var reason),"Ownership remains valid: "+reason);
        string path=Path.Combine(output,"environment-save.json");
        ShopSession.SaveToPath(fresh.layout,state,catalog,path);
        check(File.Exists(path),"Environment inventory saves");
        var loaded=ShopSession.LoadFromPath(catalog,path,out reason);
        check(loaded!=null&&loaded.economy.mujicaStageOwned&&loaded.economy.DecorationOwned("decor_ottoman")==2,"Paid stage and decor survive reload");
        var legacy=new ShopEconomy{stageOwned=false,environmentVersion=0};legacy.InitializeEnvironment();legacy.InitializeEnvironment();
        check(legacy.stageOwned&&!legacy.mujicaStageOwned&&legacy.DecorationOwned("decor_flamingo")==0,"Migration grants base stage once, no paid content");
        foreach(string id in new[]{"stage","stage_mujica","decor_flamingo","decor_ottoman"})check(catalog.Find(id)?.modelPrefab!=null,"Delivered environment prefab: "+id);
        check(catalog.entrancePrefab!=null&&catalog.entrancePrefab.transform.Find("Orientation/Delivered mesh/Left open leaf")!=null,"Entrance model opens outward and preserves doorway");
        foreach(var product in ShopProductSet.CurrentProducts(catalog))check(product.icon!=null&&product.icon.width==256,"Real product thumbnail: "+product.id);
        using(var hash=SHA256.Create())foreach(var staff in catalog.staff)foreach(string action in new[]{"perform","celebrate"})
        {
            string pathA=Path.GetFullPath("../../art/characters/performance_v001/delivery/png/"+staff.id+"/"+action+".png"),pathB="Assets/Resources/Characters/"+staff.id+"/"+action+".png";
            check(File.Exists(pathA)&&File.Exists(pathB)&&Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(pathA)))==Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(pathB))),"Exact delivered character action: "+staff.id+"/"+action);
        }
        File.WriteAllText(Path.Combine(output,"environment-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_ENVIRONMENT_CHECKS_OK "+results.Count);
    }
}
