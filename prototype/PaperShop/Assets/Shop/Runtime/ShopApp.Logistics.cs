using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public int WarehousePickups {get;private set;}
        public bool ContractProduct(string id)
        {
            bool ok = Economy.Contract(catalog, id, out string message);
            LastMessage = message; dirty |= ok;PlaySfx(ok?"purchase":"error"); return ok;
        }

        public bool OrderProduct(string id)
        {
            bool ok = Economy.Order(catalog, id, out string message);
            LastMessage = message; dirty |= ok;PlaySfx(ok?"purchase":"error"); return ok;
        }

        public bool ExpandWarehouse()
        {
            bool ok = Economy.Expand(out string message);
            if (ok) { dirty = true; World.SetWarehouseLevel(Economy.warehouseLevel); PlaySfx("upgrade"); }
            LastMessage = message; return ok;
        }

        public void SetAutoRestock(bool enabled)
        {
            Economy.autoRestock = enabled; dirty = true;
            LastMessage = enabled ? "自动补货已开启：低于容量 50% 时触发。" : "自动补货已关闭，已触发的整轮补货会继续；停止本轮可取消补货。";
        }

        public void SetRestockThreshold(float threshold)
        {
            if(float.IsNaN(threshold) || float.IsInfinity(threshold))return;
            Economy.autoRestockThreshold=.5f;autoDelay=0;dirty=true;
            LastMessage="补货阈值固定为低于容量 50%。";
        }

        public bool SetShelfRestockThreshold(string shelfId, float? threshold)
        {
            bool ok=Economy.SetShelfRestockThreshold(shelfId,threshold,out string message);
            if(ok){autoDelay=0;dirty=true;}
            LastMessage=message;return ok;
        }

        public bool OpenShelfSettings(string shelfId)
        {
            if(Economy.Shelf(shelfId)==null || !Layout.facilities.Exists(f=>f.instanceId==shelfId))
            {LastMessage="柜台不存在，无法打开设置。";return false;}
            Select(shelfId);
            if(panelKey!="shelf")OpenPanel("shelf");
            return true;
        }

        public bool SetShelfProduct(string shelfId, string productId)
        {
            bool ok = Economy.Assign(catalog, Layout, shelfId, productId, out string message);
            LastMessage = message; dirty |= ok;PlaySfx(ok?"purchase":"error"); return ok;
        }

        public bool RequestRestock(string shelfId)
        {
            if (!string.IsNullOrEmpty(placingDefinition)) { LastMessage = "请先完成摆放。"; return false; }
            var shelf = Layout.facilities.Find(p => p.instanceId == shelfId);
            if (shelf == null || Economy.Shelf(shelfId)==null) { LastMessage = "货架不存在。"; return false; }
            bool extraQueued=QueueAdditionalRestock();
            if(!HasRestockStaff)
            {
                LastMessage=extraQueued?"第二补货员已收到全店缺货检查任务。":"请先为补货员工点安排员工。";
                dirty|=extraQueued;return extraQueued;
            }
            var warehouse = Layout.facilities.Find(p => p.definitionId == "warehouse");
            var pickup = ShopModel.ServiceCell(warehouse, catalog.Find("warehouse"));
            var destination = ShopModel.ServiceCell(shelf, catalog.Find(shelf.definitionId));
            var start = path.Count > 0 ? path[0] : ActorCell;
            if (ShopModel.FindPath(Layout, catalog, start, pickup).Count == 0 ||
                ShopModel.FindPath(Layout, catalog, pickup, destination).Count == 0)
            { LastMessage = "仓库取货口或货架服务点不可到达，请先整理通路。"; return false; }
            if (!Economy.BeginRestockBatch(catalog, Layout, Economy.EffectiveCarryLimit, shelfId, out string reason))
            { LastMessage = extraQueued?"第二补货员已收到任务；第一补货员暂无缺货可补。":reason;dirty|=extraQueued;return extraQueued; }
            restockPhase = RestockPhase.ToWarehouse;
            if (!RouteActor(pickup)) { CancelRestockTask(); return false; }
            dirty = true; LastMessage = reason; return true;
        }

        public bool RequestRestockBatch()
        {
            if(Economy.shelves.Count==0){LastMessage="店铺没有售卖柜台。";return false;}
            return RequestRestock(Economy.shelves[0].instanceId);
        }

        public void CancelRestockTask()
        {
            Economy.restockFollowUp=false;
            foreach(var post in Economy.staffPosts)post.restockFollowUp=false;
            if (Economy.jobs.Count==0) return;
            Economy.CancelRestock(); restockPhase = RestockPhase.None;
            World.SetCarrying(false); StopAtCurrentCell(); autoDelay = 5f; dirty = true;
            LastMessage = "补货已取消，预留或搬运的商品已全部退回仓库。";
        }

        void AdvanceRestock(float dt)
        {
            if (dt <= 0 || !IsRestocking || path.Count > 0) return;
            if (restockPhase == RestockPhase.ToWarehouse)
            {
                if (ActorCell != ShopModel.WarehousePickup(Layout)) { CancelRestockTask(); return; }
                restockPhase = RestockPhase.Picking; taskRemaining = 1.2f;
                World.Character.SetWorking(true); LastMessage = "在仓库门口取货…";
            }
            else if (restockPhase == RestockPhase.Picking)
            {
                taskRemaining -= dt * Economy.WorkEfficiency(FacilityKind.Restock);
                if (taskRemaining > 0) return;
                // Shopping and a carrying-limit edit may change the plan while walking.
                // Recheck every counter at the actual pickup, committing only a complete plan.
                var refreshed=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
                string preferred=Economy.job.shelfId;
                refreshed.CancelCurrentRestock();
                if(!refreshed.BeginRestockBatch(catalog,Layout,refreshed.EffectiveCarryLimit,preferred,out string planReason))
                {CancelRestockTask();LastMessage=planReason;return;}
                Economy=refreshed;
                if (!Economy.Pickup(out string reason)) { CancelRestockTask(); LastMessage = reason; return; }
                WarehousePickups++;PlaySfx("pickup");
                World.SetCarrying(true); World.Character.SetWorking(false);
                restockPhase = RestockPhase.ToShelf;
                var shelf = Layout.facilities.Find(p => p.instanceId == Economy.job.shelfId);
                if (shelf == null || !RouteActor(ShopModel.ServiceCell(shelf, catalog.Find(shelf.definitionId))))
                { CancelRestockTask(); return; }
                LastMessage = "已取出 " + Economy.CurrentCarrying + " 件，正送往货架。"; dirty = true;
            }
            else if (restockPhase == RestockPhase.ToShelf)
            {
                var shelf = Layout.facilities.Find(p => p.instanceId == Economy.job.shelfId);
                if (shelf == null || ActorCell != ShopModel.ServiceCell(shelf, catalog.Find(shelf.definitionId)))
                { CancelRestockTask(); return; }
                restockPhase = RestockPhase.Stocking; taskRemaining = 1.4f;
                World.Character.SetWorking(true); LastMessage = "正在把商品摆上货架…";
            }
            else if (restockPhase == RestockPhase.Stocking)
            {
                taskRemaining -= dt * Economy.WorkEfficiency(FacilityKind.Restock);
                if (taskRemaining > 0) return;
                int count = Economy.job.quantity;
                bool ok = Economy.CompleteRestock(catalog, Layout, out string reason);
                if(ok){RestockJobsCompleted++;PlaySfx("stock");}
                if (!ok) CancelRestockTask();
                World.Character.SetWorking(false);
                if(ok && Economy.job!=null)
                {
                    restockPhase=RestockPhase.ToShelf;
                    var next=Layout.facilities.Find(p=>p.instanceId==Economy.job.shelfId);
                    if(next==null || !RouteActor(ShopModel.ServiceCell(next,catalog.Find(next.definitionId))))
                    {CancelRestockTask();return;}
                    dirty=true;LastMessage="已补 "+count+" 件，携带剩余 "+Economy.CurrentCarrying+" 件前往下一柜台。";
                    return;
                }
                restockPhase = RestockPhase.None; World.SetCarrying(false);
                dirty = true; autoDelay = .6f;
                LastMessage = ok ? "补货完成：" + count + " 件商品已上架。" : reason;
            }
        }

        void RemoveFacility(string id)
        {
            if(!CanRenovate){LastMessage="请先关店，等待顾客付款离店后再装修。";return;}
            if(Economy.Shelf(id)!=null){StoreSalesCounter(id);return;}
            var draft = ShopModel.Clone(Layout);
            var state = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            if (!ShopModel.TryRemove(draft, catalog, id, out string reason) ||
                !state.SyncShelves(catalog, draft, out reason) ||
                !state.SyncStaffPosts(catalog,draft,out reason)) { LastMessage = reason; return; }
            // Commit both state owners together, only after the whole edit is accepted.
            CancelRestockTask(); StopAtCurrentCell();
            state.ReturnAllBaskets();
            state.CancelRestock(); Economy = state; Layout = draft; dirty = true;
            Select(null); World.Rebuild(Layout, catalog); ResetGuide(); LastMessage = "设施已移除，库存和通路检查通过。";
        }

        string TaskText()
        {
            if (!IsRestocking) return "店员待命";
            string name = catalog.products.Find(p => p.id == Economy.job.productId)?.title ?? "商品";
            string phase = restockPhase == RestockPhase.ToWarehouse ? "前往仓库" :
                restockPhase == RestockPhase.Picking ? "门口取货" : restockPhase == RestockPhase.ToShelf ? "搬运到货架" : "上架整理";
            return phase + "  ·  " + name + " × " + Economy.job.quantity+" · 本趟剩 "+Economy.CurrentRestockJobs.Count+" 柜";
        }

        void DrawTaskBar(float width, float height)
        {
            float y = height - (Selected() != null ? 286 : 174);
            Rect box = new Rect(width*.5f-300,y,600,48); PanelBox(box);
            GUI.Label(new Rect(box.x+15,box.y+12,430,28),TaskText(),body);
            if (ArtButton(new Rect(box.x+466,box.y+8,119,32),"取消补货",button)) CancelRestockTask();
        }

        string ProductName(string id) => catalog.products.Find(p => p.id == id)?.title ?? id;
        public string ProductPriceText(string id)
        {
            var product=catalog.products.Find(p=>p.id==id);
            return product==null?"尚未陈列商品":"成本 "+product.buyPrice+" · 售价 "+Economy.CheckoutPrice(catalog,id)+" 金币/件";
        }

        void DrawProducts(Rect box)
        {
            for(int i=-1;i<ShopProductSet.Categories.Length;i++)
                if(ArtButton(new Rect(box.x+22+(i+1)*72,box.y+63,66,31),i<0?"全部":ShopProductSet.CategoryNames[i],productCategory==i?activeButton:button)){productCategory=i;scroll=Vector2.zero;}

            var products=ShopProductSet.CurrentProducts(catalog);if(productCategory>=0)products.RemoveAll(p=>p.category!=ShopProductSet.Categories[productCategory]);
            scroll=GUI.BeginScrollView(new Rect(box.x+20,box.y+112,576,336),scroll,new Rect(0,0,553,products.Count*112));
            for(int i=0;i<products.Count;i++)
            {
                var p=products[i];var stock=Economy.Stock(p.id);float y=i*112;
                if((TutorialStep==1||TutorialStep==2)&&p.id==TutorialProduct||(TutorialStep==26||TutorialStep==27)&&p.id==SecondTutorialProduct)RegisterTutorialTarget(new Rect(box.x+385,box.y+124+y-scroll.y,177,42),new Rect(box.x+20,box.y+112,576,336));
                DrawProductIcon(new Rect(8,y+4,64,64),p.id);
                GUI.Label(new Rect(84,y+3,268,30),p.title,body);
                int arriving=0;foreach(var order in Economy.orders)if(order.productId==p.id)arriving+=order.quantity;
                GUI.Label(new Rect(84,y+32,268,25),ProductPriceText(p.id),small);
                GUI.Label(new Rect(84,y+57,268,25),"仓库 "+(Economy.testMode?"∞":stock.available.ToString()),small);
                var delivery=Economy.orders.Find(o=>o.productId==p.id);
                if(delivery!=null)GUI.Label(new Rect(84,y+82,268,24),(delivery.awaitingDispatch?"待发货":"运输中")+" · "+arriving+" 件 · "+DeliveryEta(delivery).ToString("0")+" 秒",small);
                bool owns=Economy.OwnsDisplayFor(catalog,p.id);GUI.enabled=owns;
                if(!owns){ArtButton(new Rect(365,y+12,177,42),"需先拥有对应展柜",button);}
                else if(!stock.contracted)
                {if(ArtButton(new Rect(365,y+12,177,42),"签约 · 80 金币",button))ContractProduct(p.id);}
                else if(ArtButton(new Rect(365,y+12,177,42),"购入 "+p.batchSize+" 件 · "+(Economy.testMode?"免费":p.batchSize*p.buyPrice+" 金币"),activeButton))OrderProduct(p.id);
                GUI.enabled=true;
            }
            GUI.EndScrollView();
        }

        void DrawWarehouse(Rect box)
        {
            GUI.Label(new Rect(box.x+22,box.y+60,570,30),"仓库 Lv."+Economy.warehouseLevel+" · "+(Economy.testMode?"容量 ∞":Economy.WarehouseCommitted+" / "+Economy.Capacity),heading);
            GUI.enabled=Economy.warehouseLevel<ShopEconomy.MaxWarehouseLevel;
            if(ArtButton(new Rect(box.x+22,box.y+110,570,38),"扩容 +200 · "+(Economy.testMode?"免费":Economy.UpgradeCost+" 金币"),activeButton))ExpandWarehouse();GUI.enabled=true;
            var stocks=Economy.stocks.FindAll(s=>ShopProductSet.CurrentProducts(catalog).Exists(p=>p.id==s.productId)||s.available>0);
            scroll=GUI.BeginScrollView(new Rect(box.x+20,box.y+180,576,210),scroll,new Rect(0,0,553,stocks.Count*52));
            for(int i=0;i<stocks.Count;i++){var stock=stocks[i];DrawProductIcon(new Rect(8,i*52,44,44),stock.productId);GUI.Label(new Rect(64,i*52+7,479,29),ProductName(stock.productId)+" · 可用 "+stock.available,body);}GUI.EndScrollView();
            if(ArtButton(new Rect(box.x+22,box.y+405,570,36),"查看配送订单",button))OpenPanel("orders");
        }

        void DrawShelf(Rect box)
        {
            var f=Selected();var shelf=f==null?null:Economy.Shelf(f.instanceId);if(shelf==null)return;
            var definition=catalog.Find(f.definitionId);int capacity=Economy.ShelfCapacity(catalog,Layout,f.instanceId);
            GUI.Label(new Rect(box.x+22,box.y+65,570,34),definition.title+" · Lv."+shelf.level,heading);
            DrawProductIcon(new Rect(box.x+22,box.y+105,48,48),shelf.productId);
            GUI.Label(new Rect(box.x+82,box.y+115,500,32),ProductName(shelf.productId),body);
            GUI.Label(new Rect(box.x+22,box.y+161,570,38),"售价 "+Economy.CheckoutPrice(catalog,shelf.productId)+" 金币/件",heading);
            GUI.Label(new Rect(box.x+22,box.y+218,570,65),"库存 "+shelf.quantity+" / "+capacity+"\n"+(Economy.HasRestockFor(f.instanceId)?"补货中":shelf.quantity*2<capacity?"等待补货":"库存充足"),body);
            GUI.Label(new Rect(box.x+22,box.y+300,570,32),shelf.level<3?"升级后容量："+definition.capacity*(shelf.level+1):"已满级",body);
            GUI.enabled=shelf.level<3;
            if(ArtButton(new Rect(box.x+22,box.y+350,570,40),shelf.level<3?"升级 · "+(Economy.testMode?"免费":ShopEconomy.ShelfUpgradeCost(shelf.level)+" 金币"):"已满级",activeButton))UpgradeSalesCounter(f.instanceId);
            GUI.enabled=true;
        }
    }
}
