using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopMultiBasketChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public string[] results; }

    public static void Run(ShopCatalog catalog, string output)
    {
        catalog=ShopLedgerFixture.Catalog(catalog);
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (ok, name) =>
        {
            if (!ok) throw new InvalidOperationException("MULTI BASKET CHECK FAILED: " + name);
            results.Add(name);
        };
        var map = ShopModel.CreateDefault();
        var economy = Stocked(catalog, map);
        int originalCash = economy.cash;
        check(economy.AddToBasket(catalog, "top-1", "guest-a", 3, out _) &&
              economy.AddToBasket(catalog, "shoes-1", "guest-a", 2, out _) &&
              economy.AddToBasket(catalog, "top-1", "guest-a", 1, out _),
              "A guest can add several quantities across counters, including a repeated SKU");
        check(economy.GetBasketQuantity("guest-a") == 6 && economy.GetBasketAmount("guest-a") == 60 &&
              economy.baskets.Count == 1 && economy.baskets[0].lines.Count == 2,
              "The basket groups matching SKU and price while reporting actual total quantity and value");
        check(economy.Shelf("top-1").quantity == 6 && economy.Shelf("shoes-1").quantity == 8 &&
              economy.cash == originalCash && economy.transactions == 0 && economy.Validate(catalog, map, out _),
              "Taking several items moves stock into the unpaid basket without recording revenue");

        string before = JsonUtility.ToJson(economy);
        check(!economy.AddToBasket(catalog, "top-1", "guest-a", 7, out _) &&
              !economy.AddToBasket(catalog, "top-1", "guest-a", 0, out _) &&
              !economy.AddToBasket(catalog, "top-1", "guest-a", -1, out _) &&
              !economy.AddToBasket(catalog, "top-1", "", 1, out _) &&
              JsonUtility.ToJson(economy) == before,
              "Insufficient stock and invalid requests leave all baskets and shelf inventory unchanged");
        check(!economy.TakeFromShelf(catalog, "top-1", "guest-a", out _) && JsonUtility.ToJson(economy) == before,
              "The legacy one-item API still rejects duplicate active transaction IDs");
        check(economy.AddToBasket(catalog, "top-1", "guest-b", 2, out _) && economy.BasketQuantity("top_tshirt") == 6 &&
              economy.GetBasketQuantity("guest-b") == 2 && economy.Validate(catalog, map, out _),
              "Concurrent customer baskets own separate stock and are all included in SKU conservation");

        string path = Path.Combine(output, "multiple-unpaid-customers.json");
        ShopSession.SaveToPath(map, economy, catalog, path);
        var loaded = ShopSession.LoadFromPath(catalog, path, out _);
        check(loaded != null && loaded.economy.baskets.Count == 2 &&
              loaded.economy.GetBasketQuantity("guest-a") == 6 && loaded.economy.GetBasketQuantity("guest-b") == 2 &&
              loaded.economy.GetBasketAmount("guest-a") == 60,
              "A combined save round-trips several customers and all mixed-SKU basket lines");
        loaded.economy.ReturnAllBaskets();
        check(loaded.economy.Validate(catalog, loaded.layout, out _) && loaded.economy.baskets.Count == 0 &&
              loaded.economy.Stock("top_tshirt").available == 6 && loaded.economy.Stock("shoes_canvas").available == 2,
              "Closing or loading an interrupted shop returns every unpaid customer's merchandise exactly once");

        check(economy.PayBasket("guest-a", out _) && economy.transactions == 1 && economy.revenue == 60 &&
              economy.cash == originalCash + 60 && economy.Stock("top_tshirt").sold == 4 &&
              economy.Stock("shoes_canvas").sold == 2 && economy.GetBasketQuantity("guest-b") == 2,
              "One mixed-SKU payment records one transaction and settles each sold quantity without touching another guest");
        before = JsonUtility.ToJson(economy);
        check(!economy.PayBasket("guest-a", out _) && JsonUtility.ToJson(economy) == before,
              "Repeated payment cannot duplicate income or sold quantities");
        economy.ReturnBasket("guest-b");
        before = JsonUtility.ToJson(economy);
        economy.ReturnBasket("guest-b");
        check(economy.Validate(catalog, map, out _) && JsonUtility.ToJson(economy) == before &&
              economy.Stock("top_tshirt").available == 2 && economy.transactions == 1,
              "Returning another guest twice preserves inventory and completed sales");

        var full = Stocked(catalog, map);
        full.AddToBasket(catalog, "top-1", "return-mixed", 3, out _);
        full.AddToBasket(catalog, "shoes-1", "return-mixed", 2, out _);
        full.Stock("top_tshirt").available = 200;
        full.Stock("top_tshirt").purchased += 200;
        full.ReturnAllBaskets();
        check(full.Validate(catalog, map, out _) && full.WarehouseUsed == 200 &&
              full.Stock("top_tshirt").returned == 3 && full.Stock("shoes_canvas").returned == 2,
              "A full warehouse preserves each SKU in the return buffer after a multi-item basket is cancelled");

        var quoted = Stocked(catalog, map);
        quoted.AddToBasket(catalog, "top-1", "different-quotes", 1, out _);
        quoted.UpgradeShelf(catalog, map, "top-1", out _);
        quoted.AddToBasket(catalog, "top-1", "different-quotes", 2, out _);
        check(quoted.GetBasketAmount("different-quotes") == 30 && quoted.baskets[0].lines.Count == 1 &&
              quoted.Validate(catalog, map, out _),
              "A sales-counter capacity upgrade does not apply a second sale-price bonus");
        check(quoted.UpgradePost("checkout-1", out _) &&
              quoted.QuoteBasketAtCheckout(catalog, "different-quotes", "checkout-1", out _) &&
              quoted.GetBasketAmount("different-quotes") == 30,
              "Joining checkout freezes the whole basket using that checkout's upgraded price");
        quoted.UpgradePost("checkout-1", out _);
        before = JsonUtility.ToJson(quoted);
        check(quoted.QuoteBasketAtCheckout(catalog, "different-quotes", "checkout-1", out _) &&
              quoted.GetBasketAmount("different-quotes") == 30 &&
              !quoted.AddToBasket(catalog, "shoes-1", "different-quotes", 1, out _) && JsonUtility.ToJson(quoted) == before,
              "Already quoted baskets retain their price through later upgrades and cannot add unpaid new lines");
        ShopSession.SaveToPath(map, quoted, catalog, Path.Combine(output, "frozen-checkout-quote.json"));
        var quotedLoad = ShopSession.LoadFromPath(catalog, Path.Combine(output, "frozen-checkout-quote.json"), out _);
        check(quotedLoad != null && quotedLoad.economy.baskets[0].checkoutQuoted &&
              quotedLoad.economy.baskets[0].checkoutId == "checkout-1" &&
              quotedLoad.economy.GetBasketAmount("different-quotes") == 30,
              "The first checkout's frozen whole-basket quote survives combined save/load");
        int cashAtCheckout = quoted.cash;
        check(quoted.PayBasket("different-quotes", out _) && quoted.cash == cashAtCheckout + 30 &&
              quoted.transactions == 1 && quoted.Stock("top_tshirt").sold == 3,
              "The frozen whole-basket quote settles as one three-item purchase");

        var legacy = Stocked(catalog, map);
        legacy.TakeFromShelf(catalog, "top-1", "legacy", out _);
        legacy.baskets[0].lines = null;
        check(legacy.Validate(catalog, map, out _) && legacy.GetBasketQuantity("legacy") == 1 &&
              legacy.GetBasketAmount("legacy") == 10, "A legacy basket without a lines list retains its one-item quantity and quote");
        check(legacy.AddToBasket(catalog, "shoes-1", "legacy", 2, out _) &&
              legacy.baskets[0].quantity == 0 && string.IsNullOrEmpty(legacy.baskets[0].productId) &&
              legacy.GetBasketQuantity("legacy") == 3 && legacy.Validate(catalog, map, out _),
              "Appending to a legacy basket migrates its first item once without duplicated stock ownership");
        check(legacy.PayBasket("legacy", out _) && legacy.transactions == 1 && legacy.revenue == 30 &&
              legacy.Validate(catalog, map, out _), "A migrated legacy basket settles as one order");
        var legacyPrice = Stocked(catalog, map);
        legacyPrice.TakeFromShelf(catalog, "top-1", "legacy-price", out _);
        legacyPrice.baskets[0].unitPrice = 14;
        legacyPrice.UpgradePost("checkout-1", out _);
        check(legacyPrice.QuoteBasketAtCheckout(catalog, "legacy-price", "checkout-1", out _) &&
              legacyPrice.GetBasketAmount("legacy-price") == 14 && legacyPrice.PayBasket("legacy-price", out _) &&
              legacyPrice.revenue == 14 && legacyPrice.Validate(catalog, map, out _),
              "A legacy pre-migration price promise is retained instead of repricing to the new checkout level");

        var overflow = Stocked(catalog, map);
        overflow.AddToBasket(catalog, "top-1", "overflow", 2, out _);
        overflow.AddToBasket(catalog, "shoes-1", "overflow", 3, out _);
        overflow.cash = int.MaxValue;
        before = JsonUtility.ToJson(overflow);
        check(!overflow.PayBasket("overflow", out _) && JsonUtility.ToJson(overflow) == before,
              "A payment that would overflow cash leaves every SKU, basket and transaction statistic unchanged");
        overflow.cash = 2500;
        overflow.revenue = long.MaxValue;
        before = JsonUtility.ToJson(overflow);
        check(!overflow.PayBasket("overflow", out _) && JsonUtility.ToJson(overflow) == before,
              "A payment that would overflow revenue is rejected before any SKU is marked sold");

        var damaged = Stocked(catalog, map);
        damaged.AddToBasket(catalog, "top-1", "damaged", 2, out _);
        damaged.baskets[0].lines[0].quantity = -2;
        before = JsonUtility.ToJson(damaged);
        check(!damaged.Validate(catalog, map, out _) && !damaged.PayBasket("damaged", out _) &&
              JsonUtility.ToJson(damaged) == before, "Negative basket quantities cannot validate or produce payment");
        damaged.baskets[0].lines[0].quantity = 2;
        damaged.baskets[0].productId = "top_tshirt";
        damaged.baskets[0].quantity = 1;
        check(!damaged.Validate(catalog, map, out _),
              "A malformed save cannot hide extra inventory in both the legacy fields and multi-item lines");

        ShopSession.SaveToPath(map, economy, catalog, Path.Combine(output, "paid-multi-item-order.json"));
        var settled = ShopSession.LoadFromPath(catalog, Path.Combine(output, "paid-multi-item-order.json"), out _);
        check(settled != null && settled.economy.transactions == 1 && settled.economy.Stock("top_tshirt").sold == 4 &&
              settled.economy.Stock("shoes_canvas").sold == 2,
              "Completed multi-item sales remain valid when item totals exceed the transaction count");
        File.WriteAllText(Path.Combine(output, "multi-basket-verification.json"),
            JsonUtility.ToJson(new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("SHOP_MULTI_BASKET_CHECKS_OK " + results.Count);
    }

    static ShopEconomy Stocked(ShopCatalog catalog, ShopLayout layout)
    {
        var economy = ShopEconomy.CreateDefault(catalog, layout);
        economy.EnableTestResources();
        foreach (var id in new[] { "top-1", "shoes-1" })
        {
            if (!economy.BeginRestock(catalog, layout, id, out string reason) ||
                !economy.Pickup(out reason) || !economy.CompleteRestock(catalog, layout, out reason))
                throw new InvalidOperationException("Cannot prepare multi-basket fixture: " + reason);
        }
        economy.testMode = false;
        return economy;
    }
}
