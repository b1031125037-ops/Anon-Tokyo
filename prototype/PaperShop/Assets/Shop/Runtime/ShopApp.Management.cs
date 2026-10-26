using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        bool quitPrompt,allowQuit,anyButtonHovered;
        string hoveredButton="",hoverCandidate="",quitError="";
        int productCategory=-1;
        Vector2Int? lastPreviewCell;
        AudioSource effects;
        AudioSource registerEffects,clothEffects;
        readonly List<AudioClip> generatedEffects=new List<AudioClip>();
        readonly Dictionary<string,AudioClip> effectClips=new Dictionary<string,AudioClip>();
        public bool QuitPromptVisible=>quitPrompt;
        public bool WorldTextHidden=>hideWorldText;
        public bool RenovationActive=>renovationMode;

        void InitializeSoundEffects()
        {
            effects=gameObject.AddComponent<AudioSource>();
            effects.playOnAwake=false;effects.spatialBlend=0;effects.ignoreListenerPause=true;
            registerEffects=gameObject.AddComponent<AudioSource>();clothEffects=gameObject.AddComponent<AudioSource>();
            foreach(var source in new[]{registerEffects,clothEffects})
            {source.playOnAwake=false;source.spatialBlend=0;source.ignoreListenerPause=true;}
            foreach(string key in ShopSoundPalette.Keys)
            {
                string resource=key=="income"||key=="payment"?"register":key=="stock"||key=="pickup"?"clothes":null;
                var supplied=resource==null?null:Resources.Load<AudioClip>("Audio/Effects/"+resource);
                if(supplied!=null){effectClips[key]=supplied;continue;}
                var data=ShopSoundPalette.Samples(key);
                var clip=AudioClip.Create("Shop cartoon "+key,data.Length,1,44100,false);
                clip.SetData(data,0);effectClips[key]=clip;generatedEffects.Add(clip);
            }
        }
        readonly Dictionary<string,float> soundTimes=new Dictionary<string,float>();
        int pendingClickFrame=-1;
        public string LastPlayedEffect {get;private set;}="";
        public int HoverSoundPlayCount {get;private set;}
        void QueueClickSound(){pendingClickFrame=Time.frameCount;}
        void LateUpdate()
        {
            if(UserSettings!=null){if(registerEffects!=null)registerEffects.volume=UserSettings.effectsVolume;if(clothEffects!=null)clothEffects.volume=UserSettings.effectsVolume;}
            if(pendingClickFrame>=0&&Time.frameCount>pendingClickFrame)
            {pendingClickFrame=-1;PlaySfx("click");}
            TickBusinessPresentation();
            TickResourceFeedback();
        }
        void PlaySfx(string key)
        {
            if(key!="hover"&&key!="move"&&key!="click")pendingClickFrame=-1;
            if(effects==null||UserSettings==null||!effectClips.TryGetValue(key,out var clip))return;
            float interval=key=="hover"?.09f:key=="move"?.08f:key=="payment"||key=="income"||key=="stock"||key=="pickup"?.2f:.045f;
            if(soundTimes.TryGetValue(key,out float last)&&Time.unscaledTime-last<interval)return;
            soundTimes[key]=Time.unscaledTime;LastPlayedEffect=key;
            if(key=="hover")HoverSoundPlayCount++;
            // A successful receipt has one register sound. Dedicated channels prevent
            // concurrent customers/workers from stacking copies into a louder burst.
            var foley=key=="income"||key=="payment"?registerEffects:key=="stock"||key=="pickup"?clothEffects:null;
            if(foley!=null){foley.clip=clip;foley.volume=UserSettings.effectsVolume;foley.Play();return;}
            float gain=key=="hover"?.32f:key=="move"?.45f:key=="pickup"||key=="stock"?.7f:1f;
            effects.PlayOneShot(clip,UserSettings.effectsVolume*gain);
        }
        void ButtonSound(Rect rect,string text)
        {
            if(!GUI.enabled||Event.current.type!=EventType.Repaint||!rect.Contains(Event.current.mousePosition))return;
            anyButtonHovered=true;
            // Resolve the last drawn target once per repaint. Overlapping controls
            // must not alternate sounds during a single frame; labels may change.
            var point=GUIUtility.GUIToScreenPoint(rect.position);
            hoverCandidate=Mathf.RoundToInt(point.x)+":"+Mathf.RoundToInt(point.y)+":"+panelKey;
        }
        void CommitButtonHover(string candidate)
        {
            if(candidate==hoveredButton)return;
            hoveredButton=candidate;
            if(candidate!="")PlaySfx("hover");
        }
        void EndButtonHover(){if(Event.current.type==EventType.Repaint)CommitButtonHover(anyButtonHovered?hoverCandidate:"");}
        void PreviewPlacementSound(PlacedFacility item)
        {
            var cell=new Vector2Int(item.x,item.definitionId=="warehouse"?item.wallPosition:item.z);
            if(lastPreviewCell.HasValue&&lastPreviewCell.Value!=cell)PlaySfx("move");
            lastPreviewCell=cell;
        }
        void DisposeSoundEffects(){foreach(var clip in generatedEffects)Destroy(clip);generatedEffects.Clear();effectClips.Clear();}
        void TickCameraKeys()
        {
            if(quitPrompt||TutorialChoicePending||panelKey=="settings"||!Application.isFocused||isMenuVerification||verifyMode)return;
            if(Input.GetMouseButton(0)||Input.GetMouseButton(2))orbitHeldFrame=Time.frameCount;
            else if(Time.frameCount>orbitHeldFrame+1)panGesture=false;
            if(panGesture)return;
            Vector2 delta=Vector2.zero;
            if(Input.GetKey(KeyCode.LeftArrow))delta.x+=1;
            if(Input.GetKey(KeyCode.RightArrow))delta.x-=1;
            if(Input.GetKey(KeyCode.UpArrow))delta.y+=1;
            if(Input.GetKey(KeyCode.DownArrow))delta.y-=1;
            if(delta!=Vector2.zero)World.Pan(delta*Time.unscaledDeltaTime*420);
        }

        void OpenSelectedInformation()
        {
            if(renovationMode)return;
            var f=Selected();if(f==null)return;
            var kind=catalog.Find(f.definitionId).kind;
            if(ShopEconomy.IsStaffFacility(kind))OpenStaffPost(f.instanceId);
            else if(kind==FacilityKind.Shelf)panelKey="shelf";
            else if(kind==FacilityKind.Warehouse)panelKey="warehouse";
            else if(kind==FacilityKind.Stage)OpenPerformance();
            else panelKey="object";
            compactScroll=Vector2.zero;
        }
        void DrawManagementHUD(float width)
        {
            Rect titleBox=new Rect(24,20,316,72),businessBox=new Rect(350,20,112,72);
            var titleArt=Ui("V2/components/hud-title");uiRegions.Add(titleBox);uiRegions.Add(businessBox);
            if(titleArt!=null)GUI.DrawTexture(titleBox,titleArt);
            if(ArtButton(titleBox,GUIContent.none,GUIStyle.none))OpenPanel("bonuses");
            GUI.Label(new Rect(42,29,280,30),"ANON TOKYO   Lv."+Economy.ShopLevel,heading);
            GUI.Label(new Rect(42,62,280,22),"爱音的服装店 · 点击升级店铺",small);
            DrawBusinessSign(businessBox);
            var resources=new Rect(490,20,Mathf.Min(560,width-744),66);uiRegions.Add(resources);
            var resourceArt=Ui("V2/components/hud-resources");if(resourceArt!=null)GUI.DrawTexture(resources,resourceArt);else PanelBox(resources);
            Economy.RefreshDecoration(Layout);
            string[] icons={"currency","decoration","popularity"};
            string[] labels={"金币 "+(Economy.testMode?"∞":Economy.cash.ToString()),"装饰 "+Economy.currentDecorationValue,"人气 "+Economy.popularity+"/"+Economy.PopularityCap};
            for(int i=0;i<3;i++)
            {
                float sx=resources.x+12+i*resources.width/3;var icon=i==1?Resources.Load<Texture2D>("UI/World/decoration")??WorldIcon("status-welcome"):Ui("icons/"+icons[i]);
                DrawResourceIcon(i,new Rect(sx,36,28,28),icon);
                GUI.Label(new Rect(sx+33,40,resources.width/3-42,28),labels[i],body);
            }
            DrawResourceFeedback(resources);DrawWarehouseBadge();
            if(Time.unscaledTime<lastDecorationPayoutUntil)GUI.Label(new Rect(resources.x+14,89,240,24),"装饰奖励 +"+lastDecorationPayout+" 金币",small);
            var toggle=new Rect(width-242,108,218,36);uiRegions.Add(toggle);
            if(ArtButton(toggle,hideWorldText?"显示场景文字":"隐藏场景文字",button))
            {hideWorldText=!hideWorldText;World.PresentationLabelsEnabled=!hideWorldText;}
        }
        string PostEffect(StaffPostState post,int nextLevel=0)
        {
            if(nextLevel==0&&string.IsNullOrEmpty(Economy.EffectiveStaffId(post)))return "未上岗";
            int level=nextLevel>0?nextLevel:post.level;
            if(post.role==FacilityKind.Restock)return "携带 "+(ShopEconomy.BaseCarryFor(post,Economy.EffectiveStaffId(post))+(level-1)*2)+" 件";
            if(post.role==FacilityKind.Reception){int bonus=Math.Min(35,Economy.WorkingAttribute(post)/4+5*(level-1));return "多拿 1 件机会 "+bonus+"% · 多买种类机会 "+Math.Round(ShopBalance.ExtraKindChance(bonus)*100)+"%";}
            return "售价 +"+Math.Min(8,Economy.WorkingAttribute(post)/25+(level-1)*2)+"% · 结账 "+Economy.CheckoutSeconds.ToString("0.0")+" 秒";
        }
        void DrawShopBonuses(Rect box)
        {
            DrawShopUpgrade(box);
        }

        bool HandleQuitRequest()
        {
            if(gameFailed&&!allowQuit)return ResolveFailedGame(false);
            if(allowQuit||verifyMode||isMenuVerification||Application.isEditor)return true;
            quitPrompt=true;quitError="";return false;
        }
        public void RequestWindowCloseForVerification(){quitPrompt=true;quitError="";}
        public void CancelQuit(){quitPrompt=false;quitError="";}
        public bool SaveBeforeExit()
        {
            if(!frontEnd&&!SaveTrackedSession(false)){quitError=LastMessage;return false;}
            SaveUserSettings();return true;
        }
        void ExitWithSave()
        {
            if(!SaveBeforeExit()){quitPrompt=true;return;}
            allowQuit=true;Application.Quit();
        }
        void DrawQuitPrompt(float width,float height)
        {
            if(!quitPrompt)return;
            GUI.color=new Color(0,0,0,.6f);GUI.DrawTexture(new Rect(0,0,width,height),Texture2D.whiteTexture);GUI.color=Color.white;
            Rect box=new Rect(width*.5f-260,height*.5f-120,520,240);PanelBox(box);
            GUI.Label(new Rect(box.x+25,box.y+23,470,36),"保存并退出？",heading);
            GUI.Label(new Rect(box.x+25,box.y+73,470,65),string.IsNullOrEmpty(quitError)?
                (frontEnd?"返回桌面。":"当前店铺进度将自动保存，下次可继续。"):quitError,body);
            if(ArtButton(new Rect(box.x+25,box.y+166,222,44),"继续游戏",button))CancelQuit();
            if(ArtButton(new Rect(box.x+263,box.y+166,232,44),frontEnd?"退出游戏":"保存并退出",activeButton))ExitWithSave();
        }
    }
}
