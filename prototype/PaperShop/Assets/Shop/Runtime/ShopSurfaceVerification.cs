using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed class ShopSurfaceVerification:MonoBehaviour
    {
        [Serializable] class Report
        {
            public bool passed;
            public int checks,screenshots,transactions,restocks;
            public string[] results,errors;
        }
        ShopApp app;string output;bool active,finished;float deadline;
        readonly List<string> results=new List<string>(),errors=new List<string>();
        Report report=new Report();
        void Awake()
        {
            active=Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-surfaces")>=0;
            if(active)Application.logMessageReceived+=Log;
        }
        void Update(){if(active&&!finished&&deadline>0&&Time.realtimeSinceStartup>deadline){errors.Add("Verification timeout");Finish();}}
        IEnumerator Start()
        {
            if(!active)yield break;
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--verify-surfaces");
            output=args[index+1];Directory.CreateDirectory(output);deadline=Time.realtimeSinceStartup+90;
            yield return null;app=GetComponent<ShopApp>();
            app.ApplyApplicationFocus(true);app.ApplyApplicationPause(false);app.SetPaused(false);
            app.Economy.EnableTestResources();app.ForceResetCustomersForVerification();app.StartTwoCounterTest();app.ForceResetCustomersForVerification();
            app.SetAutoRestock(false);app.CancelRestockTask();app.SetTestPopularity(100);
            app.ExpandShop();app.ExpandShop();app.ClearFacilitySelection();
            foreach(string kind in new[]{"outfit","bag","accessory"})
            {
                Check(app.BuyCounter(kind),"Buy "+kind);
                var stored=app.Economy.storedFixtures.Find(f=>f.definitionId==kind);
                bool placed=false;
                for(int z=0;z<app.Layout.depth&&!placed;z++)for(int x=0;x<app.Layout.width&&!placed;x++)
                {
                    var p=new PlacedFacility{instanceId=stored.instanceId,definitionId=kind,x=x,z=z};
                    var draft=ShopModel.Clone(app.Layout);
                    if(!ShopModel.TryPlace(draft,app.Catalog,p,out _)||!ShopModel.ValidateCheckoutAccess(draft,app.Catalog,out _))continue;
                    placed=app.ApplyPlacement(p,out _);
                }
                Check(placed,"Place "+kind+" and preserve reachable service cells");
            }
            app.OpenCounterStore();yield return Capture("01-seven-display-categories.png");app.OpenPanel("store");
            app.OpenPanel("goods");yield return Capture("02-current-merchandise.png");app.OpenPanel("goods");
            foreach(string material in new[]{"floor_wood","floor_tile","wall_sage","wall_cream"})app.BuyMaterial(material,4);
            Check(app.ApplySurface("floor",7,4,"floor_wood")&&app.ApplySurface("floor",8,4,"floor_tile"),"Paint separate floor cells");
            Check(app.ApplySurface("north",5,0,"wall_sage")&&app.ApplySurface("east",0,6,"wall_cream"),"Paint separate columns on both walls");
            app.World.ResetCamera();
            foreach(string kind in new[]{"north","east"})
            {
                int c=kind=="north"?5:6;
                var target=kind=="north"?new Vector3(c-app.Layout.width*.5f+.5f,2.4f,app.Layout.depth*.5f):
                    new Vector3(app.Layout.width*.5f,2.4f,c-app.Layout.depth*.5f+.5f);
                Vector3 screen=app.World.WorldCamera.WorldToScreenPoint(target);
                Check(app.World.PickWallColumn(screen,out string hit,out int column)&&hit==kind&&column==c,"Screen ray picks exact "+kind+" column");
            }
            Check(app.BeginSurfacePainting("floor_wood")&&app.IsEditingLayout&&!app.IsSimulationPaused,"Material painting counts as renovation without pausing");
            app.SetShopOpen(true);Check(!app.Economy.shopOpen,"Cannot open shop while painting");
            yield return Capture("03-individual-surfaces.png");app.EndSurfacePainting();
            app.OpenPanel("materials");yield return Capture("04-surface-palette.png");app.OpenPanel("materials");
            string save=Path.Combine(output,"painted-store.json");ShopSession.SaveToPath(app.Layout,app.Economy,app.Catalog,save);
            var loaded=ShopSession.LoadFromPath(app.Catalog,save,out _);
            Check(loaded!=null&&loaded.layout.finishes.Count==4,"New displays and finishes persist together");
            Check(app.ExpandShop()&&app.ExpandShop(),"Painted shop expands to 12 by 12");
            Check(ShopSurfaces.Get(app.Layout,"floor",7,8)=="floor_wood"&&ShopSurfaces.Get(app.Layout,"east",0,10)=="wall_cream","Application expansion commits shifted finishes");
            app.SetGuideEnabled(true);app.SetAutoRestock(true);app.SetCustomerFlowStressTest(true);app.SetCustomerLimit(6);app.SetShopOpen(true);
            Check(app.Economy.shopOpen&&!app.ApplySurface("floor",0,0,"floor_tile"),"Live open shop refuses painting");
            Time.timeScale=5;
            float stop=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<stop)
            {
                bool newShelves=true;
                foreach(string kind in new[]{"outfit","bag","accessory"})
                {
                    var p=app.Layout.facilities.Find(f=>f.definitionId==kind);
                    var shelf=app.Economy.Shelf(p.instanceId);
                    var stock=app.Economy.Stock(shelf.productId);
                    if(shelf.quantity+stock.sold==0)newShelves=false;
                }
                if(newShelves&&app.Economy.transactions>=3)break;
                yield return null;
            }
            foreach(string kind in new[]{"outfit","bag","accessory"})
            {
                var p=app.Layout.facilities.Find(f=>f.definitionId==kind);var shelf=app.Economy.Shelf(p.instanceId);
                Check(shelf.quantity+app.Economy.Stock(shelf.productId).sold>0,"Actual worker supplies "+kind);
            }
            Check(app.Economy.transactions>=3&&app.PaymentsAtFront>0,"Customers shop and pay with expanded catalog");
            Check(app.Economy.Validate(app.Catalog,app.Layout,out _),"Inventory remains conserved");
            yield return Capture("05-expanded-live-shop.png");
            Finish();
        }
        void Check(bool ok,string label){if(ok)results.Add(label);else errors.Add(label);}
        void Log(string m,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(m+"\n"+trace);}
        IEnumerator Capture(string file)
        {
            yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());Destroy(image);report.screenshots++;
        }
        void Finish()
        {
            if(finished)return;finished=true;StopAllCoroutines();
            report.passed=errors.Count==0;report.checks=results.Count;report.results=results.ToArray();report.errors=errors.ToArray();
            report.transactions=app?.Economy?.transactions??0;report.restocks=app?.RestockJobsCompleted??0;
            if(output!=null)File.WriteAllText(Path.Combine(output,"surface-runtime-verification.json"),JsonUtility.ToJson(report,true));
            Time.timeScale=1;Application.logMessageReceived-=Log;Application.Quit(report.passed?0:1);
        }
    }
}
