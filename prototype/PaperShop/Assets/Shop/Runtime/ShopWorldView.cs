using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnonTokyo.Shop
{
    /// <summary>
    /// Map presentation only. Footprints, model slots, and selection/placement
    /// overlays are separate objects; replacing a prefab never changes map rules.
    /// </summary>
    public sealed partial class ShopWorldView : MonoBehaviour
    {
        public bool PresentationLabelsEnabled=true;
        public Camera WorldCamera { get; private set; }
        public PaperCharacterView Character { get; private set; }
        public Transform CharacterRoot { get; private set; }
        public bool IsEditing { get { return editing || (placementRoot != null && placementRoot.gameObject.activeSelf); } }
        public int WarehouseLevel { get { return warehouseLevel; } }
        public bool IsCarrying { get { return carrying; } }

        private const float GroundY = 0.02f;
        private const float WallHeight = 2.72f;
        private const float WallThickness = 0.12f;
        private Quaternion viewRotation = Quaternion.Euler(35.264f, 45f, 0f);
        private readonly List<Material> materials = new List<Material>();
        private readonly Dictionary<string, Material> surfaceMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, FacilityVisual> facilityVisuals = new Dictionary<string, FacilityVisual>();
        private readonly List<WorldLabel> labels = new List<WorldLabel>();
        private readonly List<Renderer> backWallSections = new List<Renderer>();
        private ShopLayout layout;
        private ShopCatalog catalog;
        private Shader shader;
        private Transform mapRoot;
        private Transform gridRoot;
        private Transform overlayRoot;
        private Transform selectionRoot;
        private Transform placementRoot;
        private Transform placementPad;
        private Transform placementService;
        private Transform placementStaff;
        private readonly List<Transform> placementEdges = new List<Transform>();
        private Vector3 focus;
        private Vector3 resetFocus;
        private float size = 8.1f;
        private float fittedSize = 8.1f;
        private bool editing;
        private bool carrying;
        private int warehouseLevel = 1;
        private string selectedId;
        private Material selectionMaterial;
        private Material validMaterial;
        private Material invalidMaterial;
        private Material validBorder;
        private Material invalidBorder;
        private Font labelFont;
        private bool ownsFont;
        private Texture2D labelBackground;
        private Texture2D facilityBubble;
        private Texture2D selectedBackground;
        private GUIStyle areaStyle;
        private GUIStyle facilityStyle;
        private GUIStyle selectedStyle;
        private Light daylight;
        public Light Daylight => daylight;

        private sealed class FacilityVisual
        {
            public Vector3 center;
            public Vector2Int size;
            public Renderer[] renderers;
            public Transform helperGeometry;
            public Transform paymentMarker, staffMarker;
        }

        private sealed class WorldLabel
        {
            public string text;
            public Vector3 position;
            public string instanceId;
            public bool alwaysVisible;
        }

        public bool FacilityScreenBounds(string id,out Rect rect)
        {
            rect=new Rect();
            if(WorldCamera==null||!facilityVisuals.TryGetValue(id,out var visual))return false;
            var bounds=new Bounds(visual.center,new Vector3(visual.size.x,.15f,visual.size.y));
            if(visual.renderers!=null&&visual.renderers.Length>0)
            {
                bounds=visual.renderers[0].bounds;
                foreach(var renderer in visual.renderers)if(renderer!=null)bounds.Encapsulate(renderer.bounds);
            }
            Vector2 min=new Vector2(float.MaxValue,float.MaxValue),max=new Vector2(float.MinValue,float.MinValue);
            for(int i=0;i<8;i++)
            {
                var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                var p=WorldCamera.WorldToScreenPoint(corner);if(p.z<=0)return false;
                var point=new Vector2(p.x,Screen.height-p.y);min=Vector2.Min(min,point);max=Vector2.Max(max,point);
            }
            rect=Rect.MinMaxRect(min.x,min.y,max.x,max.y);return true;
        }

        public void Initialize(ShopLayout initialLayout, ShopCatalog initialCatalog, Shader litShader)
        {
            if (initialLayout == null || initialCatalog == null || litShader == null)
            {
                Debug.LogError("ShopWorldView.Initialize requires layout, catalog, and an injected URP Lit shader.", this);
                return;
            }
            shader = litShader;
            layout = initialLayout;
            catalog = initialCatalog;
            if (WorldCamera == null)
            {
                BuildCameraAndLight();
                overlayRoot = Group("Map overlays - independent of model slots", transform);
                selectionRoot = Group("Selected footprint", overlayRoot);
                selectionRoot.gameObject.SetActive(false);
                BuildPlacementOverlay();
                CharacterRoot = Group("Staff logical root", transform);
                Character = CharacterRoot.gameObject.AddComponent<PaperCharacterView>();
                Character.Initialize(WorldCamera, shader);
                CharacterRoot.position = CellCenter(new Vector2Int(Mathf.Min(6, layout.width - 1), Mathf.Min(1, layout.depth - 1)));
            }
            Rebuild(initialLayout, initialCatalog);
            ResetCamera();
        }

        public void Rebuild(ShopLayout nextLayout, ShopCatalog nextCatalog)
        {
            if (nextLayout == null || nextCatalog == null || shader == null) return;
            bool dimensionsChanged = layout == null || layout.width != nextLayout.width || layout.depth != nextLayout.depth;
            layout = nextLayout;
            catalog = nextCatalog;
            if (mapRoot != null)
            {
                mapRoot.gameObject.SetActive(false);
                Destroy(mapRoot.gameObject);
            }
            ClearFinishMeshes();
            mapRoot = Group("Shop map - presentation", transform);
            facilityVisuals.Clear();stockSlots.Clear();
            labels.Clear();
            backWallSections.Clear();
            BuildRoom();
            if (layout.facilities != null)
            {
                foreach (PlacedFacility placement in layout.facilities)
                {
                    if (placement == null) continue;
                    FacilityDefinition definition = catalog.Find(placement.definitionId);
                    if (definition == null) continue;
                    BuildFacility(placement, definition);
                }
            }
            HidePlacement();
            SelectFacility(selectedId);
            RefreshEditingVisibility();
            if (dimensionsChanged) ResetCamera();
        }

        /// <summary>Presentation only: capacity and upgrade costs remain owned by the app.
        /// Storage upgrades retain the single flush wall doorway.</summary>
        public void SetWarehouseLevel(int level)
        {
            int next = Mathf.Clamp(level, 1, 3);
            if (warehouseLevel == next) return;
            warehouseLevel = next;
            if (layout == null || catalog == null || shader == null) return;
            Rebuild(layout, catalog);
        }

        public PaperCharacterView CreatePaperActor(string actorName, Color tint)
        {
            var root=Group(actorName,transform);
            var actor=root.gameObject.AddComponent<PaperCharacterView>();
            actor.Initialize(WorldCamera,shader); actor.SetTint(tint);
            return actor;
        }

        // State selects the optional restock card. No hand-held prop is generated.
        public void SetCarrying(bool value)
        { carrying=value; if(Character!=null)Character.SetRestocking(value); }

        public Vector3 CellCenter(Vector2Int cell)
        {
            int width = layout != null ? layout.width : 8;
            int depth = layout != null ? layout.depth : 8;
            return new Vector3(cell.x - (width - 1) * 0.5f, GroundY, cell.y - (depth - 1) * 0.5f);
        }

        public bool PickCell(Vector2 screen, out Vector2Int cell)
        {
            cell = Vector2Int.zero;
            if (WorldCamera == null || layout == null) return false;
            Ray ray = WorldCamera.ScreenPointToRay(screen);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, GroundY, 0f));
            float distance;
            if (!plane.Raycast(ray, out distance)) return false;
            Vector3 position = ray.GetPoint(distance);
            int x = Mathf.FloorToInt(position.x + layout.width * 0.5f);
            int z = Mathf.FloorToInt(position.z + layout.depth * 0.5f);
            cell = new Vector2Int(x, z);
            return ShopModel.IsInside(layout, cell);
        }

        /// <summary>Select visible geometry before falling back to the ground grid.
        /// Imported models need no physics colliders to remain selectable.</summary>
        public bool PickFacility(Vector2 screen, out string instanceId)
        {
            instanceId = null;
            if (WorldCamera == null) return false;
            Ray ray = WorldCamera.ScreenPointToRay(screen);
            float closest = float.PositiveInfinity;
            foreach (var item in facilityVisuals)
            {
                foreach (var renderer in item.Value.renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (renderer.bounds.IntersectRay(ray, out float distance) && distance < closest)
                    { closest = distance; instanceId = item.Key; }
                }
            }
            return instanceId != null;
        }

        /// <summary>Screen-pixel drag delta, X right and Y down (IMGUI convention).</summary>
        public void Pan(Vector2 delta)
        {
            if(delta.sqrMagnitude<.000001f)return;
            guidedFocusRemaining=0;
            if (WorldCamera == null) return;
            Vector3 right = viewRotation * Vector3.right;
            Vector3 upOnFloor = Quaternion.Euler(0,viewRotation.eulerAngles.y,0)*Vector3.forward;
            float unitsPerPixel = size * 2f / Mathf.Max(1, Screen.height);
            focus += (-right * delta.x + upOnFloor * delta.y) * unitsPerPixel;
            Vector3 offset = focus - resetFocus;
            offset.y = 0f;
            focus = resetFocus + Vector3.ClampMagnitude(offset, 8f);
            ApplyCamera();
        }

        /// <summary>Positive wheel amount zooms out. Does not rotate the camera.</summary>
        public void Zoom(float amount)
        {
            size = Mathf.Clamp(size * Mathf.Pow(1.075f, amount), 0.85f, Mathf.Max(12.6f, fittedSize * 1.9f));
            ApplyCamera();
        }

        public float CameraPitch=>Mathf.DeltaAngle(0,viewRotation.eulerAngles.x);
        public void Orbit(Vector2 delta)
        {
            guidedFocusRemaining=0;
            float pitch=Mathf.Clamp(CameraPitch+delta.y*.22f,-10f,85f);
            viewRotation=Quaternion.Euler(pitch,viewRotation.eulerAngles.y+delta.x*.22f,0);
            ApplyCamera();
        }

        public static Vector2 EdgePanDelta(Vector2 cursor,Vector2 screen,float dt)
        {
            if(cursor.x<0||cursor.y<0||cursor.x>=screen.x||cursor.y>=screen.y)return Vector2.zero;
            float edge=Mathf.Min(36f,Mathf.Min(screen.x,screen.y)*.04f);
            float x=Mathf.Clamp01((edge-cursor.x)/edge)-Mathf.Clamp01((cursor.x-screen.x+edge)/edge);
            float y=Mathf.Clamp01((cursor.y-screen.y+edge)/edge)-Mathf.Clamp01((edge-cursor.y)/edge);
            return Vector2.ClampMagnitude(new Vector2(x,y),1)*Mathf.Min(dt,.05f)*180f;
        }

        public void ResetCamera()
        {
            float h = Mathf.Max(1f, Screen.height);
            float w = Mathf.Max(1f, Screen.width);
            float uiScale = Mathf.Max(0.55f, Mathf.Min(Screen.width / 1360f, Screen.height / 850f));
            float topMargin = 90f * uiScale;
            float bottomMargin = 100f * uiScale;
            float usableHeight = Mathf.Max(h * 0.45f, h - topMargin - bottomMargin);
            float usableWidth = Mathf.Max(w * 0.65f, w - 48f * uiScale);
            float mapWidth = layout != null ? layout.width : 8;
            float mapDepth = layout != null ? layout.depth : 8;
            Vector3 right = viewRotation * Vector3.right;
            Vector3 up = viewRotation * Vector3.up;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            // Fit the current room and the road together after either expansion.
            Bounds room = new Bounds(new Vector3(0f, (WallHeight - 0.30f) * 0.5f, 0f),
                new Vector3(mapWidth + WallThickness * 2f, WallHeight + 0.30f, mapDepth + WallThickness * 2f));
            IncludeProjectedBounds(room, right, up, ref minX, ref maxX, ref minY, ref maxY);
            Vector3 outsideMin = CellCenter(new Vector2Int(-1, layout != null ? layout.depth : 8)) + new Vector3(-0.5f, -GroundY - 0.25f, -0.5f);
            Vector3 outsideMax = CellCenter(new Vector2Int(layout != null ? layout.width : 8, (layout != null ? layout.depth : 8)+3)) + new Vector3(0.5f, 0.15f, 0.5f);
            Bounds courtyard = new Bounds((outsideMin + outsideMax) * 0.5f, outsideMax - outsideMin);
            IncludeProjectedBounds(courtyard, right, up, ref minX, ref maxX, ref minY, ref maxY);
            if (layout != null && layout.facilities != null && catalog != null)
                foreach (PlacedFacility placement in layout.facilities)
                {
                    FacilityDefinition definition = placement == null ? null : catalog.Find(placement.definitionId);
                    FacilityVisual annex;
                    if (definition == null || definition.kind != FacilityKind.Warehouse || !facilityVisuals.TryGetValue(placement.instanceId, out annex)) continue;
                    foreach (Renderer renderer in annex.renderers)
                        if (renderer != null && renderer.enabled)
                            IncludeProjectedBounds(renderer.bounds, right, up, ref minX, ref maxX, ref minY, ref maxY);
                }
            float verticalFit = (maxY - minY) * 0.5f * h / usableHeight;
            float horizontalFit = (maxX - minX) * 0.5f * h / usableWidth;
            fittedSize = Mathf.Max(verticalFit, horizontalFit) * 1.055f;
            size = fittedSize;
            resetFocus = right * ((minX + maxX) * 0.5f) + up * ((minY + maxY) * 0.5f)
                + up * ((topMargin - bottomMargin) * size / h);
            focus = resetFocus;
            ApplyCamera();
        }

        private static void IncludeProjectedBounds(Bounds bounds, Vector3 right, Vector3 up,
            ref float minX, ref float maxX, ref float minY, ref float maxY)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                float px = Vector3.Dot(corner, right), py = Vector3.Dot(corner, up);
                minX = Mathf.Min(minX, px); maxX = Mathf.Max(maxX, px);
                minY = Mathf.Min(minY, py); maxY = Mathf.Max(maxY, py);
            }
        }

        /// <summary>Shows the grid and footprint/service helpers only while editing.
        /// An active placement preview also enables them until HidePlacement.</summary>
        public void SetEditing(bool value)
        {
            editing = value;
            RefreshEditingVisibility();
        }

        private void RefreshEditingVisibility()
        {
            if (gridRoot != null) gridRoot.gameObject.SetActive(IsEditing);
            foreach (var pair in facilityVisuals)
                if (pair.Value.helperGeometry != null)
                    pair.Value.helperGeometry.gameObject.SetActive(IsEditing || pair.Key == selectedId);
        }

        public void ShowPlacement(PlacedFacility candidate, bool valid)
        {
            if (candidate == null || catalog == null || placementRoot == null) { HidePlacement(); return; }
            FacilityDefinition definition = catalog.Find(candidate.definitionId);
            if (definition == null) { HidePlacement(); return; }
            Vector2Int footprint = ShopModel.Size(definition, candidate.rotation);
            Vector3 center = FootprintCenter(candidate, footprint);
            if(definition.kind==FacilityKind.Warehouse){footprint=Vector2Int.one;center=CellCenter(ShopModel.ServiceCell(candidate,definition));}
            placementRoot.gameObject.SetActive(true);
            placementRoot.position = new Vector3(center.x, 0.036f, center.z);
            placementPad.localScale = new Vector3(footprint.x - 0.05f, 0.009f, footprint.y - 0.05f);
            placementPad.GetComponent<Renderer>().sharedMaterial = valid ? validMaterial : invalidMaterial;
            SetEdges(placementEdges, footprint, 0.04f, 0.012f);
            foreach (Transform edge in placementEdges)
                edge.GetComponent<Renderer>().sharedMaterial = valid ? validBorder : invalidBorder;
            bool checkout=definition.kind==FacilityKind.Checkout;
            Vector3 servicePosition = CellCenter(checkout?ShopModel.PaymentCell(layout,catalog,candidate):ShopModel.ServiceCell(candidate, definition));
            if(definition.kind==FacilityKind.Stage)servicePosition=center+FacingDirection(candidate,definition)*(definition.width*.5f+.4f);
            placementService.position = new Vector3(servicePosition.x, 0.075f, servicePosition.z);
            placementService.rotation=Quaternion.LookRotation(FacingDirection(candidate,definition));
            placementService.gameObject.SetActive(definition.kind!=FacilityKind.Decor && definition.kind!=FacilityKind.Warehouse);
            bool serviceValid=valid;
            if(checkout)
            {
                var draft=ShopModel.Clone(layout);
                draft.facilities.RemoveAll(f=>f.instanceId==candidate.instanceId);
                draft.facilities.Add(candidate);
                serviceValid=valid && ShopModel.CanUseCheckoutFront(draft,catalog,candidate,out _);
                var staff=CellCenter(ShopModel.CheckoutStaffCell(candidate,definition));
                placementStaff.position=new Vector3(staff.x,.052f,staff.z);
                placementStaff.GetComponent<Renderer>().sharedMaterial=valid?MaterialFor(new Color(.27f,.49f,.84f)):invalidBorder;
            }
            placementStaff.gameObject.SetActive(checkout);
            placementService.GetComponent<Renderer>().sharedMaterial = serviceValid ? validBorder : invalidBorder;
            RefreshEditingVisibility();
        }

        public Vector3 PlacementPaymentMarkerPosition=>placementService.position;
        public Vector3 PlacementStaffMarkerPosition=>placementStaff.position;
        public Vector3 CheckoutPaymentMarkerPosition(string id)=>
            facilityVisuals.TryGetValue(id,out var value) && value.paymentMarker!=null?value.paymentMarker.position:Vector3.positiveInfinity;
        public Vector3 CheckoutStaffMarkerPosition(string id)=>
            facilityVisuals.TryGetValue(id,out var value) && value.staffMarker!=null?value.staffMarker.position:Vector3.positiveInfinity;

        public void HidePlacement()
        {
            if (placementRoot != null) placementRoot.gameObject.SetActive(false);
            RefreshEditingVisibility();
        }

        public void SelectFacility(string instanceId)
        {
            selectedId = instanceId;
            RefreshEditingVisibility();
            if (selectionRoot == null) return;
            foreach (Transform child in selectionRoot)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            FacilityVisual facility;
            if (string.IsNullOrEmpty(instanceId) || !facilityVisuals.TryGetValue(instanceId, out facility))
            {
                selectionRoot.gameObject.SetActive(false);
                return;
            }
            selectionRoot.gameObject.SetActive(true);
            selectionRoot.position = new Vector3(facility.center.x, 0.047f, facility.center.z);
            if (selectionMaterial == null) selectionMaterial = MaterialFor(new Color(0.25f, 0.47f, 0.47f), 0.1f);
            List<Transform> edges = CreateEdges(selectionRoot, selectionMaterial);
            SetEdges(edges, facility.size, 0.055f, 0.012f);
        }

        private void BuildCameraAndLight()
        {
            Transform cameraRoot = Group("Fixed shop isometric camera", transform);
            cameraRoot.gameObject.tag = "MainCamera";
            WorldCamera = cameraRoot.gameObject.AddComponent<Camera>();
            WorldCamera.orthographic = true;
            WorldCamera.nearClipPlane = 0.1f;
            WorldCamera.farClipPlane = 100f;
            WorldCamera.clearFlags = CameraClearFlags.SolidColor;
            WorldCamera.backgroundColor = new Color(0.64f, 0.67f, 0.68f);
            WorldCamera.allowHDR = true;
            WorldCamera.allowMSAA = true;
            // The paper view initializes immediately after this method, before
            // the final framing reset; its facing must see the real camera yaw.
            WorldCamera.transform.rotation = viewRotation;
            Transform lightRoot = Group("Natural daylight key", transform);
            Light light = daylight = lightRoot.gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.975f, 0.935f);
            light.intensity = 1.04f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.92f;
            light.shadowBias = 0.018f;
            light.shadowNormalBias = 0.018f;
            light.shadowNearPlane = 0.1f;
            light.shadowResolution = LightShadowResolution.VeryHigh;
            // Light enters from the open south side, with a slight east component:
            // the east wall casts an interior shadow and the back wall is readable.
            lightRoot.rotation = Quaternion.Euler(52f, -20f, 0f);
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.51f, 0.56f, 0.61f);
            RenderSettings.ambientEquatorColor = new Color(0.39f, 0.39f, 0.37f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.22f, 0.20f);
            RenderSettings.ambientIntensity = 0.8f;
            RenderSettings.reflectionIntensity = 0.18f;
            RenderSettings.fog = false;
        }

        public void ApplyVisualSettings(ShopUserSettings settings)
        {
            if (settings == null || daylight == null) return;
            daylight.transform.rotation=Quaternion.Euler(settings.lightPitch,settings.lightYaw,0);
            viewRotation=Quaternion.Euler(settings.cameraPitch,viewRotation.eulerAngles.y,0);ApplyCamera();
            daylight.intensity = 1.04f * settings.lightingIntensity;
            daylight.shadowStrength = settings.shadowStrength;
            daylight.shadows = !settings.shadows ? LightShadows.None :
                settings.graphicsQuality == 0 ? LightShadows.Hard : LightShadows.Soft;
            daylight.shadowResolution = settings.graphicsQuality == 0 ? LightShadowResolution.Low :
                settings.graphicsQuality == 1 ? LightShadowResolution.High : LightShadowResolution.VeryHigh;
            RenderSettings.ambientSkyColor = new Color(.51f, .56f, .61f) * settings.lightingIntensity;
            RenderSettings.ambientEquatorColor = new Color(.39f, .39f, .37f) * settings.lightingIntensity;
            RenderSettings.ambientGroundColor = new Color(.24f, .22f, .20f) * settings.lightingIntensity;
        }

        private void ApplyCamera()
        {
            if (WorldCamera == null) return;
            float distance = layout == null ? 26f : Mathf.Max(26f, Mathf.Max(layout.width, layout.depth) * 2f + WallHeight);
            var position=focus-viewRotation*Vector3.forward*distance;position.y=Mathf.Max(.3f,position.y);
            WorldCamera.transform.SetPositionAndRotation(position, viewRotation);
            WorldCamera.farClipPlane = Mathf.Max(100f, distance * 2f);
            WorldCamera.orthographicSize = size;
        }

        private void BuildRoom()
        {
            Material ground = MaterialFor(new Color(0.59f, 0.62f, 0.62f), 0.05f);
            Material edge = MaterialFor(new Color(0.43f, 0.40f, 0.35f), 0.16f);
            Material floor = MaterialFor(new Color(0.79f, 0.78f, 0.74f), 0.29f);
            Material stone = MaterialFor(new Color(0.48f, 0.50f, 0.49f), 0.10f);
            Material seam = MaterialFor(new Color(0.39f, 0.43f, 0.40f), 0.10f);
            Material wall = MaterialFor(new Color(0.75f, 0.73f, 0.68f), 0.07f);
            Material trim = MaterialFor(new Color(0.55f, 0.53f, 0.48f), 0.22f);
            Cube("Neutral backdrop ground", mapRoot, new Vector3(0f, -0.35f, 0f), new Vector3(200f, 0.10f, 200f), ground, false);
            Cube("Floor slab edge", mapRoot, new Vector3(0f, -0.16f, 0f), new Vector3(layout.width + 0.12f, 0.24f, layout.depth + 0.12f), edge);
            BuildFloorCells();
            Vector3 courtyardMin = CellCenter(new Vector2Int(-1,layout.depth))+new Vector3(-.5f,0,-.5f);
            Vector3 courtyardMax = CellCenter(new Vector2Int(layout.width,layout.depth+3))+new Vector3(.5f,0,.5f);
            Vector3 courtyardCenter=(courtyardMin+courtyardMax)*.5f;courtyardCenter.y=-.075f;
            float roadWidth = layout.width + 2f;
            Cube("Road outside left shop wall",mapRoot,courtyardCenter,new Vector3(roadWidth,.15f,4f),MaterialFor(new Color(.29f,.31f,.32f),.07f));
            Cube("Sidewalk along left wall",mapRoot,new Vector3(courtyardCenter.x,-.035f,CellCenter(new Vector2Int(0,layout.depth)).z),new Vector3(roadWidth,.08f,1f),stone);
            Cube("Road curb",mapRoot,new Vector3(courtyardCenter.x,.015f,CellCenter(new Vector2Int(0,layout.depth)).z+.48f),new Vector3(roadWidth,.10f,.08f),trim);
            for(int x=-1;x<=layout.width;x+=2)Cube("Road dashed lane marking",mapRoot,CellCenter(new Vector2Int(x,layout.depth+2))+new Vector3(0,-.014f,0),new Vector3(.9f,.012f,.055f),MaterialFor(new Color(.81f,.79f,.64f)),false);

            gridRoot = Group("Editing grid - hidden during normal play", mapRoot);
            for (int x = 1; x < layout.width; x++)
                Cube("Grid X " + x, gridRoot, new Vector3(x - layout.width * 0.5f, 0.012f, 0f), new Vector3(0.012f, 0.003f, layout.depth - 0.03f), seam, false);
            for (int z = 1; z < layout.depth; z++)
                Cube("Grid Z " + z, gridRoot, new Vector3(0f, 0.012f, z - layout.depth * 0.5f), new Vector3(layout.width - 0.03f, 0.003f, 0.012f), seam, false);
            for (int x = 1; x < 4; x++)
                Cube("Entrance grid X " + x, gridRoot, new Vector3(courtyardMin.x + x, 0.012f, courtyardCenter.z), new Vector3(0.012f, 0.003f, 0.97f), seam, false);
            gridRoot.gameObject.SetActive(IsEditing);
            float halfWidth = layout.width * 0.5f;
            float halfDepth = layout.depth * 0.5f;
            // Camera sits on -X/-Z. Real-height +X/+Z walls expose their room-side
            // faces and top cross-sections; the two foreground sides remain open.
            float doorLeft = CellCenter(new Vector2Int(ShopModel.EntranceColumn(layout)-1, layout.depth - 1)).x - 0.5f;
            float doorRight = doorLeft + 2f;
            float leftWidth = doorLeft + halfWidth;
            float rightWidth = halfWidth + WallThickness - doorRight;
            float wallZ = halfDepth + WallThickness * 0.5f;
            BuildNorthColumns();
            BuildEastColumns(trim);
            if(catalog.entrancePrefab!=null)
            {
                var entrance=Instantiate(catalog.entrancePrefab,mapRoot);entrance.name="Shop entrance - delivered model";
                entrance.transform.localPosition=new Vector3((doorLeft+doorRight)*.5f,GroundY,halfDepth);
                Cube("Entrance straight threshold",mapRoot,new Vector3((doorLeft+doorRight)*.5f,GroundY+.045f,halfDepth),new Vector3(1.98f,.09f,.24f),MaterialFor(new Color(.07f,.11f,.15f),.35f),false);
            }
            TrackBackWall(Cube("North skirting - left of door", mapRoot,
                new Vector3(-halfWidth + leftWidth * 0.5f, 0.055f, halfDepth - 0.012f), new Vector3(leftWidth, 0.11f, 0.024f), trim));
            TrackBackWall(Cube("North skirting - right of door", mapRoot,
                new Vector3((doorRight + halfWidth) * 0.5f, 0.055f, halfDepth - 0.012f), new Vector3(halfWidth - doorRight, 0.11f, 0.024f), trim));
        }

        private bool FindWarehouse(out PlacedFacility placement, out FacilityDefinition definition)
        {
            placement = null;
            definition = null;
            if (layout == null || layout.facilities == null || catalog == null) return false;
            foreach (PlacedFacility item in layout.facilities)
            {
                FacilityDefinition candidate = item == null ? null : catalog.Find(item.definitionId);
                if (candidate == null || candidate.kind != FacilityKind.Warehouse) continue;
                placement = item;
                definition = candidate;
                return true;
            }
            return false;
        }

        private void BuildEastWall(Material wall, Material trim)
        {
            float halfWidth = layout.width * 0.5f;
            float halfDepth = layout.depth * 0.5f;
            PlacedFacility warehouse;
            FacilityDefinition definition;
            if (!FindWarehouse(out warehouse, out definition) || warehouse.x < layout.width)
            {
                Cube("East interior wall +X", mapRoot, new Vector3(halfWidth + WallThickness * 0.5f, WallHeight * 0.5f, 0f), new Vector3(WallThickness, WallHeight, layout.depth), wall);
                Cube("East skirting", mapRoot, new Vector3(halfWidth - 0.012f, 0.055f, 0f), new Vector3(0.024f, 0.11f, layout.depth), trim);
                return;
            }

            // A flush door at the rear corner is the complete visible warehouse.
            // The storage room is off-screen; every other segment is a full shop wall.
            float serviceZ = Mathf.Clamp(CellCenter(ShopModel.ServiceCell(warehouse, definition)).z, -halfDepth+.6f, halfDepth-.6f);
            float openingMin = serviceZ - 0.5f, openingMax = serviceZ + 0.5f;
            EastWallSegment("East shop wall before corner door", -halfDepth, openingMin, WallHeight, wall, trim);
            EastWallSegment("East corner return", openingMax, halfDepth, WallHeight, wall, trim);
            Cube("Warehouse corner door lintel", mapRoot,
                new Vector3(halfWidth + WallThickness * 0.5f, (WallHeight + 2.12f) * 0.5f, serviceZ),
                new Vector3(WallThickness, WallHeight - 2.12f, 1f), wall);
        }

        private void EastWallSegment(string name, float startZ, float endZ, float height, Material wall, Material trim)
        {
            float length = endZ - startZ;
            if (length <= 0.01f) return;
            float x = layout.width * 0.5f;
            float z = (startZ + endZ) * 0.5f;
            Cube(name, mapRoot, new Vector3(x + WallThickness * 0.5f, height * 0.5f, z), new Vector3(WallThickness, height, length), wall);
            Cube(name + " skirting", mapRoot, new Vector3(x - 0.012f, 0.055f, z), new Vector3(0.024f, 0.11f, length), trim);
        }

        private void TrackBackWall(GameObject wall)
        {
            backWallSections.Add(wall.GetComponent<Renderer>());
        }

        private void LateUpdate()
        {
            if (CharacterRoot == null || WorldCamera == null || layout == null) return;
            bool outside = CharacterRoot.position.z > layout.depth * 0.5f - 0.02f;
            Vector3 right = WorldCamera.transform.right;
            foreach (Renderer section in backWallSections)
            {
                if (section == null) continue;
                bool blocksCharacter = false;
                if (outside)
                {
                    // Cut away only the section actually hiding the paper card.
                    // Its shadow remains, and the full wall returns indoors.
                    for (int side = -1; side <= 1 && !blocksCharacter; side++)
                    for (int level = 0; level < 3 && !blocksCharacter; level++)
                    {
                        Vector3 target = CharacterRoot.position + right * (side * 0.30f) + Vector3.up * (0.15f + level * 0.65f);
                        Ray ray = WorldCamera.ScreenPointToRay(WorldCamera.WorldToScreenPoint(target));
                        float distance;
                        if (section.bounds.IntersectRay(ray, out distance)
                            && distance < Vector3.Dot(target - ray.origin, ray.direction) - 0.04f)
                            blocksCharacter = true;
                    }
                }
                section.shadowCastingMode = blocksCharacter ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }
        }

        private void BuildFacility(PlacedFacility placement, FacilityDefinition definition)
        {
            Vector2Int footprint = ShopModel.Size(definition, placement.rotation);
            Vector3 center = FootprintCenter(placement, footprint);
            Transform facility = Group("Facility " + placement.instanceId + " - " + definition.title, mapRoot);
            facility.position = center;
            Transform geometry = Group("Footprint and service geometry", facility);
            Color floorColor = new Color(0.69f, 0.72f, 0.67f);
            if (definition.kind != FacilityKind.Warehouse) Cube("Independent footprint", geometry, new Vector3(0f, -0.004f, 0f), new Vector3(footprint.x - 0.10f, 0.008f, footprint.y - 0.10f), MaterialFor(floorColor), false);

            // Asset contract: origin at the ground-center of the unrotated
            // footprint, shelf front -Z / checkout customer front +Z.
            // No model collider is used for picking.
            Transform visualSlot = Group("VisualSlot", facility);
            visualSlot.localRotation = Quaternion.Euler(0f, 90f * placement.rotation, 0f);
            float placeholderHeight = PlaceholderHeight(definition);
            if(definition.kind==FacilityKind.Shelf)stockSlots[placement.instanceId]=new StockSlot{root=Group("Displayed products",visualSlot),definition=definition.id};
            if (definition.modelPrefab != null && definition.kind != FacilityKind.Warehouse)
            {
                GameObject imported = Instantiate(definition.modelPrefab, visualSlot);
                imported.name = definition.modelPrefab.name;
                imported.transform.localPosition = Vector3.zero;
                imported.transform.localRotation = Quaternion.identity;
            }
            else if (definition.kind == FacilityKind.Warehouse)
            {
                // The warehouse is represented exclusively by its wall-mounted door.
            }
            else if (ShopModel.IsStaffPoint(definition.kind))
            {
                var color=definition.color;
                Cube("Staff floor marker",visualSlot,new Vector3(0,.012f,0),new Vector3(.78f,.024f,.78f),MaterialFor(color),false);
                Cube("Staff marker center",visualSlot,new Vector3(0,.026f,0),new Vector3(.18f,.006f,.18f),MaterialFor(new Color(.94f,.95f,.91f)),false);
            }
            else
            {
                Color blockColor = Color.Lerp(new Color(0.56f, 0.54f, 0.49f), definition.color, 0.18f);
                float smoothness = definition.kind.ToString() == "Checkout" ? 0.34f : definition.kind.ToString() == "Warehouse" ? 0.14f : 0.25f;
                float inset = definition.kind == FacilityKind.Stage ? 0.06f : 0.22f;
                Cube("Temporary block - replace via catalog modelPrefab", visualSlot, Vector3.up * (placeholderHeight * 0.5f), new Vector3(Mathf.Max(0.1f, definition.width - inset), placeholderHeight, Mathf.Max(0.1f, definition.depth - inset)), MaterialFor(blockColor, smoothness));
            }
            if(definition.kind==FacilityKind.Stage&&definition.modelPrefab==null)
                Cube("Stage front - four-cell edge",visualSlot,new Vector3(-definition.width*.5f+.08f,placeholderHeight+.012f,0),new Vector3(.08f,.025f,definition.depth-.12f),MaterialFor(new Color(.68f,.58f,.35f)));
            Vector3 service = CellCenter(definition.kind==FacilityKind.Checkout ?
                ShopModel.PaymentCell(layout,catalog,placement) : ShopModel.ServiceCell(placement, definition));
            Vector3 warehouseSign = Vector3.zero;
            if (definition.kind == FacilityKind.Warehouse)
                warehouseSign = BuildWarehouseDoor(visualSlot, center, service);
            bool checkout=definition.kind==FacilityKind.Checkout;
            Color serviceColor=checkout?(ShopModel.CanUseCheckoutFront(layout,catalog,placement,out _)?
                new Color(.25f,.61f,.36f):new Color(.79f,.30f,.36f)):new Color(.66f,.72f,.66f);
            Vector3 facingMarker = new Vector3(service.x-center.x,.045f,service.z-center.z);
            if(definition.kind==FacilityKind.Stage)
                facingMarker=FacingDirection(placement,definition)*(definition.width*.5f+.35f)+Vector3.up*.045f;
            Transform serviceDot = null;
            if(definition.kind!=FacilityKind.Warehouse)
            {serviceDot = Arrow("Facility facing",geometry,facingMarker,MaterialFor(serviceColor));
             serviceDot.localRotation=Quaternion.LookRotation(FacingDirection(placement,definition));}
            Transform staffDot=null;
            if(checkout)
            {
                var staff=CellCenter(ShopModel.CheckoutStaffCell(placement,definition));
                staffDot=Disc("Cashier standing - rear",geometry,new Vector3(staff.x-center.x,.007f,staff.z-center.z),.23f,
                    MaterialFor(new Color(.27f,.49f,.84f)));
                labels.Add(new WorldLabel{text="前 · 顾客付款",position=service+Vector3.up*.09f,instanceId=placement.instanceId});
                labels.Add(new WorldLabel{text="后 · 收银员",position=staff+Vector3.up*.09f,instanceId=placement.instanceId});
            }
            Renderer[] modelRenderers = visualSlot.GetComponentsInChildren<Renderer>();
            float labelHeight = center.y + placeholderHeight;
            foreach (Renderer renderer in modelRenderers)
                if (renderer != null && renderer.enabled) labelHeight = Mathf.Max(labelHeight, renderer.bounds.max.y);
            facilityVisuals[placement.instanceId] = new FacilityVisual { center = definition.kind==FacilityKind.Warehouse?service:center, size = definition.kind==FacilityKind.Warehouse?Vector2Int.one:footprint, renderers = modelRenderers, helperGeometry = geometry,
                paymentMarker=checkout?serviceDot:null,staffMarker=staffDot };
            geometry.gameObject.SetActive(IsEditing || placement.instanceId == selectedId);
            if(definition.kind!=FacilityKind.Warehouse)labels.Add(new WorldLabel
            {
                text = definition.kind == FacilityKind.Warehouse
                    ? "仓库"
                    : definition.title.Replace("预留位", "").Replace("占位", ""),
                position = definition.kind == FacilityKind.Warehouse ? warehouseSign : new Vector3(center.x, labelHeight + 0.15f, center.z),
                instanceId = placement.instanceId,
                alwaysVisible = false
            });
        }

        public Vector3 WarehouseDoorPosition => new Vector3(layout.width * .5f - .1f, GroundY + 2.35f,
            Mathf.Clamp(CellCenter(ShopModel.WarehousePickup(layout)).z,-layout.depth*.5f+.6f,layout.depth*.5f-.6f));

        private Vector3 BuildWarehouseDoor(Transform slot, Vector3 facilityCenter, Vector3 service)
        {
            float x = layout.width * .5f - .025f;
            Vector3 door = new Vector3(x, GroundY, Mathf.Clamp(service.z,-layout.depth*.5f+.6f,layout.depth*.5f-.6f)) - facilityCenter;
            var prefab=catalog.Find("warehouse").modelPrefab;
            if(prefab!=null)
            {
                var model=Instantiate(prefab,slot);
                model.name="Imported flush warehouse door";
                // The delivered prefab faces -Z; turn that face toward the shop (-X).
                model.transform.rotation=Quaternion.Euler(0,90,0);
                model.transform.position=facilityCenter+door;
                var renderers=model.GetComponentsInChildren<Renderer>();
                var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                // Put the back of the frame on the inner wall plane, never behind it.
                model.transform.position+=Vector3.right*(x-bounds.max.x);
                return WarehouseDoorPosition;
            }
            Material frame = MaterialFor(new Color(.28f,.36f,.34f),.23f);
            Material panel = MaterialFor(new Color(.47f,.50f,.46f),.18f);
            Cube("Flush corner warehouse door leaf",slot,door+new Vector3(.025f,1.04f,0),new Vector3(.04f,2.08f,.96f),panel);
            Cube("Corner door near jamb",slot,door+new Vector3(0,1.07f,-.52f),new Vector3(.09f,2.14f,.065f),frame);
            Cube("Corner door rear jamb",slot,door+new Vector3(0,1.07f,.52f),new Vector3(.09f,2.14f,.065f),frame);
            Cube("Corner door header",slot,door+new Vector3(0,2.15f,0),new Vector3(.09f,.07f,1.1f),frame);
            Cube("Warehouse door handle",slot,door+new Vector3(-.06f,1.03f,-.31f),new Vector3(.08f,.055f,.16f),frame);
            return WarehouseDoorPosition;
        }

        private Vector3 FootprintCenter(PlacedFacility placement, Vector2Int footprint)
        {
            return CellCenter(new Vector2Int(placement.x, placement.z)) + new Vector3((footprint.x - 1) * 0.5f, 0f, (footprint.y - 1) * 0.5f);
        }

        private static float PlaceholderHeight(FacilityDefinition definition)
        {
            if(ShopModel.IsStaffPoint(definition.kind))return .03f;
            if (string.Equals(definition.id, "fitting", StringComparison.OrdinalIgnoreCase)) return 2.1f;
            string kind = definition.kind.ToString();
            if (string.Equals(kind, "Warehouse", StringComparison.OrdinalIgnoreCase)) return 1.5f;
            if (string.Equals(kind, "Checkout", StringComparison.OrdinalIgnoreCase)) return 0.8f;
            if (string.Equals(kind, "Stage", StringComparison.OrdinalIgnoreCase)) return 0.25f;
            if (string.Equals(kind, "Decor", StringComparison.OrdinalIgnoreCase)) return 0.54f;
            return 0.82f;
        }

        private Vector3 FacingDirection(PlacedFacility p,FacilityDefinition d)
        {
            var direction=d.kind==FacilityKind.Stage?Vector3.left:d.kind==FacilityKind.Checkout?Vector3.forward:Vector3.back;
            return Quaternion.Euler(0,p.rotation*90,0)*direction;
        }
        private Mesh directionMesh;
        private Transform Arrow(string name,Transform parent,Vector3 position,Material material)
        {
            var root=Group(name,parent);root.localPosition=position;
            if(directionMesh==null)
            {
            var mesh=new Mesh{name="Direction arrow"};
            mesh.vertices=new[]{new Vector3(-.075f,0,-.27f),new Vector3(.075f,0,-.27f),new Vector3(.075f,0,.02f),new Vector3(.24f,0,.02f),new Vector3(0,0,.34f),new Vector3(-.24f,0,.02f),new Vector3(-.075f,0,.02f)};
            mesh.triangles=new[]{0,2,1,0,6,2,5,4,3};mesh.RecalculateNormals();mesh.RecalculateBounds();directionMesh=mesh;
            }
            var shared=directionMesh;
            root.gameObject.AddComponent<MeshFilter>().sharedMesh=shared;root.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;return root;
        }
        private void BuildPlacementOverlay()
        {
            validMaterial = MaterialFor(new Color(0.70f, 0.88f, 0.73f));
            invalidMaterial = MaterialFor(new Color(0.94f, 0.70f, 0.71f));
            validBorder = MaterialFor(new Color(0.25f, 0.61f, 0.36f));
            invalidBorder = MaterialFor(new Color(0.79f, 0.30f, 0.36f));
            placementRoot = Group("Placement preview - no gameplay colliders", overlayRoot);
            placementPad = Cube("Candidate footprint", placementRoot, Vector3.zero, Vector3.one, validMaterial, false).transform;
            placementEdges.AddRange(CreateEdges(placementRoot, validBorder));
            placementService = Arrow("Candidate direction",placementRoot,Vector3.zero,validBorder);
            placementStaff = Disc("Candidate cashier rear",placementRoot,Vector3.zero,.20f,MaterialFor(new Color(.27f,.49f,.84f)));
            placementStaff.gameObject.SetActive(false);
            placementRoot.gameObject.SetActive(false);
        }

        private List<Transform> CreateEdges(Transform parent, Material material)
        {
            List<Transform> result = new List<Transform>();
            for (int i = 0; i < 4; i++) result.Add(Cube("Footprint edge " + i, parent, Vector3.zero, Vector3.one, material, false).transform);
            return result;
        }

        private static void SetEdges(List<Transform> edges, Vector2Int footprint, float thickness, float height)
        {
            float width = footprint.x - 0.02f;
            float depth = footprint.y - 0.02f;
            edges[0].localPosition = new Vector3(0f, 0.005f, -depth * 0.5f);
            edges[1].localPosition = new Vector3(0f, 0.005f, depth * 0.5f);
            edges[2].localPosition = new Vector3(-width * 0.5f, 0.005f, 0f);
            edges[3].localPosition = new Vector3(width * 0.5f, 0.005f, 0f);
            edges[0].localScale = edges[1].localScale = new Vector3(width, height, thickness);
            edges[2].localScale = edges[3].localScale = new Vector3(thickness, height, depth);
        }

        private Transform Disc(string name, Transform parent, Vector3 position, float diameter, Material material)
        {
            GameObject obj = Primitive(name, PrimitiveType.Cylinder, parent, position, new Vector3(diameter, 0.005f, diameter), material, false);
            return obj.transform;
        }

        private GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool shadows = true)
        {
            return Primitive(name, PrimitiveType.Cube, parent, position, scale, material, shadows);
        }

        private GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool shadows)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            Renderer renderer = obj.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Destroy(collider); }
            return obj;
        }

        private Material MaterialFor(Color color, float smoothness = 0.20f, float metallic = 0f)
        {
            color.a = 1f;
            string key = ColorUtility.ToHtmlStringRGB(color) + "-" + Mathf.RoundToInt(smoothness * 1000f) + "-" + Mathf.RoundToInt(metallic * 1000f);
            Material material;
            if (surfaceMaterials.TryGetValue(key, out material)) return material;
            material = new Material(shader) { name = "Shop surface " + key };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            materials.Add(material);
            surfaceMaterials[key] = material;
            return material;
        }

        private static Transform Group(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void OnGUI()
        {
            if (!PresentationLabelsEnabled || WorldCamera == null || labels.Count == 0) return;
            EnsureLabelStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            int previousDepth=GUI.depth;GUI.depth=100;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
            float uiScale = Mathf.Max(0.55f, Mathf.Min(Screen.width / 1360f, Screen.height / 850f));
            areaStyle.fontSize = Mathf.RoundToInt(14f * uiScale);
            facilityStyle.fontSize = selectedStyle.fontSize = Mathf.RoundToInt(12f * uiScale);
            foreach (WorldLabel label in labels)
            {
                if(label.instanceId=="warehouse-1")continue;
                if (!label.alwaysVisible && label.instanceId != selectedId) continue;
                Vector3 projected = WorldCamera.WorldToScreenPoint(label.position);
                if (projected.z <= 0f) continue;
                GUIStyle style = label.instanceId == selectedId ? selectedStyle : facilityStyle;
                Vector2 measured = style.CalcSize(new GUIContent(label.text));
                measured.x += 8f * uiScale;
                // Some CJK font fallbacks report a shorter bold line height.
                // Give labels explicit vertical room so glyph tops are not clipped.
                measured.y = Mathf.Max(measured.y, 30f * uiScale);
                Rect rect = new Rect(projected.x - measured.x * 0.5f, Screen.height - projected.y - measured.y * 0.5f, measured.x, measured.y);
                if (rect.xMax < 12f * uiScale || rect.xMin > Screen.width - 12f * uiScale || rect.yMax < 94f * uiScale || rect.yMin > Screen.height - 104f * uiScale) continue;
                rect.x = Mathf.Clamp(rect.x, 12f * uiScale, Screen.width - rect.width - 12f * uiScale);
                rect.y = Mathf.Clamp(rect.y, 94f * uiScale, Screen.height - rect.height - 104f * uiScale);
                if(facilityBubble!=null)GUI.DrawTexture(rect,facilityBubble,ScaleMode.StretchToFill);
                GUI.Label(rect, label.text, style);
            }
            GUI.matrix = previousMatrix;
            GUI.depth=previousDepth;
            GUI.color = previousColor;
        }

        private void EnsureLabelStyles()
        {
            if (areaStyle != null) return;
            try
            {
                labelFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "微软雅黑", "Noto Sans CJK SC", "SimHei" }, 16);
                ownsFont = labelFont != null;
            }
            catch (Exception) { labelFont = null; }
            if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelBackground = SolidTexture(new Color(0.99f, 0.99f, 0.96f, 0.91f));
            selectedBackground = SolidTexture(new Color(0.94f, 0.94f, 0.88f, 0.97f));
            areaStyle = new GUIStyle(GUI.skin.label) { font = labelFont, fontSize = 14, fontStyle = FontStyle.Normal, wordWrap = false, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 3, 3) };
            areaStyle.normal.textColor = new Color(0.48f, 0.53f, 0.50f);
            facilityStyle = new GUIStyle(areaStyle) { fontSize = 12, fontStyle = FontStyle.Normal, padding = new RectOffset(7, 7, 4, 4) };
            facilityBubble=Resources.Load<Texture2D>("UI/World/bubble-neutral");
            facilityStyle.normal.background = facilityBubble==null?labelBackground:null;
            facilityStyle.normal.textColor = new Color(0.34f, 0.38f, 0.39f);
            selectedStyle = new GUIStyle(facilityStyle) { fontStyle = FontStyle.Bold };
            selectedStyle.normal.background = facilityBubble==null?selectedBackground:null;
            selectedStyle.normal.textColor = new Color(0.22f, 0.35f, 0.34f);
        }

        private static Texture2D SolidTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "World label background" };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if(directionMesh!=null)Destroy(directionMesh);
            ClearFinishMeshes();
            foreach (Material material in materials) if (material != null) Destroy(material);
            if (labelBackground != null) Destroy(labelBackground);
            if (selectedBackground != null) Destroy(selectedBackground);
            if (ownsFont && labelFont != null) Destroy(labelFont);
        }
    }
}
