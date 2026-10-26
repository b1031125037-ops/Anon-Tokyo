using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopExpansionChecks
{
    [Serializable] private sealed class Report
    {
        public bool passed;
        public int checks;
        public string[] results;
    }

    public static void Run(ShopCatalog catalog, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (condition, name) =>
        {
            if (!condition) throw new InvalidOperationException("EXPANSION CHECK FAILED: " + name);
            results.Add(name);
        };
        var original = ShopModel.CreateDefault();
        original.facilities.Find(item => item.definitionId == "warehouse").wallPosition = 6;
        original.facilities.Find(item => item.definitionId == "top").rotation = 1;
        check(ShopModel.ValidateCheckoutAccess(original, catalog, out _),
            "The pre-expansion layout is valid with a moved warehouse door and rotated shelf");
        string originalJson = JsonUtility.ToJson(original);
        check(ShopModel.TryExpandLayout(catalog, original, out var first, out _) &&
            first.width == 10 && first.depth == 8 && originalJson == JsonUtility.ToJson(original),
            "First expansion adds two columns at the open left edge without mutating its source");
        bool preserved = first.facilities.Count == original.facilities.Count;
        foreach (var item in original.facilities)
        {
            if (item.definitionId == "warehouse") continue;
            var kept = first.facilities.Find(candidate => candidate.instanceId == item.instanceId);
            preserved &= kept != null && kept.x==item.x+2 && kept.z==item.z && kept.definitionId==item.definitionId && kept.rotation==item.rotation &&
                !ReferenceEquals(item, kept);
        }
        check(preserved, "Indoor facilities retain their IDs, wall-relative positions and rotations as independent copies");
        var warehouse = first.facilities.Find(item => item.definitionId == "warehouse");
        check(warehouse.x == 10 && warehouse.z == 4 && warehouse.wallPosition == 6 &&
            ShopModel.ServiceCell(warehouse, catalog.Find("warehouse")) == new Vector2Int(9, 5) &&
            ShopModel.WarehousePickup(first) == new Vector2Int(9, 5),
            "The warehouse follows the new right wall while retaining its chosen wall position");
        check(ShopModel.FindPath(first, catalog, ShopModel.EntranceFor(first), ShopModel.WarehousePickup(first)).Count > 0,
            "The relocated warehouse pickup remains reachable from the preserved entrance relative to the wall corner");
        check(!ShopModel.IsBlocked(first, catalog, new Vector2Int(0, 4)) &&
            ShopModel.FindPath(first, catalog, new Vector2Int(0, 3), new Vector2Int(0, 4)).Count == 2,
            "The new open-side columns are free and traversable");
        check(ShopModel.TryPlace(first, catalog, new PlacedFacility
            { instanceId = "expanded-counter", definitionId = "top", x = 0, z = 2 }, out _),
            "A shelf and its service point can use the newly purchased columns");
        check(ShopModel.TryPlace(first, catalog, new PlacedFacility
            { instanceId = "open-edge-space", definitionId = "decor", x = 0, z = 4 }, out _),
            "Furniture can use the new open-side area");
        string firstJson = JsonUtility.ToJson(first);
        check(!ShopModel.TryPlace(first, catalog, new PlacedFacility
            { instanceId = "door-blocker", definitionId = "decor", x = 4, z = 7 }, out _) &&
            firstJson == JsonUtility.ToJson(first),
            "Expansion keeps both original doorway cells protected atomically");
        check(!ShopModel.TryPlace(first, catalog, new PlacedFacility
            { instanceId = "pickup-blocker", definitionId = "decor", x = 9, z = 5 }, out _) &&
            firstJson == JsonUtility.ToJson(first),
            "Furniture cannot block the warehouse's new pickup cell");
        check(!ShopModel.TryPlace(first, catalog, new PlacedFacility
            { instanceId = "outside-counter", definitionId = "bottom", x = 9, z = 2 }, out _) &&
            firstJson == JsonUtility.ToJson(first),
            "A wide shelf crossing the expanded right boundary is rejected without side effects");
        check(!ShopModel.TryPlace(first, catalog, new PlacedFacility
            { instanceId = "warehouse-1", definitionId = "warehouse", x = 8, z = 4, wallPosition = 6 }, out _) &&
            firstJson == JsonUtility.ToJson(first),
            "The old warehouse anchor cannot create an indoor doorway after expansion");
        check(ShopModel.IsInside(first, new Vector2Int(10, 11)) &&
            !ShopModel.IsInside(first, new Vector2Int(11, 11)) &&
            ShopModel.RoadExitFor(first) == new Vector2Int(10, 10) &&
            ShopModel.FindPath(first, catalog, ShopModel.EntranceFor(first), ShopModel.RoadExitFor(first)).Count > 0,
            "The exterior road and its exit extend to the new right edge");
        var detour = ShopModel.FindPath(first, catalog, new Vector2Int(8, 7), new Vector2Int(8, 8));
        check(detour.Count > 2 && detour.Contains(ShopModel.EntranceFor(first)),
            "New wall segments remain solid and can only be crossed through the original entrance");
        check(ShopModel.TryExpandLayout(catalog, first, out var second, out _) &&
            second.width == 12 && second.depth == 8 && firstJson == JsonUtility.ToJson(first) &&
            ShopModel.WarehousePickup(second) == new Vector2Int(11, 5),
            "Second expansion produces 12 by 8 with the same doorway selection and no source mutation");
        check(second.facilities.Exists(item => item.instanceId == "expanded-counter" && item.x == 2 && item.z == 2) &&
            ShopModel.FindPath(second, catalog, ShopModel.EntranceFor(second), ShopModel.WarehousePickup(second)).Count > 0,
            "Furniture installed after the first expansion and warehouse reachability survive the second");
        string secondJson = JsonUtility.ToJson(second);
        check(ShopModel.TryExpandLayout(catalog, second, out var third, out _) && third.depth==10 &&
            secondJson == JsonUtility.ToJson(second),
            "Third expansion extends depth without mutating the previous room");
        check(ShopModel.TryExpandLayout(catalog,third,out var fourth,out _) && fourth.width==12 && fourth.depth==12 &&
            ShopModel.FindPath(fourth,catalog,ShopModel.RoadSpawnFor(fourth),ShopModel.WarehousePickup(fourth)).Count>0,
            "Fourth expansion reaches 12 by 12 and keeps the dynamic entrance connected");
        string finalSnapshot=JsonUtility.ToJson(fourth);
        check(!ShopModel.TryExpandLayout(catalog,fourth,out _,out _) && finalSnapshot==JsonUtility.ToJson(fourth),
            "A fifth expansion is refused atomically at 12 by 12");
        for (int wall = 0; wall < 8; wall++)
        {
            var empty = ShopModel.Clone(second);
            empty.facilities.RemoveAll(item => item.definitionId != "warehouse" && item.definitionId != "checkout");
            check(ShopModel.TryPlace(empty, catalog, new PlacedFacility
                { instanceId = "warehouse-1", definitionId = "warehouse", x = 12, z = 4, wallPosition = wall + 1 }, out _) &&
                ShopModel.WarehousePickup(empty) == new Vector2Int(11, wall),
                "Expanded warehouse door supports right-wall cell " + wall);
        }
        foreach (var layout in new[] { original, first, second })
        {
            string path = Path.Combine(output, "layout-" + layout.width + ".json");
            ShopModel.SaveToPath(layout, path);
            var loaded = ShopModel.LoadFromPath(catalog, path, out _);
            check(loaded != null && JsonUtility.ToJson(loaded) == JsonUtility.ToJson(layout),
                "Layout save/load preserves the " + layout.width + " by 8 room and every facility");
        }
        foreach (int width in new[] { 0, 9, 11, 14 })
        {
            var invalid = ShopModel.Clone(original);
            invalid.width = width;
            string snapshot = JsonUtility.ToJson(invalid);
            check(!ShopModel.TryExpandLayout(catalog, invalid, out _, out _) &&
                snapshot == JsonUtility.ToJson(invalid),
                "Unknown room width " + width + " is rejected without silently resizing it");
        }
        var wrongDepth = ShopModel.Clone(original);
        wrongDepth.depth = 10;
        check(!ShopModel.TryExpandLayout(catalog, wrongDepth, out _, out _),
            "Unsupported depth cannot move the established road or entrance");
        var blocked = ShopModel.Clone(original);
        blocked.facilities.Add(new PlacedFacility
            { instanceId = "invalid-door", definitionId = "decor", x = 2, z = 7 });
        check(!ShopModel.TryExpandLayout(catalog, blocked, out _, out _),
            "Expanding an already invalid layout cannot bypass entrance safety checks");
        File.WriteAllText(Path.Combine(output, "expansion-verification.json"), JsonUtility.ToJson(
            new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("SHOP_EXPANSION_CHECKS_OK " + results.Count);
    }
}
