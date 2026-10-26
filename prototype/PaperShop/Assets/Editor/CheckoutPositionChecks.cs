using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class CheckoutPositionChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public string[] results; }

    public static void Run(ShopCatalog catalog, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (ok, name) =>
        {
            if (!ok) throw new InvalidOperationException("CHECKOUT POSITION CHECK FAILED: " + name);
            results.Add(name);
        };
        var definition = catalog.Find("checkout");
        var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        for (int rotation = 0; rotation < 4; rotation++)
        {
            var layout = LayoutAt(3, 3, rotation);
            var checkout = layout.facilities[1];
            var front = new Vector2Int(3, 3) + directions[rotation];
            var rear = new Vector2Int(3, 3) - directions[rotation];
            check(ShopModel.CheckoutCustomerCell(checkout, definition) == front
                && ShopModel.CheckoutStaffCell(checkout, definition) == rear,
                "Quarter-turn " + rotation + " has opposite exact front/rear cells");
            check(ShopModel.PaymentCell(layout, catalog, checkout) == front
                && ShopModel.CanUseCheckoutFront(layout, catalog, checkout, out _)
                && ShopModel.ValidateCheckoutAccess(layout, catalog, out _),
                "Quarter-turn " + rotation + " pays only at its usable geometric front");

            var blocked = ShopModel.Clone(layout);
            check(ShopModel.TryPlace(blocked, catalog, new PlacedFacility
            {
                instanceId = "front-obstruction", definitionId = "decor", x = front.x, z = front.y
            }, out _) && ShopModel.ValidateCheckoutAccess(blocked, catalog, out _),
                "Quarter-turn " + rotation + " keeps body/rear placement valid when front is obstructed");
            var placed = blocked.facilities.Find(p => p.instanceId == "checkout-1");
            check(!ShopModel.CanUseCheckoutFront(blocked, catalog, placed, out _)
                && ShopModel.PaymentCell(blocked, catalog, placed) == front,
                "Quarter-turn " + rotation + " refuses blocked-front payment without selecting a side cell");
        }

        var entranceLayout = LayoutAt(3, 7, 3);
        var entranceCheckout = entranceLayout.facilities[1];
        check(ShopModel.ValidateCheckoutAccess(entranceLayout, catalog, out _)
            && ShopModel.CheckoutCustomerCell(entranceCheckout, definition) == new Vector2Int(2, 7)
            && ShopModel.PaymentCell(entranceLayout, catalog, entranceCheckout) == new Vector2Int(2, 7)
            && ShopModel.CheckoutStaffCell(entranceCheckout, definition) == new Vector2Int(4, 7)
            && ShopModel.CanUseCheckoutFront(entranceLayout, catalog, entranceCheckout, out _),
            "The user's entrance-adjacent (3,7,rotation3) checkout pays at (2,7), never displaced to (2,6)");

        var wallLayout = LayoutAt(3, 7, 0);
        var wallCheckout = wallLayout.facilities[1];
        var outside = ShopModel.CheckoutCustomerCell(wallCheckout, definition);
        check(ShopModel.ValidateCheckoutAccess(wallLayout, catalog, out _)
            && outside == new Vector2Int(3, 8)
            && ShopModel.FindPath(wallLayout, catalog, ShopModel.EntranceCell, outside).Count > 0,
            "A wall-facing checkout may be placed even though its geometric front is on a road reachable around the entrance");
        check(!ShopModel.CanUseCheckoutFront(wallLayout, catalog, wallCheckout, out _)
            && ShopModel.PaymentCell(wallLayout, catalog, wallCheckout) == outside,
            "A route around the wall never permits checkout through the wall or an alternate indoor payment spot");
        string wallBefore = JsonUtility.ToJson(wallCheckout);
        ShopSalesTestSetup.EnsureCheckoutAccess(wallLayout, catalog, wallCheckout);
        check(JsonUtility.ToJson(wallLayout.facilities.Find(p => p.instanceId == "checkout-1")) == wallBefore,
            "A user checkout with valid body/rear is not silently moved or rotated to repair a blocked front");

        var staffPointLayout = LayoutAt(3, 3, 0);
        var staffPointCheckout = staffPointLayout.facilities[1];
        check(ShopModel.TryPlace(staffPointLayout, catalog, new PlacedFacility
        {
            instanceId = "reception-1", definitionId = "reception", x = 3, z = 4
        }, out _) && !ShopModel.IsBlocked(staffPointLayout, catalog, new Vector2Int(3, 4)),
            "A staff floor point remains walkable as a fixture at the checkout front");
        check(ShopModel.ValidateCheckoutAccess(staffPointLayout, catalog, out _)
            && !ShopModel.CanUseCheckoutFront(staffPointLayout, catalog, staffPointCheckout, out _)
            && ShopModel.PaymentCell(staffPointLayout, catalog, staffPointCheckout) == new Vector2Int(3, 4),
            "A reserved staff point cannot become a payment location or trigger a fallback");

        var rearBlocked = LayoutAt(3, 3, 0);
        rearBlocked.facilities.Add(new PlacedFacility { instanceId = "rear-obstruction", definitionId = "decor", x = 3, z = 2 });
        check(!ShopModel.ValidateCheckoutAccess(rearBlocked, catalog, out _),
            "Actual rear cashier obstruction continues to reject placement");

        var state = ShopEconomy.CreateDefault(catalog, entranceLayout);
        string before = JsonUtility.ToJson(entranceLayout.facilities[1]);
        ShopSalesTestSetup.EnsureCheckoutAccess(entranceLayout, catalog, entranceCheckout);
        check(JsonUtility.ToJson(entranceLayout.facilities.Find(p => p.instanceId == "checkout-1")) == before,
            "A legal user checkout retains its chosen position and rotation during access migration");
        string path = Path.Combine(output, "entrance-checkout-session.json");
        ShopSession.SaveToPath(entranceLayout, state, catalog, path);
        var restored = ShopSession.LoadFromPath(catalog, path, out _);
        check(restored != null
            && JsonUtility.ToJson(restored.layout.facilities.Find(p => p.instanceId == "checkout-1")) == before
            && ShopModel.PaymentCell(restored.layout, catalog, restored.layout.facilities.Find(p => p.instanceId == "checkout-1")) == new Vector2Int(2, 7),
            "Save/load preserves the user's wall position, orientation and exact entrance-front payment cell");
        File.WriteAllText(Path.Combine(output, "checkout-position-verification.json"),
            JsonUtility.ToJson(new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("CHECKOUT_POSITION_CHECKS_OK " + results.Count);
    }

    static ShopLayout LayoutAt(int x, int z, int rotation)
    {
        return new ShopLayout
        {
            facilities = new List<PlacedFacility>
            {
                new PlacedFacility { instanceId = "warehouse-1", definitionId = "warehouse", x = 8, z = 4 },
                new PlacedFacility { instanceId = "checkout-1", definitionId = "checkout", x = x, z = z, rotation = rotation }
            }
        };
    }
}
