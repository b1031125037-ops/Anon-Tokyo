using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    [Serializable]
    public sealed class StoredFixture
    {
        public string instanceId;
        public string definitionId;
        public ShelfStock shelf;
    }

    public sealed partial class ShopEconomy
    {
        public const int BasicFixtureCost = 300;
        public const int MaxShopLevel = 5;
        // Version zero accepts the pre-store layout as already owned. Migration never
        // removes old counters, even when their count exceeds the new purchase limit.
        public int fixtureStoreVersion;
        public int shopLevel = 1;
        public int peakPopularity;
        public List<StoredFixture> storedFixtures = new List<StoredFixture>();

        public int ShopLevel => Math.Max(1, Math.Min(MaxShopLevel, shopLevel));
        public int FixtureLimit {get {int n=0;foreach(var id in ShopProductSet.Categories)n+=FixtureCategoryLimit(id);return n;}}
        public int FixtureCategoryLimit(string id) {int i=Array.IndexOf(ShopProductSet.Categories,id);return i<0?0:1+(ShopLevel>=(i<3?2:3)?1:0)+Math.Max(0,ShopLevel-3);}
        public int OwnedCategoryCount(ShopCatalog catalog,string id) => shelves.FindAll(s=>Product(catalog,s.productId)?.category==id).Count+storedFixtures.FindAll(f=>f.definitionId==id).Count;
        public int OwnedFixtureCount => (shelves == null ? 0 : shelves.Count) + StoredFixtureCount;
        public int StoredFixtureCount => storedFixtures == null ? 0 : storedFixtures.Count;
        public int NextShopLevelPopularity => ShopLevel<5?ShopLevel*10:0;

        static int LevelForPopularity(int value) => value >= 40 ? 5 : value >= 30 ? 4 : value >= 20 ? 3 : value >= 10 ? 2 : 1;

        public void RefreshShopLevel()
        {
            peakPopularity = Math.Max(peakPopularity, Math.Max(0, Math.Min(100, popularity)));
            // Popularity qualifies an upgrade; only a paid confirmation grants its level.
            shopLevel = Math.Max(ShopLevel, areaExpansionLevel + 1);
            popularity = Math.Min(PopularityCap, popularity);
        }

        public StoredFixture StoredFixture(string instanceId) =>
            storedFixtures == null ? null : storedFixtures.Find(item => item != null && item.instanceId == instanceId);

        public StoredFixture FirstStoredFixture(string definitionId) =>
            storedFixtures == null ? null : storedFixtures.Find(item => item != null && item.definitionId == definitionId);

        public bool InitializeFixtureStore(ShopCatalog catalog, ShopLayout layout, out string reason)
        {
            if (!Validate(catalog, layout, out reason)) return false;
            fixtureStoreVersion = 1;
            RefreshShopLevel();
            reason = "已保留全部现有柜台；商店 Lv." + ShopLevel + "，柜台额度 " + OwnedFixtureCount + "/" + FixtureLimit + "。";
            return true;
        }

        public bool BuyFixture(ShopCatalog catalog, ShopLayout layout, string definitionId,
            out string instanceId, out string reason)
        {
            instanceId = null;
            if (!Validate(catalog, layout, out reason)) return false;
            var definition = catalog.Find(definitionId);
            if (definition == null || definition.kind != FacilityKind.Shelf ||
                !ShopProductSet.IsDisplayCategory(definitionId))
                return Fail("请选择展柜分类中的基础货架。", out reason);
            if (OwnedCategoryCount(catalog,definitionId) >= FixtureCategoryLimit(definitionId))
                return Fail("此类展柜已达上限 "+FixtureCategoryLimit(definitionId)+"，提升店铺等级后再购买。",out reason);
            if (!testMode && cash < BasicFixtureCost) return Fail("基础柜台需要 300 金币，余额不足。", out reason);
            var product = catalog.products.Find(item => item != null && item.category == definitionId);
            if (product == null) return Fail("柜台类别没有可绑定商品。", out reason);
            string newId;
            do { newId = definitionId + "-" + Guid.NewGuid().ToString("N"); }
            while (layout.facilities.Exists(item => item.instanceId == newId) || StoredFixture(newId) != null);
            if (!testMode) cash -= BasicFixtureCost;
            fixtureStoreVersion = 1;
            RefreshShopLevel();
            storedFixtures.Add(new StoredFixture
            {
                instanceId = newId, definitionId = definitionId,
                shelf = new ShelfStock { instanceId = newId, productId = product.id, level = 1 }
            });
            instanceId = newId;
            reason = "已购买" + definition.title + "并放入收纳；关店后可在装修中摆放。";
            return true;
        }

        /// <summary>
        /// Validates a detached layout/economy transaction before changing either argument.
        /// A preview may invoke this on detached copies; no stock is consumed until commit.
        /// </summary>
        public bool PlaceStoredFixture(ShopCatalog catalog, ShopLayout layout, PlacedFacility candidate, out string reason)
        {
            if (!Validate(catalog, layout, out reason)) return false;
            if (candidate == null) return Fail("缺少待摆放柜台。", out reason);
            var stored = StoredFixture(candidate.instanceId);
            if (stored == null || stored.definitionId != candidate.definitionId ||
                layout.facilities.Exists(item => item.instanceId == candidate.instanceId))
                return Fail("该柜台不在收纳中，或柜台类别不匹配。", out reason);
            var draftLayout = ShopModel.Clone(layout);
            if (!ShopModel.TryPlace(draftLayout, catalog, candidate, out reason) ||
                !ShopModel.ValidateCheckoutAccess(draftLayout, catalog, out reason)) return false;
            var draft = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(this));
            var saved = draft.StoredFixture(candidate.instanceId);
            draft.storedFixtures.Remove(saved);
            draft.shelves.Add(saved.shelf);
            draft.fixtureStoreVersion = 1;
            draft.RefreshShopLevel();
            if (!draft.SyncShelves(catalog, draftLayout, out reason) || !draft.Validate(catalog, draftLayout, out reason)) return false;
            CommitFixtureState(draft);
            layout.facilities = draftLayout.facilities;
            reason = "已从收纳摆放柜台，等级、商品和补货阈值已保留。";
            return true;
        }

        public bool StoreFixture(ShopCatalog catalog, ShopLayout layout, string instanceId, out string reason)
        {
            if (!Validate(catalog, layout, out reason)) return false;
            var placement = layout.facilities.Find(item => item.instanceId == instanceId);
            var shelf = Shelf(instanceId);
            if (placement == null || shelf == null) return Fail("只能收纳已经摆放的售卖柜台。", out reason);
            if (HasRestockFor(instanceId))
                return Fail("请先结束该柜台的补货任务，再收纳。", out reason);
            if ((long)Stock(shelf.productId).returned + shelf.quantity > int.MaxValue)
                return Fail("柜台商品退仓数量超过支持范围。", out reason);
            var draftLayout = ShopModel.Clone(layout);
            if (!ShopModel.TryRemove(draftLayout, catalog, instanceId, out reason) ||
                !ShopModel.ValidateCheckoutAccess(draftLayout, catalog, out reason)) return false;
            var draft = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(this));
            var savedShelf = draft.Shelf(instanceId);
            draft.Stock(savedShelf.productId).returned += savedShelf.quantity;
            savedShelf.quantity = 0;
            draft.FlushReturns();
            draft.shelves.Remove(savedShelf);
            draft.storedFixtures.Add(new StoredFixture
            {
                instanceId = instanceId, definitionId = placement.definitionId, shelf = savedShelf
            });
            draft.fixtureStoreVersion = 1;
            draft.RefreshShopLevel();
            if (!draft.SyncShelves(catalog, draftLayout, out reason) || !draft.Validate(catalog, draftLayout, out reason)) return false;
            CommitFixtureState(draft);
            layout.facilities = draftLayout.facilities;
            reason = "柜台已收纳，商品安全退仓，满仓余货保留缓冲；柜台仍计入持有上限，等级、商品和阈值已保留。";
            return true;
        }

        void CommitFixtureState(ShopEconomy draft)
        {
            shelves = draft.shelves;
            stocks = draft.stocks;
            storedFixtures = draft.storedFixtures;
            fixtureStoreVersion = draft.fixtureStoreVersion;
            shopLevel = draft.shopLevel;
            peakPopularity = draft.peakPopularity;
        }

        bool ValidateFixtures(ShopCatalog catalog, ShopLayout layout, out string reason)
        {
            if ((fixtureStoreVersion != 0 && fixtureStoreVersion != 1) || shopLevel < 1 || shopLevel > MaxShopLevel ||
                peakPopularity < 0 || peakPopularity > 100 || storedFixtures == null ||
                (fixtureStoreVersion == 0 && storedFixtures.Count != 0))
                return Fail("柜台商店版本、等级、历史人气或收纳数据无效。", out reason);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var placement in layout.facilities) ids.Add(placement.instanceId);
            foreach (var stored in storedFixtures)
            {
                if (stored == null || string.IsNullOrWhiteSpace(stored.instanceId) || !ids.Add(stored.instanceId))
                    return Fail("收纳柜台实例为空、重复或同时出现在店内。", out reason);
                var definition = catalog.Find(stored.definitionId);
                var shelf = stored.shelf;
                var product = shelf == null ? null : Product(catalog, shelf.productId);
                if (definition == null || definition.kind != FacilityKind.Shelf || shelf == null ||
                    shelf.instanceId != stored.instanceId || product == null || product.category != definition.id ||
                    Stock(product.id) == null || shelf.quantity != 0 || shelf.level < 1 || shelf.level > MaxShelfLevel ||
                    !ValidShelfRestockSetting(shelf))
                    return Fail("收纳柜台类别、商品、等级或补货设置无效；只能收纳空柜台。", out reason);
            }
            // Existing saves above a limit remain valid. The limit is checked only when
            // purchasing so legacy furniture cannot be lost or erased during migration.
            reason = "";
            return true;
        }
    }
}
