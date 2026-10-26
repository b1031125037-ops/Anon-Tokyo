namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        public int popularity;
        public int PopularityCap=>ShopLevel*20;
        public int managementRulesVersion;
        public void MigrateManagementRules()
        {
            if(managementRulesVersion==0)
            {
                int oldPeak=System.Math.Max(peakPopularity,popularity);
                int oldLevel=oldPeak>=100?5:oldPeak>=75?4:oldPeak>=50?3:oldPeak>=20?2:1;
                shopLevel=System.Math.Max(shopLevel,oldLevel);
                popularity=System.Math.Min(shopLevel*10,(int)System.Math.Round(popularity*.4));
                peakPopularity=System.Math.Max((shopLevel-1)*10,popularity);
                managementRulesVersion=1;
            }
            popularity=System.Math.Min(PopularityCap,popularity);
            autoRestockThreshold=.5f;
            foreach(var shelf in shelves){shelf.overrideRestockThreshold=false;shelf.restockThreshold=.5f;}
            foreach(var p in staffPosts)p.carryLimit=0;
        }
        public int customerArrivalRandomState = ShopCustomerFlow.DefaultSeed;

        public void RecordSuccessfulVisit()
        {
            if (popularity < PopularityCap) popularity++;
            RefreshShopLevel();
        }

        public bool RollCustomerArrival(double probability)
        {
            return ShopCustomerFlow.Roll(probability, ref customerArrivalRandomState);
        }

        public bool SetTestPopularity(int value, out string reason)
        {
            if (!testMode) return Fail("人气调试只在测试档开放。", out reason);
            if (value < 0 || value > 100) return Fail("人气范围为 0–100。", out reason);
            RefreshShopLevel();
            popularity = value;
            RefreshShopLevel();
            reason = "测试人气已设为 " + value + "；后续每 6 游戏秒独立判定来客。";
            return true;
        }

        public bool SetTestCustomerFlowSeed(int value, out string reason)
        {
            if (!testMode) return Fail("客流随机种子只在测试档开放。", out reason);
            if (value <= 0 || value >= int.MaxValue) return Fail("客流随机种子范围无效。", out reason);
            customerArrivalRandomState = value;
            reason = "已设置可复现的客流随机状态。";
            return true;
        }

        bool ValidatePopularity(out string reason)
        {
            if(managementRulesVersion<0||managementRulesVersion>1)return Fail("经营规则存档版本无效。",out reason);
            if (popularity < 0 || popularity > 100 || customerArrivalRandomState <= 0 || customerArrivalRandomState >= int.MaxValue)
                return Fail("人气或客流随机状态无效。", out reason);
            reason = "";
            return true;
        }
    }
}
