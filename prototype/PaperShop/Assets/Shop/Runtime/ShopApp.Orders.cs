using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public int PendingDeliveryCount => Economy?.orders?.FindAll(o => o.awaitingDispatch).Count ?? 0;

        public bool CancelDeliveryOrder(string orderId)
        {
            bool ok = Economy.CancelOrder(orderId, out string message);
            if (ok) dirty = true;
            LastMessage = message;
            return ok;
        }

        static float DeliveryEta(DeliveryOrder order) =>
            order.remaining + (order.awaitingDispatch ? order.dispatchRemaining : 0f);

        void DrawDeliveryOrders(Rect box)
        {
            int pending = PendingDeliveryCount;
            GUI.Label(new Rect(box.x+22,box.y+57,570,24),
                "待发货 " + pending + " 单  ·  运输中 " + (Economy.orders.Count-pending) + " 单  ·  预留仓容 " + Economy.InTransit + " 件",body);
            GUI.Label(new Rect(box.x+22,box.y+88,570,42),
                "基础商品快速配送，发货前可取消订单。",small);
            if (Economy.orders.Count == 0)
            {
                GUI.Label(new Rect(box.x+22,box.y+170,570,40),"当前没有待收货订单。已到货的商品可在仓库查看。",body);
            }
            else
            {
                var visibleOrders = Economy.orders.ToArray();
                scroll = GUI.BeginScrollView(new Rect(box.x+20,box.y+143,576,237),scroll,
                    new Rect(0,0,553,visibleOrders.Length*96));
                for (int i=0;i<visibleOrders.Length;i++)
                {
                    var order = visibleOrders[i];
                    float y=i*96;
                    DrawProductIcon(new Rect(8,y+4,44,44),order.productId);
                    GUI.Label(new Rect(64,y+4,284,25),ProductName(order.productId)+" × "+order.quantity,body);
                    string status=order.awaitingDispatch
                        ? "待发货 · "+Mathf.CeilToInt(order.dispatchRemaining)+" 秒后发出"
                        : "运输中 · "+Mathf.CeilToInt(order.remaining)+" 秒后到仓";
                    GUI.Label(new Rect(64,y+31,284,24),status,small);
                    GUI.Label(new Rect(64,y+56,284,28),order.awaitingDispatch
                        ? "已付 "+order.paidAmount+" · 撤单退回 "+order.paidAmount+" 金币"
                        : "已预留仓容 · 到货后自动入库",small);
                    GUI.enabled = order.awaitingDispatch && !string.IsNullOrEmpty(order.orderId);
                    if (ArtButton(new Rect(365,y+20,177,42),order.awaitingDispatch?"撤单并退款":"已发货",button))
                        CancelDeliveryOrder(order.orderId);
                    GUI.enabled = true;
                    GUI.DrawTexture(new Rect(8,y+94,536,1),line);
                }
                GUI.EndScrollView();
            }
            if (ArtButton(new Rect(box.x+22,box.y+394,278,38),"返回商品进货",activeButton)) OpenPanel("goods");
            if (ArtButton(new Rect(box.x+314,box.y+394,280,38),"查看仓库",button)) OpenPanel("warehouse");
        }
    }
}
