using System;
using System.Collections.Generic;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public static class ShopEnvironmentAssets
    {
        public static readonly string[] Decorations={"decor_flamingo","decor_ottoman"};
        public static int Price(string id)=>id=="stage_mujica"?1500:id=="decor_flamingo"?300:id=="decor_ottoman"?200:0;
        public static bool IsDecoration(string id)=>Array.IndexOf(Decorations,id)>=0;
        public static void Ensure(ShopCatalog catalog)
        {
            foreach(var d in new[]{
                new FacilityDefinition{id="stage_mujica",title="Ave Mujica 舞台",kind=FacilityKind.Stage,width=3,depth=4},
                new FacilityDefinition{id="decor_flamingo",title="火烈鸟摆件",kind=FacilityKind.Decor,width=1,depth=1},
                new FacilityDefinition{id="decor_ottoman",title="软凳",kind=FacilityKind.Decor,width=1,depth=1}})
                if(catalog.Find(d.id)==null)catalog.facilities.Add(d);
            catalog.Find("stage").title="MyGO!!!!! 舞台";
        }
    }
    [Serializable] public sealed class DecorationStock {public string definitionId;public int owned;}
    public sealed partial class ShopEconomy
    {
        public int environmentVersion;
        public bool mujicaStageOwned;
        public List<DecorationStock> decorations=new List<DecorationStock>();
        public void InitializeEnvironment()
        {if(environmentVersion==0){stageOwned=true;environmentVersion=1;}}
        public int DecorationOwned(string id)=>decorations.Find(x=>x.definitionId==id)?.owned??0;
        public bool BuyEnvironment(string id,out string reason)
        {
            InitializeEnvironment();
            if(id=="stage_mujica")
            {if(ShopLevel<3)return Fail("店铺 Lv.3 解锁。",out reason);if(mujicaStageOwned)return Fail("已经拥有这款舞台。",out reason);}
            else if(!ShopEnvironmentAssets.IsDecoration(id))return Fail("该物品不可购买。",out reason);
            if(ShopEnvironmentAssets.IsDecoration(id)&&DecorationOwned(id)>=99)return Fail("持有已达上限。",out reason);
            int cost=ShopEnvironmentAssets.Price(id);if(!testMode&&cash<cost)return Fail("金币不足。",out reason);
            if(!testMode)cash-=cost;
            if(id=="stage_mujica")mujicaStageOwned=true;
            else {var stock=decorations.Find(x=>x.definitionId==id);if(stock==null){stock=new DecorationStock{definitionId=id};decorations.Add(stock);}stock.owned++;}
            reason="已放入装修库存。";return true;
        }
        bool ValidateEnvironment(ShopLayout layout,out string reason)
        {
            if(environmentVersion<0||environmentVersion>1||decorations==null||decorations.Count>2)return Fail("环境物品库存无效。",out reason);
            var seen=new HashSet<string>();
            foreach(var stock in decorations)
                if(stock==null||!ShopEnvironmentAssets.IsDecoration(stock.definitionId)||!seen.Add(stock.definitionId)||stock.owned<0||stock.owned>99)return Fail("装饰物库存无效。",out reason);
            foreach(string id in ShopEnvironmentAssets.Decorations)
                if(layout.facilities.FindAll(f=>f.definitionId==id).Count>DecorationOwned(id))return Fail("装饰物摆放数量超过已拥有数量。",out reason);
            if(layout.facilities.Exists(f=>f.definitionId=="stage_mujica")&&!mujicaStageOwned)return Fail("尚未拥有 Ave Mujica 舞台。",out reason);
            reason="";return true;
        }
    }
}
