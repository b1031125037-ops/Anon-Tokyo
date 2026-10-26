using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopLogicChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public string[] results; }
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool,string> check = (condition,name) => { if(!condition) throw new InvalidOperationException("MAP CHECK FAILED: "+name); results.Add(name); };
        var map = ShopModel.CreateDefault();
        check(ShopModel.Validate(map,catalog,out string reason),"Initial facilities, service cells and entrances are valid: "+reason);
        var before = JsonUtility.ToJson(map);
        check(!ShopModel.TryPlace(map,catalog,new PlacedFacility{instanceId="collision",definitionId="top",x=3,z=5},out reason) && before==JsonUtility.ToJson(map),"Overlapping furniture is rejected without mutating the layout");
        check(!ShopModel.TryPlace(map,catalog,new PlacedFacility{instanceId="door",definitionId="decor",x=2,z=7},out reason),"Entrance cannot be occupied");
        check(!ShopModel.TryPlace(map,catalog,new PlacedFacility{instanceId="outside",definitionId="bottom",x=7,z=7},out reason),"Out-of-bounds footprint is rejected");
        var target = map.facilities.Find(p=>p.definitionId=="top");
        var service = ShopModel.ServiceCell(target,catalog.Find("top"));
        check(!ShopModel.TryPlace(map,catalog,new PlacedFacility{instanceId="service",definitionId="decor",x=service.x,z=service.y},out reason),"A shelf service cell cannot be blocked");
        var rotated = new PlacedFacility{instanceId="rotation",definitionId="bottom",x=1,z=4,rotation=1};
        check(ShopModel.Size(catalog.Find("bottom"),1)==new Vector2Int(1,2) && ShopModel.ServiceCell(rotated,catalog.Find("bottom"))==new Vector2Int(0,5),"A 90-degree rotation transforms footprint and service point together");
        check(ShopModel.TryPlace(map,catalog,rotated,out reason),"A valid rotated shelf can be placed");
        var wall = ShopModel.Clone(map);
        wall.facilities.RemoveAll(p=>p.definitionId!="warehouse" && p.definitionId!="checkout");
        wall.facilities.Add(new PlacedFacility{instanceId="isolated-shelf",definitionId="top",x=3,z=2});
        for(int x=0;x<wall.width;x++) wall.facilities.Add(new PlacedFacility{instanceId="wall-"+x,definitionId="decor",x=x,z=4});
        check(!ShopModel.Validate(wall,catalog,out reason),"A complete entrance barrier fails reachability validation");
        var route = ShopModel.FindPath(map,catalog,ShopModel.EntranceCell,service);
        bool continuous = route.Count>0;
        for(int i=0;i<route.Count;i++)
        {
            continuous &= !ShopModel.IsBlocked(map,catalog,route[i]);
            if(i>0) continuous &= Mathf.Abs(route[i].x-route[i-1].x)+Mathf.Abs(route[i].y-route[i-1].y)==1;
        }
        check(continuous,"Path reaches a service point through adjacent, unoccupied cells");
        var warehouse = map.facilities.Find(p=>p.definitionId=="warehouse");
        check(warehouse.x==8 && warehouse.z==4 && warehouse.rotation==0 &&
            ShopModel.Size(catalog.Find("warehouse"),0)==new Vector2Int(3,4) &&
            ShopModel.ServiceCell(warehouse,catalog.Find("warehouse"))==ShopModel.WarehouseServiceCell,
            "East-wall warehouse has a fixed 3x4 exterior footprint and indoor pickup cell (7,7)");
        var warehouseRoute=ShopModel.FindPath(map,catalog,ShopModel.SpawnCell,ShopModel.WarehouseServiceCell);
        bool indoorPickupRoute=warehouseRoute.Count>0;
        foreach(var cell in warehouseRoute) indoorPickupRoute &= ShopModel.IsShopCell(map,cell);
        check(indoorPickupRoute,"Staff reach the warehouse pickup point entirely from inside the shop");
        check(!ShopModel.IsInside(map,new Vector2Int(8,5)) && ShopModel.FindPath(map,catalog,ShopModel.SpawnCell,new Vector2Int(8,5)).Count==0,
            "The warehouse shell is not a walkable indoor extension");
        before=JsonUtility.ToJson(map);
        check(!ShopModel.TryPlace(map,catalog,new PlacedFacility{instanceId="blocked-pickup",definitionId="decor",x=7,z=7},out reason) &&
            reason.Contains("(7,7)") && before==JsonUtility.ToJson(map),"Furniture cannot occupy the warehouse pickup point; rejection is atomic and explained");
        check(!ShopModel.TryPlace(map,catalog,new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=8,z=4,rotation=1},out reason),
            "Warehouse geometry cannot be rotated to increase capacity");
        var isolatedPickup=ShopModel.Clone(map);
        isolatedPickup.facilities.Add(new PlacedFacility{instanceId="pickup-wall-a",definitionId="decor",x=6,z=7});
        isolatedPickup.facilities.Add(new PlacedFacility{instanceId="pickup-wall-c",definitionId="decor",x=7,z=6});
        check(!ShopModel.Validate(isolatedPickup,catalog,out reason) && reason.Contains("(7,7)"),
            "The warehouse pickup point must remain reachable, not merely unoccupied");
        var courtyardRoute=ShopModel.FindPath(map,catalog,ShopModel.SpawnCell,new Vector2Int(2,8));
        check(courtyardRoute.Count>0 && courtyardRoute.Contains(new Vector2Int(2,7)) && courtyardRoute.Contains(new Vector2Int(2,8)),
            "The original left entrance and exterior courtyard remain accessible");
        check(ShopModel.IsInside(map,new Vector2Int(-1,8)) && ShopModel.IsInside(map,new Vector2Int(8,11)) &&
            !ShopModel.IsInside(map,new Vector2Int(-2,8)) && !ShopModel.IsInside(map,new Vector2Int(0,12)),
            "Walkable exterior matches the ten-by-four road beside the left wall");
        var wallDetour=ShopModel.FindPath(map,catalog,new Vector2Int(0,7),new Vector2Int(0,8));
        check(wallDetour.Count>2,"Characters cannot walk directly through a wall beside the doorway");
        check(!ShopModel.TryRemove(map,catalog,map.facilities.Find(p=>p.definitionId=="warehouse").instanceId,out reason),"Starter warehouse cannot be removed");
        var testPath = Path.Combine(output,"layout-check-"+Guid.NewGuid().ToString("N")+".json");
        ShopModel.SaveToPath(map,testPath);
        var loaded = ShopModel.LoadFromPath(catalog,testPath,out string message);
        check(loaded!=null && JsonUtility.ToJson(map)==JsonUtility.ToJson(loaded),"Save/load round trip preserves IDs, cells and rotations");
        var backupExpected = JsonUtility.ToJson(map);
        check(ShopModel.TryPlace(map,catalog,new PlacedFacility{instanceId="saved-decor",definitionId="decor",x=0,z=0},out reason),"Second revision accepts a decoration");
        ShopModel.SaveToPath(map,testPath);
        File.WriteAllText(testPath,"{truncated");
        loaded = ShopModel.LoadFromPath(catalog,testPath,out message);
        check(loaded!=null && JsonUtility.ToJson(loaded)==backupExpected,"A truncated primary file recovers the previous valid backup");
        File.WriteAllText(testPath,"broken-primary");
        File.WriteAllText(testPath+".bak","broken-backup");
        loaded = ShopModel.LoadFromPath(catalog,testPath,out message);
        check(loaded==null && File.ReadAllText(testPath)=="broken-primary" && File.ReadAllText(testPath+".bak")=="broken-backup","Two invalid files are reported without overwriting either file");
        CheckMigration(catalog,output,check);
        check(ShopProductSet.CurrentProducts(catalog).Count==7 && catalog.staff.Count==10,"Seven currently offered products and ten staff records exist");
        File.WriteAllText(Path.Combine(output,"logic-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_LOGIC_CHECKS_OK "+results.Count);
    }

    static void CheckMigration(ShopCatalog catalog,string output,Action<bool,string> check)
    {
        var legacy=ShopModel.CreateDefault();
        legacy.version=2;
        var oldWarehouse=legacy.facilities.Find(p=>p.definitionId=="warehouse");
        oldWarehouse.x=0;oldWarehouse.z=9;oldWarehouse.rotation=0;
        legacy.facilities.Find(p=>p.definitionId=="top").rotation=1;
        legacy.facilities.Add(new PlacedFacility{instanceId="user-kept-decoration",definitionId="decor",x=0,z=0,rotation=2});
        string oldJson=JsonUtility.ToJson(legacy,true);
        string directory=Path.Combine(output,"migration-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string oldPath=Path.Combine(directory,"shop-layout-v2.json");
        string newPath=Path.Combine(directory,"shop-layout-v3.json");
        File.WriteAllText(oldPath,oldJson);
        var migrated=ShopModel.LoadWithMigration(catalog,newPath,oldPath,out string message);
        bool preserved=migrated!=null && migrated.version==3 && migrated.facilities.Count==legacy.facilities.Count;
        if(migrated!=null)
        {
            foreach(var item in legacy.facilities)
            {
                if(item.definitionId=="warehouse")continue;
                var kept=migrated.facilities.Find(p=>p.instanceId==item.instanceId);
                preserved &= kept!=null && JsonUtility.ToJson(item)==JsonUtility.ToJson(kept);
            }
            var moved=migrated.facilities.Find(p=>p.instanceId=="warehouse-1");
            preserved &= moved!=null && moved.x==8 && moved.z==4 && moved.rotation==0;
        }
        check(preserved,"V2 migration moves only the warehouse and preserves every user furniture ID, position and rotation");
        check(File.ReadAllText(oldPath)==oldJson && !File.Exists(oldPath+".bak") && File.Exists(newPath) && File.Exists(newPath+".bak"),
            "Migration writes an independent v3 primary/backup and never rewrites or creates a v2 backup");

        string conflictOld=Path.Combine(directory,"conflict-v2.json");
        string conflictNew=Path.Combine(directory,"conflict-v3.json");
        var conflict=ShopModel.Clone(legacy);
        conflict.facilities.Add(new PlacedFacility{instanceId="user-existing-at-door",definitionId="decor",x=7,z=7});
        string conflictJson=JsonUtility.ToJson(conflict,true);
        File.WriteAllText(conflictOld,conflictJson);
        File.WriteAllText(conflictOld+".bak",oldJson);
        var rejected=ShopModel.LoadWithMigration(catalog,conflictNew,conflictOld,out message);
        check(rejected==null && message.Contains("(7,7)") && !File.Exists(conflictNew) && !File.Exists(conflictNew+".bak"),
            "Migration reports a blocked new pickup point without silently falling back to older furniture or creating v3 files");
        check(File.ReadAllText(conflictOld)==conflictJson && File.ReadAllText(conflictOld+".bak")==oldJson,
            "A migration conflict preserves both v2 source files byte-for-byte");

        string damagedOld=Path.Combine(directory,"damaged-v2.json");
        string recoveredNew=Path.Combine(directory,"recovered-v3.json");
        File.WriteAllText(damagedOld,"{truncated");
        File.WriteAllText(damagedOld+".bak",oldJson);
        check(ShopModel.LoadWithMigration(catalog,recoveredNew,damagedOld,out message)!=null &&
            File.ReadAllText(damagedOld)=="{truncated" && File.ReadAllText(damagedOld+".bak")==oldJson,
            "An unreadable v2 primary can migrate its valid backup without repairing either legacy file");
    }
}
