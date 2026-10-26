using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopCustomerFlowChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public int lowPopularityArrivals, highPopularityArrivals; public string[] results; }

    public static void Run(ShopCatalog catalog, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (ok, name) =>
        {
            if (!ok) throw new InvalidOperationException("CUSTOMER FLOW CHECK FAILED: " + name);
            results.Add(name);
        };
        Func<int, int, int, int, int, double> probability = (popularity, count, limit, shelfQueue, checkoutQueue) =>
            ShopCustomerFlow.ArrivalProbability(popularity, count, limit, shelfQueue, checkoutQueue, false, true);
        check(ShopCustomerFlow.CheckInterval == 6f && ShopCustomerFlow.DefaultCustomerLimit == 12,
            "Normal operation uses six-second trials and a comfortable initial hard limit of three");
        check(Math.Abs(probability(0, 0, 3, 0, 0) - .30) < .000001 &&
              Math.Abs(probability(100, 0, 3, 0, 0) - .75) < .000001,
            "An empty shop starts at thirty percent per trial and caps at seventy-five percent with full popularity");
        double previous = 0;
        bool monotonic = true;
        for (int value = 0; value <= 100; value++)
        {
            double next = probability(value, 0, 3, 0, 0);
            monotonic &= next >= previous && next <= .75;
            previous = next;
        }
        check(monotonic, "Popularity steadily increases the probability without exceeding its cap");
        previous = 1;
        monotonic = true;
        for (int count = 0; count < 12; count++)
        {
            double next = probability(100, count, 12, 0, 0);
            monotonic &= next <= previous && next > 0;
            previous = next;
        }
        check(monotonic && probability(100, 5, 12, 0, 0) < .04,
            "Raising the hard limit never disables the density penalty from customers indoors or on the road");
        check(probability(100, 3, 3, 0, 0) == 0 && probability(100, 12, 12, 0, 0) == 0,
            "Both the default and maximum player-selected hard limits stop further arrivals");
        check(probability(100, 1, 12, 2, 0) == 0 && probability(100, 1, 12, 0, 2) == 0,
            "Two customers queued for shelf browsing or checkout suspend additional arrivals");
        check(ShopCustomerFlow.ArrivalProbability(100, 0, 12, 0, 0, true, true) == 0 &&
              ShopCustomerFlow.ArrivalProbability(100, 0, 12, 0, 0, false, false) == 0,
            "Congestion at the entrance or an occupied spawn stopping point suspends entry");

        int lowState = ShopCustomerFlow.DefaultSeed, highState = ShopCustomerFlow.DefaultSeed, low = 0, high = 0;
        for (int trial = 0; trial < 600; trial++)
        {
            bool lowArrives = ShopCustomerFlow.Roll(.30, ref lowState);
            bool highArrives = ShopCustomerFlow.Roll(.75, ref highState);
            if (lowArrives) low++;
            if (highArrives) high++;
            if (lowArrives && !highArrives) throw new InvalidOperationException("Coupled arrival samples diverged.");
        }
        check(high > low && low > 0 && high < 600 && lowState == highState,
            "Identical random draws produce more high-popularity arrivals while keeping every interval probabilistic");
        int zeroState = ShopCustomerFlow.DefaultSeed, nextState = ShopCustomerFlow.DefaultSeed;
        bool zero = ShopCustomerFlow.Roll(0, ref zeroState);
        ShopCustomerFlow.NextSample(ref nextState);
        check(!zero && zeroState == nextState,
            "A blocked arrival trial still advances exactly one random sample without generating a customer");
        int reproducibleA = 731, reproducibleB = 731;
        bool identical = true;
        for (int i = 0; i < 100; i++)
            identical &= ShopCustomerFlow.NextSample(ref reproducibleA) == ShopCustomerFlow.NextSample(ref reproducibleB);
        check(identical, "A fixed seed reproduces the whole sequence independently of shopping random draws");

        var map = ShopModel.CreateDefault();
        var economy = ShopEconomy.CreateDefault(catalog, map);
        check(economy.popularity == 0 && economy.customerArrivalRandomState == ShopCustomerFlow.DefaultSeed &&
              economy.Validate(catalog, map, out _), "A new economy starts with zero popularity and a valid arrival random state");
        string before = JsonUtility.ToJson(economy);
        check(!economy.SetTestPopularity(100, out _) && !economy.SetTestCustomerFlowSeed(44, out _) &&
              JsonUtility.ToJson(economy) == before, "Normal saves cannot use the test popularity and random-seed controls");
        economy.EnableTestResources();
        economy.shopLevel=5; // This test isolates the maximum-level popularity ceiling.
        check(economy.SetTestPopularity(99, out _) && economy.SetTestCustomerFlowSeed(44, out _),
            "Test saves can set bounded popularity and a repeatable positive arrival seed");
        before = JsonUtility.ToJson(economy);
        check(!economy.SetTestPopularity(-1, out _) && !economy.SetTestPopularity(101, out _) &&
              !economy.SetTestCustomerFlowSeed(0, out _) && !economy.SetTestCustomerFlowSeed(int.MaxValue, out _) &&
              JsonUtility.ToJson(economy) == before, "Out-of-range debug values are rejected without changing the economy");
        economy.BeginRestock(catalog, map, "top-1", out _);
        economy.Pickup(out _);
        economy.CompleteRestock(catalog, map, out _);
        economy.TakeFromShelf(catalog, "top-1", "popularity-sale", out _);
        check(economy.popularity == 99, "Browsing and taking unpaid merchandise do not award popularity");
        check(economy.PayBasket("popularity-sale", out _) && economy.popularity == 100,
            "A successful transaction awards one popularity point");
        before = JsonUtility.ToJson(economy);
        check(!economy.PayBasket("popularity-sale", out _) && JsonUtility.ToJson(economy) == before,
            "Repeated payment cannot award duplicate popularity");
        economy.TakeFromShelf(catalog, "top-1", "popularity-cap", out _);
        check(economy.PayBasket("popularity-cap", out _) && economy.popularity == 100,
            "Further real sales respect the popularity cap");
        economy.SetTestPopularity(40, out _);
        economy.RollCustomerArrival(.30);
        string path = Path.Combine(output, "popularity-and-arrival-state.json");
        ShopSession.SaveToPath(map, economy, catalog, path);
        var restored = ShopSession.LoadFromPath(catalog, path, out _);
        check(restored != null && restored.economy.popularity == 40 &&
              restored.economy.customerArrivalRandomState == economy.customerArrivalRandomState,
            "Combined save/load preserves popularity and the arrival random state");
        check(restored.economy.RollCustomerArrival(.40) == economy.RollCustomerArrival(.40) &&
              restored.economy.customerArrivalRandomState == economy.customerArrivalRandomState,
            "Resuming a saved economy continues the next random trial without reseeding");
        economy.popularity = 101;
        check(!economy.Validate(catalog, map, out _), "Out-of-range saved popularity is rejected");
        economy.popularity = 40;
        economy.customerArrivalRandomState = 0;
        check(!economy.Validate(catalog, map, out _), "An invalid live arrival random state is rejected");

        File.WriteAllText(Path.Combine(output, "customer-flow-verification.json"), JsonUtility.ToJson(
            new Report { passed = true, checks = results.Count, lowPopularityArrivals = low, highPopularityArrivals = high, results = results.ToArray() }, true));
        Debug.Log("SHOP_CUSTOMER_FLOW_CHECKS_OK " + results.Count);
    }
}
