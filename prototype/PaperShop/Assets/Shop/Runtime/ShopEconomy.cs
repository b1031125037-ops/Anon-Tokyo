using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    [Serializable]
    public sealed class ProductStock
    {
        public string productId;
        public bool contracted;
        public int available;
        public int reserved;
        // Every purchased unit has exactly one owner, including completed sales.
        public int purchased;
        public int sold;
        public int returned;
    }

    [Serializable]
    public sealed class DeliveryOrder
    {
        public string productId;
        public int quantity;
        // Missing fields in older snapshots mean an already-dispatched, non-cancellable order.
        public string orderId;
        public bool awaitingDispatch;
        public float dispatchRemaining;
        public int paidAmount;
        public float remaining;
    }

    [Serializable]
    public sealed class ShelfStock
    {
        public string instanceId;
        public string productId;
        public int quantity;
        public int level = 1;
        // Older snapshots omit both fields and inherit the shop-wide trigger threshold.
        public bool overrideRestockThreshold;
        public float restockThreshold;
    }

    [Serializable]
    public sealed class RestockJob
    {
        public string postId;
        public string shelfId;
        public string productId;
        public int quantity;
        public bool pickedUp;
    }

    /// <summary>
    /// Serializable logistics state. The app owns movement and invokes pickup/completion at
    /// service points. A carried unit keeps its warehouse slot until shelving, so cancellation
    /// always has space to return it. Loaders must Validate before cancelling/restoring a job.
    /// </summary>
    [Serializable]
    public sealed partial class ShopEconomy
    {
        public const int ContractCost = 80;
        public const float DispatchSeconds = 1f;
        public const float DeliverySeconds = 3f;
        public const int MaxWarehouseLevel = 3;
        public const int FirstUpgradeCost = 400;
        public const int SecondUpgradeCost = 800;

        public int version = 1;
        public int contentVersion=1;
        // Version zero is an existing save without onboarding; never interrupt it.
        public int tutorialVersion, tutorialStep, tutorialTransactionBaseline, tutorialUpgradeBaseline;
        public bool tutorialDeferred, tutorialWallClaimed, tutorialFloorClaimed;
        public int cash = 2500;
        public int warehouseLevel = 1;
        public bool autoRestock = true;
        public float autoRestockThreshold = .5f;
        public bool restockFollowUp;
        public List<ProductStock> stocks = new List<ProductStock>();
        public List<DeliveryOrder> orders = new List<DeliveryOrder>();
        public List<ShelfStock> shelves = new List<ShelfStock>();
        // JsonUtility creates an empty object for a null inline serializable class.
        // An empty list reliably represents no job; the property preserves the command API.
        public List<RestockJob> jobs = new List<RestockJob>();
        public RestockJob job
        {
            get { return jobs?.Find(IsCurrentRestockJob); }
            set
            {
                if (jobs == null) jobs = new List<RestockJob>();
                jobs.RemoveAll(IsCurrentRestockJob);
                if (value != null) {value.postId=activeRestockPostId;jobs.Add(value);}
            }
        }

        public int Capacity { get { return warehouseLevel * 200; } }
        public int UpgradeCost { get { return warehouseLevel == 1 ? FirstUpgradeCost : warehouseLevel == 2 ? SecondUpgradeCost : 0; } }
        public int WarehouseUsed
        {
            get
            {
                long count = 0;
                if (stocks != null) foreach (var stock in stocks) if (stock != null) count += (long)stock.available + stock.reserved;
                return Bounded(count);
            }
        }
        public int InTransit
        {
            get
            {
                long count = 0;
                if (orders != null) foreach (var order in orders) if (order != null) count += order.quantity;
                return Bounded(count);
            }
        }
        public int Carrying
        {
            get
            {
                long total=0;
                if(jobs!=null)foreach(var task in jobs)if(task!=null && task.pickedUp)total+=task.quantity;
                return Bounded(total);
            }
        }
        public int WarehouseCommitted { get { return Bounded((long)WarehouseUsed + InTransit + Carrying); } }

        public ProductStock Stock(string productId)
        {
            return stocks == null ? null : stocks.Find(value => value != null && value.productId == productId);
        }

        public ShelfStock Shelf(string instanceId)
        {
            return shelves == null ? null : shelves.Find(value => value != null && value.instanceId == instanceId);
        }

        public static ShopEconomy CreateDefault(ShopCatalog catalog, ShopLayout layout)
        {
            if (catalog == null || catalog.products == null) throw new ArgumentException("商品目录不可用。");
            var result = new ShopEconomy();
            foreach (var product in catalog.products)
            {
                if (product == null || string.IsNullOrWhiteSpace(product.id) || result.Stock(product.id) != null)
                    throw new ArgumentException("商品目录含空项或重复 ID。");
                result.stocks.Add(new ProductStock { productId = product.id });
            }
            string reason;
            if (!result.SyncShelves(catalog, layout, out reason) || !result.SyncStaffPosts(catalog, layout, out reason) ||
                !result.Validate(catalog, layout, out reason))
                throw new ArgumentException(reason);
            return result;
        }

        public bool Contract(ShopCatalog catalog, string productId, out string reason)
        {
            var stock = Stock(productId);
            if (Product(catalog, productId) == null || stock == null) return Fail("商品不存在。", out reason);
            if (!OwnsDisplayFor(catalog,productId)) return Fail("请先在商店购买对应展柜。",out reason);
            if (stock.contracted) return Fail("该商品已签约，无需重复支付。", out reason);
            if (!testMode && cash < ContractCost) return Fail("签约需要 80 金币，余额不足。", out reason);
            if (!testMode) cash -= ContractCost;
            stock.contracted = true;
            reason = "签约完成。";
            return true;
        }

        public bool Order(ShopCatalog catalog, string productId, out string reason)
        {
            var product = Product(catalog, productId);
            var stock = Stock(productId);
            if (product == null || stock == null || orders == null) return Fail("商品或订单数据不可用。", out reason);
            if (!OwnsDisplayFor(catalog,productId)) return Fail("请先在商店购买对应展柜。",out reason);
            if (!stock.contracted) return Fail("请先签约该商品。", out reason);
            if (product.buyPrice <= 0 || product.batchSize <= 0) return Fail("商品进价或批量无效。", out reason);
            long cost = (long)product.buyPrice * product.batchSize;
            if (!testMode && cost > cash) return Fail("采购余额不足。", out reason);
            if (!testMode && (long)WarehouseUsed + InTransit + Carrying + product.batchSize > Capacity)
                return Fail("仓库容量不足：在途订单和搬运退回位置也占用仓容。", out reason);
            if ((long)stock.purchased + product.batchSize > int.MaxValue) return Fail("累计采购数量超过支持范围。", out reason);
            orders.Add(new DeliveryOrder
            {
                productId = productId, quantity = product.batchSize,
                orderId = Guid.NewGuid().ToString("N"), awaitingDispatch = true,
                dispatchRemaining = DispatchSeconds, paidAmount = testMode ? 0 : (int)cost,
                remaining = testMode ? 1f : DeliverySeconds
            });
            if (!testMode) cash -= (int)cost;
            stock.purchased += product.batchSize;
            reason = "订单已提交，约 " + (testMode ? "2" : "4") + " 秒到货。";
            return true;
        }

        public bool Expand(out string reason)
        {
            if (warehouseLevel < 1 || warehouseLevel >= MaxWarehouseLevel) return Fail("仓库已达最高等级或等级无效。", out reason);
            int cost = UpgradeCost;
            if (!testMode && cash < cost) return Fail("仓库升级需要 " + cost + " 金币，余额不足。", out reason);
            if (!testMode) cash -= cost;
            warehouseLevel++;
            reason = "仓库已升级，容量 " + Capacity + " 件。";
            return true;
        }

        public bool Assign(ShopCatalog catalog, ShopLayout layout, string shelfId, string productId, out string reason)
        {
            var definition = ShelfDefinition(catalog, layout, shelfId);
            var shelf = Shelf(shelfId);
            var product = Product(catalog, productId);
            if (definition == null || shelf == null || product == null || Stock(productId) == null)
                return Fail("货架或商品不存在，请先同步布局。", out reason);
            if (product.category != definition.id) return Fail("商品类别与货架不匹配。", out reason);
            if (shelf.productId == productId) { reason = "该货架已绑定此商品。"; return true; }
            if (shelf.quantity != 0 || HasRestockFor(shelfId))
                return Fail("货架有商品或正在补货，不能更换商品。", out reason);
            shelf.productId = productId;
            reason = "已绑定商品；签约和采购需另外完成。";
            return true;
        }

        public bool BeginRestock(ShopCatalog catalog, ShopLayout layout, string shelfId, out string reason)
        {
            if (job != null) return Fail("店员已有补货任务。", out reason);
            int quantity;
            if (!RestockQuantity(catalog, layout, shelfId, out quantity, out reason)) return false;
            var shelf = Shelf(shelfId);
            var stock = Stock(shelf.productId);
            if (testMode && stock.available < quantity)
            {
                int grant = quantity - stock.available;
                if ((long)stock.purchased + grant > int.MaxValue) return Fail("测试商品计数达到上限。",out reason);
                stock.available += grant; stock.purchased += grant;
            }
            stock.available -= quantity;
            stock.reserved += quantity;
            job = new RestockJob { shelfId = shelfId, productId = shelf.productId, quantity = quantity };
            reason = "已预留 " + quantity + " 件，前往仓库取货。";
            return true;
        }

        public bool Pickup(out string reason)
        {
            if (job == null) return Fail("没有补货任务。", out reason);
            var required=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(var task in CurrentRestockJobs)
            {
                if(task==null || task.pickedUp || task.quantity<=0 || Stock(task.productId)==null)
                    return Fail("补货任务无效或已取货。",out reason);
                required.TryGetValue(task.productId,out long count);
                required[task.productId]=count+task.quantity;
            }
            foreach(var item in required)
                if(Stock(item.Key).reserved<item.Value)return Fail("仓库预留数量异常，不能取货。",out reason);
            foreach(var task in CurrentRestockJobs){Stock(task.productId).reserved-=task.quantity;task.pickedUp=true;}
            reason = "已一次取齐本趟 "+CurrentCarrying+" 件，依次前往 "+CurrentRestockJobs.Count+" 个柜台。";
            return true;
        }

        public bool CompleteRestock(ShopCatalog catalog, ShopLayout layout, out string reason)
        {
            if (job == null || !job.pickedUp) return Fail("尚未从仓库取货。", out reason);
            var shelf = Shelf(job.shelfId);
            var definition = ShelfDefinition(catalog, layout, job.shelfId);
            var product = Product(catalog, job.productId);
            if (shelf == null || definition == null || product == null || shelf.productId != job.productId || product.category != definition.id)
                return Fail("目标货架已变更，请取消任务退回商品。", out reason);
            if (job.quantity <= 0 || (long)shelf.quantity + job.quantity > ShelfCapacity(definition,shelf))
                return Fail("货架剩余容量不足，请取消任务退回商品。", out reason);
            shelf.quantity += job.quantity;
            jobs.Remove(job);
            reason = "补货完成。";
            return true;
        }

        public void CancelRestock()
        {
            string previous=activeRestockPostId;
            var owners=new HashSet<string>();foreach(var task in jobs)owners.Add(task.postId??"");
            foreach(var owner in owners){activeRestockPostId=owner;CancelCurrentRestock();}
            activeRestockPostId=previous;
        }

        public void CancelCurrentRestock()
        {
            if (job == null) return;
            var returning=new Dictionary<string,long>(StringComparer.Ordinal);
            var reserved=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(var task in CurrentRestockJobs)
            {
                if(task==null || task.quantity<=0 || Stock(task.productId)==null)
                    throw new InvalidOperationException("补货任务数据异常；必须先校验存档。");
                returning.TryGetValue(task.productId,out long count);returning[task.productId]=count+task.quantity;
                reserved.TryGetValue(task.productId,out count);reserved[task.productId]=count+(task.pickedUp?0:task.quantity);
            }
            foreach(var item in returning)
                if((long)Stock(item.Key).available+item.Value>int.MaxValue || Stock(item.Key).reserved<reserved[item.Key])
                    throw new InvalidOperationException("补货退回数量异常；无法无损恢复此任务。");
            foreach(var item in returning)
            {Stock(item.Key).available+=(int)item.Value;Stock(item.Key).reserved-=(int)reserved[item.Key];}
            jobs.RemoveAll(IsCurrentRestockJob);
        }

        public void Tick(float dt)
        {
            if (!Finite(dt) || dt <= 0f || orders == null) return;
            TickOrders(dt);
            FlushReturns();
        }

        public string NextRestock(ShopCatalog catalog, ShopLayout layout)
        {
            if ((!autoRestock && !IsPerforming && !ActiveRestockFollowUp) || job != null || shelves == null || !Finite(autoRestockThreshold) || autoRestockThreshold < 0f || autoRestockThreshold > 1f) return null;
            // Do not alter a pending whole-store round when its settings are malformed.
            foreach (var shelf in shelves) if (shelf == null || !ValidShelfRestockSetting(shelf)) return null;
            foreach (var shelf in shelves)
            {
                var definition = ShelfDefinition(catalog, layout, shelf.instanceId);
                int quantity;
                string reason;
                // Overrides decide whether to depart. An already-triggered round still
                // services every shortage, even a shelf above its own trigger threshold.
                // The small tolerance keeps exact percentage boundaries inclusive after float storage.
                if (definition != null && (ActiveRestockFollowUp ||
                    (double)shelf.quantity / ShelfCapacity(definition,shelf) < .5) &&
                    RestockQuantity(catalog, layout, shelf.instanceId, out quantity, out reason)) return shelf.instanceId;
            }
            ActiveRestockFollowUp=false;
            return null;
        }

        /// <summary>Apply to a cloned economy before committing a layout edit. Never discards stock.</summary>
        public bool SyncShelves(ShopCatalog catalog, ShopLayout layout, out string reason)
        {
            if (catalog == null || catalog.products == null || layout == null || layout.facilities == null || shelves == null)
                return Fail("布局或商品数据不可用。", out reason);
            var proposed = new List<ShelfStock>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var placement in layout.facilities)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.instanceId) || !ids.Add(placement.instanceId))
                    return Fail("设施实例为空或重复。", out reason);
                var definition = catalog.Find(placement.definitionId);
                if (definition == null) return Fail("设施定义不存在。", out reason);
                if (definition.kind != FacilityKind.Shelf) continue;
                var shelf = Shelf(placement.instanceId);
                if (shelf == null)
                {
                    var product = catalog.products.Find(item => item != null && item.category == definition.id);
                    if (product == null) return Fail("货架类别没有可绑定商品。", out reason);
                    shelf = new ShelfStock { instanceId = placement.instanceId, productId = product.id };
                }
                var bound = Product(catalog, shelf.productId);
                if (bound == null || bound.category != definition.id || shelf.quantity < 0 || shelf.quantity > ShelfCapacity(definition,shelf) ||
                    !ValidShelfRestockSetting(shelf))
                    return Fail("货架绑定或库存与布局不一致。", out reason);
                proposed.Add(shelf);
            }
            var previousIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shelf in shelves)
            {
                if (shelf == null || !previousIds.Add(shelf.instanceId)) return Fail("货架库存记录为空或重复。", out reason);
                if (proposed.Exists(item => item.instanceId == shelf.instanceId)) continue;
                if (shelf.quantity != 0 || HasRestockFor(shelf.instanceId))
                    return Fail("有货或正在补货的货架不能移除。", out reason);
            }
            shelves = proposed;
            reason = string.Empty;
            return true;
        }

        public bool Validate(ShopCatalog catalog, ShopLayout layout, out string reason)
        {
            if (contentVersion != 1 || version != 1 || cash < 0 || warehouseLevel < 1 || warehouseLevel > MaxWarehouseLevel ||
                !Finite(autoRestockThreshold) || autoRestockThreshold < 0f || autoRestockThreshold > 1f)
                return Fail("经济版本、余额、仓库等级或补货阈值无效。", out reason);
            if (catalog == null || catalog.products == null || layout == null || layout.facilities == null || stocks == null || orders == null || shelves == null)
                return Fail("经济记录或内容列表缺失。", out reason);
            if (jobs == null || jobs.Count>layout.width*layout.depth)
                return Fail("补货任务列表缺失或超过设施数量上限。", out reason);
            var products = new Dictionary<string, ProductDefinition>(StringComparer.Ordinal);
            foreach (var product in catalog.products)
            {
                if (product == null || string.IsNullOrWhiteSpace(product.id) || products.ContainsKey(product.id) ||
                    string.IsNullOrWhiteSpace(product.category) || product.buyPrice <= 0 || product.sellPrice < 0 || product.batchSize <= 0)
                    return Fail("商品目录无效或 ID 重复。", out reason);
                products.Add(product.id, product);
            }
            if (products.Count == 0 || stocks.Count != products.Count) return Fail("商品库存记录数量不匹配。", out reason);
            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            long warehouse = 0, transit = 0;
            foreach (var stock in stocks)
            {
                if (stock == null || string.IsNullOrEmpty(stock.productId) || !products.ContainsKey(stock.productId) || totals.ContainsKey(stock.productId) ||
                    stock.available < 0 || stock.reserved < 0 || stock.purchased < 0 || stock.sold < 0 || stock.returned < 0 || (!stock.contracted && stock.purchased != 0))
                    return Fail("商品库存含重复 ID、负数或未签约采购。", out reason);
                long physical = (long)stock.available + stock.reserved;
                warehouse += physical;
                totals.Add(stock.productId, physical + stock.sold + stock.returned);
            }
            var orderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var order in orders)
            {
                if (order == null || string.IsNullOrEmpty(order.productId) || !products.ContainsKey(order.productId) || !Stock(order.productId).contracted ||
                    order.quantity != products[order.productId].batchSize || !ValidOrderState(order) ||
                    (!string.IsNullOrEmpty(order.orderId) && !orderIds.Add(order.orderId)))
                    return Fail("配送订单商品、数量、阶段、金额、编号或时间无效。", out reason);
                transit += order.quantity;
                totals[order.productId] += order.quantity;
            }
            var expected = new Dictionary<string, FacilityDefinition>(StringComparer.Ordinal);
            var placements = new HashSet<string>(StringComparer.Ordinal);
            foreach (var placement in layout.facilities)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.instanceId) || !placements.Add(placement.instanceId))
                    return Fail("布局设施 ID 无效或重复。", out reason);
                var definition = catalog.Find(placement.definitionId);
                if (definition == null) return Fail("布局引用未知设施。", out reason);
                if (definition.kind == FacilityKind.Shelf) expected.Add(placement.instanceId, definition);
            }
            if (expected.Count != shelves.Count) return Fail("货架库存与布局数量不匹配，请同步布局。", out reason);
            var shelfIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shelf in shelves)
            {
                FacilityDefinition definition;
                ProductDefinition product;
                if (shelf == null || string.IsNullOrEmpty(shelf.instanceId) || !shelfIds.Add(shelf.instanceId) || !expected.TryGetValue(shelf.instanceId, out definition) ||
                    string.IsNullOrEmpty(shelf.productId) || !products.TryGetValue(shelf.productId, out product) || product.category != definition.id ||
                    definition.capacity <= 0 || shelf.level<1 || shelf.level>3 || shelf.quantity < 0 || shelf.quantity > ShelfCapacity(definition,shelf) ||
                    !ValidShelfRestockSetting(shelf))
                    return Fail("货架库存类别、容量、补货阈值或实例记录无效。", out reason);
                totals[shelf.productId] += shelf.quantity;
            }
            var taskShelves=new HashSet<string>(StringComparer.Ordinal);
            var reservedTotals=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(var task in jobs)
            {
                if(task==null || !taskShelves.Add(task.shelfId))
                    return Fail("补货任务为空或重复指定柜台。",out reason);
                var shelf = Shelf(task.shelfId);
                FacilityDefinition definition;
                if (task.quantity <= 0 || shelf == null || string.IsNullOrEmpty(task.productId) || !products.ContainsKey(task.productId) ||
                    !Stock(task.productId).contracted || shelf.productId != task.productId || !expected.TryGetValue(task.shelfId, out definition) ||
                    (long)shelf.quantity + task.quantity > ShelfCapacity(definition,shelf) || (!string.IsNullOrEmpty(task.postId) && Post(task.postId)?.role!=FacilityKind.Restock) ||
                    jobs.Exists(other=>other!=null && (other.postId??"")==(task.postId??"") && other.pickedUp!=task.pickedUp))
                    return Fail("补货任务与货架、商品或空位不一致。", out reason);
                if (task.pickedUp) totals[task.productId] += task.quantity;
                else {reservedTotals.TryGetValue(task.productId,out long reserved);reservedTotals[task.productId]=reserved+task.quantity;}
            }
            foreach (var stock in stocks)
            {
                totals[stock.productId] += BasketQuantity(stock.productId);
                reservedTotals.TryGetValue(stock.productId,out long reserved);
                if (stock.reserved != reserved) return Fail("仓库预留量与本趟补货清单不一致。", out reason);
                if (totals[stock.productId] != stock.purchased) return Fail("商品数量不守恒：" + stock.productId, out reason);
            }
            if (!ValidateTrading(catalog, out reason)) return false;
            if (!ValidatePopularity(out reason) || !ValidateStaffPosts(catalog,layout,out reason) ||
                !ValidateEnvironment(layout,out reason) || !ValidateBalance(out reason) || !ValidateRenovationInventory(layout,out reason) || !ValidateFixtures(catalog, layout, out reason) || !ValidateOffline(catalog, out reason) ||
                !ValidateGrowth(layout, out reason) || !ValidateRoster(out reason) ||
                !ValidatePerformance(catalog,layout,out reason)) return false;
            if (!testMode && warehouse + transit + Carrying > Capacity) return Fail("含在途及搬运退回预留的仓容超限。", out reason);
            reason = string.Empty;
            return true;
        }

        private bool RestockQuantity(ShopCatalog catalog, ShopLayout layout, string shelfId, out int quantity, out string reason)
        {
            quantity = 0;
            var shelf = Shelf(shelfId);
            var definition = ShelfDefinition(catalog, layout, shelfId);
            if (shelf == null || definition == null) return Fail("货架不存在。", out reason);
            if(HasRestockFor(shelfId))return Fail("该柜台已有补货员负责。",out reason);
            if(!RestockWorkerShelfAllowed(shelfId))return Fail("该柜台由另一补货岗位负责。",out reason);
            var product = Product(catalog, shelf.productId);
            var stock = Stock(shelf.productId);
            if (product == null || stock == null || product.category != definition.id) return Fail("商品类别与货架不匹配。", out reason);
            int capacity=ShelfCapacity(definition,shelf);
            if (shelf.quantity < 0 || definition.capacity <= 0 || shelf.quantity >= capacity) return Fail("货架已满或容量无效。", out reason);
            if (!stock.contracted) return Fail("请先签约该商品。", out reason);
            if (!testMode && stock.available <= 0) return Fail("仓库缺货，请采购并等待送达。", out reason);
            quantity = testMode ? capacity - shelf.quantity : Math.Min(stock.available, capacity - shelf.quantity);
            reason = string.Empty;
            return true;
        }

        private static ProductDefinition Product(ShopCatalog catalog, string id)
        {
            return catalog == null || catalog.products == null ? null : catalog.products.Find(item => item != null && item.id == id);
        }

        private static FacilityDefinition ShelfDefinition(ShopCatalog catalog, ShopLayout layout, string id)
        {
            if (catalog == null || layout == null || layout.facilities == null) return null;
            var placement = layout.facilities.Find(item => item != null && item.instanceId == id);
            var definition = placement == null ? null : catalog.Find(placement.definitionId);
            return definition != null && definition.kind == FacilityKind.Shelf ? definition : null;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Fail(string message, out string reason) { reason = message; return false; }
        private static int Bounded(long value) { return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value)); }
    }
}
