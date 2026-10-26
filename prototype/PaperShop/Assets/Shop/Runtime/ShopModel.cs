using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AnonTokyo.Shop
{
    [Serializable]
    public sealed class PlacedFacility
    {
        public string instanceId;
        public string definitionId;
        // Lower-left cell of the rotated bounding box, not the prefab pivot.
        public int x;
        public int z;
        public int rotation;
        // Zero retains the legacy corner. Values 1..8 select right-wall cells z=0..7.
        public int wallPosition;
    }

    [Serializable]
    public sealed class ShopLayout
    {
        public int version = 3;
        // These dimensions describe the shop interior, excluding the courtyard and warehouse shell.
        public int width = 8;
        public int depth = 8;
        public int entranceColumn;
        public int surfaceVersion;
        public List<SurfaceFinish> finishes = new List<SurfaceFinish>();
        public List<PlacedFacility> facilities = new List<PlacedFacility>();
    }

    /// <summary>Grid layout and persistence only. No economic or staff simulation is performed.</summary>
    public static class ShopModel
    {
        private const int CurrentVersion = 3;
        private const int MaxDimension = 256;
        private const long MaxSaveBytes = 4 * 1024 * 1024;
        private static Vector2Int[] EntrancesFor(ShopLayout layout) => new[] { new Vector2Int(EntranceColumn(layout)-1, layout.depth-1), new Vector2Int(EntranceColumn(layout), layout.depth-1) };
        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1)
        };

        public static Vector2Int EntranceCell { get { return new Vector2Int(2, 7); } }
        public static Vector2Int SpawnCell { get { return new Vector2Int(2, 6); } }
        public static Vector2Int WarehouseServiceCell { get { return new Vector2Int(7, 7); } }
        public static Vector2Int RoadSpawnCell => new Vector2Int(0, 10);
        public static Vector2Int RoadExitCell => new Vector2Int(8, 10);
        public static int EntranceColumn(ShopLayout layout)=>layout.entranceColumn==0?2:layout.entranceColumn;
        public static Vector2Int EntranceFor(ShopLayout layout) => new Vector2Int(EntranceColumn(layout), layout.depth-1);
        public static Vector2Int RoadSpawnFor(ShopLayout layout) => new Vector2Int(0, layout.depth+2);
        public static Vector2Int RoadExitFor(ShopLayout layout) => new Vector2Int(layout != null ? layout.width : 8, (layout != null ? layout.depth : 8)+2);
        public static Vector2Int WarehousePickup(ShopLayout layout)
        {
            var warehouse=layout.facilities.Find(p=>p.definitionId=="warehouse");
            return new Vector2Int(layout.width-1,warehouse!=null && warehouse.wallPosition>0?warehouse.wallPosition-1:layout.depth-1);
        }
        public static string SavePath { get { return Path.Combine(Application.persistentDataPath, "shop-layout-v3.json"); } }
        public static string LegacySavePath { get { return Path.Combine(Application.persistentDataPath, "shop-layout-v2.json"); } }
        public static bool HasAnySave { get { return HasSaveFiles(SavePath) || HasSaveFiles(LegacySavePath); } }

        public static ShopLayout CreateDefault()
        {
            return new ShopLayout
            {
                facilities = new List<PlacedFacility>
                {
                    Placement("warehouse-1", "warehouse", 8, 4),
                    new PlacedFacility{instanceId="checkout-1",definitionId="checkout",x=3,z=7,rotation=3},
                    Placement("top-1", "top", 3, 5),
                    Placement("bottom-1", "bottom", 3, 3),
                    Placement("head-1", "head", 2, 3),
                    Placement("shoes-1", "shoes", 4, 2),
                    Placement("fitting-1", "fitting", 5, 5),
                    Placement("stage-1", "stage", 5, 0)
                }
            };
        }

        public static Vector2Int Size(FacilityDefinition definition, int rotation)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return (NormalizeRotation(rotation) & 1) == 0
                ? new Vector2Int(definition.width, definition.depth)
                : new Vector2Int(definition.depth, definition.width);
        }

        /// <summary>Positive quarter turns match Quaternion.Euler(0, rotation * 90, 0).</summary>
        public static Vector2Int ServiceCell(PlacedFacility placement, FacilityDefinition definition)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if(definition.kind==FacilityKind.Warehouse)return new Vector2Int(placement.x-1,placement.wallPosition>0?placement.wallPosition-1:7);
            return TransformLocalCell(placement, definition, definition.serviceOffset);
        }

        /// <summary>Customer side is local +Z; it rotates with the checkout footprint.</summary>
        public static Vector2Int CheckoutCustomerCell(PlacedFacility placement, FacilityDefinition definition)
        {
            CheckCheckoutArguments(placement, definition);
            return TransformLocalCell(placement, definition, new Vector2Int((definition.width - 1) / 2, definition.depth));
        }

        /// <summary>Cashier side is local -Z, opposite the paying customer.</summary>
        public static Vector2Int CheckoutStaffCell(PlacedFacility placement, FacilityDefinition definition)
        {
            CheckCheckoutArguments(placement, definition);
            return TransformLocalCell(placement, definition, new Vector2Int((definition.width - 1) / 2, -1));
        }

        // Kept separate from legacy layout validation so older saves can be migrated before use.
        public static bool ValidateCheckoutAccess(ShopLayout layout, ShopCatalog catalog, out string reason)
        {
            if (!Validate(layout, catalog, out reason)) return false;
            foreach (var placement in layout.facilities)
            {
                var definition = catalog.Find(placement.definitionId);
                if (definition.kind != FacilityKind.Checkout) continue;
                foreach (var service in new[] { CheckoutStaffCell(placement, definition) })
                {
                    if (!IsShopCell(layout, service) || IsBlocked(layout, catalog, service) ||
                        layout.facilities.Exists(p=>p!=placement && IsStaffPoint(catalog.Find(p.definitionId).kind) && p.x==service.x && p.z==service.y) ||
                        FindPath(layout, catalog, EntranceFor(layout), service).Count == 0)
                    {
                        reason = "收银台后方员工站点必须留空且可达。";
                        return false;
                    }
                }
            }
            reason = string.Empty;
            return true;
        }

        public static Vector2Int PaymentCell(ShopLayout layout,ShopCatalog catalog,PlacedFacility checkout)
        {
            return CheckoutCustomerCell(checkout,catalog.Find(checkout.definitionId));
        }

        // Placement only reserves the cashier's rear position. Service is a separate
        // rule: pay at the exact local front, including an indoor doorway when clear.
        // Never silently move the payment target to the side or through the back wall.
        public static bool CanUseCheckoutFront(ShopLayout layout,ShopCatalog catalog,PlacedFacility checkout,out string reason)
        {
            if(checkout==null){reason="请先放置收银台。";return false;}
            var front=PaymentCell(layout,catalog,checkout);
            if(!IsShopCell(layout,front))
            {reason="收银台正前方朝向墙外，请旋转收银台。";return false;}
            if(IsBlocked(layout,catalog,front) || front==WarehousePickup(layout) ||
                layout.facilities.Exists(f=>IsStaffPoint(catalog.Find(f.definitionId).kind) && f.x==front.x && f.z==front.y))
            {reason="收银台正前方付款位被占，请移开设施或旋转收银台。";return false;}
            if(FindPath(layout,catalog,EntranceFor(layout),front).Count==0)
            {reason="收银台正前方无法到达，请留出付款通路。";return false;}
            reason="顾客正前方付款，员工后方收银。";return true;
        }

        public static bool IsStaffPoint(FacilityKind kind)=>kind==FacilityKind.Reception || kind==FacilityKind.Restock;

        private static void CheckCheckoutArguments(PlacedFacility placement, FacilityDefinition definition)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.kind != FacilityKind.Checkout) throw new ArgumentException("Expected a checkout definition.", nameof(definition));
        }

        public static Vector2Int TransformLocalCell(PlacedFacility placement, FacilityDefinition definition, Vector2Int local)
        {
            int x = local.x;
            int z = local.y;
            Vector2Int offset;
            switch (NormalizeRotation(placement.rotation))
            {
                case 1: offset = new Vector2Int(z, definition.width - 1 - x); break;
                case 2: offset = new Vector2Int(definition.width - 1 - x, definition.depth - 1 - z); break;
                case 3: offset = new Vector2Int(definition.depth - 1 - z, x); break;
                default: offset = new Vector2Int(x, z); break;
            }
            return new Vector2Int(placement.x, placement.z) + offset;
        }

        public static bool IsInside(ShopLayout layout, Vector2Int cell)
        {
            return layout != null && (IsShopCell(layout, cell) || IsCourtyardCell(layout, cell));
        }

        /// <summary>Only indoor cells can receive player-placed furniture.</summary>
        public static bool IsShopCell(ShopLayout layout, Vector2Int cell)
        {
            return layout != null && cell.x >= 0 && cell.y >= 0 && cell.x < layout.width && cell.y < layout.depth;
        }

        private static bool IsCourtyardCell(ShopLayout layout, Vector2Int cell)
        {
            return cell.x >= -1 && cell.x <= layout.width && cell.y >= layout.depth && cell.y <= layout.depth+3;
        }

        private static bool CanStep(ShopLayout layout, Vector2Int from, Vector2Int to)
        {
            if (!IsInside(layout, from) || !IsInside(layout, to) ||
                Math.Abs(from.x - to.x) + Math.Abs(from.y - to.y) != 1) return false;
            if (IsShopCell(layout, from) == IsShopCell(layout, to)) return true;
            // The north wall can only be crossed through its two door cells.
            return from.x == to.x && (from.x == EntranceColumn(layout)-1 || from.x == EntranceColumn(layout)) &&
                   Math.Min(from.y, to.y) == layout.depth-1 && Math.Max(from.y, to.y) == layout.depth;
        }

        public static bool IsBlocked(ShopLayout layout, ShopCatalog catalog, Vector2Int cell)
        {
            if (!IsInside(layout, cell) || catalog == null || layout.facilities == null) return true;
            foreach (var placement in layout.facilities)
            {
                if (placement == null) return true;
                var definition = catalog.Find(placement.definitionId);
                if (definition == null) return true;
                if(IsStaffPoint(definition.kind) || definition.kind==FacilityKind.Warehouse)continue;
                Vector2Int size = Size(definition, placement.rotation);
                if (cell.x >= placement.x && cell.y >= placement.z &&
                    cell.x < (long)placement.x + size.x && cell.y < (long)placement.z + size.y) return true;
            }
            return false;
        }

        public static bool Validate(ShopLayout layout, ShopCatalog catalog, out string reason)
        {
            HashSet<Vector2Int> blocked;
            if (!TryBuildOccupancy(layout, catalog, out blocked, out reason)) return false;
            foreach (Vector2Int entrance in EntrancesFor(layout))
            {
                Vector2Int outsideDoor = entrance + new Vector2Int(0, 1);
                if (!IsShopCell(layout, entrance) || blocked.Contains(entrance) || blocked.Contains(outsideDoor))
                {
                    reason = "后墙两个入口格及门外通路必须留空。";
                    return false;
                }
            }
            HashSet<Vector2Int> reachable = Flood(layout, blocked, EntrancesFor(layout));
            foreach (var placement in layout.facilities)
            {
                var definition = catalog.Find(placement.definitionId);
                if (!HasService(definition.kind)) continue;
                Vector2Int service = ServiceCell(placement, definition);
                if (!IsShopCell(layout, service))
                {
                    reason = definition.title + "的服务点超出其可服务区域。";
                    return false;
                }
                if (blocked.Contains(service))
                {
                    reason = definition.kind == FacilityKind.Warehouse
                        ? "右墙仓库取货点 "+"("+service.x+","+service.y+")"+" 被设施占据，请先移开该格的家具。"
                        : definition.title + "的服务点被设施占据。";
                    return false;
                }
                if (!reachable.Contains(service))
                {
                    reason = definition.kind == FacilityKind.Warehouse
                        ? "入口无法到达右墙仓库取货点 "+"("+service.x+","+service.y+")"+"，请保留店内取货通路。"
                        : "入口无法到达" + definition.title + "的服务点。";
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        /// <summary>Returns a four-neighbour route including both endpoints, or an empty list.</summary>
        public static List<Vector2Int> FindPath(ShopLayout layout, ShopCatalog catalog, Vector2Int from, Vector2Int to)
        {
            var path = new List<Vector2Int>();
            HashSet<Vector2Int> blocked;
            string reason;
            if (!IsInside(layout, from) || !IsInside(layout, to) ||
                !TryBuildOccupancy(layout, catalog, out blocked, out reason)) return path;
            if (blocked.Contains(from) || blocked.Contains(to)) return path;
            var visited = new HashSet<Vector2Int>();
            var previous = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            visited.Add(from);
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (cell == to)
                {
                    for (Vector2Int step = to; ; step = previous[step])
                    {
                        path.Add(step);
                        if (step == from) break;
                    }
                    path.Reverse();
                    return path;
                }
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = cell + direction;
                    if (!CanStep(layout, cell, next) || blocked.Contains(next) || !visited.Add(next)) continue;
                    previous[next] = cell;
                    queue.Enqueue(next);
                }
            }
            return path;
        }

        public static ShopLayout Clone(ShopLayout layout)
        {
            if (layout == null) return null;
            var copy = new ShopLayout { version = layout.version, width = layout.width, depth = layout.depth, entranceColumn=layout.entranceColumn };
            copy.surfaceVersion=layout.surfaceVersion;copy.finishes=ShopSurfaces.Copy(layout.finishes);
            if (layout.facilities == null) copy.facilities = null;
            else foreach (var placement in layout.facilities) copy.facilities.Add(CopyPlacement(placement));
            return copy;
        }

        /// <summary>Creates the next room size without changing the source layout or charging funds.</summary>
        public static bool TryExpandLayout(ShopCatalog catalog, ShopLayout layout, out ShopLayout expanded, out string reason)
        {
            expanded = null;
            if (!Validate(layout, catalog, out reason)) return false;
            if (layout.width >= 12 && layout.depth >= 12)
            {
                reason = "店铺已完成全部扩建，当前面积为 12×12 格。";
                return false;
            }
            var candidate = Clone(layout);
            if(candidate.width<12)
            {
                candidate.width+=2;candidate.entranceColumn=EntranceColumn(layout)+2;
                foreach(var f in candidate.facilities)if(f.definitionId!="warehouse")f.x+=2;
                foreach(var finish in candidate.finishes)if(finish.kind!="east")finish.x+=2;
            }
            else
            {
                candidate.depth+=2;
                foreach(var finish in candidate.finishes)if(finish.kind!="north")finish.z+=2;
                // Grow toward the open foreground. Shift grid origins together so
                // all existing furniture keeps its relation to the entrance/walls.
                foreach(var f in candidate.facilities)
                    if(f.definitionId!="warehouse")f.z+=2;
                var door=candidate.facilities.Find(f=>f.definitionId=="warehouse");
                door.wallPosition=(door.wallPosition>0?door.wallPosition:layout.depth)+2;
            }
            // Insert cells at the two open edges; occupied cells, entrance and finishes shift together.
            // The warehouse remains attached to the same right-wall position relative to the back corner.
            candidate.facilities.Find(item => item.definitionId == "warehouse").x = candidate.width;
            if (!ValidateCheckoutAccess(candidate, catalog, out reason)) return false;
            expanded = candidate;
            reason = "店铺面积扩为 " + candidate.width + "×" + candidate.depth + " 格，原有陈列相对关系已保留。";
            return true;
        }

        public static bool TryPlace(ShopLayout layout, ShopCatalog catalog, PlacedFacility candidate, out string reason)
        {
            if (layout == null || layout.facilities == null || candidate == null || string.IsNullOrWhiteSpace(candidate.instanceId))
            {
                reason = "缺少布局、设施或实例 ID。";
                return false;
            }
            var proposed = Clone(layout);
            int index = proposed.facilities.FindIndex(item => item != null && item.instanceId == candidate.instanceId);
            if (index >= 0)
            {
                if (proposed.facilities[index].definitionId != candidate.definitionId &&
                    !(catalog.Find(proposed.facilities[index].definitionId)?.kind==FacilityKind.Stage && catalog.Find(candidate.definitionId)?.kind==FacilityKind.Stage))
                {
                    reason = "移动已有设施时不能更换其定义。";
                    return false;
                }
                proposed.facilities[index] = CopyPlacement(candidate);
            }
            else proposed.facilities.Add(CopyPlacement(candidate));
            if (!Validate(proposed, catalog, out reason)) return false;
            layout.facilities = proposed.facilities;
            return true;
        }

        public static bool TryRemove(ShopLayout layout, ShopCatalog catalog, string id, out string reason)
        {
            if (layout == null || layout.facilities == null || catalog == null)
            {
                reason = "布局或设施目录不可用。";
                return false;
            }
            int index = layout.facilities.FindIndex(item => item != null && item.instanceId == id);
            if (index < 0)
            {
                reason = "没有找到该设施。";
                return false;
            }
            var definition = catalog.Find(layout.facilities[index].definitionId);
            if (definition == null)
            {
                reason = "设施定义不存在。";
                return false;
            }
            if (definition.kind == FacilityKind.Warehouse || definition.kind == FacilityKind.Checkout)
            {
                reason = "仓库和收银台是基础设施，不能删除。";
                return false;
            }
            var proposed = Clone(layout);
            proposed.facilities.RemoveAt(index);
            if (!Validate(proposed, catalog, out reason)) return false;
            layout.facilities = proposed.facilities;
            return true;
        }

        public static void Save(ShopLayout layout) { SaveToPath(layout, SavePath); }

        public static ShopLayout Load(ShopCatalog catalog, out string message)
        {
            return LoadWithMigration(catalog, SavePath, LegacySavePath, out message);
        }

        /// <summary>Loads v3 first. A structurally readable but conflicting v2 primary never falls back to older furniture.</summary>
        public static ShopLayout LoadWithMigration(ShopCatalog catalog, string currentPath, string legacyPath, out string message)
        {
            if (HasSaveFiles(currentPath)) return LoadFromPath(catalog, currentPath, out message);
            if (!HasSaveFiles(legacyPath)) { message = "尚无布局存档。"; return null; }
            ShopLayout legacy;
            string primaryReason;
            bool usedBackup = false;
            if (!TryReadRecord(legacyPath, out legacy, out primaryReason, 2))
            {
                string backupReason;
                if (!TryReadRecord(legacyPath + ".bak", out legacy, out backupReason, 2))
                {
                    message = "旧版布局读取失败，原文件已保留。主档：" + primaryReason + "；备份：" + backupReason;
                    return null;
                }
                usedBackup = true;
            }
            ShopLayout migrated;
            string reason;
            if (!TryMigrateV2(legacy, catalog, out migrated, out reason))
            {
                message = "旧版布局迁移未完成，未创建或覆盖存档，也未清空家具。" + reason;
                return null;
            }
            try
            {
                SaveToPath(migrated, currentPath);
                message = "已将" + (usedBackup ? "旧版备份" : "旧版布局") + "迁移到 v3：仅移动仓库，其他家具全部保留；v2 原文件未修改。";
            }
            catch (Exception exception)
            {
                message = "已在内存中迁移并保留全部家具，但 v3 写入失败：" + exception.Message + "。v2 原文件未修改。";
            }
            return migrated;
        }

        public static bool TryMigrateV2(ShopLayout legacy, ShopCatalog catalog, out ShopLayout migrated, out string reason)
        {
            migrated = null;
            if (!CheckRecord(legacy, out reason, 2)) return false;
            int warehouses = 0;
            PlacedFacility warehouse = null;
            foreach (var placement in legacy.facilities)
            {
                if (placement.definitionId != "warehouse") continue;
                warehouses++;
                warehouse = placement;
            }
            if (warehouses != 1 || warehouse.instanceId != "warehouse-1" ||
                warehouse.x != 0 || warehouse.z != 9 || warehouse.rotation != 0)
            {
                reason = "v2 初始仓库记录无效，无法安全迁移。";
                return false;
            }
            var candidate = Clone(legacy);
            candidate.version = CurrentVersion;
            var relocated = candidate.facilities.Find(item => item.instanceId == "warehouse-1");
            relocated.x = 8;
            relocated.z = 4;
            relocated.rotation = 0;
            if (!Validate(candidate, catalog, out reason)) return false;
            migrated = candidate;
            return true;
        }

        private static bool HasSaveFiles(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || File.Exists(path + ".bak"));
        }

        /// <summary>Writes synchronously; the caller can catch IO errors and report that saving failed.</summary>
        public static void SaveToPath(ShopLayout layout, string path)
        {
            string reason;
            if (!CheckRecord(layout, out reason)) throw new ArgumentException(reason, nameof(layout));
            string absolutePath = Path.GetFullPath(path);
            string backupPath = absolutePath + ".bak";
            string json = JsonUtility.ToJson(layout, true);
            ShopLayout previous;
            // Never replace a useful backup with a malformed primary file.
            bool primaryReadable = TryReadRecord(absolutePath, out previous, out reason);
            WriteAtomic(absolutePath, json, primaryReadable ? backupPath : null);
            ShopLayout backup;
            if (!TryReadRecord(backupPath, out backup, out reason)) WriteAtomic(backupPath, json, null);
        }

        /// <summary>Does not create a default layout or overwrite files when neither save is valid.</summary>
        public static ShopLayout LoadFromPath(ShopCatalog catalog, string path, out string message)
        {
            string absolutePath;
            try { absolutePath = Path.GetFullPath(path); }
            catch (Exception exception)
            {
                message = "存档路径无效：" + exception.Message;
                return null;
            }
            ShopLayout primary;
            string primaryReason;
            if (TryReadValid(absolutePath, catalog, out primary, out primaryReason))
            {
                message = "已读取布局存档。";
                return primary;
            }
            ShopLayout backup;
            string backupReason;
            if (TryReadValid(absolutePath + ".bak", catalog, out backup, out backupReason))
            {
                try
                {
                    WriteAtomic(absolutePath, JsonUtility.ToJson(backup, true), null);
                    message = "主存档不可用，已从备份恢复。原因：" + primaryReason;
                }
                catch (Exception exception)
                {
                    message = "已读取备份，但主存档恢复写入失败：" + exception.Message;
                }
                return backup;
            }
            message = "没有可用的布局存档。主档：" + primaryReason + "；备份：" + backupReason;
            return null;
        }

        private static bool TryBuildOccupancy(ShopLayout layout, ShopCatalog catalog, out HashSet<Vector2Int> blocked, out string reason)
        {
            blocked = null;
            if (!CheckRecord(layout, out reason)) return false;
            if (catalog == null || catalog.facilities == null)
            {
                reason = "设施目录不可用。";
                return false;
            }
            var definitions = new Dictionary<string, FacilityDefinition>(StringComparer.Ordinal);
            foreach (var definition in catalog.facilities)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.id) || definitions.ContainsKey(definition.id) ||
                    definition.width < 1 || definition.depth < 1 || definition.width > MaxDimension || definition.depth > MaxDimension ||
                    !Enum.IsDefined(typeof(FacilityKind), definition.kind) ||
                    Math.Abs((long)definition.serviceOffset.x) > MaxDimension || Math.Abs((long)definition.serviceOffset.y) > MaxDimension)
                {
                    reason = "设施目录含重复 ID 或无效尺寸、类型、服务偏移。";
                    return false;
                }
                definitions.Add(definition.id, definition);
            }
            blocked = new HashSet<Vector2Int>();
            var occupied = new HashSet<Vector2Int>();
            var staffRoles = new Dictionary<FacilityKind,int>();
            int warehouseCount = 0, stageCount=0;
            foreach (var placement in layout.facilities)
            {
                FacilityDefinition definition;
                if (!definitions.TryGetValue(placement.definitionId, out definition))
                {
                    reason = "缺少设施定义：" + placement.definitionId;
                    return false;
                }
                Vector2Int size = Size(definition, placement.rotation);
                if(definition.kind==FacilityKind.Stage&&++stageCount>1){reason="店内只能摆放一座舞台，可从装修库存替换款式。";return false;}
                if(IsStaffPoint(definition.kind) || definition.kind==FacilityKind.Checkout || definition.kind==FacilityKind.Stage)
                {
                    staffRoles.TryGetValue(definition.kind,out int count);staffRoles[definition.kind]=count+1;
                    if(count+1>(IsStaffPoint(definition.kind)?2:1)){reason="最多两处同类员工点、一处收银台和一座舞台。";return false;}
                }
                if(IsStaffPoint(definition.kind) && (size!=Vector2Int.one || definition.serviceOffset!=Vector2Int.zero))
                {reason="接待点与补货员工点必须占一格，员工站位在该格中心。";return false;}
                bool warehouse = definition.kind == FacilityKind.Warehouse;
                if (warehouse)
                {
                    warehouseCount++;
                    if (warehouseCount > 1)
                    {
                        reason = "室外仓库仅保留初始一处，不能添加第二个仓库。";
                        return false;
                    }
                    if (placement.instanceId != "warehouse-1" || placement.definitionId != "warehouse" ||
                        placement.x != layout.width || placement.z != 4 || placement.rotation != 0 || placement.wallPosition<0 || placement.wallPosition>layout.depth || (layout.depth>8 && placement.wallPosition==0))
                    {
                        reason = "仓库逻辑锚点必须位于 (" + layout.width + ",4)，门仅可沿右墙移动，不能旋转。";
                        return false;
                    }
                    if (definition.width != 3 || definition.depth != 4 || definition.serviceOffset != new Vector2Int(-1, 3))
                    {
                        reason = "右后墙仓库定义应为 3×4，服务偏移应为 (-1,3)。";
                        return false;
                    }
                    // The legacy exterior size describes hidden storage, not furniture
                    // occupancy. Its doorway service cell is validated separately below.
                    continue;
                }
                else if (!IsShopCell(layout, new Vector2Int(placement.x, placement.z)) ||
                         (long)placement.x + size.x > layout.width || (long)placement.z + size.y > layout.depth)
                {
                    reason = definition.title + "必须完整摆放在 " + layout.width + "×" + layout.depth + " 室内，不能占用门外通路。";
                    return false;
                }
                for (int x = placement.x; x < placement.x + size.x; x++)
                for (int z = placement.z; z < placement.z + size.y; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    if (!occupied.Add(cell))
                    {
                        reason = definition.title + "与其他设施重叠。";
                        return false;
                    }
                    if(IsStaffPoint(definition.kind))
                    {
                        if(Array.IndexOf(EntrancesFor(layout),cell)>=0)
                        {reason="岗位点不能占用入口。";return false;}
                    }
                    else blocked.Add(cell);
                }
            }
            if (warehouseCount != 1)
            {
                reason = "布局必须保留初始室外仓库。";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private static HashSet<Vector2Int> Flood(ShopLayout layout, HashSet<Vector2Int> blocked, IEnumerable<Vector2Int> origins)
        {
            var visited = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (Vector2Int origin in origins)
            {
                if (!IsInside(layout, origin) || blocked.Contains(origin) || !visited.Add(origin)) continue;
                queue.Enqueue(origin);
            }
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = cell + direction;
                    if (!CanStep(layout, cell, next) || blocked.Contains(next) || !visited.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }
            return visited;
        }

        private static bool CheckRecord(ShopLayout layout, out string reason, int expectedVersion = CurrentVersion)
        {
            if (layout == null || layout.version != expectedVersion)
            {
                reason = "布局为空或存档版本不受支持（需要版本 " + expectedVersion + "；旧版布局另存保留）。";
                return false;
            }
            bool supportedWidth = expectedVersion == 2 ? layout.width == 8 :
                (layout.width == 8 && layout.depth == 8) || (layout.width == 10 && layout.depth == 8) || (layout.width == 12 && (layout.depth == 8 || layout.depth == 10 || layout.depth == 12));
            if (!supportedWidth || (expectedVersion == 2 && layout.depth != 8) || layout.facilities == null)
            {
                reason = "布局尺寸须为 8×8、10×8、12×8、12×10 或 12×12，旧版 v2 仅支持 8×8，且必须具有设施列表；仓库区域不计入店铺尺寸。";
                return false;
            }
            if(layout.entranceColumn<0||EntranceColumn(layout)<1||EntranceColumn(layout)>=layout.width){reason="入口列越界。";return false;}
            if (!ShopSurfaces.Validate(layout,out reason)) return false;
            if (layout.facilities.Count > layout.width * layout.depth + 1)
            {
                reason = "设施数量超过地图格数。";
                return false;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var placement in layout.facilities)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.instanceId) ||
                    string.IsNullOrWhiteSpace(placement.definitionId) || !ids.Add(placement.instanceId))
                {
                    reason = "设施实例为空、缺少 ID 或实例 ID 重复。";
                    return false;
                }
                bool fixedWarehouseOrigin = placement.definitionId == "warehouse" &&
                    ((expectedVersion == CurrentVersion && placement.x == layout.width && placement.z == 4) ||
                     (expectedVersion == 2 && placement.x == 0 && placement.z == 9));
                if (placement.rotation < 0 || placement.rotation > 3 ||
                    (!fixedWarehouseOrigin && !IsInside(layout, new Vector2Int(placement.x, placement.z))))
                {
                    reason = "设施坐标或旋转无效：" + placement.instanceId;
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        // No field initializers: missing JSON header fields must fail validation, not inherit defaults.
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

        private static bool TryReadRecord(string path, out ShopLayout layout, out string reason, int expectedVersion = CurrentVersion)
        {
            layout = null;
            try
            {
                if (!File.Exists(path)) { reason = "文件不存在"; return false; }
                if (new FileInfo(path).Length > MaxSaveBytes) { reason = "文件超过布局存档大小限制"; return false; }
                var stored = JsonUtility.FromJson<StoredLayout>(File.ReadAllText(path, Encoding.UTF8));
                if (stored == null) { reason = "存档内容为空"; return false; }
                var candidate = new ShopLayout
                {
                    version = stored.version, width = stored.width, depth = stored.depth, entranceColumn=stored.entranceColumn, facilities = stored.facilities,
                    surfaceVersion=stored.surfaceVersion,finishes=stored.finishes??(stored.surfaceVersion==0?new List<SurfaceFinish>():null)
                };
                if (!CheckRecord(candidate, out reason, expectedVersion)) return false;
                layout = candidate;
                return true;
            }
            catch (Exception exception)
            {
                reason = "读取或解析失败：" + exception.Message;
                return false;
            }
        }

        private static bool TryReadValid(string path, ShopCatalog catalog, out ShopLayout layout, out string reason)
        {
            if (!TryReadRecord(path, out layout, out reason)) return false;
            if (Validate(layout, catalog, out reason)) return true;
            layout = null;
            return false;
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

        private static bool HasService(FacilityKind kind)
        {
            return kind == FacilityKind.Shelf || kind == FacilityKind.Checkout || kind == FacilityKind.Warehouse || IsStaffPoint(kind);
        }

        private static int NormalizeRotation(int rotation) { return ((rotation % 4) + 4) % 4; }

        private static PlacedFacility Placement(string id, string definitionId, int x, int z)
        {
            return new PlacedFacility { instanceId = id, definitionId = definitionId, x = x, z = z, rotation = 0 };
        }

        private static PlacedFacility CopyPlacement(PlacedFacility placement)
        {
            return placement == null ? null : new PlacedFacility
            {
                instanceId = placement.instanceId, definitionId = placement.definitionId,
                x = placement.x, z = placement.z, rotation = placement.rotation, wallPosition = placement.wallPosition
            };
        }
    }
}
