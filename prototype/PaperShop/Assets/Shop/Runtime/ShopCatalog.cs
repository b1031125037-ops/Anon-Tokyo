using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public enum FacilityKind { Shelf, Checkout, Warehouse, Decor, Stage, Reception, Restock }

    [Serializable]
    public sealed class FacilityDefinition
    {
        public string id;
        public string title;
        public FacilityKind kind;
        public int width = 1;
        public int depth = 1;
        public int capacity;
        // Relative to the unrotated footprint's lower-left cell, including cells outside it.
        public Vector2Int serviceOffset;
        public Color color = Color.white;
        public GameObject modelPrefab;
    }

    [Serializable]
    public sealed class ProductDefinition
    {
        public string id;
        public string title;
        public string category;
        public int buyPrice;
        public int sellPrice;
        public int batchSize;
        public GameObject modelPrefab;
        public Texture2D icon;
    }

    [Serializable]
    public sealed class StaffDefinition
    {
        public string id;
        public string title;
        public string role;
        public bool initiallyAvailable;
        public string band, profile, sourceUrl;
        public int cashier, restocking, hospitality, contractLevel, contractCost;
    }

    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "Paper Shop/Shop Catalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        // Asset migration marker; zero on pre-migration catalogs, three after the east-wall warehouse update.
        public int layoutDefinitionVersion;
        public GameObject entrancePrefab;
        public List<FacilityDefinition> facilities = new List<FacilityDefinition>();
        public List<ProductDefinition> products = new List<ProductDefinition>();
        public List<StaffDefinition> staff = new List<StaffDefinition>();

        public FacilityDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id) || facilities == null) return null;
            return facilities.Find(item => item != null && string.Equals(item.id, id, StringComparison.Ordinal));
        }

        /// <summary>
        /// Creates editable prototype content. Prices, capacities and roles are project values;
        /// this catalog alone does not run sales, inventory, staffing or stage performances.
        /// Prefabs intentionally remain unassigned for the later model-import step.
        /// </summary>
        public static ShopCatalog CreateDefault()
        {
            var catalog = CreateInstance<ShopCatalog>();
            catalog.name = "Paper Shop Default Catalog";
            catalog.layoutDefinitionVersion = 4;
            var pink = new Color(0.79f, 0.55f, 0.62f);
            var sage = new Color(0.59f, 0.68f, 0.55f);
            var cream = new Color(0.87f, 0.78f, 0.62f);
            var blue = new Color(0.58f, 0.68f, 0.74f);
            catalog.facilities = new List<FacilityDefinition>
            {
                Facility("top", "上装货架", FacilityKind.Shelf, 1, 1, 10, pink),
                Facility("shoes", "鞋履货架", FacilityKind.Shelf, 1, 1, 10, sage),
                Facility("head", "头饰货架", FacilityKind.Shelf, 1, 1, 10, cream),
                Facility("bottom", "下装货架", FacilityKind.Shelf, 2, 1, 10, blue),
                Facility("checkout", "收银台", FacilityKind.Checkout, 1, 1, 0, new Color(0.69f, 0.56f, 0.56f)),
                Facility("warehouse", "右后墙仓库", FacilityKind.Warehouse, 3, 4, 200, new Color(0.68f, 0.67f, 0.59f)),
                Facility("decor", "装饰预留位", FacilityKind.Decor, 1, 1, 0, sage),
                Facility("stage", "舞台预留位", FacilityKind.Stage, 3, 4, 0, new Color(0.37f, 0.48f, 0.56f)),
                Facility("fitting", "试衣间", FacilityKind.Decor, 1, 1, 0, new Color(0.78f, 0.72f, 0.72f))
            };
            catalog.Find("warehouse").serviceOffset = new Vector2Int(-1,3);
            EnsureStaffDefinitions(catalog);ShopEnvironmentAssets.Ensure(catalog);
            catalog.products = new List<ProductDefinition>
            {
                Product("top_tshirt", "简约短袖", "top", 5, 10, 20),
                Product("top_shirt", "清新衬衫", "top", 5, 10, 20),
                Product("top_jacket", "轻便外套", "top", 5, 10, 20),
                Product("shoes_canvas", "帆布鞋", "shoes", 5, 10, 20),
                Product("shoes_loafers", "乐福鞋", "shoes", 5, 10, 20),
                Product("shoes_boots", "短靴", "shoes", 5, 10, 20),
                Product("head_ribbon", "缎带发饰", "head", 5, 10, 20),
                Product("head_beret", "贝雷帽", "head", 5, 10, 20),
                Product("head_hairband", "细边发箍", "head", 5, 10, 20),
                Product("bottom_skirt", "百褶裙", "bottom", 5, 10, 20),
                Product("bottom_shorts", "休闲短裤", "bottom", 5, 10, 20),
                Product("bottom_trousers", "直筒长裤", "bottom", 5, 10, 20)
            };
            catalog.staff = new List<StaffDefinition>
            {
                Staff("anon", "千早爱音", "补货", true),
                Staff("soyo", "长崎爽世", "导购", true),
                Staff("taki", "椎名立希", "收银", true),
                Staff("tomori", "高松灯", "导购", false),
                Staff("rana", "要乐奈", "补货", false)
            };
            ShopStaffProfiles.Ensure(catalog);
            ShopProductSet.Ensure(catalog);
            return catalog;
        }

        public static void EnsureStaffDefinitions(ShopCatalog catalog)
        {
            ShopProductSet.Ensure(catalog);
            ShopStaffProfiles.Ensure(catalog);
            if(catalog.Find("reception")==null)
                catalog.facilities.Add(Facility("reception","接待点",FacilityKind.Reception,1,1,0,new Color(.48f,.72f,.57f)));
            if(catalog.Find("restock")==null)
                catalog.facilities.Add(Facility("restock","补货员工点",FacilityKind.Restock,1,1,0,new Color(.76f,.54f,.64f)));
            catalog.Find("reception").serviceOffset=Vector2Int.zero;
            catalog.Find("restock").serviceOffset=Vector2Int.zero;
        }

        private static FacilityDefinition Facility(string id, string title, FacilityKind kind,
            int width, int depth, int capacity, Color color)
        {
            return new FacilityDefinition
            {
                id = id, title = title, kind = kind, width = width, depth = depth,
                capacity = capacity, color = color,
                serviceOffset = kind == FacilityKind.Decor || kind == FacilityKind.Stage
                    ? Vector2Int.zero : new Vector2Int(0, -1)
            };
        }

        private static ProductDefinition Product(string id, string title, string category,
            int buyPrice, int sellPrice, int batchSize)
        {
            return new ProductDefinition
            {
                id = id, title = title, category = category,
                buyPrice = buyPrice, sellPrice = sellPrice, batchSize = batchSize
            };
        }

        private static StaffDefinition Staff(string id, string title, string role, bool available)
        {
            return new StaffDefinition { id = id, title = title, role = role, initiallyAvailable = available };
        }
    }
}
