using System;

namespace AnonTokyo.Shop
{
    /// <summary>Customer arrivals are independent game-time trials, never a refill-to-limit loop.</summary>
    public static class ShopCustomerFlow
    {
        public const float CheckInterval = 6f;
        public const float StressCheckInterval = 2.2f;
        public const int DefaultSeed = 1907;
        public const int DefaultCustomerLimit = 12;

        public static double BaseProbability(int popularity)
        {
            return .30 + .45 * Math.Max(0, Math.Min(100, popularity)) / 100.0;
        }

        public static double ArrivalProbability(int popularity, int presentCustomers, int customerLimit,
            int shelfQueue, int checkoutQueue, bool entranceCrowded, bool spawnAvailable)
        {
            int count = Math.Max(0, presentCustomers);
            int limit = Math.Max(1, Math.Min(12, customerLimit));
            if (count >= limit || shelfQueue >= 2 || checkoutQueue >= 2 || entranceCrowded || !spawnAvailable)
                return 0;
            // Both indoor customers and people still on the road count toward this pressure.
            // Raising the hard limit does not disable the comfort/density protection.
            double density = count == 0 ? 1 : count == 1 ? .65 : count == 2 ? .35 :
                count == 3 ? .20 : count == 4 ? .10 : count == 5 ? .05 : .03;
            return BaseProbability(popularity) * density;
        }

        /// <summary>Park-Miller state fits a positive signed int and survives Unity JSON saves.</summary>
        public static double NextSample(ref int state)
        {
            if (state <= 0 || state >= int.MaxValue) throw new ArgumentOutOfRangeException(nameof(state));
            state = (int)((long)state * 48271 % int.MaxValue);
            return (state - 1) / 2147483646.0;
        }

        public static bool Roll(double probability, ref int state)
        {
            double sample = NextSample(ref state);
            return sample < Math.Max(0, Math.Min(1, probability));
        }
    }
}
