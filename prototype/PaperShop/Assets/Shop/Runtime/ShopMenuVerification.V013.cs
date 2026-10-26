using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopMenuVerification
    {
        IEnumerator VerifyV013ArrowSmoke()
        {
            App.StartSlot(2);yield return WaitForScene(false);
            Check(App.BuyCounter("bottom"),"Prepare purchased display in isolated UI scene");
            App.Economy.tutorialVersion=2;App.Economy.tutorialStep=25;App.OpenRenovationInventory(0);
            yield return Capture("01-renovation-arrow-clear.png");
            Check(ReadPresentationField<Rect>("tutorialDynamicTarget").width>0,"Small placement button registers its exact tutorial target");
            App.EndRenovation();SetPresentationField("panelKey","");App.Economy.tutorialStep=29;
            yield return Capture("02-opening-arrow-visible.png");
            Check(ReadPresentationField<Texture2D>("attentionArrowUp")!=null,"Top opening prompt has a dedicated upward arrow");
            App.Economy.tutorialStep=-1;App.OpenStaffPost("reception-1");App.SelectStaffDraft("tomori");
            yield return Capture("03-staff-probability-preview.png");
            CheckPreferencesUntouched();
        }
        IEnumerator VerifyV013Runtime()
        {
            App.SetPaused(true);App.EndRenovation();SetPresentationField("panelKey","");
            var shelf=App.Economy.shelves[0];int amount=shelf.quantity;shelf.quantity=0;
            App.World.SyncProductDisplays(App.Economy);App.World.ResetCamera();
            yield return Capture("106-empty-display-bubble.png");
            Check(App.VisibleStockAlertCount>0,"Empty display visibly renders its warning, without hover");
            SetPresentationField("hideWorldText",true);yield return Capture("107-empty-display-icon-only.png");
            Check(App.VisibleStockAlertCount>0,"Hide-text mode retains actionable empty-stock icon");
            SetPresentationField("hideWorldText",false);shelf.quantity=amount;App.World.SyncProductDisplays(App.Economy);
            yield return null;yield return new WaitForEndOfFrame();
            Check(App.VisibleStockAlertCount==0,"Restocking clears visible warning immediately");
            App.World.FocusFacility("restock-1");App.World.TickGuidedFocus(1);App.World.Zoom(-100);
            Check(Mathf.Abs(App.World.WorldCamera.orthographicSize-.85f)<.001f,"Zoom reaches character close-up regardless of room size");
            yield return Capture("108-character-closeup.png");App.World.ResetCamera();
            var spawn=typeof(ShopApp).GetMethod("CreateShopVisitor",BindingFlags.Instance|BindingFlags.NonPublic);
            var people=ReadPresentationField<IList>("guests");
            var entrance=ShopModel.EntranceFor(App.Layout);
            spawn.Invoke(App,new object[]{ShopModel.RoadSpawnFor(App.Layout),null});object payer=people[0];var payerType=payer.GetType();
            ((IList)payerType.GetField("route").GetValue(payer)).Clear();payerType.GetField("cell").SetValue(payer,entrance);payerType.GetField("destination").SetValue(payer,entrance);payerType.GetField("hasDestination").SetValue(payer,true);payerType.GetField("phase").SetValue(payer,ShopApp.GuestPhase.Paying);
            Check((bool)spawn.Invoke(App,new object[]{ShopModel.RoadSpawnFor(App.Layout),null}),"New visitor can enter while payment occupies the door tile");
            object entrant=people[1];var entrantType=entrant.GetType();var route=(IList)entrantType.GetField("route").GetValue(entrant);
            Check(route.Contains(entrance)&&(Vector2Int)entrantType.GetField("destination").GetValue(entrant)!=entrance,"Entry passes through occupied doorway and reserves a different interior stop");
            typeof(ShopApp).GetMethod("ClearCustomers",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(App,null);
            Check((bool)spawn.Invoke(App,new object[]{ShopModel.RoadSpawnFor(App.Layout),null}),"Create isolated visiting employee for assignment test");
            object guest=people[0];var type=guest.GetType();type.GetField("visitingStaffId").SetValue(guest,"rana");
            var post=App.Economy.PostFor(FacilityKind.Reception);App.OpenStaffPost(post.instanceId);
            string before=post.staffId;
            Check(!App.SelectStaffDraft("rana")&&ReadPresentationField<string>("staffDraftError").Contains("正在店内闲逛")&&post.staffId==before,"Visiting employee selection explains the reason without changing staff");
            Check(!App.ConfirmStaffSelection()&&post.staffId==before,"Blocked visitor choice cannot accidentally confirm the previous employee");
            yield return Capture("109-visiting-staff-reason.png");
            typeof(ShopApp).GetMethod("ClearCustomers",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(App,null);
            Check(App.SelectStaffDraft("tomori")&&ShopApp.StaffEffectPreview(post,"tomori").Contains("%"),"Reception draft previews numerical probabilities");
            yield return Capture("110-staff-benefit-preview.png");SetPresentationField("panelKey","");
            App.Economy.popularity=0;double normal=App.VisitShoppingProbability;
            float remaining=App.Economy.performanceRemaining;App.Economy.performanceRemaining=20;
            Check(App.VisitShoppingProbability>normal+.14,"Live performance increases entering guests' buying intent by fifteen points");
            App.Economy.performanceRemaining=remaining;
            yield return Capture("111-closed-opening-reminder.png");
            var fresh=ShopProfiles.NewGame(App.Catalog,false).economy;
            fresh.cash=0;fresh.tutorialStep=-1;foreach(var task in ShopEconomy.GrowthTasks)fresh.claimedGrowthTaskIds.Add(task.id);
            SetPresentationField("<Economy>k__BackingField",fresh);
            typeof(ShopApp).GetMethod("TickFinancialFailure",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(App,new object[]{1.1f});
            Check(App.GameFailed,"Exhausted finite store enters failure modal");yield return Capture("112-bankruptcy-choice.png");
            Check(App.ResolveFailedGame(true),"New game resolves failure by deleting only the failed slot");yield return WaitForScene(false,false);
            Check(App.TutorialChoicePending&&App.Economy.cash==800,"Failure restart creates a clean new game with tutorial choice");
            Check(ShopProfiles.Read(1,App.Catalog,out _)?.economy.cash==1777&&ShopProfiles.Exists(3)&&ShopProfiles.Exists(4),"Failure restart preserves other normal and infinite saves");
            App.ChooseTutorial(false);
        }
    }
}
