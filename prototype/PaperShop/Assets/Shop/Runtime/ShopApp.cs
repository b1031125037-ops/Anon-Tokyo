using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Shop layout, logistics commands, and the single paper clerk's service-point tasks.</summary>
    public sealed partial class ShopApp : MonoBehaviour
    {
        public ShopCatalog catalog;
        public Shader litShader;
        public ShopLayout Layout { get; private set; }
        public ShopCatalog Catalog => catalog;
        public ShopWorldView World { get; private set; }
        public ShopEconomy Economy { get; private set; }
        public bool IsRestocking => Economy != null && Economy.job != null;
        public bool IsPaused { get; private set; }
        public bool IsWalking => path.Count > 0;
        public int CompletedWalks { get; private set; }
        string lastMessage;
        float toastUntil;
        public string LastMessage { get => lastMessage; private set { lastMessage=value; toastUntil=Time.unscaledTime+5f; } }
        public Vector2Int ActorCell { get; private set; }

        readonly List<Vector2Int> path = new List<Vector2Int>();
        readonly List<Texture2D> textures = new List<Texture2D>();
        string selectedId, placingDefinition, movingId;
        int rotation;
        string panelKey = "";
        readonly List<Rect> uiRegions = new List<Rect>();
        float uiScale = 1;
        bool dirty, workAtArrival, candidateValid;
        bool renovationMode,hideWorldText;
        bool panGesture;
        bool verifyMode, blockedSave;
        string loadProblem;
        enum RestockPhase { None, ToWarehouse, Picking, ToShelf, Stocking }
        RestockPhase restockPhase;
        float taskRemaining, autoDelay;
        float workRemaining;
        PlacedFacility candidate;
        string placementReason = "请将鼠标移到店内";
        Vector2 scroll;
        GUIStyle title, heading, body, small, button, activeButton, panel, chip, rightText, toolbarButton, toolbarActive;
        Font font;
        Texture2D line;
        readonly Color ink = new Color(.35f,.28f,.22f);
        readonly Color muted = new Color(.47f,.40f,.33f);

        void Awake()
        {
            Application.runInBackground = true;
            Application.wantsToQuit+=HandleQuitRequest;
            if (catalog == null) catalog = Resources.Load<ShopCatalog>("Shop/DefaultCatalog");
            if (catalog == null) catalog = ShopCatalog.CreateDefault();
            ShopCatalog.EnsureStaffDefinitions(catalog);
            if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
            bool verify = verifyMode = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-map") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-crowd") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-mechanics") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-operations") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-renovation") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-settings") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-offline") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-growth") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-performance") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-surfaces") >= 0;
            var args=Environment.GetCommandLineArgs();
            int menuCheck=Array.IndexOf(args,"--verify-menu");isMenuVerification=menuCheck>=0;
            if(isMenuVerification)Application.runInBackground=true;
            if(menuCheck>=0&&menuCheck+1<args.Length)ShopProfiles.VerificationRoot=Path.Combine(args[menuCheck+1],"slots");
            frontEnd=!verify&&!ShopProfiles.StartRequested;
            ShopSession.TestProfile=!verify&&ShopProfiles.SelectedSlot==4;
            string loaded="";
            if(!frontEnd&&!verify&&ShopSession.HasSave)
            {
                var session=ShopProfiles.Read(ShopProfiles.SelectedSlot,catalog,out loaded);
                session=RecoverOfflineSession(session);
                if(session!=null){Layout=session.layout;Economy=session.economy;Economy.CancelRestock();Economy.ReturnAllBaskets();}
                else{blockedSave=true;loadProblem=loaded;}
            }
            if(Layout==null)
            {
                if(verify){Layout=ShopModel.CreateDefault();Economy=ShopEconomy.CreateDefault(catalog,Layout);}
                else{var fresh=ShopProfiles.NewGame(catalog,ShopSession.TestProfile);Layout=fresh.layout;Economy=fresh.economy;}
            }
            Economy.MigrateManagementRules();
            ShopProfiles.MaximizeSandbox(catalog,Layout,Economy);
            RemoveRetiredPlaceholders();
            Economy.InitializeRenovationInventory(Layout);Economy.InitializeBalance(Layout);
            EnsureCheckoutLayout();
            if(!Economy.staffPointsIntroduced)
            {ShopSalesTestSetup.EnsureStaffPoints(Layout,catalog);Economy.staffPointsIntroduced=true;}
            if(!Economy.SyncStaffPosts(catalog,Layout,out string staffReason))throw new InvalidOperationException(staffReason);
            if(!Economy.InitializeFixtureStore(catalog,Layout,out string fixtureReason))throw new InvalidOperationException(fixtureReason);
            World = new GameObject("Shop World").AddComponent<ShopWorldView>();
            World.transform.SetParent(transform, false);
            World.Initialize(Layout, catalog, litShader);
            World.SetWarehouseLevel(Economy.warehouseLevel);World.PresentationLabelsEnabled=!frontEnd;
            ActorCell = RestockHomeCell;
            if (ShopModel.FindPath(Layout, catalog, ShopModel.EntranceFor(Layout), ActorCell).Count == 0) ActorCell = ShopModel.EntranceFor(Layout);
            World.CharacterRoot.position = World.CellCenter(ActorCell);
            InitializeTradingActors();
            World.Character.SetTint(new Color(1f,.88f,.91f));
            LastMessage = Economy.testMode ? "店铺已营业：客人每 6 秒按概率来访。点击岗位地标或收银台配置员工与升级。" : verify || string.IsNullOrEmpty(loaded) ? "先在商品页签约并进货，送达后店员会从右后仓库取货补架。" : loaded;
            InitializeBusinessState();
            InitializeSettings();InitializeSoundEffects();
            if(!frontEnd)InitializeOfflineTracking();
            if(Economy.IsPerforming)simulationClock.SetSpeed(1);
            SyncPerformanceActors();
            SyncStaffCards();
            ResetResourceFeedback();
            if (blockedSave) panelKey = "save";
        }

        void Update()
        {
            if(frontEnd||loadingScene){TickSettings();return;}
            if(gameFailed){TickSettings();return;}
            TickCameraKeys();
            float dt = quitPrompt||TutorialChoicePending?0:CalculateSimulationDelta(Time.deltaTime);
            int ordersBefore = Economy.orders.Count;
            int pendingBefore = PendingDeliveryCount;
            Economy.Tick(dt);
            TickPerformance(dt);
            if (ordersBefore != Economy.orders.Count) { dirty = true; PlaySfx("delivery"); LastMessage = "货物已送达右后仓库，已计入可用库存。"; }
            else if (pendingBefore != PendingDeliveryCount) { dirty = true; LastMessage = "订单已发货，正在运往仓库；可在配送订单页查看进度。"; }
            autoDelay = Mathf.Max(0, autoDelay - dt);
            if (path.Count > 0 && dt > 0)
            {
                Vector3 destination = World.CellCenter(path[0]);
                World.CharacterRoot.position = Vector3.MoveTowards(World.CharacterRoot.position, destination, dt * 2.35f * Economy.WorkEfficiency(FacilityKind.Restock));
                if (Vector3.SqrMagnitude(World.CharacterRoot.position - destination) < .0001f)
                {
                    ActorCell = path[0];
                    path.RemoveAt(0);
                    if (path.Count == 0)
                    {
                        CompletedWalks++;
                        if (workAtArrival) { workRemaining = 2.7f; World.Character.SetWorking(true); }
                        if (!IsRestocking) LastMessage = workAtArrival ? "已到达服务点 · 翻面切换交互姿势" : "已到达目标格。人物按通路绕开设施。";
                    }
                }
            }
            if (workRemaining > 0 && path.Count == 0)
            {
                workRemaining -= dt;
                if (workRemaining <= 0) World.Character.SetWorking(false);
            }
            AdvanceRestock(dt);
            if (dt > 0 && HasRestockStaff && !IsRestocking && path.Count == 0 && workRemaining <= 0 && autoDelay <= 0 && string.IsNullOrEmpty(placingDefinition))
            {
                string shelfId = Economy.NextRestock(catalog, Layout);
                if (!string.IsNullOrEmpty(shelfId) && !RequestRestock(shelfId)) autoDelay = 3f;
            }
            TickRestockHome(dt);TickExtraRestocker(dt);
            World.Character.SetMoving(path.Count > 0);
            World.Character.Tick(dt);
            TickCustomers(dt);
            SyncStaffCards();
            TickSettings();
            TickOfflineTracking();
            TickTutorial();TickFinancialFailure(dt);World.TickGuidedFocus(Time.unscaledDeltaTime);World.SyncProductDisplays(Economy);
            if (!string.IsNullOrEmpty(placingDefinition)) UpdateCandidate();
        }

        public void SetPaused(bool value) { IsPaused = value; }

        public bool MoveActor(Vector2Int destination, bool interact = false)
        {
            if (IsRestocking) CancelRestockTask();
            autoDelay = 5f;
            return RouteActor(destination, interact);
        }

        bool RouteActor(Vector2Int destination, bool interact = false)
        {
            if (!string.IsNullOrEmpty(placingDefinition)) return false;
            // Finish the current grid edge before following a new route. Retargeting
            // must not teleport the character back to the previous cell centre.
            bool midStep = path.Count > 0;
            Vector2Int routeStart = midStep ? path[0] : ActorCell;
            var next = ShopModel.FindPath(Layout, catalog, routeStart, destination);
            if (next == null || next.Count == 0)
            { LastMessage = "这里不可到达，请选择空地或设施的服务点。"; return false; }
            path.Clear();
            World.Character.SetWorking(false);
            workRemaining = 0;
            path.AddRange(next);
            if (!midStep && path.Count > 0 && path[0] == ActorCell) path.RemoveAt(0);
            workAtArrival = interact;
            if (path.Count == 0 && interact) { workRemaining = 2.7f; World.Character.SetWorking(true); }
            LastMessage = interact ? "店员正前往服务点。" : "按网格寻路中…";
            return true;
        }

        void StopAtCurrentCell()
        {
            path.Clear();
            World.CharacterRoot.position = World.CellCenter(ActorCell);
            World.Character.SetMoving(false);
            World.Character.SetWorking(false);
            workRemaining = 0;
        }

        bool CanApply(PlacedFacility item, out string reason)
        {
            return TryDraftPlacement(item,out _,out _,out reason);
        }

        bool TryDraftPlacement(PlacedFacility item,out ShopLayout draft,out ShopEconomy state,out string reason)
        {
            draft=null;state=null;
            if(!CanRenovate){reason="请先关店，等待全部顾客付款离店后再装修。";return false;}
            if(item==null || catalog.Find(item.definitionId)==null){reason="设施不存在。";return false;}
            if(catalog.Find(item.definitionId).kind==FacilityKind.Stage && Economy.ShopLevel<3)
            {reason="表演舞台在店铺 Lv.3 解锁。";return false;}
            if(IsRetiredPlaceholder(item.definitionId)){reason="该占位设施已移除。";return false;}
            if((item.definitionId=="stage" && !Economy.stageOwned)||(item.definitionId=="stage_mujica"&&!Economy.mujicaStageOwned)){reason="请先在商店购买舞台。";return false;}
            if(ShopEnvironmentAssets.IsDecoration(item.definitionId)&&!Layout.facilities.Exists(f=>f.instanceId==item.instanceId)&&Economy.StoredFacilityCount(Layout,item.definitionId)<1){reason="请先购买装饰。";return false;}
            draft=ShopModel.Clone(Layout);
            state=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            state.CancelRestock();
            bool newShelf=catalog.Find(item.definitionId).kind==FacilityKind.Shelf &&
                !Layout.facilities.Exists(f=>f.instanceId==item.instanceId);
            if(newShelf)
            {
                if(!state.PlaceStoredFixture(catalog,draft,item,out reason))return false;
            }
            else if (!ShopModel.TryPlace(draft, catalog, item, out reason)) return false;
            if(!ShopModel.ValidateCheckoutAccess(draft,catalog,out reason))return false;
            if (ShopModel.IsBlocked(draft, catalog, ActorCell) || ShopModel.FindPath(draft, catalog, ShopModel.EntranceFor(draft), ActorCell).Count == 0)
            { reason = "请保留店员所在位置及其返回入口的通路。"; return false; }
            if (!state.SyncShelves(catalog, draft, out reason) || !state.SyncStaffPosts(catalog,draft,out reason)) return false;
            return state.Validate(catalog,draft,out reason);
        }

        public bool ApplyPlacement(PlacedFacility item, out string reason, string successSound="place")
        {
            if (!TryDraftPlacement(item,out var draft,out var state,out reason)) { LastMessage = reason; PlaySfx("error"); return false; }
            CancelRestockTask();
            StopAtCurrentCell();
            Layout=draft;Economy=state;
            dirty = true;
            World.Rebuild(Layout, catalog);
            ResetGuide();
            selectedId=item.instanceId;World.SelectFacility(item.instanceId);PlaySfx(successSound);
            LastMessage = "布局已更新 · 通路检查通过，尚未保存。";
            var checkout=Layout.facilities.Find(f=>f.definitionId=="checkout");
            if(checkout!=null && !ShopModel.CanUseCheckoutFront(Layout,catalog,checkout,out string paymentReason))
                LastMessage="布局已更新 · "+paymentReason;
            return true;
        }

        public void ReplaceLayout(ShopLayout layout)
        {
            if(!CanRenovate)throw new InvalidOperationException("请先关店，等待顾客离店后再替换布局。");
            if (!ShopModel.Validate(layout, catalog, out string reason)) throw new InvalidOperationException(reason);
            var state = JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            state.CancelRestock();
            if (!state.SyncShelves(catalog, layout, out reason) || !state.SyncStaffPosts(catalog,layout,out reason)) throw new InvalidOperationException(reason);
            Economy = state;
            ClearCustomers();
            Economy.ReturnAllBaskets();
            restockPhase = RestockPhase.None;
            World.SetCarrying(false);
            CancelPlacement();
            StopAtCurrentCell();
            Layout = ShopModel.Clone(layout);
            EnsureCheckoutLayout();
            Economy.SyncStaffPosts(catalog,Layout,out _);
            ActorCell = ShopModel.EntranceFor(Layout);
            World.Rebuild(Layout, catalog);
            World.CharacterRoot.position = World.CellCenter(ActorCell);
            ResetGuide();
            selectedId = null;
            World.SelectFacility(null);
        }

        void BeginPlacement(string definition, string existing = null)
        {
            if(IsRetiredPlaceholder(definition)){LastMessage="该占位设施已移除。";return;}
            EndSurfacePainting();
            if(!CanRenovate){OpenPanel("build");return;}
            if((definition=="stage"&&!Economy.stageOwned)||(definition=="stage_mujica"&&!Economy.mujicaStageOwned)){LastMessage="请先在商店购买舞台。";return;}
            renovationMode=true;
            if(catalog.Find(definition)?.kind==FacilityKind.Shelf && existing==null)
            {
                LastMessage="请从商店购买柜台，再从装修收纳中摆放。";
                return;
            }
            CancelRestockTask();
            StopAtCurrentCell();
            if(catalog.Find(definition)?.kind==FacilityKind.Stage&&existing==null)existing=Layout.facilities.Find(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Stage)?.instanceId;
            placingDefinition = definition;
            movingId = existing;
            candidate = null;
            rotation = existing == null ? 0 : Layout.facilities.Find(f=>f.instanceId==existing)?.rotation??0;
            LastMessage = "左键确认位置，R 旋转，Esc / 右键取消。";
            UpdateCandidate();
        }

        void CancelPlacement()
        {
            placingDefinition = null; movingId = null; candidate = null;lastPreviewCell=null;
            if (World != null) { World.HidePlacement(); World.SetEditing(renovationMode); }
        }

        void UpdateCandidate()
        {
            Vector2 guiMouse = new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y)/uiScale;
            if (uiRegions.Exists(r => r.Contains(guiMouse)))
            { candidate=null; candidateValid=false; World.HidePlacement(); return; }
            if (!World.PickCell(Input.mousePosition, out Vector2Int cell))
            { candidate = null; candidateValid = false; placementReason = "请将鼠标移到店内"; World.HidePlacement(); return; }
            if(placingDefinition=="warehouse" && (cell.x!=Layout.width-1 || !ShopModel.IsShopCell(Layout,cell)))
            {candidate=null;candidateValid=false;placementReason="请选择右侧墙面内侧的一格（x="+(Layout.width-1)+"），绿色表示门口和通路可用。";World.HidePlacement();return;}
            candidate = placingDefinition=="warehouse" ? new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=Layout.width,z=4,rotation=0,wallPosition=cell.y+1}
                : new PlacedFacility { instanceId = movingId ?? "preview", definitionId = placingDefinition, x = cell.x, z = cell.y, rotation = rotation };
            candidateValid = CanApply(candidate, out placementReason);
            if (candidateValid) placementReason = "可以摆放 · 服务点和入口保持连通";
            if(candidateValid && placingDefinition=="checkout")
            {
                var draft=ShopModel.Clone(Layout);
                ShopModel.TryPlace(draft,catalog,candidate,out _);
                placementReason=ShopModel.CanUseCheckoutFront(draft,catalog,candidate,out string paymentReason)?
                    "可以摆放 · 绿色为前方付款位，蓝色为后方员工位":"可以摆放 · "+paymentReason;
            }
            PreviewPlacementSound(candidate);
            World.ShowPlacement(candidate, candidateValid);
        }

        PlacedFacility Selected() => Layout.facilities.Find(p => p.instanceId == selectedId);
        void Select(string id) { if(id!=null&&id!=selectedId)PlaySfx("select"); selectedId = id; World.SelectFacility(id); }
        public void ClearFacilitySelection() { Select(null); }

        void ClickCell(Vector2Int cell)
        {
            if (!string.IsNullOrEmpty(placingDefinition))
            {
                UpdateCandidate();
                if (candidate == null || !candidateValid) { LastMessage = placementReason; return; }
                candidate.instanceId = movingId ?? Guid.NewGuid().ToString("N");
                if (ApplyPlacement(candidate, out _)) CancelPlacement();
                return;
            }
            foreach (var item in Layout.facilities)
            {
                var size = ShopModel.Size(catalog.Find(item.definitionId), item.rotation);
                if (cell.x >= item.x && cell.x < item.x+size.x && cell.y >= item.z && cell.y < item.z+size.y)
                { Select(item.instanceId);OpenSelectedInformation();return; }
            }
            Select(null);
            // Empty ground only clears selection; staff are controlled by their jobs.
        }

        void Save()
        {
            if (blockedSave) { LastMessage = "旧存档读取失败，已保留文件；本次预览不会覆盖。"; return; }
            if(SaveTrackedSession(true))PlaySfx("save");
        }

        void Load()
        {
            var loaded = ShopSession.Load(catalog, out string message);
            if (loaded != null)
            {
                CancelRestockTask(); CancelPlacement(); StopAtCurrentCell();
                ClearCustomers();
                Layout = loaded.layout; Economy = loaded.economy; Economy.MigrateManagementRules();
            ShopProfiles.MaximizeSandbox(catalog,Layout,Economy); Economy.CancelRestock(); Economy.ReturnAllBaskets();
                if(ShopSession.TestProfile)Economy.EnableTestResources();
                if(ShopSession.TestProfile && Economy.salesTestVersion<ShopSalesTestSetup.CurrentVersion){var prepared=ShopSalesTestSetup.Prepare(Layout,Economy,catalog);Layout=prepared.layout;Economy=prepared.economy;}
                RemoveRetiredPlaceholders();
            Economy.InitializeRenovationInventory(Layout);Economy.InitializeBalance(Layout);
                EnsureCheckoutLayout();
                if(!Economy.staffPointsIntroduced)
                {ShopSalesTestSetup.EnsureStaffPoints(Layout,catalog);Economy.staffPointsIntroduced=true;}
                Economy.SyncStaffPosts(catalog,Layout,out _);
                if(!Economy.InitializeFixtureStore(catalog,Layout,out string fixtureReason))throw new InvalidOperationException(fixtureReason);
                restockPhase = RestockPhase.None; ActorCell = ShopModel.EntranceFor(Layout);
                World.Rebuild(Layout, catalog); World.SetWarehouseLevel(Economy.warehouseLevel);
                World.SetCarrying(false); World.CharacterRoot.position = World.CellCenter(ActorCell);
                ResetGuide();
                Select(null); dirty = false; blockedSave = false; autoDelay = 2f;
                ResetResourceFeedback();
                InitializeBusinessState();
                // Loading during active play restores a save; it is not a new
                // offline interval and must not reward the same wall time twice.
                offlineAway=false;
                pendingOfflineUntilUtc=0;
                offlineTrackingReady=!verifyMode;
                if(!verifyMode){SaveTrackedSession(false);nextOnlineSave=Time.unscaledTime+30f;}
            }
            LastMessage = message;
        }

        void OnGUI()
        {
            if (World == null) return;
            Styles();
            float scale = Mathf.Max(.55f, Mathf.Min(Screen.width/1360f, Screen.height/850f));
            uiScale=scale;
            float width = Screen.width/scale, height = Screen.height/scale;
            var ev = Event.current;
            Vector2 mouse = ev.mousePosition;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            if(ev.type==EventType.Repaint)anyButtonHovered=false;
            GUI.enabled=true;
            tutorialDynamicTarget=new Rect();
            if(gameFailed){DrawFailure(width,height);EndButtonHover();GUI.matrix=previous;return;}
            if(ev.type==EventType.KeyDown&&ev.keyCode==KeyCode.Escape){ToggleSettingsFromEscape();ev.Use();}
            if(quitPrompt){DrawQuitPrompt(width,height);EndButtonHover();GUI.matrix=previous;return;}
            uiRegions.Clear();
            if(frontEnd){DrawFrontEnd(width,height);GUI.enabled=true;DrawQuitPrompt(width,height);EndButtonHover();GUI.matrix=previous;return;}
            if(TutorialChoicePending&&panelKey!="settings"&&panelKey!="save")
            {DrawTutorial(width,height);EndButtonHover();GUI.matrix=previous;return;}
            DrawManagementHUD(width);
            DrawFeverHUD(width);
            DrawStaffRoles();
            float barWidth=838, left=(width-barWidth)*.5f, top=height-112;
            string[] keys={"build","store","goods","warehouse","staff","growth","settings","save"};
            string[] names={"装修","商店","商品","仓库","员工","任务","设置","存档"};
            for(int i=0;i<keys.Length;i++)
            {
                Rect rect=new Rect(left+i*105,top,98,82);uiRegions.Add(rect);
                bool selected=panelKey==keys[i]||(keys[i]=="store"&&IsStorePanel(panelKey))||(keys[i]=="build"&&renovationMode);
                if(ArtButton(rect,names[i],selected?toolbarActive:toolbarButton))
                {
                    if(keys[i]=="build"&&renovationMode)ShowRenovationDrawer();
                    else OpenPanel(keys[i]);
                }
                DrawIcon(new Vector2(rect.center.x,rect.y+25),keys[i],ink);
                if(keys[i]=="growth")DrawClaimableTaskBadge(rect);
            }

            if(renovationMode&&(panelKey==""||panelKey=="build"))DrawRenovationDrawer(width,height);
            else if(!string.IsNullOrEmpty(panelKey)) DrawPopup(width,height);
            else if(IsPaintingSurface) DrawSurfaceHint(width,height);
            else if(!string.IsNullOrEmpty(placingDefinition)) DrawPlacementBar(width,height);
            else if(Selected()!=null) DrawSelectionBar(width,height);
            GUI.Label(new Rect(width*.5f-440,height-22,880,20),renovationMode?"左键摆放 / 选择    R 旋转    右键取消    左键拖动平移 / 中键转视角    滚轮缩放    Esc 设置":"左键查看    左键拖动平移    中键拖动转视角    方向键移镜    滚轮缩放    Esc 设置",small);
            GUI.enabled=true;DrawClosedReminder(width,height);DrawTutorial(width,height);DrawQuitPrompt(width,height);EndButtonHover();
            GUI.matrix=previous;
            bool overPanel = uiRegions.Exists(r=>r.Contains(mouse/scale));
            if(!quitPrompt)HandleInput(ev,mouse,overPanel);
        }

        public void OpenPanel(string key)
        {
            if(key=="goods")productCategory=-1;
            if(IsStorePanel(key))renovationMode=false;
            EndSurfacePainting();
            if(key=="build" && !CanRenovate)
            {
                CancelPlacement();panelKey="renovation_gate";scroll=Vector2.zero;
                LastMessage="装修前需停止迎客，并等顾客结账离开。";
                return;
            }
            if(key=="build"){ShowRenovationDrawer();return;}
            panelKey=panelKey==key?"":key;
            scroll=Vector2.zero;
            CancelPlacement();
            World.SetEditing(renovationMode);
        }

        void PanelBox(Rect rect)
        { uiRegions.Add(rect); NineSlice(rect,panel.normal.background,panel.border); }

        void DrawPopup(float width,float height)
        {
            if(IsCompactPanel){DrawCompactPanel();return;}
            float popupHeight=IsStorePanel(panelKey)||panelKey=="build"?560:panelKey=="bonuses"?570:panelKey=="decorations"?500:panelKey=="roster"||panelKey=="profile"||panelKey=="performance"?580:panelKey=="post"?520:panelKey=="staff"?550:panelKey=="build"?510:panelKey=="poststore"?490:panelKey=="store"?560:panelKey=="materials"?500:panelKey=="growth"?570:panelKey=="expansion"?570:panelKey=="offline"?460:panelKey=="renovation_gate"?285:panelKey=="settings"?570:panelKey=="save"?350:panelKey=="help"?330:panelKey=="warehouse"?490:panelKey=="goods"?480:panelKey=="orders"?480:panelKey=="shelf"?480:panelKey=="trade"?480:390;
            Rect box=new Rect(width*.5f-308,height-132-popupHeight,616,popupHeight);
            PanelBox(box);
            string name=panelKey=="decorations"?"商店":panelKey=="bonuses"?"店铺升级":panelKey=="roster"?"角色档案":panelKey=="profile"?"角色属性":panelKey=="performance"?"舞台与 FEVER":panelKey=="build"?"装修库存":panelKey=="poststore"?"商店":panelKey=="store"?"商店":panelKey=="materials"?"商店":panelKey=="growth"?"成长任务":panelKey=="expansion"?"店铺面积扩建":panelKey=="offline"?"离线营业结算":panelKey=="renovation_gate"?"装修前准备":panelKey=="settings"?"设置":panelKey=="goods"?"商品":panelKey=="warehouse"?"仓库管理":panelKey=="orders"?"配送订单":panelKey=="trade"?"营业与客流":panelKey=="shelf"?"货架商品":panelKey=="staff"?"员工岗位":panelKey=="post"?"岗位设置":panelKey=="save"?"店铺存档":"操作帮助";
            GUI.Label(new Rect(box.x+22,box.y+18,400,30),name,heading);
            if(ArtButton(new Rect(box.xMax-61,box.y+14,43,31),"×",button)) { panelKey=""; World.SetEditing(renovationMode); }
            if(panelKey=="bonuses")DrawShopBonuses(box);
            else if(panelKey=="roster")DrawRoster(box);
            else if(panelKey=="profile")DrawStaffProfile(box);
            else if(panelKey=="performance")DrawPerformance(box);
            else if(panelKey=="build")DrawRenovation(box);
            else if(panelKey=="renovation_gate")DrawRenovationGate(box);
            else if(panelKey=="store")DrawCounterStore(box);
            else if(panelKey=="materials")DrawSurfaceStore(box);
            else if(panelKey=="decorations")DrawDecorationStore(box);
            else if(panelKey=="poststore")DrawPostStore(box);
            else if(panelKey=="growth")DrawGrowth(box);
            else if(panelKey=="expansion")DrawExpansion(box);
            else if(panelKey=="offline")DrawOfflineSummary(box);
            else if(panelKey=="settings")DrawSettingsPopup(box);
            else if(panelKey=="goods") DrawProducts(box);
            else if(panelKey=="orders") DrawDeliveryOrders(box);
            else if(panelKey=="warehouse") DrawWarehouse(box);
            else if(panelKey=="trade") DrawTrading(box);
            else if(panelKey=="shelf") DrawShelf(box);
            else if(panelKey=="staff") DrawWorkingStaff(box);
            else if(panelKey=="post") DrawStaffPost(box);
            else if(panelKey=="save")
            {
                GUI.Label(new Rect(box.x+22,box.y+60,570,83),blockedSave?loadProblem:"保存当前店铺进度，或读取最近一次存档。",body);
                GUI.enabled=!blockedSave;
                if(ArtButton(new Rect(box.x+22,box.y+154,278,40),"保存店铺",activeButton)) Save();
                GUI.enabled=ShopSession.HasSave;
                if(ArtButton(new Rect(box.x+314,box.y+154,278,40),"读取店铺",button)) Load();
                GUI.enabled=true;
                if(ArtButton(new Rect(box.x+22,box.y+211,570,36),"查看最近离线收益 · 最多 24 小时",button))OpenOfflineSummary();
                if(ArtButton(new Rect(box.x+22,box.y+256,570,36),"保存并返回主界面",button))ReturnToTitle();
            }
            else if(panelKey=="help")
            {
                GUI.Label(new Rect(box.x+22,box.y+62,570,184),"商店购买柜台，装修中从收纳摆放；上限由店铺等级及类别决定。\n先关店，等顾客结清离店后才能装修；装修不暂停时间。\n基础商品待发货 1 秒、运输 1 秒；发货前可撤单。\n正常速度经营，点击左上角升级店铺；Esc 打开设置。\n设置中调整画面、阴影及背景音乐；默认单曲循环。\n离线每小时售出 5% 可售现货，最多 24 小时；存档页查看结算。",body);
            }
        }

        void DrawPlacementBar(float width,float height)
        {
            Rect box=new Rect(width*.5f-308,height-222,616,92);
            PanelBox(box);
            GUI.Label(new Rect(box.x+18,box.y+12,290,28),placingDefinition=="warehouse"?"仓库门 · 右侧墙面":catalog.Find(placingDefinition).title+"  ·  "+rotation*90+"°",heading);
            GUI.Label(new Rect(box.x+18,box.y+51,575,28),placementReason,small);
            GUI.enabled=placingDefinition!="warehouse";
            if(ArtButton(new Rect(box.x+337,box.y+12,125,33),"旋转 R",button)) { rotation=(rotation+1)%4; PlaySfx("rotate");UpdateCandidate(); }
            GUI.enabled=true;
            if(ArtButton(new Rect(box.x+473,box.y+12,125,33),"取消",button)) CancelPlacement();
        }

        void DrawSelectionBar(float width,float height)
        {
            var item=Selected();var d=catalog.Find(item.definitionId);
            Rect box=new Rect(width*.5f-308,height-230,616,100);
            PanelBox(box);
            var shelfStock = Economy.Shelf(item.instanceId);
            string selectionTitle = d.title + (shelfStock != null ? "  ·  " + shelfStock.quantity + " / " + Economy.ShelfCapacity(catalog,Layout,item.instanceId) : "");
            if(d.kind==FacilityKind.Checkout)selectionTitle+=ShopModel.CanUseCheckoutFront(Layout,catalog,item,out _)?" · 正前方付款":" · 前方受阻，需调整";
            GUI.Label(new Rect(box.x+18,box.y+12,390,27),selectionTitle,heading);
            GUI.Label(new Rect(box.x+390,box.y+17,208,22),d.kind==FacilityKind.Warehouse?"右墙门位 / 门前取货":d.kind==FacilityKind.Checkout?"绿：付款 / 蓝：收银":d.width+" × "+d.depth+"  /  "+item.rotation*90+"°",rightText);
            float y=box.y+51;
            if(!renovationMode)
            {
                if(ArtButton(new Rect(box.x+18,y,576,34),"查看状态",button))OpenSelectedInformation();
                return;
            }
            GUI.enabled=true;
            if(ArtButton(new Rect(box.x+18,y,135,34),d.kind==FacilityKind.Warehouse?"移动仓库门":"移动",button)) { BeginPlacement(d.id,item.instanceId); World.SetEditing(true); }
            GUI.enabled=d.kind!=FacilityKind.Warehouse;
            if(ArtButton(new Rect(box.x+165,y,135,34),"旋转",button))
            {
                ApplyPlacement(new PlacedFacility{instanceId=item.instanceId,definitionId=item.definitionId,x=item.x,z=item.z,rotation=(item.rotation+1)%4},out _,"rotate");
            }
            GUI.enabled=d.kind==FacilityKind.Stage||d.kind==FacilityKind.Shelf||d.kind==FacilityKind.Warehouse||ShopEconomy.IsStaffFacility(d.kind);
            if(ArtButton(new Rect(box.x+312,y,135,34),d.kind==FacilityKind.Stage?"舞台 / FEVER":d.kind==FacilityKind.Warehouse?"管理仓库":d.kind==FacilityKind.Shelf?"商品 / 补货":"员工 / 升级",button))
            { if(d.kind==FacilityKind.Stage)OpenPerformance();else if(d.kind==FacilityKind.Warehouse) OpenPanel("warehouse"); else if(d.kind==FacilityKind.Shelf) OpenPanel("shelf"); else OpenStaffPost(item.instanceId); }
            GUI.enabled=d.kind!=FacilityKind.Warehouse&&d.kind!=FacilityKind.Checkout;
            if(ArtButton(new Rect(box.x+459,y,135,34),d.kind==FacilityKind.Shelf?"收纳并退货":"移除",button))
            {
                RemoveFacility(item.instanceId);
            }
            GUI.enabled=true;
        }

        void DrawContentPopup(Rect box,bool products)
        {
            GUI.Label(new Rect(box.x+22,box.y+57,570,24),"当前由一名占位店员补货 · 多人岗位随后接入",small);
            var currentProducts=ShopProductSet.CurrentProducts(catalog);
            int count=products?currentProducts.Count:catalog.staff.Count;
            scroll=GUI.BeginScrollView(new Rect(box.x+20,box.y+95,576,273),scroll,new Rect(0,0,553,count*61));
            for(int i=0;i<count;i++)
            {
                float y=i*61;
                if(products)
                {
                    var p=currentProducts[i];
                    DrawProductIcon(new Rect(8,y+5,42,42),p.id);
                    GUI.Label(new Rect(58,y+7,200,26),p.title,body);
                    string category=p.category=="top"?"上装":p.category=="shoes"?"鞋履":p.category=="head"?"头饰":p.category=="bottom"?"下装":p.category;
                    GUI.Label(new Rect(8,y+33,250,21),category,small);
                    GUI.Label(new Rect(276,y+15,268,26),"进价 "+p.buyPrice+"  /  售价 "+p.sellPrice+"  /  每批 "+p.batchSize,small);
                }
                else
                {
                    var s=catalog.staff[i];
                    GUI.Label(new Rect(8,y+7,250,26),s.title,body);
                    GUI.Label(new Rect(8,y+33,250,21),"可在岗位设置中安排工作",small);
                    GUI.Label(new Rect(340,y+15,204,26),"计划岗位："+s.role,small);
                }
                GUI.DrawTexture(new Rect(8,y+59,536,1),line);
            }
            GUI.EndScrollView();
        }

        void HandleInput(Event ev, Vector2 mouse, bool overPanel)
        {
            if(ev.type==EventType.MouseUp && (ev.button==0||ev.button==2))
            {
                bool clicked=panGesture&&!orbitDragged&&ev.button==0&&!overPanel;
                bool consumed=panGesture;panGesture=false;
                if(clicked){HandleSceneClick(mouse);ev.Use();return;}
                if(consumed){ev.Use();return;}
            }
            if(ev.type==EventType.MouseDown && (ev.button==0||ev.button==2))
            {panGesture=!overPanel&&panelKey!="settings";orbitDragged=false;orbitDistance=Vector2.zero;orbitHeldFrame=Time.frameCount;}
            if(ev.type==EventType.KeyDown)
            {
                if(ev.keyCode==KeyCode.Escape) { ToggleSettingsFromEscape(); ev.Use(); }
                else if(ev.keyCode==KeyCode.R && !string.IsNullOrEmpty(placingDefinition) && placingDefinition!="warehouse") { rotation=(rotation+1)%4; PlaySfx("rotate");UpdateCandidate(); ev.Use(); }
                
                else if(ev.keyCode==KeyCode.S && ev.control) { Save(); ev.Use(); }
            }
            if(ev.type==EventType.MouseDrag && panGesture && (ev.button==2 || ev.button==0))
            {
                orbitDistance+=ev.delta;
                if(orbitDragged||orbitDistance.sqrMagnitude>=16)
                {
                    var delta=orbitDragged?ev.delta:orbitDistance;orbitDragged=true;
                    if(ev.button==0)World.Pan(delta);
                    else {World.Orbit(delta);UserSettings.cameraPitch=World.CameraPitch;SettingsChanged();}
                }
                ev.Use();return;
            }
            if(overPanel||panelKey=="settings") return;
            if(ev.type==EventType.ScrollWheel) { World.Zoom(ev.delta.y); ev.Use(); }
            else if(ev.type==EventType.MouseDown && ev.button==1)
            {
                if(IsPaintingSurface)EndSurfacePainting();
                else if(!string.IsNullOrEmpty(placingDefinition))CancelPlacement();
                ev.Use();
            }
        }
        void HandleSceneClick(Vector2 mouse)
        {
            if(!string.IsNullOrEmpty(panelKey)&&panelKey!="build") { panelKey=""; World.SetEditing(renovationMode); return; }
            Vector2 screen=new Vector2(mouse.x,Screen.height-mouse.y);
            if(IsPaintingSurface){PaintSurfaceAt(screen);return;}
            if(TryCelebrateCharacter(screen))return;
            if(string.IsNullOrEmpty(placingDefinition) && World.PickFacility(screen,out string hitId))
            { Select(hitId);OpenSelectedInformation(); }
            else if(World.PickCell(screen,out Vector2Int cell)) ClickCell(cell);
        }

        bool orbitDragged;
        int orbitHeldFrame;
        Vector2 orbitDistance;

        GUIStyle Text(int size,Color color,FontStyle weight=FontStyle.Normal)
        {
            var s=new GUIStyle(GUI.skin.label){font=font,fontSize=size,fontStyle=weight,padding=new RectOffset(0,0,0,0),wordWrap=true};
            s.normal.textColor=color; return s;
        }
        void DrawIcon(Vector2 c,string key,Color color)
        {
            string artKey=key=="growth"?"tasks":key=="build"?"renovation":key=="trade"?"business":key=="camera"?"home":key;
            var icon=Ui("icons/"+artKey);
            if(icon!=null){GUI.DrawTexture(new Rect(c.x-22,c.y-22,44,44),icon,ScaleMode.ScaleToFit);return;}
            Color old=GUI.color;GUI.color=color;
            Action<Vector2,Vector2> stroke=(a,b)=>
            {
                Matrix4x4 m=GUI.matrix;
                Vector2 v=b-a;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(v.y,v.x)*Mathf.Rad2Deg,c+a);
                GUI.DrawTexture(new Rect(c.x+a.x,c.y+a.y,v.magnitude,2),Texture2D.whiteTexture);
                GUI.matrix=m;
            };
            Action<float,float,float,float> rect=(x,y,w,h)=>{stroke(new Vector2(x,y),new Vector2(x+w,y));stroke(new Vector2(x+w,y),new Vector2(x+w,y+h));stroke(new Vector2(x+w,y+h),new Vector2(x,y+h));stroke(new Vector2(x,y+h),new Vector2(x,y));};
            if(key=="build") { rect(-13,-11,26,24);stroke(new Vector2(-13,-2),new Vector2(13,-2));stroke(new Vector2(-13,6),new Vector2(13,6)); }
            else if(key=="goods") { stroke(new Vector2(-15,-10),new Vector2(-6,-15));stroke(new Vector2(-6,-15),new Vector2(0,-10));stroke(new Vector2(0,-10),new Vector2(6,-15));stroke(new Vector2(6,-15),new Vector2(15,-10));stroke(new Vector2(15,-10),new Vector2(12,-3));stroke(new Vector2(12,-3),new Vector2(8,-5));stroke(new Vector2(8,-5),new Vector2(8,14));stroke(new Vector2(8,14),new Vector2(-8,14));stroke(new Vector2(-8,14),new Vector2(-8,-5));stroke(new Vector2(-8,-5),new Vector2(-12,-3));stroke(new Vector2(-12,-3),new Vector2(-15,-10)); }
            else if(key=="staff") { rect(-5,-13,10,10);stroke(new Vector2(-12,13),new Vector2(-10,2));stroke(new Vector2(-10,2),new Vector2(10,2));stroke(new Vector2(10,2),new Vector2(12,13));stroke(new Vector2(-12,13),new Vector2(12,13)); }
            else if(key=="warehouse") { stroke(new Vector2(-16,-5),new Vector2(0,-15));stroke(new Vector2(0,-15),new Vector2(16,-5));rect(-13,-5,26,20);rect(-5,3,10,12); }
            else if(key=="store") { rect(-14,-4,28,20);rect(-17,-12,34,8);rect(-6,3,12,13); }
            else if(key=="settings") { rect(-14,-12,28,24);stroke(new Vector2(-10,-4),new Vector2(10,-4));stroke(new Vector2(-10,5),new Vector2(10,5));rect(-6,-7,4,6);rect(3,2,4,6); }
            else if(key=="trade") { rect(-13,-10,26,22);stroke(new Vector2(-18,-10),new Vector2(18,-10));rect(-4,2,8,10); }
            else if(key=="save") { rect(-13,-13,26,26);rect(-7,-12,14,8);rect(-7,3,14,10); }
            else if(key=="pause") { if(IsPaused){stroke(new Vector2(-7,-12),new Vector2(12,0));stroke(new Vector2(12,0),new Vector2(-7,12));stroke(new Vector2(-7,12),new Vector2(-7,-12));}else{rect(-10,-12,5,25);rect(5,-12,5,25);} }
            else if(key=="camera") { rect(-15,-9,30,22);rect(-7,-14,13,5);rect(-5,-4,10,12); }
            else { rect(-12,-13,24,26);stroke(new Vector2(-6,-6),new Vector2(5,-6));stroke(new Vector2(5,-6),new Vector2(5,-1));stroke(new Vector2(5,-1),new Vector2(0,2));stroke(new Vector2(0,2),new Vector2(0,6));rect(-1,10,2,2); }
            GUI.color=old;
        }
        Texture2D Rounded(Color color)
        {
            const int size=32; const float radius=10;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float dx=Mathf.Max(radius-x-.5f,x+.5f-size+radius,0),dy=Mathf.Max(radius-y-.5f,y+.5f-size+radius,0);
                var c=color; c.a*=Mathf.Clamp01(radius+.5f-Mathf.Sqrt(dx*dx+dy*dy)); pixels[y*size+x]=c;
            }
            texture.SetPixels(pixels); texture.Apply(); textures.Add(texture); return texture;
        }
        void Styles()
        {
            if(panel!=null)return;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft YaHei UI","SimHei"},18);
            if(font==null)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            title=Text(26,ink,FontStyle.Bold); heading=Text(19,ink,FontStyle.Bold); body=Text(14,ink); small=Text(12,muted);
            rightText=Text(14,muted);rightText.alignment=TextAnchor.MiddleRight;
            panel=new GUIStyle(GUI.skin.box){border=new RectOffset(10,10,10,10)};
            panel.normal.background=Rounded(new Color(.96f,.965f,.95f,.96f));
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=13,border=new RectOffset(10,10,10,10),padding=new RectOffset(3,3,3,3)};
            button.normal.background=Rounded(new Color(.89f,.91f,.89f));
            button.hover.background=Rounded(new Color(.79f,.84f,.82f));
            button.active.background=Rounded(new Color(.73f,.79f,.77f));
            button.focused.background=button.normal.background;
            button.normal.textColor=ink;button.hover.textColor=ink;button.active.textColor=ink;button.focused.textColor=ink;
            activeButton=new GUIStyle(button);activeButton.normal.background=Rounded(new Color(.27f,.39f,.37f));activeButton.normal.textColor=Color.white;
            activeButton.hover.background=activeButton.normal.background;activeButton.hover.textColor=Color.white;
            toolbarButton=new GUIStyle(button){fontSize=14,alignment=TextAnchor.LowerCenter,padding=new RectOffset(3,3,3,10)};
            toolbarActive=new GUIStyle(activeButton){fontSize=14,alignment=TextAnchor.LowerCenter,padding=new RectOffset(3,3,3,10)};
            chip=Text(12,new Color(.55f,.36f,.42f));chip.alignment=TextAnchor.MiddleCenter;chip.border=new RectOffset(10,10,10,10);chip.normal.background=Rounded(new Color(.95f,.87f,.87f));
            line=new Texture2D(1,1);line.SetPixel(0,0,new Color(.84f,.86f,.82f));line.Apply();textures.Add(line);
            ApplyFabricStyles();
        }
        void OnDestroy()
        { Application.wantsToQuit-=HandleQuitRequest;DisposeSoundEffects();DisposeSettings();foreach(var tex in textures)if(tex!=null)Destroy(tex);if(font!=null)Destroy(font); }
        void OnApplicationQuit()
        { if(!frontEnd && !loadingScene && !Application.isEditor && !verifyMode && !blockedSave && Economy!=null && Layout!=null) Save(); }
    }
}
