using System;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        public bool HasRecoverableGoods=>WarehouseUsed>0||InTransit>0||Carrying>0||stocks.Exists(s=>s.returned>0)||shelves.Exists(s=>s.quantity>0)||baskets.Exists(b=>b!=null&&GetBasketQuantity(b.transactionId)>0);
        long ReachableTaskRewards(bool withNewContract=false)
        {
            long reward=0;
            foreach(var task in GetGrowthTaskStates())
            {
                if(task.claimed)continue;
                if(!task.complete&&!(withNewContract&&task.definition.id=="first_contract"))break;
                reward+=task.definition.reward;
            }
            return reward;
        }
        public bool CanFinanceNewStock(ShopCatalog catalog)
        {
            if(testMode)return true;
            foreach(var p in ShopProductSet.CurrentProducts(catalog))
            {
                bool signed=Stock(p.id).contracted;
                long setup=(signed?0:80)+(OwnsDisplayFor(catalog,p.id)?0:BasicFixtureCost);
                // Only count rewards reachable in order. A small unclaimed reward
                // cannot rescue a shop unless it actually finances a full batch.
                if((long)cash+ReachableTaskRewards()<setup)continue;
                long budget=(long)cash+ReachableTaskRewards(!signed);
                if(budget>=setup+(long)p.buyPrice*p.batchSize)return true;
            }
            return false;
        }
        public bool IsFinanciallyStranded(ShopCatalog catalog)=>!testMode&&!HasRecoverableGoods&&!CanFinanceNewStock(catalog);
    }
}
