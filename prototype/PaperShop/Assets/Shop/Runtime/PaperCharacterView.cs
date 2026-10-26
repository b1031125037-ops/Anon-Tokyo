using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace AnonTokyo.Shop
{
    /// <summary>
    /// Visual-only paper actor. The owner moves this transform and supplies Tick's
    /// clock; pausing the owner therefore freezes flips and sway without side effects.
    /// </summary>
    public sealed partial class PaperCharacterView : MonoBehaviour
    {
        public bool IsWorking { get { return working; } }
        public bool IsMoving=>moving;
        public string CharacterId {get;private set;}="";
        public bool IsCelebrating=>celebrationUntil>Time.unscaledTime;
        public string CurrentPose=>IsCelebrating?"celebrate":poseOverride;
        float celebrationUntil=-1;
        public bool HasPose(string pose)=>poses.ContainsKey(pose);
        public bool Celebrate(float seconds=1.8f)
        {
            if(!HasCharacterArt||IsCelebrating)return false;
            celebrationUntil=Time.unscaledTime+Mathf.Max(.3f,seconds);
            BeginVisualFlip();return true;
        }
        void BeginVisualFlip(){flipping=true;flipTime=0;swapped=false;flipTarget=working||moving||restocking;}
        void Update()
        {
            if(celebrationUntil>=0&&Time.unscaledTime>=celebrationUntil)
            {celebrationUntil=-1;BeginVisualFlip();}
        }
        public Texture2D DisplayedCard=>material!=null?material.GetTexture("_BaseMap") as Texture2D:null;
        public bool HasCharacterArt {get;private set;}
        string poseOverride="",appliedPose="";
        readonly Dictionary<string,Texture2D> poses=new Dictionary<string,Texture2D>();
        public void SetCharacter(string id)
        {
            id=id??"";if(CharacterId==id)return;
            CharacterId=id;poses.Clear();celebrationUntil=-1;
            foreach(string pose in new[]{"idle","walk","cashier","welcome","think","restock","interact","pay","carry","talk","perform","celebrate"})
            {var t=Resources.Load<Texture2D>("Characters/"+id+"/"+pose);if(t!=null)poses[pose]=t;}
            HasCharacterArt=poses.ContainsKey("idle");
            if(HasCharacterArt)SetTint(Color.white);
            SetTexture(visibleWork);
        }
        public void SetPose(string pose)
        {
            if(poseOverride==pose)return;
            poseOverride=pose;
            if(IsCelebrating)return;
            if(!flipping||swapped){flipping=true;flipTime=0;swapped=false;flipTarget=working||moving||restocking;}
        }
        public void SetTint(Color tint) { if(material!=null)material.SetColor("_BaseColor",tint); }

        private const float Height = 1.55f;
        private const float FlipSeconds = 0.22f;
        private Transform facingPivot;
        private Camera facingCamera;
        public Vector3 FacingDirection=>facingPivot!=null?facingPivot.forward:transform.forward;
        void LateUpdate()
        {
            if(facingCamera!=null&&facingPivot!=null)
            {
                // Stand on the floor: pitching the entire card toward an overhead
                // camera pushes its head through door frames and adjacent walls.
                var direction=-facingCamera.transform.forward;direction.y=0;
                if(direction.sqrMagnitude>.0001f)facingPivot.rotation=Quaternion.LookRotation(direction.normalized,Vector3.up);
            }
        }
        private Transform flipPivot;
        private Transform swayPivot;
        private Mesh mesh;
        private Material material;
        private Texture2D idle;
        private Texture2D work;
        private Texture2D restock;
        private bool restocking;
        private bool initialized;
        private bool moving;
        private bool working;
        private bool desiredWork;
        private bool visibleWork;
        private bool flipping;
        private bool swapped;
        private bool flipTarget;
        private float clock;
        private float flipTime;
        private float sway;
        public float SwayMultiplier {get;set;}=1f;

        public void Initialize(Camera camera, Shader litShader)
        {
            if (camera == null || litShader == null)
            {
                Debug.LogError("PaperCharacterView requires an injected camera and URP Lit shader.", this);
                return;
            }
            if (!initialized)
            {
                idle = Resources.Load<Texture2D>("CardTextures/idle");
                work = Resources.Load<Texture2D>("CardTextures/work");
                restock = Resources.Load<Texture2D>("CardTextures/restock");
                facingPivot = Group("Camera-facing yaw", transform);
                flipPivot = Group("Pose flip at feet", facingPivot);
                swayPivot = Group("Visual sway at feet", flipPivot);
                GameObject card = new GameObject("Lit alpha-cut paper card");
                card.transform.SetParent(swayPivot, false);

                // Shared texture anchor: (256,736) from top-left on a 512x768
                // canvas. The logical transform's origin stays exactly at the feet.
                float width = Height * 512f / 768f;
                float bottom = -Height * 32f / 768f;
                mesh = new Mesh { name = "Shop paper card - foot pivot" };
                mesh.vertices = new[]
                {
                    new Vector3(-width * 0.5f, bottom, 0f),
                    new Vector3(width * 0.5f, bottom, 0f),
                    new Vector3(-width * 0.5f, bottom + Height, 0f),
                    new Vector3(width * 0.5f, bottom + Height, 0f)
                };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
                mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
                mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
                mesh.RecalculateBounds();
                card.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = card.AddComponent<MeshRenderer>();
                material = new Material(litShader) { name = "Shop paper - clipped lit double-sided" };
                material.SetColor("_BaseColor", Color.white);
                Set("_Surface", 0f);
                Set("_AlphaClip", 1f);
                Set("_Cutoff", 0.38f);
                Set("_Cull", (float)CullMode.Off);
                Set("_ZWrite", 1f);
                Set("_SrcBlend", (float)BlendMode.One);
                Set("_DstBlend", (float)BlendMode.Zero);
                Set("_AlphaToMask", 1f);
                Set("_ReceiveShadows", 1f);
                Set("_Metallic", 0f);
                Set("_Smoothness", 0.12f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.doubleSidedGI = true;
                material.SetShaderPassEnabled("ShadowCaster", true);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                renderer.receiveShadows = true;
                renderer.enabled = idle != null && work != null;
                if (!renderer.enabled)
                    Debug.LogError("PaperCharacterView: missing Resources/CardTextures/idle or work.", this);
                SetTexture(false);
                initialized = true;
            }
            facingCamera=camera;
            LateUpdate();
        }

        public void SetMoving(bool value)
        {
            moving = value;
            if(string.IsNullOrEmpty(poseOverride))RequestPose(moving || working || restocking);
        }

        public void SetWorking(bool value)
        {
            working = value;
            if(string.IsNullOrEmpty(poseOverride))RequestPose(moving || working || restocking);
        }

        public void SetRestocking(bool value)
        {
            if(restocking==value)return;
            restocking=value;
            if(!string.IsNullOrEmpty(poseOverride))return;
            if(!flipping){flipping=true;flipTime=0;swapped=false;flipTarget=moving||working||restocking;}
            else if(!swapped)flipTarget=moving||working||restocking;
        }

        public void Tick(float dt)
        {
            if (!initialized || dt <= 0f) return;
            clock += dt;
            if (flipping)
            {
                flipTime += dt;
                float t = Mathf.Clamp01(flipTime / FlipSeconds);
                if (t >= 0.5f && !swapped)
                {
                    SetTexture(flipTarget);
                    swapped = true;
                }
                float angle = t < 0.5f
                    ? Mathf.Lerp(0f, 90f, Mathf.SmoothStep(0f, 1f, t * 2f))
                    : Mathf.Lerp(-90f, 0f, Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f));
                flipPivot.localRotation = Quaternion.Euler(0f, angle, 0f);
                if (t >= 1f)
                {
                    flipping = false;
                    flipPivot.localRotation = Quaternion.identity;
                    if (string.IsNullOrEmpty(poseOverride)&&visibleWork != desiredWork) RequestPose(desiredWork);
                    else if(!string.IsNullOrEmpty(CurrentPose)&&appliedPose!=CurrentPose){flipping=true;flipTime=0;swapped=false;}
                }
            }
            float target = moving
                ? Mathf.Sin(clock * Mathf.PI * 4f) * 2.05f
                : working ? 1.1f + Mathf.Sin(clock * Mathf.PI * 2.2f) * 1.15f
                : Mathf.Sin(clock * Mathf.PI * 0.8f) * 0.28f;
            sway = Mathf.Lerp(sway, target * SwayMultiplier, 1f - Mathf.Exp(-16f * dt));
            swayPivot.localRotation = Quaternion.Euler(0f, 0f, sway);
            if(IsCelebrating)swayPivot.localRotation=Quaternion.Euler(0,0,Mathf.Sin(clock*11)*4f);
        }

        private void RequestPose(bool value)
        {
            desiredWork = value;
            if (flipping)
            {
                if (!swapped) flipTarget = value;
                return;
            }
            if (visibleWork == value) return;
            flipping = true;
            flipTime = 0f;
            swapped = false;
            flipTarget = value;
        }

        private void SetTexture(bool value)
        {
            visibleWork = value;
            Texture2D texture = restocking ? (restock!=null?restock:work) : value ? work : idle;
            if(HasCharacterArt)
            {
                string key=string.IsNullOrEmpty(CurrentPose)?(value?"interact":"idle"):CurrentPose;
                if(!poses.TryGetValue(key,out texture))
                {string fallback=key=="celebrate"?"welcome":key=="perform"?"interact":"idle";if(!poses.TryGetValue(fallback,out texture))texture=poses["idle"];}
            }
            appliedPose=CurrentPose;
            if (material != null && texture != null) material.SetTexture("_BaseMap", texture);
        }

        private void Set(string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static Transform Group(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
        }
    }
}
