using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        int shownBusinessState=-1;
        float businessFlipAt=-1;
        string businessError="";
        float businessErrorUntil;
        void BusinessFailure(string reason){LastMessage=businessError=reason;businessErrorUntil=Time.unscaledTime+5f;PlaySfx("error");}
        public void ToggleSettingsFromEscape()
        {
            if(frontEnd&&pendingDeleteSlot!=0){CancelDeleteSlot();PlaySfx("click");return;}
            if(quitPrompt){CancelQuit();return;}
            PlaySfx("click");
            if(frontEnd){frontPage=frontPage=="settings"?"home":"settings";return;}
            if(panelKey=="settings"){panelKey="";World.SetEditing(renovationMode);return;}
            EndSurfacePainting();CancelPlacement();Select(null);OpenSettings();
        }
        void TickBusinessPresentation()
        {
            if(Economy==null||frontEnd)return;
            int state=Economy.shopOpen?0:IsClosing?1:2;
            if(state==shownBusinessState)return;
            if(shownBusinessState>=0){businessFlipAt=Time.unscaledTime;if(shownBusinessState==1&&state==2)PlaySfx("closed");}
            shownBusinessState=state;
        }
        void DrawBusinessSign(Rect box)
        {
            bool closing=IsClosing,open=Economy.shopOpen;
            string key=open?"open":closing?"closing":"closed";
            bool hover=box.Contains(Event.current.mousePosition);
            string state=!closing&&hover?(Input.GetMouseButton(0)?"pressed":"hover"):"normal";
            float t=(Time.unscaledTime-businessFlipAt)/.18f;
            float scale=t>=0&&t<1?1f-.28f*Mathf.Sin(t*Mathf.PI):1f;
            var visual=new Rect(box.center.x-box.width*scale*.5f,box.y,box.width*scale,box.height);
            NineSlice(visual,Ui("V3/components/business-"+key+"-"+state),new RectOffset(22,22,22,24));
            GUI.enabled=!closing;
            if(ArtButton(box,GUIContent.none,GUIStyle.none))
            {
                EndRenovation();
                SetShopOpen(!open);
            }
            GUI.enabled=true;
            var label=new GUIStyle(small){alignment=TextAnchor.MiddleCenter,fontSize=11};
            float offset=state=="pressed"?2:0;
            if(open)
            {
                GUI.DrawTexture(new Rect(box.x+5,box.y+5,16,16),Ui("V3/components/open-indicator"));
                GUI.Label(new Rect(box.x+10,box.y+9+offset,box.width-20,18),"OPEN",label);
                GUI.Label(new Rect(box.x+10,box.y+26+offset,box.width-20,18),"营业中",label);
            }
            else if(closing)
            {
                label.fontSize=13;
                GUI.Label(new Rect(box.x+6,box.y+18,box.width-12,22),CustomerCount>0?"等待清客":"等待演出结束",label);
            }
            else
            {
                GUI.Label(new Rect(box.x+10,box.y+9+offset,box.width-20,18),"CLOSED",label);
                GUI.Label(new Rect(box.x+10,box.y+26+offset,box.width-20,18),CanRenovate?"可装修":"暂不可装修",label);
            }
            label.fontSize=closing?11:13;
            GUI.Label(new Rect(box.x+8,box.y+44+offset,box.width-16,22),open?"关店":closing?"剩余 "+CustomerCount+" 位":"开店",label);
            if(Time.unscaledTime<businessErrorUntil)GUI.Label(new Rect(box.x,box.yMax+4,240,45),businessError,small);
        }
    }
}
