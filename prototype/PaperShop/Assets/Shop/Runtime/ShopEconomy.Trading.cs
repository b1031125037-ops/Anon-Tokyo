using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    [Serializable] public sealed class CustomerBasketLine
    {
        public string productId;
        public int quantity;
        public int unitPrice;
    }

    [Serializable] public sealed class CustomerBasket
    {
        public string transactionId;
        // These fields retain the earlier single-item save format. When lines are present
        // they must be empty: a physical item is recorded in only one representation.
        public string productId;
        public int quantity;
        public int unitPrice;
        public List<CustomerBasketLine> lines = new List<CustomerBasketLine>();
        // Version zero is a basket from before checkout-owned pricing: its promise is retained.
        public int pricingVersion;
        public bool checkoutQuoted;
        public string checkoutId;
    }

    public sealed partial class ShopEconomy
    {
        public bool testMode;
        public bool shopOpen;
        public int transactions;
        public long revenue;
        public int salesTestVersion;
        public List<CustomerBasket> baskets = new List<CustomerBasket>();

        public void EnableTestResources()
        {
            testMode = true;
            foreach (var stock in stocks) stock.contracted = true;
        }

        public bool TakeFromShelf(ShopCatalog catalog, string shelfId, string transactionId, out string reason)
        {
            var shelf = Shelf(shelfId);
            if (string.IsNullOrWhiteSpace(transactionId) || baskets == null || baskets.Exists(b => b != null && b.transactionId == transactionId))
                return Fail("购物篮交易编号无效或重复。", out reason);
            if (shelf == null || shelf.quantity < 1) return Fail("货架缺货，顾客没有拿走商品。", out reason);
            var product = Product(catalog,shelf.productId);
            if (product == null) return Fail("商品定义不存在。",out reason);
            shelf.quantity--;
            baskets.Add(new CustomerBasket { transactionId=transactionId,productId=product.id,quantity=1,unitPrice=ShelfPrice(catalog,shelfId) });
            reason="顾客已拿取商品，等待收银后确认收入。";
            return true;
        }

        /// <summary>
        /// Takes exactly the requested quantity, or changes nothing. A customer can append
        /// several products while shopping. New baskets freeze their price at checkout;
        /// an imported older basket keeps the price it already promised.
        /// </summary>
        public bool AddToBasket(ShopCatalog catalog, string shelfId, string transactionId, int quantity, out string reason)
        {
            if (string.IsNullOrWhiteSpace(transactionId) || baskets == null || quantity <= 0)
                return Fail("购物篮交易编号或拿取数量无效。", out reason);
            var shelf = Shelf(shelfId);
            if (shelf == null || shelf.quantity < quantity)
                return Fail("柜台商品不足，本次未拿取商品。", out reason);
            var product = Product(catalog, shelf.productId);
            if (product == null || Stock(product.id) == null)
                return Fail("商品定义或库存不存在。", out reason);
            int unitPrice = ShelfPrice(catalog, shelfId);
            if (unitPrice < 0) return Fail("商品报价无效。", out reason);

            var basket = baskets.Find(value => value != null && value.transactionId == transactionId);
            var proposed = new List<CustomerBasketLine>();
            if (basket != null)
            {
                if (basket.checkoutQuoted) return Fail("已进入付款队列的购物篮不能追加商品。", out reason);
                Dictionary<string, long> quantities;
                long amount;
                if (!ReadBasket(basket, out quantities, out amount, out reason)) return false;
                long total = 0;
                foreach (var count in quantities.Values) total += count;
                if (total + quantity > int.MaxValue || amount > long.MaxValue - (long)quantity * unitPrice)
                    return Fail("购物篮数量或金额超过支持范围。", out reason);
                foreach (var line in BasketLines(basket))
                    proposed.Add(new CustomerBasketLine { productId = line.productId, quantity = line.quantity, unitPrice = line.unitPrice });
            }

            var match = proposed.Find(line => line.productId == product.id && line.unitPrice == unitPrice);
            if (match == null)
                proposed.Add(new CustomerBasketLine { productId = product.id, quantity = quantity, unitPrice = unitPrice });
            else
                match.quantity += quantity;

            // All checks and cloning precede the inventory mutation.
            if (basket == null)
            {
                basket = new CustomerBasket { transactionId = transactionId, pricingVersion = 1 };
                baskets.Add(basket);
            }
            basket.productId = null;
            basket.quantity = 0;
            basket.unitPrice = 0;
            basket.lines = proposed;
            shelf.quantity -= quantity;
            reason = "已拿取 " + quantity + " 件商品，购物篮共 " + GetBasketQuantity(transactionId) + " 件，等待收银。";
            return true;
        }

        public bool QuoteBasketAtCheckout(ShopCatalog catalog, string transactionId, string checkoutId, out string reason)
        {
            var basket = baskets == null ? null : baskets.Find(value => value != null && value.transactionId == transactionId);
            if (basket == null) return Fail("购物篮不存在。", out reason);
            Dictionary<string, long> quantities;
            long amount;
            if (!ReadBasket(basket, out quantities, out amount, out reason)) return false;
            if (basket.pricingVersion == 0 || basket.checkoutQuoted)
            { reason = "沿用已承诺的结账价格。"; return true; }
            var post = Post(checkoutId);
            if (post == null || post.role != FacilityKind.Checkout)
                return Fail("收银台不存在，不能冻结结账价格。", out reason);
            var proposed = new List<CustomerBasketLine>();
            long total = 0;
            foreach (var line in basket.lines)
            {
                if (Product(catalog, line.productId) == null)
                    return Fail("购物篮商品定义不存在，不能冻结报价。", out reason);
                int price = CheckoutPrice(catalog, line.productId, checkoutId);
                if (price < 0 || total > long.MaxValue - (long)line.quantity * price)
                    return Fail("结账报价超过支持范围。", out reason);
                total += (long)line.quantity * price;
                proposed.Add(new CustomerBasketLine { productId = line.productId, quantity = line.quantity, unitPrice = price });
            }
            basket.lines = proposed;
            basket.checkoutQuoted = true;
            basket.checkoutId = checkoutId;
            reason = "整篮结账价已冻结：" + total;
            return true;
        }

        public int GetBasketQuantity(string transactionId)
        {
            var basket = baskets == null ? null : baskets.Find(value => value != null && value.transactionId == transactionId);
            long total = 0;
            if (basket != null) foreach (var line in BasketLines(basket)) if (line != null) total += line.quantity;
            return Bounded(total);
        }

        public long GetBasketAmount(string transactionId)
        {
            var basket = baskets == null ? null : baskets.Find(value => value != null && value.transactionId == transactionId);
            Dictionary<string, long> quantities;
            long amount;
            string reason;
            return basket != null && ReadBasket(basket, out quantities, out amount, out reason) ? amount : 0;
        }

        /// <summary>All unpaid ownership of one SKU, including legacy single-item baskets.</summary>
        public long BasketQuantity(string productId)
        {
            long total = 0;
            if (baskets != null) foreach (var basket in baskets)
                if (basket != null) foreach (var line in BasketLines(basket))
                    if (line != null && line.productId == productId) total += line.quantity;
            return total;
        }

        public bool PayBasket(string transactionId, out string reason)
        {
            var basket=baskets == null ? null : baskets.Find(b=>b != null && b.transactionId==transactionId);
            if(basket==null)return Fail("订单已经结算或不存在。",out reason);
            Dictionary<string, long> quantities;
            long amount;
            if (!ReadBasket(basket, out quantities, out amount, out reason)) return false;
            foreach (var item in quantities)
                if ((long)Stock(item.Key).sold + item.Value > int.MaxValue)
                    return Fail("交易累计超过支持范围。", out reason);
            double decorExact=amount*DecorationRate+decorationRemainder;
            long decorBonus=(long)Math.Floor(decorExact);
            if(amount>long.MaxValue-decorBonus)return Fail("交易累计超过支持范围。",out reason);
            long receipt=amount+decorBonus;
            if(decorationRevenue>long.MaxValue-decorBonus||revenue>long.MaxValue-receipt || transactions==int.MaxValue || (!testMode && receipt>int.MaxValue-(long)cash))
                return Fail("交易累计超过支持范围。",out reason);
            foreach (var item in quantities) Stock(item.Key).sold += (int)item.Value;
            revenue+=receipt;decorationRevenue+=decorBonus;decorationRemainder=decorExact-decorBonus;lastDecorationBonus=(int)decorBonus; transactions++;
            RecordSuccessfulVisit();
            RecordFeverSale();
            if(!testMode)cash+=(int)receipt;
            baskets.Remove(basket);
            reason="成交 +"+amount+(decorBonus>0?" · 装饰奖励 +"+decorBonus:"");
            return true;
        }

        public void ReturnBasket(string transactionId)
        {
            var basket=baskets == null ? null : baskets.Find(b=>b != null && b.transactionId==transactionId);
            if(basket==null)return;
            Dictionary<string, long> quantities;
            long amount;
            string reason;
            if (!ReadBasket(basket, out quantities, out amount, out reason))
                throw new InvalidOperationException("购物篮数据异常，不能安全退货：" + reason);
            foreach (var item in quantities)
                if ((long)Stock(item.Key).returned + item.Value > int.MaxValue)
                    throw new InvalidOperationException("购物篮退货数量超过支持范围。");
            foreach (var item in quantities) Stock(item.Key).returned += (int)item.Value;
            baskets.Remove(basket);
            FlushReturns();
        }

        public void ReturnAllBaskets()
        { while(baskets.Count>0)ReturnBasket(baskets[0].transactionId); }

        void FlushReturns()
        {
            if(stocks==null)return;
            foreach(var stock in stocks)
            {
                int room=testMode?int.MaxValue-stock.available:Math.Max(0,Capacity-WarehouseCommitted);
                int count=Math.Min(room,stock.returned);
                stock.available+=count; stock.returned-=count;
            }
        }

        bool ValidateTrading(ShopCatalog catalog,out string reason)
        {
            if(baskets==null || transactions<0 || revenue<0)return Fail("交易统计或购物篮记录缺失。",out reason);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var basket in baskets)
            {
                if(basket==null || string.IsNullOrWhiteSpace(basket.transactionId) || !ids.Add(basket.transactionId))
                    return Fail("购物篮商品、交易编号或数量无效。",out reason);
                Dictionary<string, long> quantities;
                long amount;
                if (!ReadBasket(basket, out quantities, out amount, out reason)) return false;
            }
            long sold=0; foreach(var stock in stocks)sold+=stock.sold;
            long onlineSold=sold-offlineUnitsSold,onlineRevenue=revenue-offlineRevenue;
            if(onlineSold<transactions || onlineRevenue<0 || (transactions==0 && (onlineSold!=0 || onlineRevenue!=0)))
                return Fail("每单至少售出一件，成交单数与已售记录不一致。",out reason);
            reason="";return true;
        }

        static IEnumerable<CustomerBasketLine> BasketLines(CustomerBasket basket)
        {
            if (basket.lines != null && basket.lines.Count > 0)
            {
                foreach (var line in basket.lines) yield return line;
            }
            else
                yield return new CustomerBasketLine { productId = basket.productId, quantity = basket.quantity, unitPrice = basket.unitPrice };
        }

        bool ReadBasket(CustomerBasket basket, out Dictionary<string, long> quantities, out long amount, out string reason)
        {
            quantities = new Dictionary<string, long>(StringComparer.Ordinal);
            amount = 0;
            bool multiline = basket.lines != null && basket.lines.Count > 0;
            if (basket.pricingVersion < 0 || basket.pricingVersion > 1 ||
                (basket.pricingVersion == 1 && !multiline) ||
                (basket.checkoutQuoted && string.IsNullOrWhiteSpace(basket.checkoutId)))
                return Fail("购物篮报价版本或收银绑定无效。", out reason);
            if ((multiline && (!string.IsNullOrEmpty(basket.productId) || basket.quantity != 0 || basket.unitPrice != 0)) ||
                (!multiline && basket.quantity != 1))
                return Fail("购物篮旧记录与商品清单混用，或旧记录数量无效。", out reason);
            long totalQuantity = 0;
            foreach (var line in BasketLines(basket))
            {
                if (line == null || string.IsNullOrWhiteSpace(line.productId) || Stock(line.productId) == null ||
                    line.quantity <= 0 || line.unitPrice < 0)
                    return Fail("购物篮商品、数量或报价无效。", out reason);
                totalQuantity += line.quantity;
                long lineAmount = (long)line.quantity * line.unitPrice;
                if (totalQuantity > int.MaxValue || amount > long.MaxValue - lineAmount)
                    return Fail("购物篮数量或金额超过支持范围。", out reason);
                amount += lineAmount;
                long previous;
                quantities.TryGetValue(line.productId, out previous);
                quantities[line.productId] = previous + line.quantity;
            }
            reason = "";
            return true;
        }
    }
}
