using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    [DefaultExecutionOrder(-420)]
    public sealed class ShopPerformanceVerification:MonoBehaviour
    {
        [Serializable] class Report
        {
            public bool passed,timedOut,inventoryConserved=true,uniqueStops=true;
            public int checks,width,depth,level,fixtureLimit,signedMembers,transactions,items,warehousePickups,restocks,performances,screenshots;
            public int extraPickups,extraRestocks,extraGreetings,extraRecommendations;
            public float seconds;
            public string[] results,errors;
        }
        ShopApp app;Report report;string output;bool active,finished;float started,deadline,oldScale,nextCheck;
        readonly List<string> results=new List<string>(),errors=new List<string>();
        void Awake()
        {
            active=Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-performance")>=0;
            if(active)Application.logMessageReceived+=Log;
        }
        void Update()
        {
            if(!active||finished||report==null)return;
            if(Time.realtimeSinceStartup>deadline){report.timedOut=true;errors.Add("Exceeded 100 seconds");Finish();return;}
            if(app?.Economy!=null && Time.realtimeSinceStartup>nextCheck)
            {
                nextCheck=Time.realtimeSinceStartup+.5f;
                if(!app.Economy.Validate(app.Catalog,app.Layout,out string reason)){report.inventoryConserved=false;Error(reason);}
                if(app.CustomerStopConflicts>0){report.uniqueStops=false;Error("Overlapping customer stops");}
            }
        }
        IEnumerator Start()
        {
            if(!active)yield break;
            started=Time.realtimeSinceStartup;deadline=started+100;oldScale=Time.timeScale;report=new Report();
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--verify-performance");
            if(index+1>=args.Length){Error("Output path required");Finish();yield break;}
            output=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(output);
            Application.runInBackground=true;app=GetComponent<ShopApp>();yield return null;
            app.Economy.EnableTestResources();app.ForceResetCustomersForVerification();app.StartTwoCounterTest();app.ForceResetCustomersForVerification();
            app.ApplyApplicationFocus(true);app.ApplyApplicationPause(false);app.SetPaused(false);
            Time.timeScale=5;app.SetTestPopularity(100);app.SetAutoRestock(false);app.CancelRestockTask();
            for(int i=0;i<4;i++)Check(app.ExpandShop(),"Actual room expansion "+(i+1));
            Check(app.Layout.width==12&&app.Layout.depth==12&&app.Economy.ShopLevel==5&&app.Economy.FixtureLimit==36,"Full five-level, 144-cell, 36-display-case state");
            Check(!app.ExpandShop(),"Maximum expansion refused");
            Check(app.MoveWarehouseDoor(10)&&ShopModel.WarehousePickup(app.Layout)==new Vector2Int(11,10),"Warehouse door moves to a newly available wall cell");
            Check(ShopModel.FindPath(app.Layout,app.Catalog,ShopModel.RoadSpawnFor(app.Layout),ShopModel.EntranceFor(app.Layout)).Count>0,"New road connects to the shifted entrance");
            app.OpenExpansion();yield return Capture("01-full-expansion.png");app.OpenPanel("expansion");
            Check(app.ApplyPlacement(new PlacedFacility{instanceId="stage-live",definitionId="stage",x=4,z=0},out _),"Place 3x4 stage in expanded foreground");
            Check(!app.AssignStaffToPost(app.Economy.PostFor(FacilityKind.Restock).instanceId,"umiri"),"Locked character cannot be assigned");
            app.OpenStaffProfile("uika");yield return Capture("02-character-before-signing.png");
            foreach(var s in ShopStaffProfiles.All)if(s.band=="Ave Mujica")
            {Check(app.SignStaff(s.id),"Sign "+s.id);Check(!app.SignStaff(s.id),"Prevent repeat signature "+s.id);}
            Check(app.AssignStaffToPost(app.Economy.PostFor(FacilityKind.Restock).instanceId,"umiri"),"Assign signed Umiri to restocking");
            app.OpenRoster();yield return Capture("03-roster.png");app.OpenPanel("roster");
            Check(app.BuyAdditionalStaffPost(FacilityKind.Restock)&&app.BuyAdditionalStaffPost(FacilityKind.Reception),"Purchase both extra work posts at level five");
            Check(PlaceExtra("restock","restock-extra-live"),"Place second restocking point");
            Check(PlaceExtra("reception","reception-extra-live"),"Place second reception point");
            Check(app.AssignStaffToPost("restock-extra-live","rana")&&app.AssignStaffToPost("reception-extra-live","uika"),"Assign different real staff to both additional posts");
            app.OpenPanel("poststore");yield return Capture("07-extra-post-store.png");app.OpenPanel("poststore");
            app.SetCustomerFlowStressTest(true);app.SetCustomerLimit(8);app.SetGuideEnabled(true);
            app.SetAutoRestock(true);app.SetShopOpen(true);
            float limit=Time.realtimeSinceStartup+25;
            while(app.Economy.transactions<5 && Time.realtimeSinceStartup<limit)yield return null;
            Check(app.Economy.transactions>=5&&app.Economy.feverMask!=0,"Real sales on the full map generate FEVER letters");
            Check(app.WarehousePickups>0&&app.RestockJobsCompleted>0,"Actual pickup and restocking use the expanded warehouse");
            app.FillFeverTest();app.SetGameSpeed(2);
            Check(app.BeginPerformance("Ave Mujica")&&app.VisiblePerformerCount==5,"Five contracted performers appear on stage");
            Check(app.GameSpeed==1&&!app.SetGameSpeed(2),"Performance locks one-times simulation speed");
            Check(app.Economy.IsPerformingStaff("umiri")&&app.Economy.AssignedStaffId(FacilityKind.Restock)=="rana" && app.Economy.PerformanceAssignments().Count==5,"All five non-performing members cover five distinct real work posts");
            app.OpenPanel("staff");yield return Capture("08-fever-real-staff.png");app.OpenPanel("staff");
            app.SetPaused(true);float remaining=app.Economy.performanceRemaining;
            yield return new WaitForSecondsRealtime(.3f);
            Check(app.Economy.performanceRemaining==remaining,"Manual pause freezes performance timer");
            app.SetPaused(false);
            app.RequestCloseShop(out _);
            Check(!app.CanRenovate&&app.IsClosing,"Closing waits for active stage performance");
            app.SetShopOpen(true);
            app.World.ResetCamera();app.ClearFacilitySelection();
            yield return Capture("04-five-person-stage.png");
            app.OpenPerformance();yield return Capture("05-fever-performance-panel.png");
            string save=Path.Combine(output,"mid-performance.json");
            ShopSession.SaveToPath(app.Layout,app.Economy,app.Catalog,save);
            var loaded=ShopSession.LoadFromPath(app.Catalog,save,out string reason);
            Check(loaded!=null&&loaded.economy.IsPerforming&&loaded.economy.signedStaffIds.Count==5&&
                loaded.economy.performanceRemaining==app.Economy.performanceRemaining&&loaded.layout.depth==12,"Live timer, roster and expanded geometry persist: "+reason);
            app.OpenPanel("performance");int sales=app.Economy.transactions;
            limit=deadline-10;
            while(app.Economy.IsPerforming&&Time.realtimeSinceStartup<limit)yield return null;
            Check(!app.Economy.IsPerforming&&app.Economy.performancesCompleted==1&&app.Economy.performanceCooldown>0,"Performance completes once and enters cooldown");
            yield return null;
            Check(app.VisiblePerformerCount==0&&app.GameSpeed==2&&app.Economy.AssignedStaffId(FacilityKind.Restock)=="umiri"&&
                app.Economy.WorkEfficiency(FacilityKind.Restock)>1,"Actors disappear, original worker and speed restore");
            Check(app.Economy.transactions>sales&&app.PaymentsAtFront>0,"Customers complete real checkout during stage performance");
            Check(!app.BeginPerformance("Ave Mujica"),"Cooldown refuses repeated start");
            Check(app.ExtraWarehousePickups>0&&app.ExtraRestocksCompleted>0,"Second restocker actually picks up and shelves stock");
            Check(app.ExtraGuideGreetings>0&&app.ExtraGuideRecommendations>0,"Second guide independently greets and recommends to real customers");
            app.OpenPerformance();yield return Capture("06-performance-completed.png");
            Check(app.Economy.Validate(app.Catalog,app.Layout,out reason),"Final inventory and state consistency: "+reason);
            Finish();
        }
        bool PlaceExtra(string definition,string id)
        {
            for(int z=2;z<app.Layout.depth-1;z++)for(int x=1;x<app.Layout.width-1;x++)
            {var f=new PlacedFacility{instanceId=id,definitionId=definition,x=x,z=z};
                if(app.ApplyPlacement(f,out _))return true;}
            return false;
        }
        void Check(bool ok,string label){if(ok)results.Add(label);else Error(label);}
        void Error(string s){if(!errors.Contains(s))errors.Add(s);}
        void Log(string message,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)Error(message+"\n"+trace);}
        IEnumerator Capture(string file)
        {
            yield return new WaitForEndOfFrame();
            var shot=ScreenCapture.CaptureScreenshotAsTexture();
            if(shot==null){Error("Missing image "+file);yield break;}
            File.WriteAllBytes(Path.Combine(output,file),shot.EncodeToPNG());Destroy(shot);report.screenshots++;
        }
        void Finish()
        {
            if(finished)return;finished=true;StopAllCoroutines();
            report.checks=results.Count;report.results=results.ToArray();report.errors=errors.ToArray();
            report.passed=errors.Count==0&&!report.timedOut&&report.inventoryConserved&&report.uniqueStops;
            report.width=app?.Layout?.width??0;report.depth=app?.Layout?.depth??0;
            report.level=app?.Economy?.ShopLevel??0;report.fixtureLimit=app?.Economy?.FixtureLimit??0;
            report.signedMembers=app?.Economy?.signedStaffIds.Count??0;report.transactions=app?.Economy?.transactions??0;
            report.items=(int)(app?.Economy?.OnlineUnitsSold??0);report.warehousePickups=app?.WarehousePickups??0;
            report.restocks=app?.RestockJobsCompleted??0;report.performances=app?.Economy?.performancesCompleted??0;
            report.extraPickups=app?.ExtraWarehousePickups??0;report.extraRestocks=app?.ExtraRestocksCompleted??0;
            report.extraGreetings=app?.ExtraGuideGreetings??0;report.extraRecommendations=app?.ExtraGuideRecommendations??0;
            report.seconds=Time.realtimeSinceStartup-started;
            if(output!=null)File.WriteAllText(Path.Combine(output,"performance-runtime-verification.json"),JsonUtility.ToJson(report,true));
            Time.timeScale=oldScale;Application.logMessageReceived-=Log;
            Debug.Log("SHOP_PERFORMANCE_RUNTIME_OK "+report.passed);Application.Quit(report.passed?0:1);
        }
        void OnDestroy(){if(active){Application.logMessageReceived-=Log;Time.timeScale=oldScale;}}
    }
}
