using System;
using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        [NonSerialized] public string activeRestockPostId="";
        bool IsCurrentRestockJob(RestockJob task)=>(task?.postId??"")== (activeRestockPostId??"");
        public List<RestockJob> CurrentRestockJobs=>jobs.FindAll(IsCurrentRestockJob);
        public int CurrentCarrying {get {int n=0;foreach(var task in CurrentRestockJobs)if(task.pickedUp)n+=task.quantity;return n;}}
        public bool ActiveRestockFollowUp
        {
            get=>string.IsNullOrEmpty(activeRestockPostId)?restockFollowUp:Post(activeRestockPostId)?.restockFollowUp??false;
            set {if(string.IsNullOrEmpty(activeRestockPostId))restockFollowUp=value;
                else if(Post(activeRestockPostId)!=null)Post(activeRestockPostId).restockFollowUp=value;}
        }
        bool RestockWorkerShelfAllowed(string shelfId)
        {
            var workers=staffPosts.FindAll(p=>p.role==FacilityKind.Restock&&!string.IsNullOrEmpty(EffectiveStaffId(p)));
            if(workers.Count<2)return true;
            int index=shelves.FindIndex(s=>s.instanceId==shelfId);
            var first=staffPosts.Find(p=>p.role==FacilityKind.Restock);
            string owner=string.IsNullOrEmpty(activeRestockPostId)?first.instanceId:activeRestockPostId;
            // Stable, disjoint cabinet responsibility prevents both workers reserving
            // the same deficit; each worker still batches all of their missing stock.
            return workers[index%workers.Count].instanceId==owner;
        }
    }
}
