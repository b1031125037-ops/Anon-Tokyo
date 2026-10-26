using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed class GrowthTaskDefinition
    {
        public readonly string id, title, description;
        public readonly int reward, target;

        public GrowthTaskDefinition(string id, string title, string description, int reward, int target)
        { this.id = id; this.title = title; this.description = description; this.reward = reward; this.target = target; }
    }

    public sealed class GrowthTaskState
    {
        public GrowthTaskDefinition definition;
        public int progress;
        public bool complete, unlocked, claimed, canClaim;
        public string progressText;
    }

    public sealed partial class ShopEconomy
    {
        public const int MaxAreaExpansionLevel = 4;
        // Version zero is an untouched pre-growth save: no claimed rewards and the original room.
        public int growthVersion;
        public int areaExpansionLevel;
        public List<string> claimedGrowthTaskIds = new List<string>();

        public static int AreaWidth(int stage) => stage==0?8:stage==1?10:12;
        public static int AreaDepth(int stage) => stage<=2?8:stage==3?10:12;
        public int ExpansionLevel => areaExpansionLevel;
        public int NextAreaWidth => AreaWidth(Math.Min(4,areaExpansionLevel+1));
        public int NextAreaDepth => AreaDepth(Math.Min(4,areaExpansionLevel+1));
        public int NextExpansionCost => areaExpansionLevel == 0 ? 600 : areaExpansionLevel == 1 ? 1200 : areaExpansionLevel == 2 ? 1800 : areaExpansionLevel == 3 ? 2400 : 0;
        public int NextExpansionPopularity => areaExpansionLevel == 0 ? 10 : areaExpansionLevel == 1 ? 20 : areaExpansionLevel == 2 ? 30 : areaExpansionLevel == 3 ? 40 : 0;
        public int ExpansionQualifiedPopularity => Math.Max(peakPopularity, popularity);
        public long OnlineUnitsSold
        {
            get
            {
                long total = 0;
                if (stocks != null) foreach (var stock in stocks) if (stock != null) total += stock.sold;
                return Math.Max(0, total - offlineUnitsSold);
            }
        }

        public static readonly GrowthTaskDefinition[] GrowthTasks =
        {
            new GrowthTaskDefinition("staff_ready", "准备营业", "为收银台、接待点、补货点各安排一位员工。", 50, 3),
            new GrowthTaskDefinition("first_contract", "选择商品", "签约至少一种商品；已有签约同样有效。", 50, 1),
            new GrowthTaskDefinition("shelf_ready", "货品上架", "让任意售卖柜台至少有一件现货。", 100, 1),
            new GrowthTaskDefinition("first_checkout", "第一笔成交", "让店内顾客完成一次收银付款。", 100, 1),
            new GrowthTaskDefinition("sell_ten", "小店起步", "店内顾客累计付款购入 10 件商品。", 150, 10),
            new GrowthTaskDefinition("three_counters", "增加陈列", "持有至少 3 个售卖柜台，含收纳中的柜台。", 150, 3),
            new GrowthTaskDefinition("upgraded_post", "提升服务", "将收银台、接待点或补货点中的任意一处升至 Lv.2。", 200, 1),
            new GrowthTaskDefinition("first_expansion", "迈出第一步", "完成第一次扩店，将室内扩至 10 × 8 格。", 200, 1),
            new GrowthTaskDefinition("assortment_growth", "丰富货品", "签约至少 4 种商品，并由店内顾客累计付款购入 50 件商品。", 300, 50),
            new GrowthTaskDefinition("second_expansion", "宽敞店铺", "完成第二次扩店，将室内扩至 12 × 8 格。", 500, 2)
        };

        public List<GrowthTaskState> GetGrowthTaskStates()
        {
            var result = new List<GrowthTaskState>();
            int staffed = 0, contracted = 0, stocked = 0, upgraded = 0;
            foreach (var role in new[] { FacilityKind.Checkout, FacilityKind.Reception, FacilityKind.Restock })
                if (!string.IsNullOrEmpty(PostFor(role)?.staffId)) staffed++;
            if (stocks != null) foreach (var stock in stocks) if (stock != null && stock.contracted) contracted++;
            if (shelves != null) foreach (var shelf in shelves) if (shelf != null && shelf.quantity > 0) stocked = 1;
            if (staffPosts != null) foreach (var post in staffPosts) if (post != null && post.level >= 2) upgraded = 1;
            int onlineSold = (int)Math.Min(int.MaxValue, OnlineUnitsSold);
            int[] progress = { staffed, contracted, stocked, transactions, onlineSold, OwnedFixtureCount,
                upgraded, areaExpansionLevel, onlineSold, areaExpansionLevel };
            bool predecessorsClaimed = true;
            for (int i = 0; i < GrowthTasks.Length; i++)
            {
                var definition = GrowthTasks[i];
                bool claimed = claimedGrowthTaskIds != null && claimedGrowthTaskIds.Contains(definition.id);
                bool complete = progress[i] >= definition.target && (definition.id != "assortment_growth" || contracted >= 4);
                result.Add(new GrowthTaskState
                {
                    definition = definition, progress = Math.Max(0, Math.Min(definition.target, progress[i])),
                    complete = complete, unlocked = predecessorsClaimed, claimed = claimed,
                    canClaim = predecessorsClaimed && complete && !claimed,
                    progressText = definition.id == "assortment_growth"
                        ? "签约 " + Math.Min(contracted, 4) + "/4 · 在线售出 " + Math.Min(onlineSold, 50) + "/50"
                        : Math.Max(0, Math.Min(definition.target, progress[i])) + "/" + definition.target
                });
                predecessorsClaimed &= claimed;
            }
            return result;
        }

        public GrowthTaskState NextGrowthTask => GetGrowthTaskStates().Find(task => !task.claimed);

        public bool TryClaimGrowthTask(ShopCatalog catalog, ShopLayout layout, string taskId, out string reason)
        {
            if (!Validate(catalog, layout, out reason)) return false;
            var state = GetGrowthTaskStates().Find(task => task.definition.id == taskId);
            if (state == null) return Fail("引导任务不存在。", out reason);
            if (state.claimed) return Fail("该任务奖励已经领取。", out reason);
            if (!state.unlocked) return Fail("请先领取前一项引导任务的奖励。", out reason);
            if (!state.complete) return Fail("任务条件尚未完成：" + state.progressText, out reason);
            if (!testMode && cash > int.MaxValue - state.definition.reward)
                return Fail("余额已达支持上限，请消费部分金币后再领取。", out reason);
            if (!testMode) cash += state.definition.reward;
            claimedGrowthTaskIds.Add(taskId);
            growthVersion = 1;
            reason = "已完成「" + state.definition.title + "」" +
                (testMode ? "；测试资源模式记录领取，不追加现金。" : "，获得 " + state.definition.reward + " 金币。");
            return true;
        }

        /// <summary>
        /// Expands only after both the new room and economy validate. Existing furniture keeps
        /// its coordinates; the warehouse follows the new right wall. The app also checks that
        /// physically departing customers have left before invoking this command.
        /// </summary>
        public bool TryExpandShop(ShopCatalog catalog, ShopLayout layout, out string reason)
        {
            if (!Validate(catalog, layout, out reason)) return false;
            if (shopOpen || baskets.Count > 0) return Fail("请先关店并等待所有客人付款离店，再扩建。", out reason);
            if (areaExpansionLevel >= MaxAreaExpansionLevel) return Fail("店铺已经完成全部扩建，当前为 12 × 12 格。", out reason);
            if (ExpansionQualifiedPopularity < NextExpansionPopularity)
                return Fail("扩店需要历史最高人气达到 " + NextExpansionPopularity + "，当前为 " + ExpansionQualifiedPopularity + "。", out reason);
            int cost = NextExpansionCost;
            if (!testMode && cash < cost) return Fail("扩店需要 " + cost + " 金币，余额不足。", out reason);
            if (!ShopModel.TryExpandLayout(catalog, layout, out ShopLayout expanded, out reason)) return false;
            var draft = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(this));
            draft.areaExpansionLevel++;
            draft.growthVersion = 1;
            draft.RefreshShopLevel();
            if (!draft.testMode) draft.cash -= cost;
            if (!draft.Validate(catalog, expanded, out reason) ||
                !ShopModel.ValidateCheckoutAccess(expanded, catalog, out reason)) return false;
            // Every possible rejection precedes this commit to the paired state owners.
            cash = draft.cash;
            growthVersion = draft.growthVersion;
            areaExpansionLevel = draft.areaExpansionLevel;
            shopLevel = draft.shopLevel;
            peakPopularity = draft.peakPopularity;
            layout.entranceColumn=expanded.entranceColumn;
            layout.width = expanded.width;
            layout.depth = expanded.depth;
            layout.facilities = expanded.facilities;
            layout.surfaceVersion=expanded.surfaceVersion;layout.finishes=expanded.finishes;
            reason = "扩店完成：室内 " + layout.width + " × " + layout.depth +
                " 格，原有陈列相对关系保留，仓库与入口已适配新墙面。";
            return true;
        }

        bool ValidateGrowth(ShopLayout layout, out string reason)
        {
            if ((growthVersion != 0 && growthVersion != 1) ||
                areaExpansionLevel < 0 || areaExpansionLevel > MaxAreaExpansionLevel ||
                claimedGrowthTaskIds == null || claimedGrowthTaskIds.Count > GrowthTasks.Length ||
                layout == null || layout.width != AreaWidth(areaExpansionLevel) || layout.depth != AreaDepth(areaExpansionLevel) ||
                (growthVersion == 0 && (areaExpansionLevel != 0 || claimedGrowthTaskIds.Count != 0)))
                return Fail("成长版本、扩店阶段、面积或任务领奖记录无效。", out reason);
            // Rewards form a permanent prefix. Current facilities may later be stored or staff
            // removed, so completed conditions are deliberately not re-evaluated on load.
            for (int i = 0; i < claimedGrowthTaskIds.Count; i++)
                if (claimedGrowthTaskIds[i] != GrowthTasks[i].id)
                    return Fail("任务领奖记录含未知、重复或跳过前置的任务。", out reason);
            reason = "";
            return true;
        }
    }
}
