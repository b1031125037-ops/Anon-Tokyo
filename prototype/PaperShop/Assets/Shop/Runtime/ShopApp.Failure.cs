using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        bool gameFailed;string failureError="";float failureTimer;
        float failureStarted;
        public float FailureAnimationProgress=>gameFailed?Mathf.Clamp01((Time.unscaledTime-failureStarted)/1.1f):0;
        public bool GameFailed=>gameFailed;
        void TickFinancialFailure(float dt)
        {
            if(gameFailed||frontEnd||loadingScene||blockedSave||TutorialChoicePending)return;
            if(!Economy.IsFinanciallyStranded(catalog)){failureTimer=0;return;}
            failureTimer+=dt;if(failureTimer<1)return;
            gameFailed=true;failureStarted=Time.unscaledTime;panelKey="";offlineTrackingReady=false;panGesture=false;
            // Persist the exhausted state; delete only this slot when the player chooses an exit.
            SaveTrackedSession(false);PlaySfx("error");
        }
        public bool ResolveFailedGame(bool newGame)
        {
            if(!gameFailed||Economy.testMode||loadingScene)return false;
            if(!ShopProfiles.Delete(ShopProfiles.SelectedSlot,out failureError))return false;
            blockedSave=true;offlineTrackingReady=false;SaveUserSettings();
            if(newGame){ShopProfiles.StartRequested=true;loadingScene=true;StartCoroutine(ReloadShopScene());}
            else {allowQuit=true;Application.Quit();}
            return true;
        }
        void DrawFailure(float width,float height)
        {
            float t=FailureAnimationProgress,fade=Mathf.SmoothStep(0,1,Mathf.Clamp01(t*2));
            GUI.color=new Color(0,0,0,.55f*fade);GUI.DrawTexture(new Rect(0,0,width,height),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.color=new Color(1,1,1,fade);
            var box=new Rect(width*.5f-275,height*.5f-160-(1-Mathf.SmoothStep(0,1,t))*65,550,320);PanelBox(box);
            GUI.Label(new Rect(box.x+30,box.y+27,490,38),"这次经营结束了",heading);
            GUI.Label(new Rect(box.x+30,box.y+86,490,106),"货物已经售空，金币和可领取奖励不足以再次进货。\n新开一家店，再试一次吧！\n选择后将删除本次存档，其他存档不受影响。",body);
            var coin=Ui("icons/currency");
            if(coin!=null&&t<1)
                for(int i=0;i<7;i++)
                {
                    float fall=Mathf.Clamp01(t*1.6f-i*.07f);
                    GUI.color=new Color(1,1,1,Mathf.Sin(fall*Mathf.PI)*fade);
                    float x=box.center.x+(i-3)*46+Mathf.Sin(fall*5+i)*10;
                    GUI.DrawTexture(new Rect(x,box.y+169+fall*fall*48,24,24),coin,ScaleMode.ScaleToFit);
                }
            var matrix=GUI.matrix;var stamp=new Rect(box.xMax-151,box.y+30,122,36);
            GUIUtility.RotateAroundPivot(-8,stamp.center);
            GUI.color=new Color(.65f,.19f,.23f,Mathf.Clamp01((t-.35f)*4));
            GUI.DrawTexture(new Rect(stamp.x,stamp.y,stamp.width,2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(stamp.x,stamp.yMax-2,stamp.width,2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(stamp.x,stamp.y,2,stamp.height),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(stamp.xMax-2,stamp.y,2,stamp.height),Texture2D.whiteTexture);
            var stampStyle=new GUIStyle(heading){fontSize=22,alignment=TextAnchor.MiddleCenter};
            stampStyle.normal.textColor=new Color(.65f,.19f,.23f);
            GUI.Label(stamp,"经营破产",stampStyle);
            GUI.matrix=matrix;GUI.color=new Color(1,1,1,fade);
            if(failureError!="")GUI.Label(new Rect(box.x+30,box.y+190,490,38),failureError,small);
            GUI.enabled=t>=1;
            if(ArtButton(new Rect(box.x+30,box.y+242,235,48),"新游戏",activeButton))ResolveFailedGame(true);
            if(ArtButton(new Rect(box.x+285,box.y+242,235,48),"退出游戏",button))ResolveFailedGame(false);
            GUI.enabled=true;GUI.color=Color.white;
        }
    }
}
