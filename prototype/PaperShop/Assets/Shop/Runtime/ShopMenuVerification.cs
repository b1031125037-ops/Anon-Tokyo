using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopMenuVerification:MonoBehaviour
    {
        [Serializable] class Report{public bool passed;public int checks,screenshots;public string[] results,errors;}
        readonly List<string> results=new List<string>(),errors=new List<string>();
        string output,originalPreferences;bool preferencesExisted;float deadline;bool finished;int shots;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-menu")<0)return;
            var obj=new GameObject("Isolated menu and art acceptance");DontDestroyOnLoad(obj);obj.AddComponent<ShopMenuVerification>();
        }
        void Awake(){Application.logMessageReceived+=Log;}
        ShopApp App=>FindFirstObjectByType<ShopApp>();
        IEnumerator Start()
        {
            // Portfolio capture owns its isolated session and must not run acceptance actions.
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--portfolio-only")>=0)yield break;
            var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"--verify-menu")+1];
            Directory.CreateDirectory(output);deadline=Time.realtimeSinceStartup+480;
            preferencesExisted=File.Exists(ShopUserSettings.SavePath);
            originalPreferences=preferencesExisted?File.ReadAllText(ShopUserSettings.SavePath):null;
            yield return null;yield return null;
            if(Array.IndexOf(args,"--verify-dev102")>=0){yield return VerifyDev101();yield return VerifyDev102();Finish();yield break;}
            if(Array.IndexOf(args,"--verify-dev101")>=0){yield return VerifyDev101();Finish();yield break;}
            if(Array.IndexOf(args,"--verify-v013-ui")>=0){yield return VerifyV013ArrowSmoke();Finish();yield break;}
            Check(Path.GetFullPath(ShopProfiles.Root)==Path.GetFullPath(Path.Combine(output,"slots")),
                "Acceptance profiles are isolated beneath the output directory");
            Check(App.IsFrontEnd&&App.FrontPage=="home","Boot displays title without entering a store");
            Check(!ShopProfiles.Exists(1)&&!ShopProfiles.Exists(2)&&!ShopProfiles.Exists(3)&&!ShopProfiles.Exists(4),"Title does not create any save");
            yield return Capture("01-title.png");
            App.OpenTitleSettings();yield return Capture("02-title-settings.png");
            Check(App.BackgroundMusic!=null&&App.BackgroundMusic.clip!=null,"Music and settings available before starting");
            Check(Mathf.Approximately(App.BackgroundMusic.volume,.18f)&&Mathf.Approximately(App.UserSettings.effectsVolume,.9f),"Actual music source uses quieter mix and effects use ninety percent");
            Check(App.OpenSaveSlots(),"Start opens slots");
            yield return Capture("03-three-empty-slots.png");
            for(int slot=1;slot<=3;slot++)
            {
                Check(App.StartSlot(slot),"Open normal slot "+slot);yield return WaitForScene(false);
                Check(!App.Economy.testMode&&App.Economy.cash==800,"Normal slot starts finite and fresh "+slot);
                Check(App.CustomerLimit==12,"Fresh normal store has the fixed default of twelve customers "+slot);
                if(slot==1){App.Economy.cash=1777;yield return Capture("04-finite-store.png");}
                Check(App.ReturnToTitle(),"Save and return from slot "+slot);yield return WaitForScene(true);
                Check(ShopProfiles.Read(slot,App.Catalog,out _)?.economy.cash==(slot==1?1777:800),"Saved independent balance "+slot);
            }
            Check(App.StartSlot(1),"Continue first slot");yield return WaitForScene(false);
            Check(App.Economy.cash==1777,"Continuing restores its own progress");
            Check(App.ReturnToTitle(),"Return from continued game");yield return WaitForScene(true);
            Check(App.StartSlot(4),"Start infinite mode");yield return WaitForScene(false);
            Check(App.Economy.testMode&&ShopSession.SavePath==ShopProfiles.PathFor(4),"Infinite mode uses separate progress");
            Check(App.Economy.ShopLevel==5&&App.Layout.width==12&&App.Layout.depth==12,"Infinite mode starts at maximum shop level and area");
            Check(!App.SetGameSpeed(2)&&App.GameSpeed==1,"Player cannot enable acceleration");
            App.SetCustomerFlowStressTest(true);App.SetCustomerLimit(5);Time.timeScale=4;
            float until=Time.realtimeSinceStartup+30;
            while(App.Economy.transactions<2&&Time.realtimeSinceStartup<until)yield return null;
            Check(App.Economy.transactions>=2,"Imported-card customers complete shopping and payment");
            Check(App.Economy.Validate(App.Catalog,App.Layout,out _),"Stock accounting remains valid with imported cards");
            Time.timeScale=1;App.SetPaused(true);App.ClearFacilitySelection();
            // Finish only the visual flips for a readable acceptance screenshot.
            // Simulation, queues and inventory remain paused.
            foreach(var actor in FindObjectsByType<PaperCharacterView>(FindObjectsSortMode.None))
            {
                actor.Tick(.3f);
                if(!actor.HasCharacterArt)continue;
                var expected=Resources.Load<Texture2D>("Characters/"+actor.CharacterId+"/"+actor.CurrentPose)
                    ??Resources.Load<Texture2D>("Characters/"+actor.CharacterId+"/idle");
                Check(actor.DisplayedCard==expected,"Rendered card matches current action: "+actor.CharacterId+" / "+actor.CurrentPose);
                Check(actor.transform.localScale==Vector3.one,"Consistent character scale: "+actor.CharacterId);
            }
            yield return Capture("05-card-store.png");
            App.OpenCounterStore();yield return Capture("06-fabric-store.png");App.OpenPanel("store");
            var cards=FindObjectsByType<PaperCharacterView>(FindObjectsSortMode.None);
            int imported=0;foreach(var actor in cards)if(actor.HasCharacterArt)imported++;
            Check(imported>=3,"Roster art replaces staff and visitor placeholders");
            yield return VerifyManagementPresentation();
            App.OpenPanel("settings");App.OpenPanel("settings");App.ClearFacilitySelection();
            App.SetShopOpen(false);
            Check(!App.Economy.shopOpen,"Closing plaque stops new arrivals");
            if(App.CustomerCount>0){Check(App.IsClosing&&!App.CanRenovate,"Closing plaque is waiting until real guests depart");yield return Capture("23-closing-plaque.png");}
            App.SetPaused(false);Time.timeScale=4;
            float closeUntil=Time.realtimeSinceStartup+25;
            while(App.IsClosing&&Time.realtimeSinceStartup<closeUntil)yield return null;
            Time.timeScale=1;App.SetPaused(true);
            Check(App.CanRenovate,"Cleared plaque permits renovation only after checkout and departure");
            yield return Capture("24-closed-plaque.png");
            yield return VerifyStorePresentation();
            yield return VerifyAllCharacterCards();
            yield return VerifyDeliveredModels();
            yield return VerifyCharacterCelebration();
            yield return VerifyProductStock();
            yield return VerifyEnvironmentPresentation();
            App.OpenPerformance();yield return Capture("45-player-fever.png");App.OpenPanel("performance");
            App.OpenPanel("goods");yield return Capture("46-product-prices.png");App.OpenPanel("goods");
            foreach(var product in ShopProductSet.CurrentProducts(App.Catalog))
                Check(App.ProductPriceText(product.id).Contains("成本 "+product.buyPrice+" · 售价 "+App.Economy.CheckoutPrice(App.Catalog,product.id)),"Buying page shows cost and actual checkout price: "+product.id);
            var display=App.Layout.facilities.Find(p=>App.Catalog.Find(p.definitionId)?.kind==FacilityKind.Shelf);
            SetPresentationField("selectedId",display.instanceId);App.OpenPanel("shelf");yield return Capture("47-shelf-price.png");App.OpenPanel("shelf");App.ClearFacilitySelection();
            Check(App.ReturnToTitle(),"Save infinite and return");yield return WaitForScene(true);
            App.OpenSaveSlots();yield return Capture("07-saved-slots.png");
            Check(ShopProfiles.Read(1,App.Catalog,out _)?.economy.cash==1777,"Infinite play never changes normal save");
            Check(App.RequestDeleteSlot(2)&&App.PendingDeleteSlot==2,"Save deletion asks for confirmation");
            yield return Capture("48-delete-slot-confirmation.png");
            Check(!App.StartSlot(1),"Deletion modal blocks starting another slot");
            App.CancelDeleteSlot();Check(ShopProfiles.Exists(2),"Cancel deletion preserves progress");
            Check(App.RequestDeleteSlot(2)&&App.ConfirmDeleteSlot()&&!ShopProfiles.Exists(2),"Confirmed deletion removes selected save");
            Check(ShopProfiles.Exists(1)&&ShopProfiles.Exists(3)&&ShopProfiles.Exists(4),"Other normal and infinite slots survive deletion");
            yield return Capture("49-deleted-slot-empty.png");
            Check(App.StartSlot(2),"Deleted slot can start a new game");yield return WaitForScene(false);
            Check(App.Economy.cash==800&&!App.Economy.shopOpen,"Recreated slot starts fresh without recovering deleted backup");
            yield return VerifyBalancedTraffic();
            yield return VerifyV013Runtime();
            Check(App.ReturnToTitle(),"Return from recreated slot");yield return WaitForScene(true);
            yield return VerifyTutorialAndDrawer();
            CheckPreferencesUntouched();
            yield return VerifyDisplayModes();
            CheckPreferencesUntouched();
            Finish();
        }
        // This harness drives application commands and presentation state, not
        // synthetic pointer events. Screenshots require separate visual review.
        IEnumerator VerifyAllCharacterCards()
        {
            var originals=FindObjectsByType<PaperCharacterView>(FindObjectsSortMode.None);
            foreach(var actor in originals)actor.gameObject.SetActive(false);
            var samples=new List<PaperCharacterView>();
            string[] ids={"anon","tomori","soyo","taki","rana","uika","mutsumi","umiri","nyamu","sakiko"};
            for(int i=0;i<ids.Length;i++)
            {
                var actor=App.World.CreatePaperActor("Art acceptance "+ids[i],Color.white);
                actor.transform.position=App.World.CellCenter(new Vector2Int(1+(i%5),1+(i/5)*3));
                actor.SetCharacter(ids[i]);actor.SetMoving(true);actor.SetPose("walk");actor.Tick(.3f);samples.Add(actor);
                Check(actor.HasCharacterArt&&actor.DisplayedCard==Resources.Load<Texture2D>("Characters/"+ids[i]+"/walk"),"Unique walking card renders for "+ids[i]);
                foreach(string pose in new[]{"cashier","welcome","think","restock","interact","pay","carry","idle"})
                {actor.SetPose(pose);actor.Tick(.3f);Check(actor.DisplayedCard==Resources.Load<Texture2D>("Characters/"+ids[i]+"/"+pose),"Pose flip resolves "+ids[i]+" / "+pose);}
                actor.SetPose("walk");actor.Tick(.3f);
            }
            yield return Capture("25-ten-staff-walking.png");
            foreach(var sample in samples){sample.gameObject.SetActive(false);Destroy(sample.gameObject);}
            foreach(var actor in originals)if(actor!=null)actor.gameObject.SetActive(true);
        }
        IEnumerator VerifyManagementPresentation()
        {
            Check(ShopCustomerFlow.DefaultCustomerLimit==12,"Customer flow default is twelve");
            Check(Mathf.Approximately(App.Economy.autoRestockThreshold,.5f),"Runtime restock threshold is fixed at fifty percent");
            Check(ShopProductSet.Categories.Length==7,"Goods catalog exposes all seven display categories");
            App.OpenPanel("store");yield return Capture("08-management-counter-store.png");
            SetPresentationField("productCategory",2);
            App.OpenPanel("goods");
            Check(ReadPresentationField<int>("productCategory")==-1,"Opening goods resets its filter to all categories");
            yield return Capture("09-goods-all-categories.png");
            App.ToggleSettingsFromEscape();Check(ReadPresentationField<string>("panelKey")=="settings","Escape switches directly from another panel to settings");
            App.ToggleSettingsFromEscape();Check(ReadPresentationField<string>("panelKey")=="","Escape closes settings without leaving the store");
            App.RequestWindowCloseForVerification();App.ToggleSettingsFromEscape();
            Check(!App.QuitPromptVisible&&ReadPresentationField<string>("panelKey")=="","Escape cancels quit confirmation without opening another panel");
            App.OpenPanel("staff");yield return Capture("10-staff-posts.png");
            App.OpenPanel("roster");yield return Capture("11-roster-contract-status.png");
            int postImage=12;
            foreach(var role in new[]{FacilityKind.Checkout,FacilityKind.Reception,FacilityKind.Restock})
            {
                var post=App.Layout.facilities.Find(p=>App.Catalog.Find(p.definitionId)?.kind==role);
                Check(post!=null,"Default layout supplies a "+role+" post");
                if(post==null)continue;
                App.OpenStaffPost(post.instanceId);
                yield return Capture((postImage++).ToString("00")+"-post-"+role.ToString().ToLowerInvariant()+".png");
            }
            Check(App.Economy.shelves.Count>0,"Default store supplies a shelf for status inspection");
            if(App.Economy.shelves.Count>0)
            {
                Check(App.OpenShelfSettings(App.Economy.shelves[0].instanceId),"Shelf information opens outside renovation");
                Check(!App.RenovationActive,"Inspecting facility information does not activate renovation");
                yield return Capture("15-shelf-status-upgrade.png");
            }
            App.OpenGrowth();yield return Capture("16-independent-tasks.png");
            App.OpenPanel("bonuses");yield return Capture("17-title-shop-bonuses.png");
            App.OpenSettings();SetPresentationField("settingsTab",0);
            yield return Capture("18-settings-graphics.png");
            App.ShowMusicSettingsForVerification();yield return Capture("19-settings-volume.png");
            SetPresentationField("settingsTab",2);yield return Capture("20-settings-help.png");
            CheckSettingsConstraints();

            var initial=App.UserSettings.Copy();
            var camera=App.World.WorldCamera;
            App.UserSettings.cameraPitch=67f;App.UserSettings.lightYaw=85f;App.UserSettings.lightPitch=32f;
            App.World.ApplyVisualSettings(App.UserSettings);
            Check(Quaternion.Angle(camera.transform.rotation,Quaternion.Euler(67f,45f,0f))<.1f,
                "Camera pitch changes while horizontal viewing angle stays fixed");
            Check(Quaternion.Angle(App.World.Daylight.transform.rotation,Quaternion.Euler(32f,85f,0f))<.1f,
                "Light direction and elevation update the real scene light");
            App.OpenPanel("settings");App.ClearFacilitySelection();
            yield return Capture("21-adjusted-camera-light.png");
            App.UserSettings.cameraPitch=-10f;App.World.ApplyVisualSettings(App.UserSettings);
            Check(Quaternion.Angle(camera.transform.rotation,Quaternion.Euler(-10f,45f,0f))<.1f&&camera.transform.position.y>=.3f,
                "Low viewing angle keeps the camera above the ground");
            App.UserSettings.cameraPitch=initial.cameraPitch;
            App.UserSettings.lightYaw=initial.lightYaw;App.UserSettings.lightPitch=initial.lightPitch;
            App.World.ApplyVisualSettings(App.UserSettings);

            var before=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(App.Economy));
            App.RequestWindowCloseForVerification();
            Check(App.QuitPromptVisible,"Window-close presentation requests save confirmation");
            yield return Capture("22-exit-save-confirmation.png");
            App.CancelQuit();
            Check(!App.QuitPromptVisible&&!App.IsFrontEnd,"Canceling exit keeps the current store");
            Check(App.SaveBeforeExit(),"Exit-save command writes the active isolated profile");
            var saved=ShopProfiles.Read(4,App.Catalog,out _);
            Check(saved!=null&&saved.economy.cash==before.cash&&saved.economy.transactions==before.transactions
                &&saved.economy.revenue==before.revenue,"Exit save preserves coins, transaction count and revenue");
            Check(saved!=null&&saved.economy.Validate(App.Catalog,saved.layout,out _),
                "Exit save reloads with valid inventory and layout accounting");
            if(saved!=null)
                foreach(var stock in before.stocks)
                {
                    var stored=saved.economy.Stock(stock.productId);
                    Check(stored!=null&&stored.purchased==stock.purchased&&stored.sold==stock.sold&&stored.returned==stock.returned,
                        "Exit save conserves purchase, sale and return totals for "+stock.productId);
                }
            Check(App.Economy.cash==before.cash&&App.Economy.transactions==before.transactions,
                "Saving before exit does not award duplicate live income");
            CheckPreferencesUntouched();
        }
        void CheckSettingsConstraints()
        {
            var low=ShopUserSettings.Defaults();
            low.cameraPitch=-10f;low.lightYaw=-180f;low.lightPitch=10f;low.effectsVolume=0f;
            Check(low.Validate(out _),"New camera, lighting and effect-volume lower bounds are accepted");
            var high=ShopUserSettings.Defaults();
            high.cameraPitch=85f;high.lightYaw=180f;high.lightPitch=85f;high.effectsVolume=1f;
            Check(high.Validate(out _),"New camera, lighting and effect-volume upper bounds are accepted");
            var invalid=new Action<ShopUserSettings>[]{
                s=>s.cameraPitch=-11f,s=>s.cameraPitch=86f,s=>s.cameraPitch=float.NaN,
                s=>s.lightYaw=-181f,s=>s.lightYaw=181f,s=>s.lightYaw=float.PositiveInfinity,
                s=>s.lightPitch=9f,s=>s.lightPitch=86f,s=>s.effectsVolume=-.01f,s=>s.effectsVolume=1.01f};
            for(int i=0;i<invalid.Length;i++)
            {
                var sample=ShopUserSettings.Defaults();invalid[i](sample);
                Check(!sample.Validate(out _),"Invalid new graphics/audio setting is rejected "+(i+1));
            }
        }
        IEnumerator VerifyStorePresentation()
        {
            Check(!App.Layout.facilities.Exists(f=>f.definitionId=="decor"||f.definitionId=="fitting"),"Retired decoration and fitting-room placeholders are absent");
            App.OpenPanel("bonuses");yield return Capture("26-shop-upgrade.png");
            App.OpenPanel("store");yield return Capture("27-display-catalog.png");
            SetPresentationField("surfaceStoreFloor",false);App.OpenPanel("materials");yield return Capture("28-wallpaper-tab.png");
            SetPresentationField("surfaceStoreFloor",true);yield return Capture("29-floor-tab.png");
            App.OpenPanel("decorations");yield return Capture("30-decoration-empty.png");
            App.OpenPanel("poststore");yield return Capture("31-staff-facility-shop.png");
            App.OpenPanel("build");yield return Capture("32-renovation-no-placeholders.png");
            App.OpenPanel("build");
            var warehouse=App.Layout.facilities.Find(f=>f.definitionId=="warehouse");
            App.World.SelectFacility(warehouse.instanceId);
            bool arrow=false;
            foreach(var t in App.World.GetComponentsInChildren<Transform>(true))
                if(t.name=="Facility facing"&&t.parent!=null&&t.parent.parent!=null&&t.parent.parent.name.StartsWith("Facility "+warehouse.instanceId+" -"))arrow=true;
            Check(!arrow,"Selected warehouse has no facing arrow geometry");
            App.World.ShowPlacement(warehouse,true);
            bool preview=false;
            foreach(var t in App.World.GetComponentsInChildren<Transform>(true))if(t.name=="Candidate direction")preview=t.gameObject.activeSelf;
            Check(!preview,"Moving the warehouse also hides its direction arrow");
            yield return Capture("33-warehouse-no-arrow.png");
            App.World.HidePlacement();App.ClearFacilitySelection();
            // Isolated acceptance-only finite HUD samples; gameplay accounting is tested in editor suites.
            int coins=App.Economy.cash,pop=App.Economy.popularity;
            App.Economy.testMode=false;App.Economy.cash=2500;yield return null;yield return null;
            var feedback=ReadPresentationField<ShopResourceFeedback>("resourceFeedback");
            App.Economy.cash+=120;App.Economy.popularity++;
            // Observe commits in LateUpdate, then batches for 100 ms. Wait for
            // the observable result instead of racing that exact time boundary.
            float feedbackDeadline=Time.realtimeSinceStartup+2;
            while((feedback.displayed[0]!=120||feedback.displayed[2]!=1)&&Time.realtimeSinceStartup<feedbackDeadline)yield return null;
            yield return Capture("34-coins-gain-popularity.png");
            Check(feedback.displayed[0]==120&&feedback.displayed[2]==1,"Runtime gain and popularity feedback matches committed displayed balances");
            App.Economy.cash-=300;
            feedbackDeadline=Time.realtimeSinceStartup+2;
            while(feedback.displayed[0]!=-300&&Time.realtimeSinceStartup<feedbackDeadline)yield return null;
            yield return Capture("35-coins-spend.png");
            Check(feedback.displayed[0]==-300,"Runtime spend feedback displays the negative amount");
            App.Economy.testMode=true;App.Economy.cash=coins;App.Economy.popularity=pop;yield return null;yield return null;
            Check(feedback.started[0]<0,"Switching back to infinite mode clears finite-resource animation");
            yield return VerifyRenovationInventory();
        }
        IEnumerator VerifyRenovationInventory()
        {
            App.OpenPanel("store");Check(!App.RenovationActive,"Store is a purchasing mode and exits renovation editing");
            Check(App.BuyCounter("bottom"),"Purchase a display into storage without placing it");
            App.OpenRenovationInventory(0);yield return Capture("36-renovation-display-stock.png");
            for(int category=0;category<5;category++)
            {
                App.OpenRenovationInventory(category);
                Check(ReadPresentationField<string>("panelKey")=="build"&&App.RenovationCategory==category,"Renovation category "+category+" stays inside owned inventory instead of opening store");
            }
            App.OpenPanel("materials");Check(!App.RenovationActive,"Material store cannot directly edit the room");
            int materialCoins=App.Economy.cash;
            Check(App.BuyMaterial("floor_tile",10)&&App.Economy.cash==materialCoins,"Free floor material is claimed into counted stock");
            Check(!App.IsPaintingSurface,"Claiming material does not enter painting mode");
            App.OpenRenovationInventory(3);yield return Capture("37-renovation-floor-stock.png");
            Check(App.BeginSurfacePainting("floor_tile"),"Owned floor material can be selected from renovation inventory");
            App.EndSurfacePainting();Check(App.Economy.MaterialCount("floor_tile")==10,"Canceling surface preview consumes nothing");
            Check(App.ApplySurface("floor",0,0,"floor_tile")&&App.Economy.MaterialCount("floor_tile")==9&&App.Economy.MaterialCount("floor_wood")==1,"Actual surface painting consumes new material and returns the replaced tile");
            App.OpenRenovationInventory(3);yield return Capture("38-renovation-returned-material.png");
            SetPresentationField("panelKey","");SetPresentationField("renovationMode",false);App.World.SetEditing(false);
            yield return Capture("39-before-open-side-expansion.png");
            Check(App.Layout.width==12&&App.Layout.depth==12&&!App.ExpandShop(),"Max-level sandbox refuses further expansion without changing layout");
            Check(ShopSurfaces.Get(App.Layout,"floor",0,0)=="floor_tile","Max-level rejection preserves placed material");
            Check(ShopModel.FindPath(App.Layout,App.Catalog,ShopModel.EntranceFor(App.Layout),ShopModel.WarehousePickup(App.Layout)).Count>0,"Maximum sandbox keeps entrance connected to warehouse");
            yield return Capture("40-max-level-store.png");
            Check(App.Economy.Validate(App.Catalog,App.Layout,out _),"Inventory remains valid after material purchases, replacement and open-edge expansions");
        }
        IEnumerator VerifyDeliveredModels()
        {
            var real=App.Layout;var showroom=ShopModel.Clone(real);showroom.width=12;showroom.depth=12;
            showroom.facilities.RemoveAll(p=>p.definitionId!="warehouse");
            var door=showroom.facilities[0];door.x=12;door.wallPosition=11;
            string[] ids={"top","bottom","outfit","shoes","head","bag","accessory","checkout"};
            for(int i=0;i<ids.Length;i++)showroom.facilities.Add(new PlacedFacility{instanceId="asset-"+ids[i],definitionId=ids[i],x=1+(i%3)*3,z=2+(i/3)*3});
            var actors=FindObjectsByType<PaperCharacterView>(FindObjectsSortMode.None);foreach(var actor in actors)actor.gameObject.SetActive(false);
            App.ClearFacilitySelection();SetPresentationField("panelKey","");
            for(int rotation=0;rotation<4;rotation++)
            {
                foreach(var p in showroom.facilities)if(p.definitionId!="warehouse")p.rotation=rotation;
                App.World.Rebuild(showroom,App.Catalog);App.World.SetEditing(true);yield return null;yield return null;
                foreach(var p in showroom.facilities)
                {
                    Transform root=null;foreach(var t in App.World.GetComponentsInChildren<Transform>())if(t.name.StartsWith("Facility "+p.instanceId+" -")){root=t;break;}
                    var slot=root.Find("VisualSlot");var renderers=slot.GetComponentsInChildren<Renderer>();
                    Check(renderers.Length==1,"One delivered mesh replaces placeholder: "+p.definitionId+" / "+rotation);
                    var bounds=renderers[0].bounds;
                    Check(Mathf.Abs(bounds.min.y-.02f)<.002f,"Model rests on shop floor: "+p.definitionId+" / "+rotation);
                    var screen=App.World.WorldCamera.WorldToScreenPoint(bounds.center);
                    Check(App.World.PickFacility(screen,out var picked)&&picked==p.instanceId,"Imported geometry remains selectable: "+p.definitionId+" / "+rotation);
                    if(p.definitionId=="warehouse"){
                        Check(Mathf.Abs(bounds.max.x-(showroom.width*.5f-.025f))<.002f,"Imported warehouse back aligns with right wall");
                        Check(Vector3.Dot(slot.GetChild(0).forward,Vector3.right)>.99f,"Warehouse front faces inward");
                    }
                }
                yield return Capture("42-models-rotation-"+rotation+".png");
            }
            App.World.Rebuild(real,App.Catalog);App.World.SetEditing(false);foreach(var actor in actors)if(actor!=null)actor.gameObject.SetActive(true);
            foreach(string key in new[]{"income","stock"}){
                typeof(ShopApp).GetMethod("PlaySfx",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(App,new object[]{key});
                var source=ReadPresentationField<AudioSource>(key=="income"?"registerEffects":"clothEffects");
                Check(source.clip==Resources.Load<AudioClip>("Audio/Effects/"+(key=="income"?"register":"clothes")),"Runtime event uses supplied sound: "+key);
                Check(Mathf.Approximately(source.volume,App.UserSettings.effectsVolume),"Imported sound shares configured effect volume: "+key);
                source.Stop();
            }
        }
        IEnumerator VerifyCharacterCelebration()
        {
            bool renovation=App.RenovationActive;SetPresentationField("renovationMode",false);
            var actors=new List<PaperCharacterView>();int index=0;
            foreach(string id in new[]{"anon","tomori","soyo","taki","rana","uika","mutsumi","umiri","nyamu","sakiko"})
            {
                var actor=App.World.CreatePaperActor("Celebration acceptance "+id,Color.white);actor.SetCharacter(id);
                actor.transform.position=App.World.CellCenter(new Vector2Int(1+index%5*2,1+index/5*3));index++;
                actor.SetPose("perform");actor.Tick(.3f);
                Check(actor.CurrentPose=="perform","Dedicated performance pose requested: "+id);
                Check(actor.Celebrate()&&actor.IsCelebrating,"Celebration starts: "+id);
                Check(!actor.Celebrate(),"Repeated click does not endlessly extend celebration: "+id);
                actor.SetPose("restock");actor.Tick(.3f);
                Check(actor.CurrentPose=="celebrate","Business pose changes do not interrupt celebration: "+id);
                actors.Add(actor);
            }
            yield return Capture("43-ten-celebrations.png");
            yield return new WaitForSecondsRealtime(1.9f);
            foreach(var actor in actors){actor.Tick(.3f);Check(!actor.IsCelebrating&&actor.CurrentPose=="restock","Celebration restores latest task instead of stale pose: "+actor.CharacterId);}
            var first=actors[0];first.SetPose("idle");first.Tick(.3f);
            Vector3 target=first.transform.position+Vector3.up*.7f;var screen=App.World.WorldCamera.WorldToScreenPoint(target);
            Check(App.TryCelebrateCharacter(screen)&&first.IsCelebrating,"Clicking the visible character triggers celebration");
            var miss=first.transform.position+App.World.WorldCamera.transform.right*3+Vector3.up*3;
            Check(!first.HitCard(App.World.WorldCamera.ScreenPointToRay(App.World.WorldCamera.WorldToScreenPoint(miss)),out _),"Click outside the card is rejected");
            foreach(var actor in actors)Destroy(actor.gameObject);SetPresentationField("renovationMode",renovation);
        }
        IEnumerator VerifyDisplayModes()
        {
            Check(App.DisplayChoices.Count>=30,"Thirty common resolutions remain available regardless of current window size");
            var before=App.UserSettings.Copy();
            Check(!App.ApplyDisplaySettings(1111,777,0),"Unknown display size is rejected without mutation");
            Check(!App.ApplyDisplaySettings(1280,720,3),"Unknown display mode is rejected");
            bool hardware=Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-display")>=0;
            var native=ShopApp.NativeDisplaySize();
            Check(App.ApplyDisplaySettings(1360,850,1),"Borderless mode can be applied");
            Check(App.UserSettings.DisplayMode==1&&App.UserSettings.windowWidth==native.x&&App.UserSettings.windowHeight==native.y,"Borderless fills the native monitor instead of a small render window");
            if(hardware){yield return new WaitForSecondsRealtime(1);Check(Screen.fullScreenMode==FullScreenMode.FullScreenWindow&&Screen.width==native.x&&Screen.height==native.y,"Actual native borderless screen mode and dimensions");}
            App.OpenTitleSettings();yield return Capture("44-native-fullscreen-settings.png");
            Check(App.ApplyDisplaySettings(1280,720,0),"Window mode remains available");
            if(hardware){yield return new WaitForSecondsRealtime(.8f);Check(Screen.fullScreenMode==FullScreenMode.Windowed&&Screen.width==1280&&Screen.height==720,"Actual window restoration after fullscreen");}
            Check(ShopDisplayModes.UnityMode(2)==FullScreenMode.ExclusiveFullScreen,"Exclusive fullscreen maps to Windows exclusive mode");
            App.ApplyDisplaySettings(before.windowWidth,before.windowHeight,before.DisplayMode);
            yield return null;
        }
        void SetPresentationField<T>(string name,T value)
        {
            var field=typeof(ShopApp).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            if(field==null){errors.Add("Missing presentation state "+name);return;}
            field.SetValue(App,value);
        }
        T ReadPresentationField<T>(string name)
        {
            var field=typeof(ShopApp).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            if(field==null){errors.Add("Missing presentation state "+name);return default(T);}
            return (T)field.GetValue(App);
        }
        void CheckPreferencesUntouched()
        {
            bool exists=File.Exists(ShopUserSettings.SavePath);
            Check(exists==preferencesExisted&&(!exists||File.ReadAllText(ShopUserSettings.SavePath)==originalPreferences),
                "Acceptance never creates or rewrites the player's graphics/audio preference file");
        }
        IEnumerator WaitForScene(bool menu,bool skipTutorial=true)
        {
            yield return null;yield return null;
            float until=Time.realtimeSinceStartup+12;
            while((App==null||App.IsFrontEnd!=menu)&&Time.realtimeSinceStartup<until)yield return null;
            yield return null;Check(App!=null&&App.IsFrontEnd==menu,"Scene transition completes");
            if(App!=null){App.ApplyApplicationFocus(true);App.ApplyApplicationPause(false);}
            if(App!=null&&!menu&&skipTutorial&&App.TutorialChoicePending)
            {Check(App.ChooseTutorial(false)&&App.TutorialStep==-1,"Each fresh slot offers and persists tutorial skip");}
        }
        IEnumerator WaitForTutorial(int step,float seconds=12)
        {
            float end=Time.realtimeSinceStartup+seconds;
            while(App.TutorialStep!=step&&Time.realtimeSinceStartup<end)yield return null;
            Check(App.TutorialStep==step,"Onboarding reaches step "+step);
        }
        IEnumerator VerifyTutorialAndDrawer()
        {
            App.OpenSaveSlots();Check(App.RequestDeleteSlot(2)&&App.ConfirmDeleteSlot(),"Prepare isolated fresh tutorial slot");
            App.StartSlot(2);yield return WaitForScene(false,false);
            Check(App.TutorialChoicePending,"Deleted/recreated slot asks again");
            yield return Capture("50-anon-new-save-choice.png");
            int cash=App.Economy.cash;
            Check(App.ChooseTutorial(true)&&!App.Economy.shopOpen&&App.Economy.cash==cash,"Learning starts closed without buying or charging");
            yield return Capture("51-anon-first-dialogue.png");
            string product=App.Economy.shelves[0].productId;
            App.TutorialAction();Check(ReadPresentationField<string>("panelKey")=="","Dialogue does not open a facility window");App.SkipTutorial();Check(App.TutorialStep==3,"Skip is unavailable once learning begins");
            Check(App.ReturnToTitle(),"Save midway through onboarding");yield return WaitForScene(true);
            App.StartSlot(2);yield return WaitForScene(false,false);
            Check(!App.TutorialChoicePending&&App.TutorialStep==3,"Reload resumes without a second choice or purchase");
            for(int step=3;step<=5;step++)
            {
                App.TutorialAction();Check(ReadPresentationField<string>("panelKey")=="","Tutorial only focuses scene, never opens post "+step);
                var post=App.Economy.PostFor(step==3?FacilityKind.Restock:step==4?FacilityKind.Checkout:FacilityKind.Reception);
                yield return new WaitForSecondsRealtime(.8f);
                Check(App.World.GuidedFacilityId==post.instanceId,"Camera focuses the correct facility "+step);
                App.OpenStaffPost(post.instanceId);
                string before=post.staffId;
                Check(App.SelectStaffDraft("")&&post.staffId==before,"Draft employee choice does not mutate job "+step);
                App.OpenStaffPost(post.instanceId);Check(App.SelectedStaffDraft==before,"Closing and reopening discards pending choice "+step);
                Check(App.SelectStaffDraft(before),"Existing employee can be selected "+step);
                yield return Capture("53-anon-post-"+step+".png");
                Check(App.ConfirmStaffSelection()&&App.TutorialStep==(step==5?23:step+1),"Post confirmation commits and advances tutorial "+step);
            }
            App.OpenCounterStore();yield return Capture("101-guide-buy-display.png");
            Check(App.BuyCounter("bottom"),"Buy second display with the new 800-coin starting budget");yield return WaitForTutorial(24);
            App.TutorialAction();yield return WaitForTutorial(25);App.OpenRenovationInventory(0);yield return Capture("102-guide-renovation.png");
            var stored=App.Economy.storedFixtures.Find(f=>f.definitionId=="bottom");bool placed=false;
            if(stored!=null)
            {
                App.BeginStoredCounterPlacement(stored.instanceId);
                for(int z=1;z<App.Layout.depth-1&&!placed;z++)for(int x=1;x<App.Layout.width-1&&!placed;x++)
                    placed=App.ApplyPlacement(new PlacedFacility{instanceId=stored.instanceId,definitionId=stored.definitionId,x=x,z=z,rotation=0},out _);
            }
            Check(placed,"Place purchased display through actual renovation validation");yield return WaitForTutorial(1);
            App.OpenPanel("goods");yield return Capture("52-anon-products-side.png");
            Check(App.ContractProduct(product),"Tutorial player signs first displayed product");yield return WaitForTutorial(2);
            Check(App.OrderProduct(product),"Order first real product");yield return WaitForTutorial(26);
            string second=App.Economy.shelves.Find(v=>v.productId!=product).productId;
            Check(App.ContractProduct(second),"Sign second displayed product");yield return WaitForTutorial(27);
            yield return Capture("103-guide-second-product.png");
            Check(App.OrderProduct(second),"Order second real product");yield return WaitForTutorial(6);
            float until=Time.realtimeSinceStartup+30;Time.timeScale=4;
            while(!App.Economy.shelves.Exists(s=>s.productId==product&&s.quantity>0)&&Time.realtimeSinceStartup<until)yield return null;
            Time.timeScale=1;App.OpenShelfSettings(App.Economy.shelves.Find(x=>x.productId==product).instanceId);yield return WaitForTutorial(7);yield return Capture("54-anon-stock-and-price.png");
            Check(App.TutorialStep==7,"Shelf inspection confirms real stock before opening");
            App.SetShopOpen(true);Check(App.Economy.shopOpen,"Player opens real shop");yield return WaitForTutorial(8);
            Check(ReadPresentationField<bool>("tutorialCollapsed"),"Opening collapses dialogue while awaiting a real customer");
            App.SetCustomerFlowStressTest(true);Time.timeScale=4;yield return WaitForTutorial(9,40);Time.timeScale=1;
            yield return Capture("55-anon-first-sale.png");
            App.TutorialAction();Check(App.TutorialStep==28,"First sale leads to task rewards");
            App.OpenPanel("growth");yield return Capture("104-guide-task-reward.png");
            Check(App.ClaimGrowthTask("staff_ready"),"Claim actual task reward");yield return WaitForTutorial(10);
            App.RequestCloseShop(out _);Time.timeScale=4;until=Time.realtimeSinceStartup+15;
            while(!App.CanRenovate&&Time.realtimeSinceStartup<until)yield return null;Time.timeScale=1;
            App.Economy.cash=0;App.Economy.popularity=10;App.Economy.peakPopularity=10;yield return null;yield return null;
            Check(App.TutorialStep==10,"Popularity alone does not trigger upgrade guide");
            App.Economy.cash=2500;yield return WaitForTutorial(11);yield return Capture("56-anon-upgrade-reminder.png");
            App.DeferTutorial();Check(App.Economy.tutorialDeferred,"Upgrade reminder can be deferred");
            App.ReturnToTitle();yield return WaitForScene(true);App.StartSlot(2);yield return WaitForScene(false,false);
            Check(App.TutorialStep==11&&App.Economy.tutorialDeferred,"Deferred upgrade reminder stays deferred after reload");
            App.ResumeTutorial();App.TutorialAction();Check(ReadPresentationField<string>("panelKey")=="","Upgrade dialogue does not open title panel");App.OpenPanel("bonuses");yield return WaitForTutorial(12);
            Check(App.ExpandShop(),"Real paid upgrade completes during tutorial");yield return WaitForTutorial(13);
            yield return Capture("57-anon-upgrade-complete.png");App.TutorialAction();Check(App.TutorialStep==15,"First expansion leads to wallpaper lesson");
            int finalCash=App.Economy.cash;App.ReviewTutorial();App.TutorialAction();
            Check(App.TutorialStep==15&&App.Economy.cash==finalCash,"Help review neither resets progress nor rewards money");
            SetPresentationField("tutorialReview",-1);
            Check(App.BuyMaterial("wall_rose",10),"Claim wallpaper through actual store command");yield return WaitForTutorial(16);
            Check(App.BuyMaterial("floor_tile",10),"Claim floor material through actual store command");yield return WaitForTutorial(29);
            SetPresentationField("panelKey","");yield return Capture("105-guide-reopen.png");
            App.SetShopOpen(true);yield return WaitForTutorial(17);
            App.RequestCloseShop(out _);until=Time.realtimeSinceStartup+30;while(!App.CanRenovate&&Time.realtimeSinceStartup<until)yield return null;
            Check(App.ReturnToTitle(),"Save material lessons");yield return WaitForScene(true);App.StartSlot(2);yield return WaitForScene(false,false);
            Check(App.Economy.tutorialWallClaimed&&App.Economy.tutorialFloorClaimed&&App.TutorialStep==17,"Material lesson flags survive save and load");
            App.OpenRenovationInventory(0);yield return Capture("58-renovation-drawer.png");
            App.CollapseRenovationDrawer();Check(App.RenovationActive&&App.RenovationDrawerCollapsed,"Collapse keeps editing active");
            yield return Capture("59-renovation-collapsed.png");App.ShowRenovationDrawer();
            Check(App.RenovationActive&&!App.RenovationDrawerCollapsed,"Drawer reopens while already editing");
            App.ToggleSettingsFromEscape();App.ToggleSettingsFromEscape();App.ShowRenovationDrawer();
            Check(App.RenovationActive&&!App.RenovationDrawerCollapsed,"Settings round trip cannot strand renovation panel");
            App.BuyMaterial("floor_tile",10);App.OpenRenovationInventory(3);yield return Capture("60-renovation-materials.png");
            App.BeginSurfacePainting("floor_tile");App.CollapseRenovationDrawer();App.ShowRenovationDrawer();
            Check(App.IsPaintingSurface,"Collapsing and reopening preserves active material");
            Check(App.ApplySurface("floor",0,0,"floor_tile"),"Surface can still be applied after reopening");
            App.EndRenovation();Check(!App.RenovationActive&&!App.IsPaintingSurface,"Done exits mode and cancels preview");
            App.OpenRenovationInventory(2);Check(App.RenovationActive,"Renovation may be entered again after completion");App.EndRenovation();
            Check(App.Economy.Validate(App.Catalog,App.Layout,out _),"Tutorial and drawer preserve inventory accounting");
            yield return VerifyStageLessonsAndMusic();
            App.ReturnToTitle();yield return WaitForScene(true);
        }
        IEnumerator Capture(string file)
        {
            yield return new WaitForSecondsRealtime(.15f);yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            if(image==null){errors.Add("Capture failed "+file);yield break;}
            File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());Destroy(image);shots++;
        }
        void Update(){if(!finished&&deadline>0&&Time.realtimeSinceStartup>deadline){errors.Add("Timeout");Finish();}}
        void Check(bool ok,string message){if(ok)results.Add(message);else errors.Add(message);}
        void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
        void Finish()
        {
            if(finished)return;finished=true;StopAllCoroutines();Time.timeScale=1;
            var report=new Report{passed=errors.Count==0,checks=results.Count,screenshots=shots,results=results.ToArray(),errors=errors.ToArray()};
            File.WriteAllText(Path.Combine(output,"menu-art-runtime-verification.json"),JsonUtility.ToJson(report,true));
            Application.logMessageReceived-=Log;Application.Quit(report.passed?0:1);
        }
    }
}
