using System;
using System.Collections.Generic;
namespace AnonTokyo.Shop
{
    public static class ShopBalance
    {
        public static readonly int[] Costs={20,26,65,24,16,36,30};
        public static readonly int[] Prices={36,46,110,44,30,64,54};
        public static double EntryChance(int popularity)=>.28+.27*Math.Min(100,Math.Max(0,popularity))/100.0;
        public static double PerformanceEntryChance(int popularity)=>.85+.10*Math.Min(100,Math.Max(0,popularity))/100.0;
        public static double ShoppingChance(int popularity)=>.62+.20*Math.Min(100,Math.Max(0,popularity))/100.0;
        public static double DisplayPurchaseChance(int popularity)=>.80+.10*Math.Min(100,Math.Max(0,popularity))/100.0;
        public const double AdditionalKindRetention=.65;
        public static double PurchaseChance(int popularity,bool guided,double performanceBonus,int boughtKinds)=>Math.Min(.95,DisplayPurchaseChance(popularity)+(guided?.04:0)+performanceBonus)*Math.Pow(AdditionalKindRetention,Math.Max(0,boughtKinds));
        public static double ExtraUnitChance(int guideBonus)=>Math.Min(.35,Math.Max(0,guideBonus)/100.0);
        public static double ExtraKindChance(int guideBonus)=>Math.Min(.38,.12+Math.Max(0,guideBonus)*.007);
        public static int DecorationFor(string id)=>id=="stage"||id=="warehouse"?0:id=="decor_flamingo"?25:id=="decor_ottoman"?15:10;
    }
    public sealed partial class ShopEconomy
    {
        public int balanceVersion;
        public List<string> freeDecorationIds=new List<string>();
        public double decorationRemainder;
        public long decorationRevenue;
        [NonSerialized] public int currentDecorationValue,lastDecorationBonus;
        public void InitializeBalance(ShopLayout layout)
        {
            if(balanceVersion==0)
            {
                foreach(var f in layout.facilities)
                    if(f.definitionId=="checkout"||f.definitionId=="reception"&&f.instanceId=="reception-1"||f.definitionId=="restock"&&f.instanceId=="restock-1"||f.definitionId=="top"&&f.instanceId=="top-1")freeDecorationIds.Add(f.instanceId);
                balanceVersion=1;
            }
            RefreshDecoration(layout);
        }
        public int DecorationForFacility(PlacedFacility f)=>freeDecorationIds.Contains(f.instanceId)?0:ShopBalance.DecorationFor(f.definitionId);
        public int DecorationValue(ShopLayout layout)
        {int value=0;foreach(var f in layout.facilities)value+=DecorationForFacility(f);return value;}
        public void RefreshDecoration(ShopLayout layout)=>currentDecorationValue=DecorationValue(layout);
        public double DecorationRate=>Math.Min(.05,currentDecorationValue*.0005);
        public bool OwnsDisplayFor(ShopCatalog catalog,string productId)
        {
            var p=Product(catalog,productId);if(p==null)return false;
            return shelves.Exists(s=>Product(catalog,s.productId)?.category==p.category)||storedFixtures.Exists(f=>f.definitionId==p.category);
        }
        public float CheckoutSeconds=>4.5f-Math.Min(100,Math.Max(0,WorkingAttribute(PostFor(FacilityKind.Checkout))))*.0075f;
        bool ValidateBalance(out string reason)
        {
            if(balanceVersion<0||balanceVersion>1||freeDecorationIds==null||freeDecorationIds.Count>64||decorationRevenue<0||double.IsNaN(decorationRemainder)||decorationRemainder<0||decorationRemainder>=1)return Fail("经营数据无效。",out reason);
            var ids=new HashSet<string>();foreach(string id in freeDecorationIds)if(string.IsNullOrEmpty(id)||!ids.Add(id))return Fail("初始设施记录无效。",out reason);
            reason="";return true;
        }
    }
}
