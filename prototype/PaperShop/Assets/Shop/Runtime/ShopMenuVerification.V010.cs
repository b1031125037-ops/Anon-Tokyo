using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopMenuVerification
    {
        IEnumerator VerifyProductStock()
        {
            App.EndRenovation();App.ClearFacilitySelection();SetPresentationField("panelKey","");
            var original=App.Layout;var showroom=ShopModel.Clone(original);
            showroom.facilities.RemoveAll(p=>p.definitionId!="warehouse");
            var shelves=App.Economy.shelves;var sampleShelves=new List<ShelfStock>();
            // Isolated visual fixture; no writes or game progression occur until original ledgers are restored.
            for(int i=0;i<7;i++)
            {
                string category=ShopProductSet.Categories[i],id="product-preview-"+category;
                showroom.facilities.Add(new PlacedFacility{instanceId=id,definitionId=category,x=2+(i%3)*3,z=2+(i/3)*3});
                sampleShelves.Add(new ShelfStock{instanceId=id,productId=ShopProductSet.ProductIds[i],quantity=20,level=1});
                Check(App.Catalog.products.Find(p=>p.id==ShopProductSet.ProductIds[i]).modelPrefab!=null,"Delivered product prefab bound: "+category);
            }
            App.Economy.shelves=sampleShelves;
            var actors=FindObjectsByType<PaperCharacterView>(FindObjectsSortMode.None);foreach(var actor in actors)actor.gameObject.SetActive(false);
            float pitch=App.UserSettings.cameraPitch;App.UserSettings.cameraPitch=38;App.World.ApplyVisualSettings(App.UserSettings);
            for(int rotation=0;rotation<4;rotation++)
            {
                foreach(var item in showroom.facilities)if(item.definitionId!="warehouse")item.rotation=rotation;
                App.World.Rebuild(showroom,App.Catalog);App.World.ResetCamera();App.World.SyncProductDisplays(App.Economy);
                yield return null;
                foreach(var shelf in sampleShelves)Check(App.World.DisplayedProductCount(shelf.instanceId)>0,"Actual stocked product visible: "+shelf.productId+" rotation "+rotation);
                yield return Capture("61-products-rotation-"+rotation+".png");
            }
            foreach(var item in showroom.facilities)if(item.definitionId!="warehouse")item.rotation=0;
            App.World.Rebuild(showroom,App.Catalog);
            foreach(var shelf in sampleShelves)
            {
                App.World.ResetCamera();App.World.FocusFacility(shelf.instanceId);App.World.TickGuidedFocus(1);App.World.Zoom(-10);
                yield return Capture("61-product-detail-"+shelf.productId+".png");
            }
            App.World.ResetCamera();
            foreach(var shelf in sampleShelves)shelf.quantity=0;
            yield return null;yield return null;
            foreach(var shelf in sampleShelves)Check(App.World.DisplayedProductCount(shelf.instanceId)==0,"Sold-out display hides product: "+shelf.productId);
            yield return Capture("62-products-empty.png");
            App.Economy.shelves=shelves;App.World.Rebuild(original,App.Catalog);App.UserSettings.cameraPitch=pitch;App.World.ApplyVisualSettings(App.UserSettings);App.World.ResetCamera();
            foreach(var actor in actors)if(actor!=null)actor.gameObject.SetActive(true);
            Check(App.Economy.Validate(App.Catalog,App.Layout,out _),"Product presentation never changes purchase or sale ledgers");
        }
        IEnumerator VerifyStageLessonsAndMusic()
        {
            App.EndRenovation();SetPresentationField("panelKey","");App.Economy.cash=100000;
            App.Economy.popularity=20;App.Economy.peakPopularity=20;
            Check(App.ExpandShop()&&App.Economy.ShopLevel==3,"Prepare level-three stage lesson with real expansion");
            yield return null;Check(App.TutorialStep==17,"Level alone does not prematurely trigger stage lesson");
            foreach(var id in new[]{"uika","mutsumi","umiri"})Check(App.SignStaff(id),"Sign required eighth employee: "+id);
            yield return null;Check(App.TutorialStep==17,"Eight employees without FEVER do not trigger lesson");
            App.OpenPerformance();yield return Capture("63-fever-missing-conditions.png");
            Check(App.PerformanceMissingConditions("MyGO!!!!!").Exists(x=>x.Contains("FEVER")),"FEVER lists missing letters");
            App.Economy.performanceVersion=1;App.Economy.feverMask=31;
            yield return WaitForTutorial(19);yield return Capture("64-stage-inventory-lesson.png");
            Check(App.Economy.stageOwned,"MyGO stage is available in initial inventory");
            bool placed=false;
            for(int z=0;z<App.Layout.depth&&!placed;z++)for(int x=0;x<App.Layout.width&&!placed;x++)
                placed=App.ApplyPlacement(new PlacedFacility{instanceId="tutorial-stage",definitionId="stage",x=x,z=z},out _);
            Check(placed,"Purchased stage can be placed with real layout validation");yield return WaitForTutorial(20);
            App.EndRenovation();SetPresentationField("panelKey","");App.SetShopOpen(true);App.SetCustomerFlowStressTest(false);
            Check(App.BeginPerformance("MyGO!!!!!"),"Tutorial begins actual MyGO performance");yield return WaitForTutorial(21);
            yield return new WaitForSecondsRealtime(.5f);
            Check(App.StageMusic!=null&&App.StageMusic.clip==Resources.Load<AudioClip>("Audio/Stage/mygo")&&App.StageMusic.isPlaying&&!App.StageMusic.loop,"MyGO performance uses the supplied non-looping track");
            Check(App.IsMusicPaused&&!App.BackgroundMusic.isPlaying,"Regular BGM is suspended during performance");
            App.SetPaused(true);yield return null;float remaining=App.Economy.performanceRemaining,audioTime=App.StageMusic.time;
            yield return new WaitForSecondsRealtime(.25f);
            Check(App.Economy.performanceRemaining==remaining&&Mathf.Abs(App.StageMusic.time-audioTime)<.08f&&!App.StageMusic.isPlaying,"Pausing freezes both performance clock and music");
            App.SetPaused(false);yield return new WaitForSecondsRealtime(.2f);
            Check(App.StageMusic.isPlaying,"Performance music resumes after pause");
            App.ClearFacilitySelection();SetPresentationField("panelKey","");App.World.FocusFacility("tutorial-stage");App.World.TickGuidedFocus(1);App.World.Zoom(-5);yield return Capture("77-mygo-performing.png");
            App.OpenPerformance();yield return Capture("65-mygo-song-playing.png");
            Check(App.ReturnToTitle(),"Save mid-song and return to title");yield return WaitForScene(true);
            App.StartSlot(2);yield return WaitForScene(false,false);yield return new WaitForSecondsRealtime(.3f);
            Check(App.StageMusic!=null&&Mathf.Abs(App.StageMusic.time-(App.StageMusic.clip.length-App.Economy.performanceRemaining))<.6f,"Reload seeks to saved performance position");
            int completed=App.Economy.performancesCompleted;
            while(App.Economy.IsPerforming)yield return null;
            yield return WaitForTutorial(22);yield return null;
            Check(App.Economy.performancesCompleted==completed+1&&App.BackgroundMusic.isPlaying&&!App.IsMusicPaused,"MyGO ends once at actual track duration and background music resumes");
            App.RequestCloseShop(out _);Time.timeScale=4;
            float until=Time.realtimeSinceStartup+30;while(!App.CanRenovate&&Time.realtimeSinceStartup<until)yield return null;Time.timeScale=1;
            Check(App.CanRenovate,"Customers finish paying before post-performance setup");
            // Complete five-person Ave roster at its actual level requirements.
            while(App.Economy.ShopLevel<5){App.Economy.popularity=App.Economy.PopularityCap;App.Economy.peakPopularity=App.Economy.popularity;Check(App.ExpandShop(),"Expand to unlock remaining performers");}
            foreach(var id in new[]{"nyamu","sakiko"})Check(App.SignStaff(id),"Sign remaining Ave performer: "+id);
            Check(App.BuyEnvironmentItem("stage_mujica"),"Buy paid stage in tutorial test");
            var oldStage=App.Layout.facilities.Find(f=>f.instanceId=="tutorial-stage");
            Check(App.ApplyPlacement(new PlacedFacility{instanceId=oldStage.instanceId,definitionId="stage_mujica",x=oldStage.x,z=oldStage.z,rotation=oldStage.rotation},out var swapReason),"Replace base stage with purchased Mujica style: "+swapReason);
            Check(App.Economy.StoredFacilityCount(App.Layout,"stage")==1&&App.Economy.StoredFacilityCount(App.Layout,"stage_mujica")==0,"Replacing stage returns MyGO style to inventory");
            App.EndRenovation();App.Economy.performanceCooldown=0;App.Economy.feverMask=31;App.SetShopOpen(true);
            Check(App.BeginPerformance("Ave Mujica"),"Begin actual Ave Mujica performance");yield return new WaitForSecondsRealtime(.4f);
            Check(App.StageMusic.clip==Resources.Load<AudioClip>("Audio/Stage/mujica")&&Mathf.Abs(App.StageMusic.clip.length-50.6195f)<.05f,"Ave uses Ether and its actual duration");
            App.SetMusicVolume(.31f);yield return null;Check(Mathf.Abs(App.StageMusic.volume-.31f)<.001f,"Stage follows music volume slider");
            App.SetMusicMuted(true);yield return null;Check(App.StageMusic.mute,"Stage follows music mute");App.SetMusicMuted(false);App.SetMusicVolume(.18f);
            App.ClearFacilitySelection();SetPresentationField("panelKey","");App.World.FocusFacility("tutorial-stage");App.World.TickGuidedFocus(1);App.World.Zoom(-5);yield return Capture("78-mujica-performing.png");
            App.OpenPerformance();yield return Capture("66-mujica-song-playing.png");
            completed=App.Economy.performancesCompleted;
            SetPresentationField("panelKey","");
            while(App.Economy.IsPerforming&&App.Economy.performanceRemaining>1.1f)yield return null;
            yield return Capture("79-mujica-finale.png");
            while(App.Economy.IsPerforming)yield return null;yield return null;
            Check(App.Economy.performancesCompleted==completed+1&&App.BackgroundMusic.isPlaying,"Ether ends once and restores normal BGM");
            Check(App.Economy.Validate(App.Catalog,App.Layout,out _),"Full tutorial, contracts, stock and both performances preserve valid state");
        }
    }
}
