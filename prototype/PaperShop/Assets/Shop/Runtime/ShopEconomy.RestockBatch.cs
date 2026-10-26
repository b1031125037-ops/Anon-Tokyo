using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        public bool HasRestockFor(string shelfId)=>jobs!=null && jobs.Exists(task=>task!=null && task.shelfId==shelfId);

        // Snapshot all current shortages once per warehouse trip. Distribute a small
        // carrying budget across shelves so one nearly empty counter cannot starve another.
        public bool BeginRestockBatch(ShopCatalog catalog,ShopLayout layout,int carryLimit,string preferredShelfId,out string reason)
        {
            if(job!=null)return Fail("补货员已有任务。",out reason);
            if(carryLimit<1 || carryLimit>300)return Fail("携带额度无效。",out reason);
            var candidates=new List<RestockJob>();
            var deficits=new Dictionary<string,int>(StringComparer.Ordinal);
            var availability=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var shelf in shelves)
            {
                if(!RestockQuantity(catalog,layout,shelf.instanceId,out int count,out _))continue;
                if(shelf.quantity*2L>=ShelfCapacity(catalog,layout,shelf.instanceId))continue;
                var placed=layout.facilities.Find(p=>p.instanceId==shelf.instanceId);
                var target=ShopModel.ServiceCell(placed,catalog.Find(placed.definitionId));
                if(ShopModel.FindPath(layout,catalog,ShopModel.WarehousePickup(layout),target).Count==0)continue;
                candidates.Add(new RestockJob{postId=activeRestockPostId,shelfId=shelf.instanceId,productId=shelf.productId});
                deficits[shelf.instanceId]=count;
                availability[shelf.productId]=testMode?int.MaxValue:Stock(shelf.productId).available;
            }
            if(candidates.Count==0){ActiveRestockFollowUp=false;return Fail("暂无可补货的柜台，请检查库存与通路。",out reason);}
            candidates.Sort((a,b)=>a.shelfId==b.shelfId?0:a.shelfId==preferredShelfId?-1:b.shelfId==preferredShelfId?1:
                string.CompareOrdinal(a.shelfId,b.shelfId));
            int remaining=carryLimit;
            while(remaining>0)
            {
                bool changed=false;
                foreach(var task in candidates)
                {
                    if(remaining==0)break;
                    if(task.quantity>=deficits[task.shelfId] || availability[task.productId]<=0)continue;
                    task.quantity++;remaining--;availability[task.productId]--;changed=true;
                }
                if(!changed)break;
            }
            candidates.RemoveAll(task=>task.quantity==0);
            var quantities=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var task in candidates)
            {quantities.TryGetValue(task.productId,out int count);quantities[task.productId]=count+task.quantity;}
            foreach(var item in quantities)
            {
                var stock=Stock(item.Key);
                int grant=testMode?Math.Max(0,item.Value-stock.available):0;
                if((long)stock.purchased+grant>int.MaxValue || (long)stock.reserved+item.Value>int.MaxValue)
                    return Fail("补货计数达到上限。",out reason);
            }
            var ordered=OrderRestockRoute(catalog,layout,candidates);
            if(ordered==null)return Fail("本趟补货通路不可到达。",out reason);
            foreach(var item in quantities)
            {
                var stock=Stock(item.Key);
                int grant=testMode?Math.Max(0,item.Value-stock.available):0;
                stock.available+=grant;stock.purchased+=grant;
                stock.available-=item.Value;stock.reserved+=item.Value;
            }
            jobs.AddRange(ordered);
            ActiveRestockFollowUp=false;
            foreach(var shelf in shelves)
            {
                var assigned=jobs.Find(task=>task.shelfId==shelf.instanceId);
                if(RestockQuantity(catalog,layout,shelf.instanceId,out int count,out _) &&
                    count>(assigned==null?0:assigned.quantity))ActiveRestockFollowUp=true;
            }
            // For finite stock, RestockQuantity observes the reservation, so also retain
            // any known unfilled snapshot. The next scan clears it when no stock remains.
            foreach(var need in deficits)
            {
                var assigned=jobs.Find(task=>task.shelfId==need.Key);
                if(need.Value>(assigned==null?0:assigned.quantity))ActiveRestockFollowUp=true;
            }
            reason="本趟检查全店缺货：取 "+(carryLimit-remaining)+" 件，依次补 "+CurrentRestockJobs.Count+" 个柜台"+
                (ActiveRestockFollowUp?"，剩余缺货下趟继续。":"。");
            return true;
        }

        // Exact shortest grid route for up to twelve targets (the prototype uses two).
        // Larger player-built stores use nearest-first to bound frame time.
        static List<RestockJob> OrderRestockRoute(ShopCatalog catalog,ShopLayout layout,List<RestockJob> tasks)
        {
            int n=tasks.Count;
            var points=new Vector2Int[n+1];points[n]=ShopModel.WarehousePickup(layout);
            for(int i=0;i<n;i++)
            {
                var placed=layout.facilities.Find(p=>p.instanceId==tasks[i].shelfId);
                points[i]=ShopModel.ServiceCell(placed,catalog.Find(placed.definitionId));
            }
            var distance=new int[n+1,n+1];
            for(int i=0;i<=n;i++)for(int j=0;j<=n;j++)
            {
                var path=ShopModel.FindPath(layout,catalog,points[i],points[j]);
                if(path.Count==0)return null;
                distance[i,j]=path.Count-1;
            }
            var result=new List<RestockJob>();
            if(n>12)
            {
                var pending=new List<int>();for(int i=0;i<n;i++)pending.Add(i);
                int current=n;
                while(pending.Count>0)
                {
                    int best=pending[0];foreach(int item in pending)if(distance[current,item]<distance[current,best])best=item;
                    result.Add(tasks[best]);pending.Remove(best);current=best;
                }
                return result;
            }
            int states=1<<n;
            var costs=new int[states,n];var previous=new int[states,n];
            for(int mask=0;mask<states;mask++)for(int i=0;i<n;i++){costs[mask,i]=int.MaxValue/4;previous[mask,i]=-1;}
            for(int i=0;i<n;i++)costs[1<<i,i]=distance[n,i];
            for(int mask=1;mask<states;mask++)for(int i=0;i<n;i++)
            {
                if((mask&(1<<i))==0)continue;
                for(int j=0;j<n;j++)
                {
                    if((mask&(1<<j))!=0)continue;
                    int next=mask|(1<<j),cost=costs[mask,i]+distance[i,j];
                    if(cost<costs[next,j]){costs[next,j]=cost;previous[next,j]=i;}
                }
            }
            int end=0,all=states-1;for(int i=1;i<n;i++)if(costs[all,i]<costs[all,end])end=i;
            while(all>0){result.Add(tasks[end]);int next=previous[all,end];all^=1<<end;end=next;}
            result.Reverse();return result;
        }
    }
}
