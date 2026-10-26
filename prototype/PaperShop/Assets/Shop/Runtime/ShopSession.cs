using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>One committed snapshot owns both furniture layout and logistics state.</summary>
    [Serializable]
    public sealed class ShopSession
    {
        public int version = 1;
        public ShopLayout layout;
        public ShopEconomy economy;

        private const long MaxSaveBytes = 4 * 1024 * 1024;
        public static bool TestProfile;
        public static string NormalSavePath { get { return ShopProfiles.PathFor(ShopProfiles.SelectedSlot==4?1:ShopProfiles.SelectedSlot); } }
        public static string SavePath { get { return TestProfile ? ShopProfiles.PathFor(4) : NormalSavePath; } }
        public static bool HasSave { get { return File.Exists(SavePath) || File.Exists(SavePath + ".bak"); } }

        public static void Save(ShopLayout layout, ShopEconomy economy, ShopCatalog catalog)
        {
            SaveToPath(layout, economy, catalog, SavePath);
        }

        public static void SaveToPath(ShopLayout layout, ShopEconomy economy, ShopCatalog catalog, string path)
        {
            var source = new ShopSession { layout = layout, economy = economy };
            string reason;
            if (!Validate(source, catalog, out reason)) throw new ArgumentException(reason);

            // Clone through the exact storage representation before touching either save file.
            // Both members of this detached snapshot are validated again as one transaction.
            string serialized = JsonUtility.ToJson(source);
            ShopSession snapshot;
            if (!TryDecode(serialized, catalog, out snapshot, out reason))
                throw new InvalidOperationException("组合存档序列化校验失败：" + reason);
            if (JsonUtility.ToJson(snapshot) != serialized)
                throw new InvalidOperationException("组合存档序列化未完整保留布局与经济字段，已取消保存。");
            string json = JsonUtility.ToJson(snapshot, true);
            if (Encoding.UTF8.GetByteCount(json) > MaxSaveBytes)
                throw new InvalidOperationException("组合存档超过支持的大小，已取消保存。");

            string absolutePath = Path.GetFullPath(path);
            string backupPath = absolutePath + ".bak";
            ShopSession existing;
            bool validPrimary = TryRead(absolutePath, catalog, out existing, out reason);
            // A damaged or mismatched primary must not replace the last valid paired backup.
            WriteAtomic(absolutePath, json, validPrimary ? backupPath : null);
            if (!TryRead(backupPath, catalog, out existing, out reason)) WriteAtomic(backupPath, json, null);
        }

        public static ShopSession Load(ShopCatalog catalog, out string message)
        {
            return LoadFromPath(catalog, SavePath, out message);
        }

        /// <summary>
        /// Validates without advancing time, synchronizing shelves or cancelling restocking.
        /// The app may call economy.CancelRestock only after accepting this validated snapshot.
        /// </summary>
        public static ShopSession LoadFromPath(ShopCatalog catalog, string path, out string message)
        {
            string absolutePath;
            try { absolutePath = Path.GetFullPath(path); }
            catch (Exception exception)
            {
                message = "组合存档路径无效：" + exception.Message;
                return null;
            }
            if (!File.Exists(absolutePath) && !File.Exists(absolutePath + ".bak"))
            {
                message = "尚无组合存档。";
                return null;
            }
            ShopSession primary;
            string primaryReason;
            if (TryRead(absolutePath, catalog, out primary, out primaryReason))
            {
                message = "已读取店铺布局与经营进度。";
                return primary;
            }
            ShopSession backup;
            string backupReason;
            if (TryRead(absolutePath + ".bak", catalog, out backup, out backupReason))
            {
                try
                {
                    WriteAtomic(absolutePath, JsonUtility.ToJson(backup, true), null);
                    message = "组合主档不可用，已从同一份备份恢复布局与经营进度。原因：" + primaryReason;
                }
                catch (Exception exception)
                {
                    message = "已读取组合备份，但主档恢复写入失败：" + exception.Message;
                }
                return backup;
            }
            message = "组合存档读取失败，未覆盖文件或重置进度。主档：" + primaryReason + "；备份：" + backupReason;
            return null;
        }

        private static bool Validate(ShopSession session, ShopCatalog catalog, out string reason)
        {
            if (session == null || session.version != 1 || session.layout == null || session.economy == null)
            {
                reason = "组合存档版本无效，或布局、经济状态缺失。";
                return false;
            }
            if (!ShopModel.Validate(session.layout, catalog, out reason))
            {
                reason = "布局校验失败：" + reason;
                return false;
            }
            var t=session.economy;
            if(t.tutorialVersion<0||t.tutorialVersion>2||t.tutorialStep< -1||t.tutorialStep>29||t.tutorialTransactionBaseline<0||t.tutorialUpgradeBaseline<0||t.tutorialUpgradeBaseline>4)
            {reason="引导进度无效。";return false;}
            if (!session.economy.Validate(catalog, session.layout, out reason))
            {
                reason = "经济与布局一致性校验失败：" + reason;
                return false;
            }
            return true;
        }

        // Keep schema headers free of gameplay defaults. JsonUtility may materialize missing
        // lists as empty; semantic validation below must still prove all merchandise is owned.
        [Serializable]
        private sealed class StoredSession
        {
            public int version;
            public StoredLayout layout;
            public StoredEconomy economy;
        }

        [Serializable]
        private sealed class StoredLayout
        {
            public int version;
            public int width;
            public int depth;
            public int entranceColumn;
            public int surfaceVersion;
            public List<SurfaceFinish> finishes;
            public List<PlacedFacility> facilities;
        }

        [Serializable]
        private sealed class StoredEconomy
        {
            public int contentVersion;
            public int version;
            public int cash;
            public int warehouseLevel;
            public bool autoRestock;
            public float autoRestockThreshold;
            public bool restockFollowUp;
            public int popularity, customerArrivalRandomState;
            public int fixtureStoreVersion, shopLevel, peakPopularity;
            public int managementRulesVersion;
            public int tutorialVersion, tutorialStep, tutorialTransactionBaseline, tutorialUpgradeBaseline;
            public bool tutorialDeferred, tutorialWallClaimed, tutorialFloorClaimed;
            public List<StoredFixture> storedFixtures;
            public int renovationInventoryVersion;
            public bool stageOwned;
            public int balanceVersion;
            public List<string> freeDecorationIds;
            public double decorationRemainder;
            public long decorationRevenue;
            public int environmentVersion;
            public bool mujicaStageOwned;
            public List<DecorationStock> decorations;
            public List<RenovationMaterialStock> renovationMaterials;
            public int growthVersion, areaExpansionLevel;
            public List<string> claimedGrowthTaskIds;
            public int rosterVersion;
            public List<string> signedStaffIds;
            public int performanceVersion, feverMask, feverMisses, performancesCompleted, feverRandomState, performancePreviousSpeed;
            public float performanceRemaining, performanceCooldown;
            public string performingBand;
            public bool extraRestockPurchased, extraReceptionPurchased;
            public int staffSystemVersion;
            public bool staffPointsIntroduced;
            public List<StaffPostState> staffPosts;
            public List<ProductStock> stocks;
            public List<DeliveryOrder> orders;
            public List<ShelfStock> shelves;
            public List<RestockJob> jobs;
            public bool testMode, shopOpen;
            public int transactions;
            public long revenue;
            public int salesTestVersion;
            public List<CustomerBasket> baskets;
            public int offlineSnapshotVersion;
            public long offlineLastCheckUtcSeconds, offlineUnitsSold, offlineRevenue;
            public bool offlineWasEligible;
            public List<OfflineStockSnapshot> offlineStockSnapshot;
            public List<OfflineSaleRemainder> offlineRemainders;
        }

        private static bool TryDecode(string json, ShopCatalog catalog, out ShopSession session, out string reason)
        {
            session = null;
            try
            {
                var stored = JsonUtility.FromJson<StoredSession>(json);
                if (stored == null || stored.version != 1 || stored.layout == null || stored.economy == null)
                {
                    reason = "组合存档缺少版本、布局或经济对象。";
                    return false;
                }
                // JsonUtility can materialize an omitted list as empty. Once growth has been
                // introduced, losing the claim ledger must not make paid rewards claimable again.
                if(stored.economy.rosterVersion!=0 &&
                    !System.Text.RegularExpressions.Regex.IsMatch(json,@"""signedStaffIds""\s*:\s*\["))
                {reason="员工签约存档缺少签约列表。";return false;}
                if(stored.economy.performanceVersion!=0)
                    foreach(var field in new[]{"feverMask","feverMisses","feverRandomState","performanceRemaining","performanceCooldown","performancesCompleted","performingBand","performancePreviousSpeed"})
                        if(!json.Contains("\""+field+"\""))
                        {reason="表演存档缺少 "+field+"。";return false;}
                if (stored.economy.growthVersion != 0 &&
                    (!System.Text.RegularExpressions.Regex.IsMatch(json, @"""claimedGrowthTaskIds""\s*:\s*\[") ||
                     !System.Text.RegularExpressions.Regex.IsMatch(json, @"""areaExpansionLevel""\s*:\s*-?\d+")))
                {
                    reason = "成长存档缺少扩店阶段或任务领奖列表。";
                    return false;
                }
                var candidate = new ShopSession
                {
                    version = stored.version,
                    layout = new ShopLayout
                    {
                        version = stored.layout.version, width = stored.layout.width,
                        depth = stored.layout.depth, entranceColumn=stored.layout.entranceColumn, facilities = stored.layout.facilities,
                        surfaceVersion=stored.layout.surfaceVersion,finishes=stored.layout.finishes??(stored.layout.surfaceVersion==0?new List<SurfaceFinish>():null)
                    },
                    economy = new ShopEconomy
                    {
                        version = stored.economy.version, contentVersion=stored.economy.contentVersion, cash = stored.economy.cash,
                        warehouseLevel = stored.economy.warehouseLevel, autoRestock = stored.economy.autoRestock,
                        autoRestockThreshold = stored.economy.autoRestockThreshold,
                        restockFollowUp=stored.economy.restockFollowUp,
                        popularity=stored.economy.popularity,
                        fixtureStoreVersion=stored.economy.fixtureStoreVersion,
                        renovationInventoryVersion=stored.economy.renovationInventoryVersion,
                        stageOwned=stored.economy.stageOwned,
                        balanceVersion=stored.economy.balanceVersion,freeDecorationIds=stored.economy.freeDecorationIds??(stored.economy.balanceVersion==0?new List<string>():null),decorationRemainder=stored.economy.decorationRemainder,decorationRevenue=stored.economy.decorationRevenue,
                        environmentVersion=stored.economy.environmentVersion,mujicaStageOwned=stored.economy.mujicaStageOwned,
                        decorations=stored.economy.decorations??(stored.economy.environmentVersion==0?new List<DecorationStock>():null),
                        renovationMaterials=stored.economy.renovationMaterials??(stored.economy.renovationInventoryVersion==0?new List<RenovationMaterialStock>():null),
                        managementRulesVersion=stored.economy.managementRulesVersion,
                        tutorialVersion=stored.economy.tutorialVersion, tutorialStep=stored.economy.tutorialStep,
                        tutorialTransactionBaseline=stored.economy.tutorialTransactionBaseline,
                        tutorialUpgradeBaseline=stored.economy.tutorialUpgradeBaseline,
                        tutorialDeferred=stored.economy.tutorialDeferred,
                        tutorialWallClaimed=stored.economy.tutorialWallClaimed, tutorialFloorClaimed=stored.economy.tutorialFloorClaimed,
                        shopLevel=stored.economy.shopLevel==0 && stored.economy.fixtureStoreVersion==0?1:stored.economy.shopLevel,
                        peakPopularity=stored.economy.peakPopularity,
                        storedFixtures=stored.economy.storedFixtures??
                            (stored.economy.fixtureStoreVersion==0?new List<StoredFixture>():null),
                        growthVersion=stored.economy.growthVersion,
                        rosterVersion=stored.economy.rosterVersion,
                        signedStaffIds=stored.economy.signedStaffIds??(stored.economy.rosterVersion==0?new List<string>():null),
                        performanceVersion=stored.economy.performanceVersion,
                        feverMask=stored.economy.feverMask,feverMisses=stored.economy.feverMisses,
                        performancesCompleted=stored.economy.performancesCompleted,
                        feverRandomState=stored.economy.performanceVersion==0?173913:stored.economy.feverRandomState,
                        performancePreviousSpeed=stored.economy.performanceVersion==0?1:stored.economy.performancePreviousSpeed,
                        performanceRemaining=stored.economy.performanceRemaining,performanceCooldown=stored.economy.performanceCooldown,
                        performingBand=stored.economy.performingBand??"",
                        areaExpansionLevel=stored.economy.areaExpansionLevel,
                        claimedGrowthTaskIds=stored.economy.claimedGrowthTaskIds??
                            (stored.economy.growthVersion==0?new List<string>():null),
                        customerArrivalRandomState=stored.economy.customerArrivalRandomState==0?
                            ShopCustomerFlow.DefaultSeed:stored.economy.customerArrivalRandomState,
                        extraRestockPurchased=stored.economy.extraRestockPurchased,extraReceptionPurchased=stored.economy.extraReceptionPurchased,
                        staffSystemVersion=stored.economy.staffSystemVersion,
                        staffPointsIntroduced=stored.economy.staffPointsIntroduced,
                        staffPosts=stored.economy.staffPosts??new List<StaffPostState>(),
                        stocks = stored.economy.stocks, orders = stored.economy.orders,
                        shelves = stored.economy.shelves,
                        // Missing/null jobs means idle only if reservations and the purchased
                        // quantity ledger also prove no active task or carried goods were lost.
                        jobs = stored.economy.jobs ?? new List<RestockJob>(),
                        testMode = stored.economy.testMode, shopOpen = stored.economy.shopOpen,
                        transactions = stored.economy.transactions, revenue = stored.economy.revenue, salesTestVersion=stored.economy.salesTestVersion,
                        baskets = stored.economy.baskets ?? new List<CustomerBasket>(),
                        offlineSnapshotVersion = stored.economy.offlineSnapshotVersion,
                        offlineLastCheckUtcSeconds = stored.economy.offlineLastCheckUtcSeconds,
                        offlineUnitsSold = stored.economy.offlineUnitsSold,
                        offlineRevenue = stored.economy.offlineRevenue,
                        offlineWasEligible = stored.economy.offlineWasEligible,
                        offlineStockSnapshot = stored.economy.offlineStockSnapshot ??
                            (stored.economy.offlineSnapshotVersion == 0 ? new List<OfflineStockSnapshot>() : null),
                        offlineRemainders = stored.economy.offlineRemainders ??
                            (stored.economy.offlineSnapshotVersion == 0 ? new List<OfflineSaleRemainder>() : null)
                    }
                };
                if(!ShopProductSet.MigrateStocks(candidate.economy,out reason))return false;
                // Shelf levels were absent from the earlier v1 snapshot; zero is the legacy Lv.1.
                if(candidate.economy.shelves!=null)foreach(var shelf in candidate.economy.shelves)
                    if(shelf!=null && shelf.level==0)shelf.level=1;
                if(candidate.economy.staffSystemVersion==0 &&
                    !candidate.economy.SyncStaffPosts(catalog,candidate.layout,out reason))return false;
                if (!Validate(candidate, catalog, out reason)) return false;
                session = candidate;
                return true;
            }
            catch (Exception exception)
            {
                reason = "组合存档解析失败：" + exception.Message;
                return false;
            }
        }

        private static bool TryRead(string path, ShopCatalog catalog, out ShopSession session, out string reason)
        {
            session = null;
            try
            {
                if (!File.Exists(path)) { reason = "文件不存在"; return false; }
                if (new FileInfo(path).Length > MaxSaveBytes) { reason = "文件超过大小限制"; return false; }
                return TryDecode(File.ReadAllText(path, Encoding.UTF8), catalog, out session, out reason);
            }
            catch (Exception exception)
            {
                reason = "读取失败：" + exception.Message;
                return false;
            }
        }

        private static void WriteAtomic(string path, string json, string backupPath)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporaryPath, path, backupPath);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
