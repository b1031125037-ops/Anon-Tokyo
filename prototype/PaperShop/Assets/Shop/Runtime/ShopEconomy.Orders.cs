using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        /// <summary>Cancel only an identified, paid-but-not-dispatched order as one transaction.</summary>
        public bool CancelOrder(string orderId, out string reason)
        {
            if (string.IsNullOrWhiteSpace(orderId) || orders == null)
                return Fail("订单不存在或不支持取消。", out reason);
            DeliveryOrder target = null;
            foreach (var order in orders)
            {
                if (order == null || order.orderId != orderId) continue;
                if (target != null) return Fail("订单编号重复，不能安全退款。", out reason);
                target = order;
            }
            if (target == null) return Fail("订单已完成、已取消或不存在。", out reason);
            if (!target.awaitingDispatch) return Fail("订单已经发货，不能取消。", out reason);
            var stock = Stock(target.productId);
            if (!ValidOrderState(target) || stock == null || !stock.contracted || stock.purchased < target.quantity)
                return Fail("订单或库存账本异常，不能安全退款。", out reason);
            if ((long)cash + target.paidAmount > int.MaxValue)
                return Fail("退款后余额超过支持范围，暂不能取消。", out reason);

            // Refund the recorded amount, independent of later prices or test-mode changes.
            // Purchased is the ledger of retained merchandise; cancelled units have no owner.
            cash += target.paidAmount;
            stock.purchased -= target.quantity;
            orders.Remove(target);
            reason = "已取消订单，原额退还 " + target.paidAmount + " 金币，并释放 " + target.quantity + " 件仓容。";
            return true;
        }

        private static bool ValidOrderState(DeliveryOrder order)
        {
            if (order == null || order.quantity <= 0 || order.paidAmount < 0 ||
                !Finite(order.remaining) || order.remaining <= 0f || order.remaining > DeliverySeconds ||
                !Finite(order.dispatchRemaining))
                return false;
            if (string.IsNullOrEmpty(order.orderId))
                return !order.awaitingDispatch && order.dispatchRemaining == 0f && order.paidAmount == 0;
            if (string.IsNullOrWhiteSpace(order.orderId) || order.orderId.Length > 64) return false;
            return order.awaitingDispatch
                ? order.dispatchRemaining > 0f && order.dispatchRemaining <= 5f
                : order.dispatchRemaining == 0f;
        }

        private void TickOrders(float dt)
        {
            // Check every record and combined arrival before mutating timers or merchandise.
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var arrivals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var order in orders)
            {
                if (!ValidOrderState(order) || Stock(order.productId) == null ||
                    (!string.IsNullOrEmpty(order.orderId) && !ids.Add(order.orderId)))
                    throw new InvalidOperationException("配送数据异常，请先校验存档。");
                float transportTime = order.awaitingDispatch ? Math.Max(0f, dt - order.dispatchRemaining) : dt;
                if (order.remaining > transportTime) continue;
                arrivals.TryGetValue(order.productId, out long count);
                arrivals[order.productId] = count + order.quantity;
            }
            foreach (var arrival in arrivals)
                if ((long)Stock(arrival.Key).available + arrival.Value > int.MaxValue)
                    throw new InvalidOperationException("到货数量超过支持范围。");
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                var order = orders[i];
                float transportTime = dt;
                if (order.awaitingDispatch)
                {
                    transportTime = Math.Max(0f, dt - order.dispatchRemaining);
                    order.dispatchRemaining = Math.Max(0f, order.dispatchRemaining - dt);
                    if (order.dispatchRemaining > 0f) continue;
                    order.awaitingDispatch = false;
                }
                order.remaining = Math.Max(0f, order.remaining - transportTime);
                if (order.remaining > 0f) continue;
                Stock(order.productId).available += order.quantity;
                orders.RemoveAt(i);
            }
        }
    }
}
