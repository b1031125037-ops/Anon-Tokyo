using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        string performanceBandChoice="MyGO!!!!!";
        string visiblePerformanceBand="";
        bool performanceFinaleStarted;
        readonly List<PaperCharacterView> performers=new List<PaperCharacterView>();
        readonly List<string> performerNames=new List<string>();
        public int VisiblePerformerCount=>performers.Count;
        public void OpenPerformance(){if(panelKey!="performance")OpenPanel("performance");}
        public bool BeginPerformance(string band)
        {
            if(guests.Exists(g=>!string.IsNullOrEmpty(g.visitingStaffId))){LastMessage="请等来访员工结账离店后再集合演出。";return false;}
            if(!Economy.StartPerformance(catalog,Layout,band,GameSpeed,out string message)){LastMessage=message;return false;}
            performanceBandChoice=band;
            simulationClock.SetSpeed(1);dirty=true;LastMessage=message;
            SyncPerformanceActors();return true;
        }
        public bool FillFeverTest()
        {
            bool ok=Economy.FillFeverForTesting();dirty|=ok;
            LastMessage=ok?"测试：已填满五个独立字母槽。":"仅测试模式且不在表演或冷却时可填满。";return ok;
        }
        void SyncPerformanceActors()
        {
            string band=Economy.IsPerforming?Economy.performingBand:"";
            var stage=Layout.facilities.Find(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Stage);
            string visualKey=band==""?"":band+"|"+stage?.definitionId+"|"+Layout.width+"|"+Layout.depth+"|"+stage?.x+"|"+stage?.z+"|"+stage?.rotation;
            if(band!="")performanceBandChoice=band;
            if(visualKey==visiblePerformanceBand)return;
            foreach(var a in performers)if(a!=null)Destroy(a.gameObject);
            performers.Clear();performerNames.Clear();visiblePerformanceBand=visualKey;performanceFinaleStarted=false;
            if(band=="")return;
            if(stage==null)return;
            int i=0;
            foreach(var id in ShopStageFormation.Members(band))
            {
                var s=ShopStaffProfiles.Find(id);
                var actor=World.CreatePaperActor(s.title+" stage placeholder",Color.Lerp(new Color(.95f,.72f,.85f),new Color(.6f,.75f,1f),i/4f));
                var cell=ShopStageFormation.GridPosition(stage,catalog.Find(stage.definitionId),i);
                var origin=World.CellCenter(Vector2Int.zero);
                actor.transform.position=origin+new Vector3(cell.x,stage.definitionId=="stage_mujica"?.42f:.34f,cell.y);
                actor.transform.localScale=Vector3.one;actor.SwayMultiplier=4f;actor.SetWorking(true);
                actor.SetCharacter(id);actor.SetPose("perform");
                performers.Add(actor);performerNames.Add(s.title);i++;
            }
        }
        void TickPerformance(float dt)
        {
            bool finished=Economy.TickPerformance(dt);
            if(finished)
            {
                if(!HasRestockStaff)CancelRestockTask();
                if(!HasGuideStaff && guideGuest!=null)ReleaseGuide(false);SetGameSpeed(Economy.performancePreviousSpeed);dirty=true;LastMessage="表演结束，人气 +5；恢复原员工绑定，进入 120 游戏秒冷却。";}
            SyncPerformanceActors();
            if(Economy.IsPerforming&&Economy.performanceRemaining<=1.8f&&!performanceFinaleStarted)
            {performanceFinaleStarted=true;foreach(var actor in performers)actor.Celebrate(Mathf.Max(.3f,Economy.performanceRemaining));}
            foreach(var actor in performers)actor.Tick(dt);
        }
        void DrawFeverHUD(float width)
        {
            var box=new Rect(width-242,22,218,78);PanelBox(box);
            string[] letters={"F","E","V","E","R"};
            for(int i=0;i<5;i++)
            {
                var rect=new Rect(box.x+10+i*40,box.y+8,38,30);
                bool collected=(Economy.feverMask&(1<<i))!=0;
                string artState=Economy.IsPerforming?"performing":Economy.performanceCooldown>0?"cooldown":Economy.FeverReady?"ready":collected?"collected":"empty";
                var slotArt=Ui("components/fever-slot-"+artState);
                if(slotArt!=null)GUI.DrawTexture(rect,slotArt,ScaleMode.StretchToFill);
                var letterStyle=new GUIStyle(body){alignment=TextAnchor.MiddleCenter};GUI.Label(rect,letters[i],letterStyle);
                if(ArtButton(rect,GUIContent.none,GUIStyle.none))OpenPerformance();
            }
            string state=Economy.IsPerforming?"FEVER · "+Mathf.CeilToInt(Economy.performanceRemaining)+" 秒":
                Economy.performanceCooldown>0?"冷却 "+Mathf.CeilToInt(Economy.performanceCooldown)+" 秒":
                Economy.FeverReady?"已集齐 · 点击查看演出":"成交收集 · 点击查看舞台";
            if(ArtButton(new Rect(box.x+10,box.y+44,198,26),state,Economy.FeverReady?activeButton:button))OpenPerformance();
        }
        void DrawPerformance(Rect box)
        {
            GUI.Label(new Rect(box.x+22,box.y+60,570,30),"FEVER · 五人舞台表演",heading);
            string[] letters={"F","E","V","E","R"};
            for(int i=0;i<5;i++)
                GUI.Label(new Rect(box.x+30+i*113,box.y+108,104,38),letters[i]+((Economy.feverMask&(1<<i))!=0?" ✓":" —"),body);
            string status=Economy.IsPerforming?Economy.performingBand+" 演出中 · "+Economy.performanceRemaining.ToString("0.0")+" 秒":
                Economy.performanceCooldown>0?"休息中 · "+Mathf.CeilToInt(Economy.performanceCooldown)+" 秒后可再次演出":Economy.FeverReady?"FEVER 已集齐":"完成交易，点亮 FEVER";
            GUI.Label(new Rect(box.x+22,box.y+159,570,34),status,heading);
            GUI.enabled=!Economy.IsPerforming;
            if(ArtButton(new Rect(box.x+22,box.y+210,278,36),"MyGO!!!!!",performanceBandChoice=="MyGO!!!!!"?activeButton:button))performanceBandChoice="MyGO!!!!!";
            if(ArtButton(new Rect(box.x+314,box.y+210,278,36),"Ave Mujica",performanceBandChoice=="Ave Mujica"?activeButton:button))performanceBandChoice="Ave Mujica";
            GUI.enabled=true;
            var names=new List<string>();
            foreach(var s in ShopStaffProfiles.All)if(s.band==performanceBandChoice)names.Add(s.title+(Economy.IsStaffUnlocked(s.id)?"":"（未签）"));
            GUI.Label(new Rect(box.x+22,box.y+260,570,62),string.Join(" · ",names),body);
            GUI.Label(new Rect(box.x+22,box.y+322,570,48),"演出期间：更多路人进店，客人更愿意购买。\n演出结束后，人气 +5。",body);
            var missing=PerformanceMissingConditions(performanceBandChoice);
            var area=new Rect(box.x+22,box.y+382,570,122);uiRegions.Add(area);
            performanceRequirementsScroll=GUI.BeginScrollView(area,performanceRequirementsScroll,new Rect(0,0,545,Mathf.Max(122,missing.Count*25)));
            for(int i=0;i<missing.Count;i++)GUI.Label(new Rect(0,i*25,535,25),"· "+missing[i],small);
            GUI.EndScrollView();
            GUI.enabled=missing.Count==0;
            if(ArtButton(new Rect(box.x+22,box.y+520,570,40),"开始表演",activeButton))BeginPerformance(performanceBandChoice);
            GUI.enabled=true;
        }
        Vector2 performanceRequirementsScroll;
        public List<string> PerformanceMissingConditions(string band,bool requireStage=true,bool requireOpen=true)
        {
            var missing=new List<string>();
            if(Economy.IsPerforming){missing.Add("正在演出");return missing;}
            if(requireOpen&&!Economy.shopOpen)missing.Add("请先开店");
            if(Economy.performanceCooldown>0)missing.Add("伙伴休息中 · 还需 "+Mathf.CeilToInt(Economy.performanceCooldown)+" 秒");
            if(Economy.feverMask!=31)missing.Add("还需要点亮全部 FEVER 字母");
            if(Economy.ShopLevel<3)missing.Add("舞台需要店铺 Lv.3");
            if(Economy.UnlockedStaffCount<8)missing.Add("签约员工 "+Economy.UnlockedStaffCount+" / 8 人");
            if(requireStage&&!Layout.facilities.Exists(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Stage))missing.Add(Economy.stageOwned?"请从装修库存摆放舞台":"请在商店的设施分类购买舞台");
            foreach(var member in ShopStaffProfiles.All)if(member.band==band&&!Economy.IsStaffUnlocked(member.id))missing.Add("还需要签约 "+member.title);
            if(Economy.PostFor(FacilityKind.Restock)==null||Economy.PostFor(FacilityKind.Reception)==null)missing.Add("请摆放补货位和接待点");
            if(!ShopModel.ValidateCheckoutAccess(Layout,catalog,out _)||string.IsNullOrEmpty(Economy.AssignedStaffId(FacilityKind.Checkout)))missing.Add("请安排收银员，并留出收银台前后的通路");
            if(guests.Exists(g=>!string.IsNullOrEmpty(g.visitingStaffId)))missing.Add("请等来访的员工购物离店");
            return missing;
        }
        string PerformanceRequirement()=>string.Join("\n",PerformanceMissingConditions(performanceBandChoice));
    }
}
