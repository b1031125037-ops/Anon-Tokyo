using System;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        /// <summary>Null follows the shop default; zero is a real empty-shelf-only override.</summary>
        public bool SetShelfRestockThreshold(string shelfId, float? threshold, out string reason)
        {
            var shelf = Shelf(shelfId);
            if (shelf == null) return Fail("柜台不存在，不能设置补货阈值。", out reason);
            if (threshold.HasValue && (!Finite(threshold.Value) || threshold.Value < 0f || threshold.Value > 1f))
                return Fail("柜台补货阈值必须在 0% 至 100% 之间。", out reason);
            if(threshold.HasValue && threshold.Value!=.5f)return Fail("补货阈值固定为低于容量的 50%。",out reason);
            shelf.overrideRestockThreshold = false;
            shelf.restockThreshold = .5f;
            reason="固定在库存低于容量 50% 时触发补货。";
            return true;
        }

        /// <summary>Returns NaN for an unknown shelf or invalid settings instead of inventing a trigger.</summary>
        public float EffectiveRestockThreshold(string shelfId)
        {
            var shelf = Shelf(shelfId);
            if (shelf == null || !ValidShelfRestockSetting(shelf) || !Finite(autoRestockThreshold) ||
                autoRestockThreshold < 0f || autoRestockThreshold > 1f) return float.NaN;
            return .5f;
        }

        static bool ValidShelfRestockSetting(ShelfStock shelf)
        {
            return shelf != null && Finite(shelf.restockThreshold) &&
                shelf.restockThreshold >= 0f && shelf.restockThreshold <= 1f;
        }
    }
}
