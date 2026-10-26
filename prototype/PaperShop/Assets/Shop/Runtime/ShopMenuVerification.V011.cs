using System.Collections;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopMenuVerification
    {
        IEnumerator VerifyEnvironmentPresentation()
        {
            App.EndRenovation();App.ClearFacilitySelection();SetPresentationField("panelKey","");
            foreach(var product in ShopProductSet.CurrentProducts(App.Catalog))Check(product.icon!=null,"Runtime product icon: "+product.id);
            Check(App.Economy.stageOwned&&!App.Economy.mujicaStageOwned,"Default MyGO owned, Mujica still purchasable");
            Check(App.BuyEnvironmentItem("stage_mujica"),"Buy Mujica stage from store into inventory");
            Check(!App.BuyEnvironmentItem("stage_mujica"),"Owned stage cannot be bought twice");
            Check(App.BuyEnvironmentItem("decor_flamingo")&&App.BuyEnvironmentItem("decor_ottoman"),"Buy both decorative objects");
            App.OpenPanel("decorations");yield return Capture("70-decoration-shop.png");App.OpenPanel("poststore");yield return Capture("71-stage-shop.png");SetPresentationField("panelKey","");
            var actual=App.Layout;var room=ShopModel.Clone(actual);
            room.facilities.RemoveAll(f=>f.definitionId!="warehouse");
            room.facilities.Add(new PlacedFacility{instanceId="preview-stage",definitionId="stage",x=5,z=4});
            room.facilities.Add(new PlacedFacility{instanceId="preview-flamingo",definitionId="decor_flamingo",x=3,z=7});
            room.facilities.Add(new PlacedFacility{instanceId="preview-ottoman",definitionId="decor_ottoman",x=4,z=2});
            foreach(string style in new[]{"stage","stage_mujica"})
            {
                room.facilities.Find(f=>f.instanceId=="preview-stage").definitionId=style;
                App.World.Rebuild(room,App.Catalog);App.World.ResetCamera();
                yield return Capture("72-"+style+"-models.png");
                App.World.FocusFacility("preview-stage");App.World.TickGuidedFocus(1);App.World.Zoom(-5);
                yield return Capture("73-"+style+"-close.png");
            }
            App.World.Rebuild(actual,App.Catalog);App.World.ResetCamera();
            var amounts=new int[App.Economy.shelves.Count];
            for(int i=0;i<amounts.Length;i++){amounts[i]=App.Economy.shelves[i].quantity;App.Economy.shelves[i].quantity=0;}
            App.World.SyncProductDisplays(App.Economy);Check(App.EmptyDisplayCount==amounts.Length,"All empty placed displays report stock alerts");
            yield return Capture("74-empty-display-alerts.png");
            for(int i=0;i<amounts.Length;i++)App.Economy.shelves[i].quantity=1;
            App.World.SyncProductDisplays(App.Economy);Check(App.EmptyDisplayCount==0,"Restocked displays immediately clear alerts");
            yield return Capture("75-restocked-alerts-clear.png");
            for(int i=0;i<amounts.Length;i++)App.Economy.shelves[i].quantity=amounts[i];
            App.World.SyncProductDisplays(App.Economy);
            App.ShowRenovationDrawer();App.OpenRenovationInventory(1);yield return Capture("76-decoration-inventory.png");App.EndRenovation();
        }
    }
}
