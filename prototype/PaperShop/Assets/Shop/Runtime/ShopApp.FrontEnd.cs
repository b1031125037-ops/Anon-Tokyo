using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        bool frontEnd,loadingScene,isMenuVerification;
        string frontPage="home",frontMessage="";
        int pendingDeleteSlot;
        public int PendingDeleteSlot=>pendingDeleteSlot;
        public bool RequestDeleteSlot(int slot)
        {
            if(!frontEnd||loadingScene||slot<1||slot>4||!ShopProfiles.Exists(slot))return false;
            pendingDeleteSlot=slot;return true;
        }
        public void CancelDeleteSlot(){pendingDeleteSlot=0;}
        public bool ConfirmDeleteSlot()
        {
            if(!frontEnd||loadingScene||pendingDeleteSlot==0)return false;
            if(!ShopProfiles.Delete(pendingDeleteSlot,out frontMessage))return false;
            pendingDeleteSlot=0;OpenSaveSlots();PlaySfx("select");return true;
        }
        readonly ShopSession[] slotPreviews=new ShopSession[4];
        readonly string[] slotErrors=new string[4];
        public bool IsFrontEnd=>frontEnd;
        public string FrontPage=>frontPage;
        public bool OpenSaveSlots()
        {
            if(!frontEnd||loadingScene)return false;
            frontPage="slots";frontMessage="";
            pendingDeleteSlot=0;
            for(int i=0;i<4;i++)slotPreviews[i]=ShopProfiles.Exists(i+1)?ShopProfiles.Read(i+1,catalog,out slotErrors[i]):null;
            return true;
        }
        public void OpenTitleSettings(){if(frontEnd)frontPage="settings";}
        public bool StartSlot(int slot)
        {
            if(!frontEnd||loadingScene||pendingDeleteSlot!=0||slot<1||slot>4)return false;
            if(ShopProfiles.Exists(slot)&&ShopProfiles.Read(slot,catalog,out frontMessage)==null)return false;
            ShopProfiles.SelectedSlot=slot;ShopProfiles.StartRequested=true;
            loadingScene=true;StartCoroutine(ReloadShopScene());return true;
        }
        public bool ReturnToTitle()
        {
            if(frontEnd||loadingScene)return false;
            if(!SaveTrackedSession(false)){LastMessage="返回前保存失败："+LastMessage;return false;}
            // The saved departure is the end of this play session; title time is offline time.
            SaveUserSettings();offlineTrackingReady=false;ShopProfiles.StartRequested=false;
            loadingScene=true;StartCoroutine(ReloadShopScene());return true;
        }
        IEnumerator ReloadShopScene()
        {
            yield return null;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        void DrawFrontEnd(float width,float height)
        {
            Color old=GUI.color;GUI.color=new Color(.97f,.94f,.88f,1f);
            GUI.DrawTexture(new Rect(0,0,width,height),Texture2D.whiteTexture);GUI.color=old;
            var titleStyle=new GUIStyle(title){fontSize=48,alignment=TextAnchor.MiddleCenter};
            var centerStyle=new GUIStyle(body){alignment=TextAnchor.MiddleCenter};
            GUI.Label(new Rect(width*.5f-400,56,800,70),"ANON TOKYO",titleStyle);
            GUI.Label(new Rect(width*.5f-360,130,720,30),"爱音的服装店  /  一起经营梦想中的小店",centerStyle);
            var bow=Resources.Load<Texture2D>("ShopUI/components/bow");
            if(bow!=null)GUI.DrawTexture(new Rect(width*.5f-34,177,68,44),bow,ScaleMode.ScaleToFit);
            GUI.enabled=pendingDeleteSlot==0&&!loadingScene;
            if(frontPage=="home")
            {
                var box=new Rect(width*.5f-220,251,440,380);PanelBox(box);
                GUI.Label(new Rect(box.x+35,box.y+36,370,45),"今天，也来打造喜欢的店铺吧。",centerStyle);
                if(ArtButton(new Rect(box.x+60,box.y+107,320,62),"开始游戏",menuStartStyle??activeButton))OpenSaveSlots();
                if(ArtButton(new Rect(box.x+60,box.y+189,320,56),"设置",button))OpenTitleSettings();
                var card=Resources.Load<Texture2D>("Characters/anon/idle");
                if(ArtButton(new Rect(box.x+60,box.y+261,320,56),"退出游戏",button))ExitWithSave();
                if(card!=null)GUI.DrawTexture(new Rect(width*.5f-520,245,260,390),card,ScaleMode.ScaleToFit);
            }
            else if(frontPage=="slots")
            {
                float left=width*.5f-483;
                for(int i=0;i<3;i++)
                {
                    int slot=i+1;var box=new Rect(left+i*326,246,310,295);NineSlice(box,(slotPanelStyle??panel).normal.background,(slotPanelStyle??panel).border);
                    var session=slotPreviews[i];bool exists=ShopProfiles.Exists(slot);
                    GUI.Label(new Rect(box.x+30,box.y+30,250,30),ShopProfiles.Label(slot),heading);
                    GUI.Label(new Rect(box.x+30,box.y+79,250,100),session!=null?
                        "爱音的服装店\nLv."+session.economy.ShopLevel+" · "+session.layout.width+" × "+session.layout.depth+" 格\n金币 "+session.economy.cash:
                        exists?"存档暂不可用\n"+slotErrors[i]:"空存档\n从一间新的小店开始",body);
                    GUI.enabled=pendingDeleteSlot==0&&!loadingScene&&(!exists||session!=null);
                    if(ArtButton(new Rect(box.x+28,box.y+208,exists?170:254,52),exists?"继续游戏":"新建游戏",activeButton))StartSlot(slot);
                    GUI.enabled=pendingDeleteSlot==0&&!loadingScene;
                    if(exists&&ArtButton(new Rect(box.x+208,box.y+208,74,52),"删除",button))RequestDeleteSlot(slot);
                }
                var infinite=new Rect(left,563,962,100);NineSlice(infinite,(infinitePanelStyle??panel).normal.background,(infinitePanelStyle??panel).border);
                GUI.Label(new Rect(left+30,585,600,27),"无限资源模式",heading);
                GUI.Label(new Rect(left+30,619,600,24),"自由经营，金币与货源无限",small);
                bool infiniteExists=ShopProfiles.Exists(4);
                GUI.enabled=pendingDeleteSlot==0&&!loadingScene&&(!infiniteExists||slotPreviews[3]!=null);
                if(ArtButton(new Rect(left+714,586,infiniteExists?139:219,52),infiniteExists?"继续游戏":"开始无限模式",activeButton))StartSlot(4);
                GUI.enabled=pendingDeleteSlot==0&&!loadingScene;
                if(infiniteExists&&ArtButton(new Rect(left+865,586,68,52),"删除",button))RequestDeleteSlot(4);
            }
            else
            {
                var box=new Rect(width*.5f-308,200,616,570);PanelBox(box);
                GUI.Label(new Rect(box.x+26,box.y+18,500,30),"设置",heading);DrawSettingsPopup(box);
            }
            if(frontPage!="home"&&ArtButton(new Rect(32,32,150,44),"返回主界面",button))frontPage="home";
            GUI.Label(new Rect(width*.5f-460,height-54,920,40),loadingScene?"正在打开店铺…":frontMessage,centerStyle);
            GUI.enabled=true;
            if(pendingDeleteSlot!=0)
            {
                Color tint=GUI.color;GUI.color=new Color(0,0,0,.5f);GUI.DrawTexture(new Rect(0,0,width,height),Texture2D.whiteTexture);GUI.color=tint;
                var confirm=new Rect(width*.5f-260,height*.5f-125,520,250);PanelBox(confirm);
                GUI.Label(new Rect(confirm.x+26,confirm.y+25,468,38),"删除"+ShopProfiles.Label(pendingDeleteSlot)+"？",heading);
                GUI.Label(new Rect(confirm.x+26,confirm.y+80,468,70),"这份店铺进度将被永久删除，无法恢复。\n其他存档不会受到影响。",body);
                if(ArtButton(new Rect(confirm.x+26,confirm.y+175,224,46),"取消",button))CancelDeleteSlot();
                if(ArtButton(new Rect(confirm.x+270,confirm.y+175,224,46),"确认删除",activeButton))ConfirmDeleteSlot();
            }
        }
    }
}
