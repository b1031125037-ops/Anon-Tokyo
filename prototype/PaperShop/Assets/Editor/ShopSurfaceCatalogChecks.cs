using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopSurfaceCatalogChecks
{
    [Serializable] class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("SURFACE CATALOG CHECK FAILED: "+label);results.Add(label);};
        var layout=ShopModel.CreateDefault();
        layout.facilities.RemoveAll(f=>f.definitionId=="stage"||f.definitionId=="fitting"||f.definitionId=="head"||f.definitionId=="bottom");
        ShopSalesTestSetup.EnsureStaffPoints(layout,catalog);
        var economy=ShopEconomy.CreateDefault(catalog,layout);economy.shopOpen=false;economy.cash=20000;
        economy.InitializeFixtureStore(catalog,layout,out _);
        check(ShopProductSet.CurrentProducts(catalog).Count==7,"Exactly seven currently offered SKUs");
        foreach(var category in ShopProductSet.Categories)
            check(ShopProductSet.CurrentProducts(catalog).FindAll(p=>p.category==category).Count==1&&catalog.Find(category).kind==FacilityKind.Shelf,"One active SKU for "+category);
        foreach(var category in new[]{"outfit","bag","accessory"})
        {
            int cash=economy.cash,owned=economy.OwnedFixtureCount;
            check(economy.BuyFixture(catalog,layout,category,out var stored,out _)&&
                economy.cash==cash-ShopEconomy.BasicFixtureCost&&economy.OwnedFixtureCount==owned+1&&
                economy.StoredFixture(stored)!=null,"Purchase and own "+category);
        }
        check(economy.Validate(catalog,layout,out _),"New products and purchased fixture inventory conserve stock");
        string before=JsonUtility.ToJson(layout);
        check(!ShopSurfaces.Apply(layout,"floor",8,0,"floor_wood",out _)&&
            !ShopSurfaces.Apply(layout,"east",0,0,"floor_wood",out _)&&before==JsonUtility.ToJson(layout),"Invalid cells and cross-type materials reject atomically");
        check(ShopSurfaces.Apply(layout,"floor",3,2,"floor_wood",out _)&&
            ShopSurfaces.Apply(layout,"north",4,0,"wall_sage",out _)&&
            ShopSurfaces.Apply(layout,"east",0,5,"wall_cream",out _),"Paint independent tile and both wall columns");
        check(ShopSurfaces.Get(layout,"floor",3,3)=="floor_default"&&ShopSurfaces.Get(layout,"north",5,0)=="wall_default","Adjacent tiles and columns remain unchanged");
        var clone=ShopModel.Clone(layout);ShopSurfaces.Apply(clone,"floor",3,2,"floor_tile",out _);
        check(ShopSurfaces.Get(layout,"floor",3,2)=="floor_wood","Layout clone does not alias finishes");
        ShopSurfaces.Apply(clone,"floor",3,2,"floor_default",out _);
        check(clone.finishes.Count==2&&ShopSurfaces.Get(clone,"floor",3,2)=="floor_default","Restoring original finish removes its override");
        var corrupt=ShopModel.Clone(layout);corrupt.finishes.Add(ShopSurfaces.Copy(layout.finishes)[0]);
        check(!ShopModel.Validate(corrupt,catalog,out _),"Duplicate finish records cannot load");
        corrupt=ShopModel.Clone(layout);corrupt.surfaceVersion=99;
        check(!ShopModel.Validate(corrupt,catalog,out _),"Unknown finish schema rejects");
        string path=Path.Combine(output,"surface-save.json");ShopSession.SaveToPath(layout,economy,catalog,path);
        var loaded=ShopSession.LoadFromPath(catalog,path,out string reason);
        check(loaded!=null&&loaded.layout.finishes.Count==3&&ShopSurfaces.Get(loaded.layout,"east",0,5)=="wall_cream","Combined save/load retains finishes: "+reason);
        var expanded=layout;
        for(int i=0;i<4;i++)
        {
            check(ShopModel.TryExpandLayout(catalog,expanded,out var next,out _),"Expand painted map step "+(i+1));
            expanded=next;
        }
        check(ShopSurfaces.Get(expanded,"floor",7,6)=="floor_wood"&&
            ShopSurfaces.Get(expanded,"east",0,9)=="wall_cream"&&
            ShopSurfaces.Get(expanded,"north",8,0)=="wall_sage","Open-side growth shifts floor and east columns while preserving north wall columns");
        check(ShopSurfaces.Get(expanded,"floor",11,0)=="floor_default","New expansion area receives original finish");
        var old=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));old.contentVersion=0;
        old.stocks.RemoveAll(s=>s.productId=="outfit_soyo"||s.productId=="bag_basic"||s.productId=="accessory_necklace");
        int previous=old.stocks.Count;
        check(ShopProductSet.MigrateStocks(old,out _)&&old.stocks.Count==previous+3&&old.Validate(catalog,layout,out _),"Legacy stock ledgers gain exactly three empty SKU records");
        check(ShopProductSet.MigrateStocks(old,out _)&&old.stocks.Count==previous+3,"SKU migration is idempotent");
        old.stocks.RemoveAt(0);
        check(!old.Validate(catalog,layout,out _),"Missing pre-existing inventory is not silently replaced");
        var inventory=ShopProfiles.NewGame(catalog,false);var stockState=inventory.economy;var room=inventory.layout;
        int coins=stockState.cash;
        check(stockState.MaterialLimit(room,"floor_tile")==64&&stockState.MaterialLimit(room,"wall_sage")==16,"Each floor style is capped by floor cells and each wallpaper by wall columns");
        check(!stockState.BuyRenovationMaterial(room,"floor_wood",1,out _)&&stockState.MaterialCount("floor_wood")==0,"Already installed material counts toward its per-style limit");
        check(stockState.BuyRenovationMaterial(room,"floor_tile",64,out _)&&stockState.cash==coins,"Current default materials remain free and enter counted renovation inventory");
        string snap=JsonUtility.ToJson(stockState),mapSnap=JsonUtility.ToJson(room);
        check(!stockState.BuyRenovationMaterial(room,"floor_tile",1,out _)&&snap==JsonUtility.ToJson(stockState),"Cannot claim beyond the room-size limit");
        check(!stockState.UseRenovationMaterial(room,"floor",-1,0,"floor_tile",out _)&&snap==JsonUtility.ToJson(stockState)&&mapSnap==JsonUtility.ToJson(room),"Invalid painting never consumes inventory or changes the room");
        check(stockState.UseRenovationMaterial(room,"floor",0,0,"floor_tile",out _)&&stockState.MaterialCount("floor_tile")==63&&stockState.MaterialCount("floor_wood")==1,"Painting consumes one new tile and returns one old tile");
        snap=JsonUtility.ToJson(stockState);
        check(!stockState.UseRenovationMaterial(room,"floor",0,0,"floor_tile",out _)&&snap==JsonUtility.ToJson(stockState),"Painting the same finish twice does not consume or duplicate inventory");
        check(!stockState.UseRenovationMaterial(room,"north",0,0,"wall_sage",out _),"Unowned material cannot be painted");
        check(stockState.BuyRenovationMaterial(room,"wall_sage",16,out _)&&!stockState.BuyRenovationMaterial(room,"wall_sage",1,out _),"Wallpaper stock respects the two-wall column count");
        string inventoryPath=Path.Combine(output,"renovation-inventory.json");ShopSession.SaveToPath(room,stockState,catalog,inventoryPath);
        var inventoryLoaded=ShopSession.LoadFromPath(catalog,inventoryPath,out _);
        check(inventoryLoaded!=null&&JsonUtility.ToJson(inventoryLoaded)==JsonUtility.ToJson(inventory),"Surface stock, applied finishes and ownership survive exact save/load");
        stockState.popularity=10;stockState.RefreshShopLevel();
        check((stockState.cash=600)==600&&stockState.TryExpandShop(catalog,room,out _)&&stockState.MaterialLimit(room,"floor_tile")==80&&stockState.MaterialRoom(room,"floor_tile")==16&&stockState.MaterialRoom(room,"wall_sage")==2,"Expansion increases material allowances by the actual new floor cells and wall columns");
        check(!stockState.BuyStage(out _)&&stockState.stageOwned,"MyGO stage is initially owned; no duplicate purchase");
        stockState.shopLevel=3;
        check(!stockState.BuyStage(out _)&&stockState.StoredFacilityCount(room,"stage")==1,"Default stage remains one unit in facility inventory");
        File.WriteAllText(Path.Combine(output,"surface-catalog-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_SURFACE_CATALOG_CHECKS_OK "+results.Count);
    }
}
