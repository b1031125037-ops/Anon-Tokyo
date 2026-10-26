using System;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopEconomy
    {
        public int performanceVersion, feverMask, feverMisses, performancesCompleted;
        public int feverRandomState=173913;
        public float performanceRemaining, performanceCooldown;
        public string performingBand="";
        public int performancePreviousSpeed=1;
        public bool IsPerforming=>performanceRemaining>0;
        public bool FeverReady=>feverMask==31 && !IsPerforming && performanceCooldown<=0;
        public const float PerformanceDuration=48.413605f, PerformanceCooldownDuration=120f;
        public static float PerformanceLength(string band)=>band=="Ave Mujica"?50.6195f:PerformanceDuration;
        public bool IsPerformingStaff(string id)=>IsPerforming && ShopStaffProfiles.Find(id)?.band==performingBand;
        public float WorkEfficiency(FacilityKind role)=>StaffEfficiency(role);
        public float WorkEfficiency(StaffPostState post)=>1f;
        public int WorkingAttribute(FacilityKind role)=>WorkingAttribute(PostFor(role));
        public int WorkingAttribute(StaffPostState post)=>Ability(ShopStaffProfiles.Find(EffectiveStaffId(post)),post?.role??FacilityKind.Checkout);
        public float PerformancePurchaseBonus=>IsPerforming?.15f:0f;
        public float PerformanceArrivalMultiplier=>IsPerforming?1.5f:1f;
        public void RecordFeverSale()
        {
            if(IsPerforming||performanceCooldown>0||feverMask==31)return;
            performanceVersion=1;
            uint x=(uint)feverRandomState;x=x*1664525u+1013904223u;feverRandomState=(int)x;
            int slot=(int)((x>>8)%5);
            bool acquired=(x%100)<35 && (feverMask&(1<<slot))==0;
            if(!acquired && ++feverMisses>=5)
            {slot=0;while((feverMask&(1<<slot))!=0)slot++;acquired=true;}
            if(acquired){feverMask|=1<<slot;feverMisses=0;}
        }
        public bool CanStartPerformance(ShopCatalog catalog,ShopLayout layout,string band,out string reason)
        {
            if(!shopOpen)return Fail("请先开店，再开始舞台表演。",out reason);
            if(IsPerforming)return Fail("当前表演尚未结束。",out reason);
            if(performanceCooldown>0)return Fail("表演冷却尚未结束。",out reason);
            if(!FeverReady)return Fail("请先集齐 F、E、V、E、R 五个槽位。",out reason);
            if(UnlockedStaffCount<8)return Fail("至少解锁 8 名员工：5 人演出，至少 3 人留守。当前 "+UnlockedStaffCount+" / 8。",out reason);
            if(ShopLevel<3)return Fail("舞台需要店铺 Lv.3。",out reason);
            if(layout?.facilities.FindAll(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Stage).Count!=1)
                return Fail("请先在关店装修中放置一座表演舞台。",out reason);
            if(band!="MyGO!!!!!" && band!="Ave Mujica")return Fail("演出阵容不存在。",out reason);
            foreach(var s in ShopStaffProfiles.All)
                if(s.band==band&&!IsStaffUnlocked(s.id))return Fail("请先签约 "+s.title+"，该阵容需要五人齐备。",out reason);
            if(PostFor(FacilityKind.Restock)==null || PostFor(FacilityKind.Reception)==null)return Fail("请先放置收银、补货和招待三种基础岗位。",out reason);
            if(!ShopModel.ValidateCheckoutAccess(layout,catalog,out reason))return false;
            if(string.IsNullOrEmpty(AssignedStaffId(FacilityKind.Checkout)))return Fail("需要可用收银岗位。",out reason);
            reason="";return true;
        }
        public bool StartPerformance(ShopCatalog catalog,ShopLayout layout,string band,int previousSpeed,out string reason)
        {
            if(!CanStartPerformance(catalog,layout,band,out reason))return false;
            performanceVersion=1;feverMask=0;feverMisses=0;
            performanceRemaining=PerformanceLength(band);performingBand=band;
            performancePreviousSpeed=previousSpeed==2?2:1;
            reason=band+" 表演开始！";return true;
        }
        public bool TickPerformance(float dt)
        {
            if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return false;
            if(IsPerforming)
            {
                performanceRemaining=Math.Max(0,performanceRemaining-dt);
                if(performanceRemaining==0)
                {
                    performanceCooldown=PerformanceCooldownDuration;performingBand="";
                    performancesCompleted++;
                    popularity=Math.Min(PopularityCap,popularity+5);RefreshShopLevel();
                    return true;
                }
            }
            else performanceCooldown=Math.Max(0,performanceCooldown-dt);
            return false;
        }
        public bool FillFeverForTesting()
        {
            if(!testMode||IsPerforming||performanceCooldown>0)return false;
            performanceVersion=1;feverMask=31;feverMisses=0;return true;
        }
        bool ValidatePerformance(ShopCatalog catalog,ShopLayout layout,out string reason)
        {
            if(performanceVersion<0||performanceVersion>1||feverMask<0||feverMask>31||feverMisses<0||feverMisses>4||
                performancesCompleted<0||performancesCompleted==int.MaxValue||
                float.IsNaN(performanceRemaining)||float.IsInfinity(performanceRemaining)||performanceRemaining<0||performanceRemaining>PerformanceLength(performingBand)+.01f||
                float.IsNaN(performanceCooldown)||float.IsInfinity(performanceCooldown)||performanceCooldown<0||performanceCooldown>120||
                (performancePreviousSpeed!=1&&performancePreviousSpeed!=2))
                return Fail("FEVER 或表演进度无效。",out reason);
            if(performanceVersion==0&&(feverMask!=0||feverMisses!=0||IsPerforming||performanceCooldown>0||performancesCompleted!=0))
                return Fail("表演版本与进度不一致。",out reason);
            if(IsPerforming)
            {
                if(feverMask!=0||performanceCooldown!=0||(performingBand!="MyGO!!!!!"&&performingBand!="Ave Mujica")||
                    layout.facilities.FindAll(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Stage).Count!=1)
                    return Fail("表演阵容或舞台状态无效。",out reason);
                foreach(var s in ShopStaffProfiles.All)if(s.band==performingBand&&!IsStaffUnlocked(s.id))
                    return Fail("演出成员尚未签约。",out reason);
            }
            else if(!string.IsNullOrEmpty(performingBand))return Fail("空闲表演仍占用阵容。",out reason);
            reason="";return true;
        }
    }
}
