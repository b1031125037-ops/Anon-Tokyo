using System;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        Rect tutorialDynamicTarget;
        Texture2D attentionArrow,attentionArrowUp,attentionArrowLeft,attentionArrowRight;
        Texture2D tutorialRing;
        public Rect LastTutorialCircle {get;private set;}
        Rect TutorialObjectRect(PlacedFacility facility)
        {
            if(facility==null||!World.FacilityScreenBounds(facility.instanceId,out var rect))return new Rect();
            return new Rect(rect.x/uiScale,rect.y/uiScale,rect.width/uiScale,rect.height/uiScale);
        }
        void DrawTutorialRing(Rect target,bool worldObject)
        {
            if(tutorialRing==null)
            {
                tutorialRing=new Texture2D(256,256,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                for(int y=0;y<256;y++)for(int x=0;x<256;x++)
                {
                    float distance=Mathf.Abs(Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(128,128))-122);
                    tutorialRing.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(3.5f-distance)));
                }
                tutorialRing.Apply();textures.Add(tutorialRing);
            }
            var ring=new Rect(target.x-7,target.y-7,target.width+14,target.height+14);
            if(worldObject)
            {
                float diameter=Mathf.Sqrt(target.width*target.width+target.height*target.height)+18;
                ring=new Rect(target.center.x-diameter*.5f,target.center.y-diameter*.5f,diameter,diameter);
                LastTutorialCircle=ring;
            }
            var old=GUI.color;GUI.color=new Color(1,.76f,.26f,.75f+.2f*Mathf.Sin(Time.unscaledTime*4));
            GUI.DrawTexture(ring,tutorialRing);GUI.color=old;
        }
        string SecondTutorialProduct=>Economy.shelves.Find(s=>s.productId!=TutorialProduct)?.productId;
        void TickTutorialV2()
        {
            if(TutorialStep==23&&Economy.OwnedFixtureCount>=2)TutorialAdvance(24);
            else if(TutorialStep==25&&Economy.shelves.Exists(s=>s.productId!=TutorialProduct))
            {EndRenovation();TutorialAdvance(1);}
            else if(TutorialStep==26)
            {if(SecondTutorialProduct==null)TutorialAdvance(23);else if(Economy.Stock(SecondTutorialProduct).contracted)TutorialAdvance(27);}
            else if(TutorialStep==27&&SecondTutorialProduct!=null&&Economy.Stock(SecondTutorialProduct).purchased>0)TutorialAdvance(6);
            else if(TutorialStep==28&&Economy.claimedGrowthTaskIds.Count>0){panelKey="";TutorialAdvance(10);}
            else if(TutorialStep==29&&Economy.shopOpen)TutorialAdvance(17);
        }
        string TutorialExtraSpeech(int step)
        {
            switch(step)
            {
                case 23:return "三处岗位安排好了！到商店买一个不同种类的展柜吧，比如下装架。购买后会放进装修库存。";
                case 24:return "装修用来摆放和移动已有物品。先保持关店，点装修，从库存拿出刚买的展柜，再点击空地摆好；R 可以旋转。";
                case 25:return string.IsNullOrEmpty(placingDefinition)?"点装修的展柜库存，选择新展柜的摆放按钮。新展柜和原来的上装架会卖不同商品哦！":"把展柜放到空地上，给正面的顾客留一格通路。左键确认，R 旋转；放好后我们去进货。";
                case 26:return "上装已经订好了。再签约新展柜对应的商品，我们就有两种选择啦！";
                case 27:return "再为第二种商品订一批。别忘了看看成本，剩下的金币可以留作周转。";
                case 28:return "第一轮开店完成啦！点任务，领取已经完成的奖励，继续经营就更有底气了。";
                case 29:return IsEditingLayout?"装修完成后记得点结束装修，再重新开店迎客哦！":"新空间准备好啦！点标题旁的开店按钮，让客人进来看看吧。";
                default:return null;
            }
        }
        void DrawAttentionArrow(Rect target)
        {
            if(target.width<=0)return;
            if(attentionArrow==null)
            {
                attentionArrow=new Texture2D(40,56,TextureFormat.RGBA32,false);attentionArrow.filterMode=FilterMode.Bilinear;
                for(int y=0;y<56;y++)for(int x=0;x<40;x++)
                {float dx=Mathf.Abs(x-19.5f);bool shape=y<26?dx<y*.76f:dx<8;bool inner=y<26?y>4&&dx<(y-4)*.76f:y<53&&dx<5;
                    attentionArrow.SetPixel(x,y,!shape?Color.clear:inner?new Color(1,.82f,.25f):new Color(.32f,.19f,.11f));}
                attentionArrow.Apply();textures.Add(attentionArrow);
                attentionArrowUp=new Texture2D(40,56,TextureFormat.RGBA32,false);attentionArrowUp.filterMode=FilterMode.Bilinear;
                for(int y=0;y<56;y++)for(int x=0;x<40;x++)attentionArrowUp.SetPixel(x,y,attentionArrow.GetPixel(x,55-y));
                attentionArrowUp.Apply();textures.Add(attentionArrowUp);
                attentionArrowLeft=new Texture2D(56,40,TextureFormat.RGBA32,false);attentionArrowRight=new Texture2D(56,40,TextureFormat.RGBA32,false);
                for(int y=0;y<40;y++)for(int x=0;x<56;x++){attentionArrowLeft.SetPixel(x,y,attentionArrow.GetPixel(y,x));attentionArrowRight.SetPixel(x,y,attentionArrow.GetPixel(y,55-x));}
                attentionArrowLeft.Apply();attentionArrowRight.Apply();textures.Add(attentionArrowLeft);textures.Add(attentionArrowRight);
            }
            float bounce=4*Mathf.Sin(Time.unscaledTime*4);
            if(target.y>155&&(target.width>100&&!string.IsNullOrEmpty(panelKey)||renovationMode&&string.IsNullOrEmpty(placingDefinition)&&tutorialDynamicTarget.width>0))
            {
                bool fromLeft=target.xMax+68>Screen.width/uiScale-8;
                float x=fromLeft?target.x-64-bounce:Mathf.Max(target.xMax+8,Screen.width/uiScale*.5f+316)+bounce;
                GUI.DrawTexture(new Rect(x,target.center.y-20,56,40),fromLeft?attentionArrowRight:attentionArrowLeft);return;
            }
            var arrow=new Rect(target.center.x-20,target.y-60+bounce,40,56);
            if(arrow.y<100){arrow.y=target.yMax+5-bounce;GUI.DrawTexture(arrow,attentionArrowUp);}
            else GUI.DrawTexture(arrow,attentionArrow);
        }
        void DrawClosedReminder(float width,float height)
        {
            if(!CanRenovate||IsEditingLayout||panelKey!=""||TutorialVisible||TutorialChoicePending||gameFailed)return;
            var sign=new Rect(350,20,112,72);DrawAttentionArrow(sign);
            var hint=new Rect(332,164,186,36);uiRegions.Add(hint);
            if(ArtButton(hint,"已关店 · 点击开店",activeButton))SetShopOpen(true);
        }
        void RegisterTutorialTarget(Rect target,Rect viewport)
        {if(target.y>=viewport.y&&target.yMax<=viewport.yMax)tutorialDynamicTarget=target;}
    }
}
