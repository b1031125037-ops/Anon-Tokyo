using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopMenuVerification
    {
        IEnumerator VerifyDev101()
        {
            Check(App.StartSlot(2),"Start isolated finite development save");yield return WaitForScene(false);
            App.SetPaused(true);SetPresentationField("panelKey","");
            var camera=App.World.WorldCamera;
            var actors=FindObjectsByType<PaperCharacterView>(FindObjectsSortMode.None);
            Check(actors.Length>=3,"Test real staff cards");
            for(int i=0;i<4;i++)
            {
                App.World.Orbit(new Vector2(100,30));yield return null;yield return new WaitForEndOfFrame();
                var facing=-camera.transform.forward;facing.y=0;
                foreach(var actor in actors)Check(Vector3.Dot(actor.FacingDirection,facing.normalized)>.999f&&Mathf.Abs(actor.FacingDirection.y)<.001f,"Upright card continuously faces camera yaw through orbit: "+actor.CharacterId+" / "+i);
            }
            yield return Capture("01-orbit-billboards.png");
            float yaw=camera.transform.eulerAngles.y;App.World.ApplyVisualSettings(App.UserSettings);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw,camera.transform.eulerAngles.y))<.01,"Changing light/settings retains camera yaw");
            App.World.Orbit(new Vector2(0,10000));Check(Mathf.Abs(App.World.CameraPitch-85)<.01,"Orbit clamps upper pitch");
            App.World.Orbit(new Vector2(0,-10000));Check(Mathf.Abs(App.World.CameraPitch+10)<.01,"Orbit clamps lower pitch");
            App.World.Orbit(new Vector2((45-camera.transform.eulerAngles.y)/.22f,35/.22f));App.World.ResetCamera();
            Vector2 screen=new Vector2(1360,850);
            Check(ShopWorldView.EdgePanDelta(screen*.5f,screen,.02f)==Vector2.zero,"Center pointer does not drift camera");
            Check(ShopWorldView.EdgePanDelta(new Vector2(-1,200),screen,.02f)==Vector2.zero,"Outside window does not edge pan");
            var left=ShopWorldView.EdgePanDelta(new Vector2(1,425),screen,.02f);
            var right=ShopWorldView.EdgePanDelta(new Vector2(1359,425),screen,.02f);
            Check(left.x>0&&right.x<0&&left.y==0,"Opposite boundaries pan in opposite directions");
            Check(ShopWorldView.EdgePanDelta(new Vector2(1359,849),screen,.02f).magnitude<=3.601f,"Diagonal edge motion is speed limited");
            Vector3 before=camera.transform.position;App.World.Pan(right);
            Check(Vector3.Dot(camera.transform.position-before,camera.transform.right)>0,"Right-edge movement follows the current camera's right axis");
            App.World.FocusFacility("restock-1");App.World.Pan(Vector2.zero);App.World.TickGuidedFocus(1);
            Check(App.World.GuidedFacilityId=="restock-1","Stationary cursor preserves tutorial camera focus");
            App.World.ResetCamera();

            var input=typeof(ShopApp).GetMethod("HandleInput",BindingFlags.Instance|BindingFlags.NonPublic);
            int mouseButton=2;Action<EventType,Vector2,bool> pointer=(kind,delta,over)=>input.Invoke(App,new object[]{new Event{type=kind,button=mouseButton,delta=delta},new Vector2(680,400),over});
            var placement=typeof(ShopApp).GetMethod("BeginPlacement",BindingFlags.Instance|BindingFlags.NonPublic);
            placement.Invoke(App,new object[]{"top","top-1"});
            pointer(EventType.MouseDown,Vector2.zero,false);pointer(EventType.MouseDrag,new Vector2(40,0),false);pointer(EventType.MouseUp,Vector2.zero,false);
            Check(ReadPresentationField<string>("placingDefinition")=="top","Middle drag rotates while preserving renovation preview");
            var panBefore=camera.transform.position;float panYaw=camera.transform.eulerAngles.y;mouseButton=0;
            pointer(EventType.MouseDown,Vector2.zero,false);pointer(EventType.MouseDrag,new Vector2(50,10),false);pointer(EventType.MouseUp,Vector2.zero,false);
            Check((camera.transform.position-panBefore).sqrMagnitude>.0001f&&Mathf.Abs(Mathf.DeltaAngle(panYaw,camera.transform.eulerAngles.y))<.01f&&ReadPresentationField<string>("placingDefinition")=="top","Left drag pans without rotating or committing furniture");
            mouseButton=1;pointer(EventType.MouseDown,Vector2.zero,false);pointer(EventType.MouseUp,Vector2.zero,false);
            Check(string.IsNullOrEmpty(ReadPresentationField<string>("placingDefinition")),"Right click without drag cancels renovation preview");
            float uiYaw=camera.transform.eulerAngles.y;mouseButton=2;
            pointer(EventType.MouseDown,Vector2.zero,true);pointer(EventType.MouseDrag,new Vector2(40,0),false);pointer(EventType.MouseUp,Vector2.zero,false);
            Check(Mathf.Abs(Mathf.DeltaAngle(uiYaw,camera.transform.eulerAngles.y))<.01f,"Drag beginning on UI never rotates scene");
            App.EndRenovation();SetPresentationField("panelKey","");

            var hover=typeof(ShopApp).GetMethod("CommitButtonHover",BindingFlags.Instance|BindingFlags.NonPublic);
            hover.Invoke(App,new object[]{""});yield return new WaitForSecondsRealtime(.12f);
            int count=App.HoverSoundPlayCount;
            hover.Invoke(App,new object[]{"test-close"});
            Check(App.HoverSoundPlayCount==count+1,"Entering close control plays one hover effect");
            // Calls are in one frame so no natural pointer event alters the target.
            for(int i=0;i<120;i++)hover.Invoke(App,new object[]{"test-close"});
            ReadPresentationField<System.Collections.Generic.Dictionary<string,float>>("soundTimes")["hover"]=Time.unscaledTime-5;
            hover.Invoke(App,new object[]{"test-close"});
            Check(App.HoverSoundPlayCount==count+1,"Repeated repaint of same target does not repeat hover audio");
            hover.Invoke(App,new object[]{""});

            Check(App.HasClaimableTask,"Fresh ready-staff task advertises its claimable reward");
            yield return Capture("02-task-claim-badge.png");
            Check(App.ClaimGrowthTask("staff_ready"),"Claim offered reward");
            Check(!App.HasClaimableTask,"Claim marker clears when next task is incomplete");
            Check(App.Economy.Contract(App.Catalog,"top_tshirt",out _),"Contract completes next task");
            Check(App.HasClaimableTask,"Claim marker returns when a subsequent task becomes ready");
            App.OpenStaffPost("checkout-1");yield return Capture("03-close-control.png");

            var fresh=ShopProfiles.NewGame(App.Catalog,false).economy;fresh.cash=0;fresh.tutorialStep=-1;
            Check(fresh.GetGrowthTaskStates().Exists(t=>t.canClaim)&&fresh.IsFinanciallyStranded(App.Catalog),"Insufficient unclaimed reward no longer blocks actual insolvency");
            SetPresentationField("<Economy>k__BackingField",fresh);
            typeof(ShopApp).GetMethod("TickFinancialFailure",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(App,new object[]{1.1f});
            Check(App.GameFailed&&App.FailureAnimationProgress<.1f,"Bankruptcy starts its animation once");
            yield return new WaitForSecondsRealtime(.42f);yield return Capture("04-bankruptcy-animation.png");
            yield return new WaitForSecondsRealtime(1.1f);
            Check(App.FailureAnimationProgress==1,"Bankruptcy animation finishes even when simulation is paused");
            yield return Capture("05-bankruptcy-actions.png");
            Check(App.ResolveFailedGame(true),"Failure allows a new game after deleting only isolated failed save");
            yield return WaitForScene(false,false);
            Check(App.TutorialChoicePending&&App.Economy.cash==800,"Restart returns to a fresh tutorial choice with 800 coins");
            CheckPreferencesUntouched();
        }
    }
}
