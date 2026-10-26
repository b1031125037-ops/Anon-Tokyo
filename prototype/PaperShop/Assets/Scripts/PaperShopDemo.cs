using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnonTokyo.Preview
{
    /// <summary>
    /// A deliberately small visual prototype: one plant, one lit paper character,
    /// a fixed isometric camera, and two poses. All animation uses a private clock.
    /// No gameplay state depends on an animation callback or on Time.timeScale.
    /// </summary>
    public sealed class PaperShopDemo : MonoBehaviour
    {
        public Shader litShader;

        public string CurrentState
        {
            get
            {
                string state = flipping ? "翻面切换" : moving ? "绕行 · 空间遮挡" : interacting ? "交互 · 轻轻摇晃" : "待机 · 纸片受光";
                return IsPaused ? "已暂停 / " + state : state;
            }
        }
        public int FlipCount { get; private set; }
        public float ElapsedDemoTime { get; private set; }
        public bool IsPaused { get; private set; }
        public Vector3 CharacterPosition { get { return characterRoot != null ? characterRoot.position : Vector3.zero; } }
        public Camera DemoCamera { get; private set; }
        public bool AutomaticDemo { get { return automatic; } }

        private const float FloorY = 0.021f;
        private const float CardHeight = 2.10f;
        private const float FlipDuration = 0.22f;
        private const float WalkSpeed = 0.98f;
        // Both source cards are 512 x 768; their common foot pivot is (256, 736),
        // measured from the top-left. Transparent padding below the foot is kept.
        private const float FootPaddingRatio = 32f / 768f;
        private const float CameraPitch = 35.264f;
        private const float CameraYaw = 45f;

        private readonly Vector3 plantPosition = new Vector3(0.38f, 0f, 0.28f);
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<Texture2D> ownedUITextures = new List<Texture2D>();
        private readonly List<Vector3> walkPath = new List<Vector3>();
        private Transform characterRoot;
        private Transform facingRoot;
        private Transform flipRoot;
        private Transform swayRoot;
        private Transform targetMarker;
        private Mesh cardMesh;
        private Material cardMaterial;
        private Texture2D idleTexture;
        private Texture2D workTexture;
        private Light keyLight;
        private Vector3 cameraFocus;
        private Vector3 initialCameraFocus;
        private Vector3 homePosition;
        private Quaternion cameraRotation;
        private float cameraSize = 3.58f;
        private float lightAngle = 38f;
        private float shadowStrength = 0.62f;
        private float swayAngle;
        private float flipElapsed;
        private float phaseElapsed;
        private int walkIndex;
        private int autoPhase;
        private bool built;
        private bool automatic = true;
        private bool moving;
        private bool interacting;
        private bool showingWork;
        private bool desiredWork;
        private bool flipTargetWork;
        private bool flipping;
        private bool swappedAtEdge;
        private bool texturesAvailable;
        private bool panning;
        private Font uiFont;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;
        private GUIStyle badgeStyle;
        private GUIStyle buttonStyle;
        private GUIStyle selectedButtonStyle;
        private GUIStyle valueStyle;
        private GUIStyle sceneTitleStyle;
        private Texture2D lineTexture;

        private void Awake()
        {
            BuildDemo();
        }

        private void BuildDemo()
        {
            if (built) return;
            built = true;
            if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("Paper Shop needs the Universal Render Pipeline/Lit shader. Assign litShader in the scene.", this);
                enabled = false;
                return;
            }

            idleTexture = Resources.Load<Texture2D>("CardTextures/idle");
            workTexture = Resources.Load<Texture2D>("CardTextures/work");
            texturesAvailable = idleTexture != null && workTexture != null;
            if (!texturesAvailable)
                Debug.LogError("Paper Shop: add idle.png and work.png to Resources/CardTextures (512 x 768, transparent alpha, foot at 256,736).", this);

            BuildLightingAndCamera();
            BuildPlatform();
            BuildPlant();
            BuildCharacter();
            BuildDestinationMarker();
            ResetDemo();
        }

        private void BuildLightingAndCamera()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.74f, 0.79f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.68f, 0.65f);
            RenderSettings.ambientGroundColor = new Color(0.44f, 0.42f, 0.40f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 0.3f;
            RenderSettings.fog = false;

            GameObject lightObject = new GameObject("Warm key light");
            lightObject.transform.SetParent(transform, false);
            keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.94f, 0.86f);
            keyLight.intensity = 1.30f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = shadowStrength;
            keyLight.shadowBias = 0.025f;
            keyLight.shadowNormalBias = 0.025f;
            keyLight.shadowNearPlane = 0.1f;
            UpdateLight();
            RenderSettings.sun = keyLight;

            GameObject cameraObject = new GameObject("Fixed isometric camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            DemoCamera = cameraObject.AddComponent<Camera>();
            DemoCamera.orthographic = true;
            DemoCamera.nearClipPlane = 0.05f;
            DemoCamera.farClipPlane = 70f;
            DemoCamera.clearFlags = CameraClearFlags.SolidColor;
            DemoCamera.backgroundColor = new Color(0.91f, 0.935f, 0.936f);
            DemoCamera.allowHDR = true;
            DemoCamera.allowMSAA = true;
            cameraRotation = Quaternion.Euler(CameraPitch, CameraYaw, 0f);
            initialCameraFocus = new Vector3(0.08f, 0.65f, 0.03f) - cameraRotation * Vector3.right * 1.15f;
            cameraFocus = initialCameraFocus;
            UpdateCamera();
        }

        private void BuildPlatform()
        {
            Transform stage = NewGroup("Miniature platform", transform);
            Material backdrop = NewLitMaterial("Backdrop", new Color(0.85f, 0.89f, 0.89f), 0.16f);
            Material rim = NewLitMaterial("Dusty rose platform edge", new Color(0.69f, 0.56f, 0.56f), 0.26f);
            Material floor = NewLitMaterial("Warm porcelain floor", new Color(0.91f, 0.87f, 0.78f), 0.22f);
            Material seam = NewLitMaterial("Fine floor seams", new Color(0.81f, 0.77f, 0.69f), 0.15f);
            Primitive("Quiet ground", PrimitiveType.Cube, stage, new Vector3(0f, -0.48f, 0f), new Vector3(200f, 0.12f, 200f), backdrop, false);
            Primitive("Platform base", PrimitiveType.Cube, stage, new Vector3(0f, -0.25f, 0f), new Vector3(5.3f, 0.36f, 4.25f), rim);
            Primitive("Platform top", PrimitiveType.Cube, stage, new Vector3(0f, -0.045f, 0f), new Vector3(5.35f, 0.11f, 4.3f), floor);
            // Only a few quiet seams: the scene's subject stays the paper card.
            for (int i = -1; i <= 1; i++)
            {
                Primitive("Floor seam X " + i, PrimitiveType.Cube, stage, new Vector3(i * 1.35f, 0.012f, 0f), new Vector3(0.012f, 0.003f, 4.24f), seam, false);
                Primitive("Floor seam Z " + i, PrimitiveType.Cube, stage, new Vector3(0f, 0.012f, i * 1.08f), new Vector3(5.29f, 0.003f, 0.012f), seam, false);
            }
        }

        private void BuildPlant()
        {
            Transform plant = NewGroup("Single decoration - sculptural potted plant", transform);
            plant.localPosition = plantPosition;
            Material pot = NewLitMaterial("Matte cream ceramic", new Color(0.92f, 0.83f, 0.73f), 0.2f);
            Material soil = NewLitMaterial("Soil", new Color(0.28f, 0.23f, 0.20f), 0.05f);
            Material stem = NewLitMaterial("Plant stems", new Color(0.38f, 0.44f, 0.31f), 0.12f);
            Material green = NewLitMaterial("Sage leaves", new Color(0.43f, 0.58f, 0.46f), 0.24f);
            Material lightGreen = NewLitMaterial("Young sage leaves", new Color(0.59f, 0.68f, 0.48f), 0.25f);
            Primitive("Ceramic pot", PrimitiveType.Cylinder, plant, new Vector3(0f, 0.29f, 0f), new Vector3(0.79f, 0.28f, 0.79f), pot);
            Primitive("Pot rim", PrimitiveType.Cylinder, plant, new Vector3(0f, 0.55f, 0f), new Vector3(0.86f, 0.045f, 0.86f), pot);
            Primitive("Soil surface", PrimitiveType.Cylinder, plant, new Vector3(0f, 0.599f, 0f), new Vector3(0.71f, 0.008f, 0.71f), soil, false);
            Primitive("Central stem", PrimitiveType.Cylinder, plant, new Vector3(0f, 1.03f, 0f), new Vector3(0.075f, 0.45f, 0.075f), stem);
            Leaf(plant, "Leaf crown", new Vector3(0.01f, 1.70f, 0.01f), new Vector3(0.58f, 0.89f, 0.50f), new Vector3(0f, 14f, -9f), green);
            Leaf(plant, "Leaf left", new Vector3(-0.34f, 1.30f, -0.02f), new Vector3(0.71f, 0.48f, 0.38f), new Vector3(3f, -18f, -29f), green);
            Leaf(plant, "Leaf right", new Vector3(0.30f, 1.41f, 0.05f), new Vector3(0.69f, 0.51f, 0.37f), new Vector3(0f, 12f, 27f), lightGreen);
            Leaf(plant, "Leaf front", new Vector3(0.04f, 1.18f, -0.26f), new Vector3(0.49f, 0.55f, 0.40f), new Vector3(-25f, -26f, 14f), lightGreen);
            Leaf(plant, "Leaf back", new Vector3(-0.03f, 1.43f, 0.25f), new Vector3(0.45f, 0.61f, 0.35f), new Vector3(18f, 25f, -15f), green);
        }

        private void Leaf(Transform parent, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            GameObject leaf = Primitive(name, PrimitiveType.Sphere, parent, position, scale, material);
            leaf.transform.localRotation = Quaternion.Euler(euler);
        }

        private void BuildCharacter()
        {
            characterRoot = NewGroup("Character - stable logical root", transform);
            facingRoot = NewGroup("01 Fixed camera-facing yaw", characterRoot);
            Vector3 facing = -(cameraRotation * Vector3.forward);
            facing.y = 0f;
            facingRoot.localRotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            flipRoot = NewGroup("02 Pose flip about feet", facingRoot);
            swayRoot = NewGroup("03 Visual-only foot sway", flipRoot);
            GameObject card = new GameObject("04 Alpha-clipped paper character");
            card.transform.SetParent(swayRoot, false);

            float width = CardHeight * 512f / 768f;
            float bottom = -CardHeight * FootPaddingRatio;
            cardMesh = new Mesh { name = "Foot-anchored card mesh" };
            cardMesh.vertices = new[]
            {
                new Vector3(-width * 0.5f, bottom, 0f),
                new Vector3(width * 0.5f, bottom, 0f),
                new Vector3(-width * 0.5f, bottom + CardHeight, 0f),
                new Vector3(width * 0.5f, bottom + CardHeight, 0f)
            };
            cardMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            cardMesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            cardMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            cardMesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            cardMesh.RecalculateBounds();
            card.AddComponent<MeshFilter>().sharedMesh = cardMesh;
            MeshRenderer renderer = card.AddComponent<MeshRenderer>();
            cardMaterial = NewLitMaterial("Paper card - lit cutout double-sided", Color.white, 0.12f);
            SetFloat(cardMaterial, "_Surface", 0f);
            SetFloat(cardMaterial, "_AlphaClip", 1f);
            SetFloat(cardMaterial, "_Cutoff", 0.38f);
            SetFloat(cardMaterial, "_Cull", (float)CullMode.Off);
            SetFloat(cardMaterial, "_ZWrite", 1f);
            SetFloat(cardMaterial, "_SrcBlend", (float)BlendMode.One);
            SetFloat(cardMaterial, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(cardMaterial, "_AlphaToMask", 1f);
            SetFloat(cardMaterial, "_ReceiveShadows", 1f);
            cardMaterial.EnableKeyword("_ALPHATEST_ON");
            cardMaterial.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            cardMaterial.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            cardMaterial.SetOverrideTag("RenderType", "TransparentCutout");
            cardMaterial.renderQueue = (int)RenderQueue.AlphaTest;
            cardMaterial.doubleSidedGI = true;
            cardMaterial.SetShaderPassEnabled("ShadowCaster", true);
            renderer.sharedMaterial = cardMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            renderer.receiveShadows = true;
            // Missing art is visible in UI and logs; no rectangle is substituted for it.
            renderer.enabled = texturesAvailable;
            ApplyPose(false);
            homePosition = plantPosition + new Vector3(-1.13f, FloorY, -0.86f);
        }

        private void BuildDestinationMarker()
        {
            Material markerMaterial = NewLitMaterial("Destination indicator", new Color(0.76f, 0.43f, 0.54f), 0.1f);
            targetMarker = Primitive("Ground click destination", PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(0.16f, 0.006f, 0.16f), markerMaterial, false).transform;
            targetMarker.gameObject.SetActive(false);
        }

        private static Transform NewGroup(string name, Transform parent)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool castsShadow = true)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            Renderer renderer = obj.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castsShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return obj;
        }

        private Material NewLitMaterial(string name, Color color, float smoothness)
        {
            Material material = new Material(litShader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", smoothness);
            ownedMaterials.Add(material);
            return material;
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private void Update()
        {
            if (!built || characterRoot == null || IsPaused) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            ElapsedDemoTime += dt;
            if (automatic) UpdateAutomatic(dt);
            UpdateWalking(dt);
            UpdateFlip(dt);
            UpdateSway(dt);
        }

        private void UpdateAutomatic(float dt)
        {
            phaseElapsed += dt;
            switch (autoPhase)
            {
                case 0:
                    if (phaseElapsed >= 1.8f)
                    {
                        BeginOrbit();
                        autoPhase = 1;
                        phaseElapsed = 0f;
                    }
                    break;
                case 1:
                    if (!moving && !flipping)
                    {
                        interacting = true;
                        RequestPose(true);
                        autoPhase = 2;
                        phaseElapsed = 0f;
                    }
                    break;
                case 2:
                    if (phaseElapsed >= 3.4f)
                    {
                        interacting = false;
                        RequestPose(false);
                        autoPhase = 3;
                        phaseElapsed = 0f;
                    }
                    break;
                case 3:
                    if (phaseElapsed >= 2.0f && !flipping)
                    {
                        autoPhase = 0;
                        phaseElapsed = 0f;
                    }
                    break;
            }
        }

        private void BeginOrbit()
        {
            StopWalking();
            interacting = false;
            RequestPose(true);
            // Camera is to the negative X/Z side: this loop crosses both the
            // foreground and the far side of the plant, using ordinary depth tests.
            Vector3[] offsets =
            {
                new Vector3(0.82f, 0f, -1.15f),
                new Vector3(1.25f, 0f, -0.14f),
                new Vector3(0.70f, 0f, 1.21f),
                new Vector3(-0.48f, 0f, 1.15f),
                new Vector3(-1.25f, 0f, 0.20f),
                new Vector3(-1.13f, 0f, -0.86f)
            };
            foreach (Vector3 offset in offsets)
            {
                Vector3 point = plantPosition + offset;
                point.y = FloorY;
                walkPath.Add(point);
            }
            moving = true;
            walkIndex = 0;
        }

        private void UpdateWalking(float dt)
        {
            if (!moving || walkIndex >= walkPath.Count) return;
            float remainingStep = WalkSpeed * dt;
            while (moving && remainingStep > 0f)
            {
                Vector3 target = walkPath[walkIndex];
                float distance = Vector3.Distance(characterRoot.position, target);
                if (distance <= remainingStep + 0.0001f)
                {
                    characterRoot.position = target;
                    remainingStep -= distance;
                    walkIndex++;
                    if (walkIndex >= walkPath.Count)
                    {
                        moving = false;
                        targetMarker.gameObject.SetActive(false);
                        if (!automatic) RequestPose(false);
                    }
                }
                else
                {
                    characterRoot.position = Vector3.MoveTowards(characterRoot.position, target, remainingStep);
                    remainingStep = 0f;
                }
            }
        }

        private void StopWalking()
        {
            moving = false;
            walkPath.Clear();
            walkIndex = 0;
            if (targetMarker != null) targetMarker.gameObject.SetActive(false);
        }

        private void RequestPose(bool work)
        {
            desiredWork = work;
            if (flipping)
            {
                if (!swappedAtEdge) flipTargetWork = work;
                return;
            }
            if (showingWork == work) return;
            flipping = true;
            swappedAtEdge = false;
            flipElapsed = 0f;
            flipTargetWork = work;
        }

        private void UpdateFlip(float dt)
        {
            if (!flipping) return;
            flipElapsed += dt;
            float progress = Mathf.Clamp01(flipElapsed / FlipDuration);
            if (progress >= 0.5f && !swappedAtEdge)
            {
                ApplyPose(flipTargetWork);
                swappedAtEdge = true;
            }
            // Never rotate through the visible back face: +90 at the edge,
            // swap the card, then unfold from -90 to 0. No mirrored text/faces.
            float angle = progress < 0.5f
                ? Mathf.Lerp(0f, 90f, Mathf.SmoothStep(0f, 1f, progress * 2f))
                : Mathf.Lerp(-90f, 0f, Mathf.SmoothStep(0f, 1f, (progress - 0.5f) * 2f));
            flipRoot.localRotation = Quaternion.Euler(0f, angle, 0f);
            if (progress < 1f) return;
            flipRoot.localRotation = Quaternion.identity;
            flipping = false;
            FlipCount++;
            // At most one latest desired state is retained, never a growing queue.
            if (showingWork != desiredWork) RequestPose(desiredWork);
        }

        private void ApplyPose(bool work)
        {
            showingWork = work;
            Texture2D texture = work ? workTexture : idleTexture;
            if (cardMaterial != null && texture != null)
            {
                cardMaterial.SetTexture("_BaseMap", texture);
                cardMaterial.mainTexture = texture;
            }
        }

        private void UpdateSway(float dt)
        {
            float targetAngle;
            if (moving) targetAngle = Mathf.Sin(ElapsedDemoTime * Mathf.PI * 4f) * 2.05f;
            else if (interacting) targetAngle = 1.1f + Mathf.Sin(ElapsedDemoTime * Mathf.PI * 2.2f) * 1.15f;
            else targetAngle = Mathf.Sin(ElapsedDemoTime * Mathf.PI * 0.8f) * 0.28f;
            swayAngle = Mathf.Lerp(swayAngle, targetAngle, 1f - Mathf.Exp(-16f * dt));
            swayRoot.localRotation = Quaternion.Euler(0f, 0f, swayAngle);
        }

        /// <summary>0 = idle, 1 = flip current pose, 2 = walk around plant,
        /// 3 = work/sway, 4 = automatic sequence. Does not reset the public clock.</summary>
        public void SetDemoState(int state)
        {
            if (!built) BuildDemo();
            if (characterRoot == null) return;
            automatic = state == 4;
            StopWalking();
            interacting = false;
            switch (state)
            {
                case 1: RequestPose(!desiredWork); break;
                case 2: BeginOrbit(); break;
                case 3: interacting = true; RequestPose(true); break;
                case 4: autoPhase = 0; phaseElapsed = 0f; RequestPose(false); break;
                default: RequestPose(false); break;
            }
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        public void ResetDemo()
        {
            if (characterRoot == null) return;
            StopWalking();
            automatic = true;
            IsPaused = false;
            interacting = false;
            flipping = false;
            desiredWork = false;
            flipTargetWork = false;
            swappedAtEdge = false;
            flipElapsed = 0f;
            phaseElapsed = 0f;
            autoPhase = 0;
            swayAngle = 0f;
            FlipCount = 0;
            ElapsedDemoTime = 0f;
            characterRoot.position = homePosition;
            flipRoot.localRotation = Quaternion.identity;
            swayRoot.localRotation = Quaternion.identity;
            ApplyPose(false);
            cameraFocus = initialCameraFocus;
            cameraSize = 3.58f;
            lightAngle = 38f;
            shadowStrength = 0.62f;
            UpdateCamera();
            UpdateLight();
        }

        private void MoveToGroundPoint(Vector3 destination)
        {
            if (IsPaused) return;
            automatic = false;
            interacting = false;
            StopWalking();
            destination.x = Mathf.Clamp(destination.x, -2.3f, 2.3f);
            destination.z = Mathf.Clamp(destination.z, -1.78f, 1.78f);
            destination.y = FloorY;
            Vector3 center = new Vector3(plantPosition.x, FloorY, plantPosition.z);
            Vector3 fromCenter = destination - center;
            if (fromCenter.magnitude < 0.83f)
            {
                if (fromCenter.sqrMagnitude < 0.001f) fromCenter = characterRoot.position - center;
                destination = center + fromCenter.normalized * 0.83f;
            }
            Vector3 start = characterRoot.position;
            Vector3 delta = destination - start;
            float along = delta.sqrMagnitude < 0.001f ? 0f : Mathf.Clamp01(Vector3.Dot(center - start, delta) / delta.sqrMagnitude);
            float closestDistance = Vector3.Distance(start + delta * along, center);
            if (closestDistance < 0.78f)
            {
                Vector3 startDirection = (start - center).normalized;
                Vector3 endDirection = (destination - center).normalized;
                if (startDirection.sqrMagnitude < 0.1f) startDirection = Vector3.back;
                float arc = Vector3.SignedAngle(startDirection, endDirection, Vector3.up);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(arc) / 38f));
                walkPath.Add(center + startDirection * 1.04f);
                for (int i = 1; i <= steps; i++)
                    walkPath.Add(center + Quaternion.AngleAxis(arc * i / steps, Vector3.up) * startDirection * 1.04f);
            }
            walkPath.Add(destination);
            walkIndex = 0;
            moving = true;
            RequestPose(true);
            targetMarker.position = new Vector3(destination.x, 0.025f, destination.z);
            targetMarker.gameObject.SetActive(true);
        }

        private void UpdateLight()
        {
            if (keyLight == null) return;
            keyLight.transform.rotation = Quaternion.Euler(48f, lightAngle, 0f);
            keyLight.shadowStrength = shadowStrength;
        }

        private void UpdateCamera()
        {
            if (DemoCamera == null) return;
            // These angles are never changed by input; only focus and size change.
            DemoCamera.transform.SetPositionAndRotation(cameraFocus - cameraRotation * Vector3.forward * 15f, cameraRotation);
            DemoCamera.orthographicSize = cameraSize;
        }

        private void HandleScenePointer(Event evt, Vector2 mousePixels, Rect panelPixels)
        {
            if (DemoCamera == null) return;
            bool overPanel = panelPixels.Contains(mousePixels);
            if (evt.type == EventType.MouseUp && evt.button == 1) panning = false;
            if (evt.type == EventType.MouseDown && evt.button == 1 && !overPanel)
            {
                panning = true;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 1 && panning)
            {
                Vector3 right = cameraRotation * Vector3.right;
                Vector3 groundUp = Vector3.ProjectOnPlane(cameraRotation * Vector3.up, Vector3.up).normalized;
                float unitsPerPixel = cameraSize * 2f / Mathf.Max(1, Screen.height);
                cameraFocus += (-right * evt.delta.x + groundUp * evt.delta.y) * unitsPerPixel;
                Vector3 offset = cameraFocus - initialCameraFocus;
                offset.y = 0f;
                offset = Vector3.ClampMagnitude(offset, 3f);
                cameraFocus = initialCameraFocus + offset;
                UpdateCamera();
                evt.Use();
            }
            else if (evt.type == EventType.ScrollWheel && !overPanel)
            {
                cameraSize = Mathf.Clamp(cameraSize * Mathf.Pow(1.075f, evt.delta.y), 2.6f, 6.2f);
                UpdateCamera();
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 0 && !overPanel && !IsPaused)
            {
                Ray ray = DemoCamera.ScreenPointToRay(new Vector3(mousePixels.x, Screen.height - mousePixels.y, 0f));
                Plane floorPlane = new Plane(Vector3.up, new Vector3(0f, FloorY, 0f));
                float distance;
                if (floorPlane.Raycast(ray, out distance))
                {
                    Vector3 hit = ray.GetPoint(distance);
                    if (Mathf.Abs(hit.x) <= 2.62f && Mathf.Abs(hit.z) <= 2.08f)
                    {
                        MoveToGroundPoint(hit);
                        evt.Use();
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (characterRoot == null) return;
            EnsureGUIStyles();
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 800f), 0.65f, 1.4f);
            Rect panel = new Rect(22f, 22f, 306f, 591f);
            Vector2 mousePixels = Event.current.mousePosition;
            Rect panelPixels = new Rect(panel.x * scale, panel.y * scale, panel.width * scale, panel.height * scale);
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.Box(panel, GUIContent.none, panelStyle);
            float x = panel.x + 22f;
            float y = panel.y + 20f;
            GUI.Label(new Rect(x, y, 260f, 35f), "Paper Shop", titleStyle);
            GUI.Label(new Rect(x, y + 40f, 260f, 25f), "纸片角色 × 立体空间", subtitleStyle);
            GUI.Label(new Rect(x, y + 72f, 264f, 22f), "01 / 视觉表现小样例", smallStyle);
            GUI.Box(new Rect(x, y + 108f, 262f, 41f), CurrentState, badgeStyle);
            string poseInfo = texturesAvailable ? (showingWork ? "当前卡面：Work / 交互姿势" : "当前卡面：Idle / 待机姿势") : "缺少角色贴图，请检查 Resources/CardTextures";
            GUI.Label(new Rect(x, y + 160f, 264f, 40f), poseInfo, bodyStyle);
            float buttonY = y + 205f;
            if (GUI.Button(new Rect(x, buttonY, 127f, 36f), automatic ? "自动演示 · 开" : "自动演示 · 关", automatic ? selectedButtonStyle : buttonStyle))
            {
                if (automatic) automatic = false;
                else SetDemoState(4);
            }
            if (GUI.Button(new Rect(x + 135f, buttonY, 127f, 36f), "翻面", buttonStyle)) SetDemoState(1);
            if (GUI.Button(new Rect(x, buttonY + 44f, 127f, 36f), "移动", buttonStyle)) SetDemoState(2);
            if (GUI.Button(new Rect(x + 135f, buttonY + 44f, 127f, 36f), "交互", buttonStyle)) SetDemoState(3);
            if (GUI.Button(new Rect(x, buttonY + 88f, 127f, 36f), IsPaused ? "继续" : "暂停", buttonStyle)) SetPaused(!IsPaused);
            if (GUI.Button(new Rect(x + 135f, buttonY + 88f, 127f, 36f), "复位", buttonStyle)) ResetDemo();
            GUI.DrawTexture(new Rect(x, y + 349f, 262f, 1f), lineTexture);
            GUI.Label(new Rect(x, y + 365f, 180f, 23f), "主光方向", bodyStyle);
            GUI.Label(new Rect(x + 185f, y + 365f, 77f, 23f), Mathf.RoundToInt(lightAngle) + "°", valueStyle);
            float nextAngle = GUI.HorizontalSlider(new Rect(x, y + 395f, 262f, 18f), lightAngle, 0f, 360f);
            GUI.Label(new Rect(x, y + 423f, 180f, 23f), "投影强度", bodyStyle);
            GUI.Label(new Rect(x + 185f, y + 423f, 77f, 23f), Mathf.RoundToInt(shadowStrength * 100f) + "%", valueStyle);
            float nextShadow = GUI.HorizontalSlider(new Rect(x, y + 453f, 262f, 18f), shadowStrength, 0f, 1f);
            if (!Mathf.Approximately(nextAngle, lightAngle) || !Mathf.Approximately(nextShadow, shadowStrength))
            {
                lightAngle = nextAngle;
                shadowStrength = nextShadow;
                UpdateLight();
            }
            GUI.Label(new Rect(x, y + 486f, 262f, 39f), "正交镜头 45° / 35.264°\n透明裁切 · 双面受光 · 真实空间遮挡", smallStyle);
            GUI.Label(new Rect(x, y + 538f, 180f, 22f), "已运行 " + ElapsedDemoTime.ToString("0.0") + " 秒", smallStyle);
            GUI.Label(new Rect(x + 175f, y + 538f, 87f, 22f), "翻面 " + FlipCount + " 次", valueStyle);
            float logicalWidth = Screen.width / scale;
            float logicalHeight = Screen.height / scale;
            GUI.Label(new Rect(365f, 39f, 620f, 34f), "同一张纸片，也有空间感。", sceneTitleStyle);
            GUI.Label(new Rect(367f, 77f, 590f, 44f), "观察角色走到盆栽前后时的遮挡，以及光照与轮廓投影。", smallStyle);
            GUI.Label(new Rect(logicalWidth - 325f, logicalHeight - 66f, 300f, 44f), "左键地面：移动   /   右键拖拽：平移\n滚轮：缩放   /   镜头角度保持固定", smallStyle);
            GUI.matrix = oldMatrix;
            HandleScenePointer(Event.current, mousePixels, panelPixels);
        }

        private void EnsureGUIStyles()
        {
            if (panelStyle != null) return;
            try
            {
                uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "微软雅黑", "Noto Sans CJK SC", "SimHei", "Arial Unicode MS" }, 18);
            }
            catch (Exception)
            {
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Color ink = new Color(0.21f, 0.26f, 0.30f);
            Color muted = new Color(0.47f, 0.51f, 0.54f);
            Texture2D white = RoundTexture(new Color(0.995f, 0.992f, 0.975f, 0.97f));
            Texture2D soft = RoundTexture(new Color(0.94f, 0.925f, 0.91f));
            Texture2D hover = RoundTexture(new Color(0.90f, 0.87f, 0.84f));
            Texture2D pink = RoundTexture(new Color(0.79f, 0.46f, 0.56f));
            Texture2D palePink = RoundTexture(new Color(0.97f, 0.90f, 0.91f));
            panelStyle = new GUIStyle(GUI.skin.box) { border = new RectOffset(13, 13, 13, 13) };
            panelStyle.normal.background = white;
            titleStyle = TextStyle(29, ink, FontStyle.Bold);
            subtitleStyle = TextStyle(16, new Color(0.64f, 0.39f, 0.46f));
            bodyStyle = TextStyle(14, ink);
            bodyStyle.wordWrap = true;
            smallStyle = TextStyle(12, muted);
            smallStyle.wordWrap = true;
            sceneTitleStyle = TextStyle(24, ink, FontStyle.Bold);
            valueStyle = TextStyle(12, muted);
            valueStyle.alignment = TextAnchor.UpperRight;
            badgeStyle = TextStyle(14, new Color(0.61f, 0.34f, 0.41f), FontStyle.Bold);
            badgeStyle.alignment = TextAnchor.MiddleCenter;
            badgeStyle.normal.background = palePink;
            badgeStyle.border = new RectOffset(13, 13, 13, 13);
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = uiFont,
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(13, 13, 13, 13),
                padding = new RectOffset(4, 4, 3, 3)
            };
            buttonStyle.normal.background = soft;
            buttonStyle.hover.background = hover;
            buttonStyle.active.background = palePink;
            buttonStyle.focused.background = palePink;
            buttonStyle.normal.textColor = ink;
            buttonStyle.hover.textColor = ink;
            buttonStyle.active.textColor = ink;
            buttonStyle.focused.textColor = ink;
            selectedButtonStyle = new GUIStyle(buttonStyle);
            selectedButtonStyle.normal.background = pink;
            selectedButtonStyle.normal.textColor = Color.white;
            selectedButtonStyle.hover.background = pink;
            selectedButtonStyle.hover.textColor = Color.white;
            lineTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "UI separator" };
            lineTexture.SetPixel(0, 0, new Color(0.86f, 0.85f, 0.82f));
            lineTexture.Apply();
            ownedUITextures.Add(lineTexture);
        }

        private GUIStyle TextStyle(int size, Color color, FontStyle style = FontStyle.Normal)
        {
            GUIStyle result = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = size, fontStyle = style, padding = new RectOffset(0, 0, 0, 0) };
            result.normal.textColor = color;
            return result;
        }

        private Texture2D RoundTexture(Color color)
        {
            const int size = 32;
            const float radius = 12f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "UI rounded panel", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius), 0f);
                float dy = Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius), 0f);
                Color pixel = color;
                pixel.a *= Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = pixel;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            ownedUITextures.Add(texture);
            return texture;
        }

        /// <summary>Captures the rendered frame including IMGUI after end-of-frame.</summary>
        public void CapturePreview(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A screenshot path is required.", nameof(path));
            StartCoroutine(CaptureAtEndOfFrame(path));
        }

        private IEnumerator CaptureAtEndOfFrame(string path)
        {
            yield return new WaitForEndOfFrame();
            Texture2D screenshot = null;
            try
            {
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                string absolutePath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
                Debug.Log("Paper Shop preview saved: " + absolutePath, this);
            }
            catch (Exception exception)
            {
                Debug.LogError("Paper Shop screenshot failed: " + exception.Message, this);
            }
            finally
            {
                if (screenshot != null) Destroy(screenshot);
            }
        }

        private void OnDestroy()
        {
            foreach (Material material in ownedMaterials) if (material != null) Destroy(material);
            foreach (Texture2D texture in ownedUITextures) if (texture != null) Destroy(texture);
            if (cardMesh != null) Destroy(cardMesh);
            if (uiFont != null) Destroy(uiFont);
        }
    }
}
