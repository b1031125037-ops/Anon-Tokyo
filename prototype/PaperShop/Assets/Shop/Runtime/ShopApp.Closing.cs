using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        bool closingWasPending;
        public bool IsClosing => Economy!=null && !Economy.shopOpen && (guests.Count>0 || Economy.IsPerforming);
        public bool CanRenovate => Economy!=null && !Economy.shopOpen && guests.Count==0 && !Economy.IsPerforming;
        public string BusinessStateText => Economy!=null && Economy.shopOpen?"营业中":IsClosing?"关店中 · 等待结账／表演结束":"已关店 · 可以装修";
        public string CheckoutBlockReason
        {
            get
            {
                if(!HasCashier)return "收银员未上岗，请在员工页配置收银岗位。";
                var checkout=Layout.facilities.Find(f=>f.definitionId=="checkout");
                ShopModel.CanUseCheckoutFront(Layout,catalog,checkout,out string reason);
                return reason;
            }
        }
        string ClosingStatusText => Economy.IsPerforming?"停止迎客，等待舞台结束与客人离店":guests.Count==0?"已关店 · 客人全部离开，可以装修":
            "停止迎客，等待 "+guests.Count+" 位客人结账离店"+
            (!CanCheckoutServe && guests.Exists(g=>g.bought>0 && g.phase!=GuestPhase.Leaving)?"\n收银暂停："+CheckoutBlockReason:"");

        public bool RequestCloseShop(out string reason)
        {
            SetShopOpen(false);reason=ClosingStatusText;return CanRenovate;
        }

        // Legacy saves have no live customers after safe basket restoration, so a
        // blocked checkout can be repaired before admitting any new shoppers.
        public void InitializeBusinessState()
        {
            if(Economy.shopOpen && !CanCheckoutServe)
            {Economy.shopOpen=false;dirty=true;LastMessage="店铺暂未开门："+CheckoutBlockReason;}
            TradingStatus=Economy.shopOpen?"营业中 · 每 6 游戏秒独立判定来客":ClosingStatusText;
        }

        void BeginClosingCustomers()
        {
            closingWasPending=guests.Count>0;
            if(guideGuest!=null)ReleaseGuide(false);
            if(extraGuideGuest!=null)ReleaseExtraGuide(false);
            foreach(var g in guests)
            {
                g.itinerary.Clear();g.guided=false;
                if(g.phase==GuestPhase.CheckoutQueue || g.phase==GuestPhase.Paying || g.phase==GuestPhase.Leaving)continue;
                g.closingRedirect=true;g.view.SetWorking(false);
                // Keep the current edge so paper actors never jump backwards
                // when closure redirects them from shopping to payment.
                if(g.route.Count>1)g.route.RemoveRange(1,g.route.Count-1);
                if(g.route.Count>0){g.destination=g.route[0];g.hasDestination=true;}
                else RedirectClosingGuest(g);
            }
        }

        void RedirectClosingGuest(Guest g)
        {
            g.closingRedirect=false;g.hasDestination=false;g.itinerary.Clear();
            if(g.bought>0 && g.phase!=GuestPhase.Leaving)JoinNextShelf(g);
            else if(g.phase!=GuestPhase.Leaving)LeaveGuest(g,"关店中，未选购客人离店");
        }

        // Test fixtures may discard actors, but no normal gameplay command can
        // turn a close request into free cancellation of an unpaid basket.
        public bool ForceResetCustomersForVerification()
        {
            if(!verifyMode)return false;
            Economy.shopOpen=false;ClearCustomers();closingWasPending=false;ResetCustomerArrival();
            TradingStatus=ClosingStatusText;return true;
        }
    }
}
