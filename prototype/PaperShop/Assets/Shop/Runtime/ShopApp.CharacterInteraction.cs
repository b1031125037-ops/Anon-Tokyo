using UnityEngine;
using System.Collections.Generic;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public bool TryCelebrateCharacter(Vector2 screen)
        {
            if(frontEnd||renovationMode||IsPaintingSurface||!string.IsNullOrEmpty(placingDefinition))return false;
            var ray=World.WorldCamera.ScreenPointToRay(screen);PaperCharacterView nearest=null;float distance=float.PositiveInfinity;
            foreach(var actor in World.GetComponentsInChildren<PaperCharacterView>())
            {
                if(ShopStaffProfiles.Find(actor.CharacterId)==null)continue;
                if(actor.HitCard(ray,out float hit)&&hit<distance){nearest=actor;distance=hit;}
            }
            if(nearest==null)return false;
            if(nearest.Celebrate())PlaySfx("select");
            profileId=nearest.CharacterId;panelKey="profile";compactScroll=Vector2.zero;
            return true;
        }
    }
    public sealed partial class PaperCharacterView
    {
        // Read a small alpha mask once per texture; transparent card corners are not clickable.
        static readonly Dictionary<Texture2D,bool[]> hitMasks=new Dictionary<Texture2D,bool[]>();
        const int MaskSize=96;
        public bool HitCard(Ray ray,out float distance)
        {
            distance=0;if(!initialized||!gameObject.activeInHierarchy||!HasCharacterArt)return false;
            var card=swayPivot.GetChild(0);var plane=new Plane(card.forward,card.position);
            if(!plane.Raycast(ray,out distance))return false;
            var p=card.InverseTransformPoint(ray.GetPoint(distance));float width=Height*512f/768f,bottom=-Height*32f/768f;
            float u=p.x/width+.5f,v=(p.y-bottom)/Height;
            if(u<0||u>=1||v<0||v>=1)return false;
            var texture=DisplayedCard;if(texture==null)return false;
            if(!hitMasks.TryGetValue(texture,out var mask))
            {
                var rt=RenderTexture.GetTemporary(MaskSize,MaskSize,0,RenderTextureFormat.ARGB32);
                var previous=RenderTexture.active;var sample=new Texture2D(MaskSize,MaskSize,TextureFormat.RGBA32,false);
                try{Graphics.Blit(texture,rt);RenderTexture.active=rt;sample.ReadPixels(new Rect(0,0,MaskSize,MaskSize),0,0);sample.Apply();
                    var pixels=sample.GetPixels32();mask=new bool[pixels.Length];for(int i=0;i<mask.Length;i++)mask[i]=pixels[i].a>=97;hitMasks[texture]=mask;
                }finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Destroy(sample);}
            }
            return mask[Mathf.Min(MaskSize-1,(int)(v*MaskSize))*MaskSize+Mathf.Min(MaskSize-1,(int)(u*MaskSize))];
        }
    }
}
