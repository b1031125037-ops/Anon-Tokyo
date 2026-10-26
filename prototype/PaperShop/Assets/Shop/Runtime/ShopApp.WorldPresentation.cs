using System.Collections.Generic;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        readonly Dictionary<string,Texture2D> worldIcons=new Dictionary<string,Texture2D>();
        Texture2D WorldIcon(string name)
        {if(!worldIcons.TryGetValue(name,out var image)){image=Resources.Load<Texture2D>("UI/World/"+name);worldIcons[name]=image;}return image;}
        void DrawProductIcon(Rect rect,string id)
        {
            var frame=WorldIcon("thumbnail-frame");if(frame!=null)GUI.DrawTexture(rect,frame,ScaleMode.StretchToFill);
            Color original=GUI.color;GUI.color=new Color(.72f,.69f,.61f,.16f);GUI.DrawTexture(new Rect(rect.x+6,rect.y+6,rect.width-12,rect.height-12),Texture2D.whiteTexture);GUI.color=original;
            var icon=catalog.products.Find(p=>p.id==id)?.icon;
            if(icon!=null)GUI.DrawTexture(new Rect(rect.x+3,rect.y+3,rect.width-6,rect.height-6),icon,ScaleMode.ScaleToFit);
        }
        readonly Dictionary<int,string> roleStates=new Dictionary<int,string>();
        readonly Dictionary<int,float> roleChanged=new Dictionary<int,float>();
        int expandedRoleCount;
        void DrawCompactRole(Transform actor,string state,string icon,bool idle=false,bool alwaysText=false)
        {
            int id=actor.GetInstanceID();
            if(!roleStates.TryGetValue(id,out var old)||old!=state){roleStates[id]=state;roleChanged[id]=Time.unscaledTime;}
            Vector3 point=World.WorldCamera.WorldToScreenPoint(actor.position+Vector3.up*1.68f);
            if(point.z<=0)return;
            Vector2 anchor=new Vector2(point.x/uiScale,(Screen.height-point.y)/uiScale);
            bool hover=new Rect(anchor.x-38,anchor.y,76,110).Contains(Event.current.mousePosition);
            if(idle&&!hover)return;
            bool expanded=alwaysText||expandedRoleCount<3&&(hover||(!idle&&Time.unscaledTime-roleChanged[id]<1.8f));
            float width=expanded?Mathf.Min(alwaysText?200:132,roleLabel.CalcSize(new GUIContent(state)).x+40):28;
            var rect=new Rect(anchor.x-width*.5f,anchor.y-34,width,30);
            if(rect.yMin<154||rect.yMax>Screen.height/uiScale-125||uiRegions.Exists(r=>r.Overlaps(rect))||roleRects.Exists(r=>r.Overlaps(rect)))return;
            roleRects.Add(rect);if(expanded)expandedRoleCount++;
            var texture=WorldIcon("status-"+icon);
            if(expanded)
            {
                NineSlice(rect,WorldIcon("bubble-neutral"),new RectOffset(24,24,24,28));
                if(texture!=null)GUI.DrawTexture(new Rect(rect.x+5,rect.y+4,22,22),texture);
                GUI.Label(new Rect(rect.x+31,rect.y+1,rect.width-35,28),state,small);
            }
            else if(texture!=null)GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit);
        }
        public int EmptyDisplayCount=>Economy.shelves.FindAll(s=>s.quantity==0&&Layout.facilities.Exists(f=>f.instanceId==s.instanceId)).Count;
        readonly List<Rect> cardScreenRects=new List<Rect>();
        void RecordCardRect(Transform actor)
        {
            if(actor==null||!actor.gameObject.activeInHierarchy)return;
            var foot=World.WorldCamera.WorldToScreenPoint(actor.position);var head=World.WorldCamera.WorldToScreenPoint(actor.position+Vector3.up*1.55f);
            if(foot.z<=0)return;float height=Mathf.Abs(head.y-foot.y)/uiScale,width=height*.68f;
            cardScreenRects.Add(new Rect(foot.x/uiScale-width*.5f,(Screen.height-head.y)/uiScale,width,height));
        }
        public int VisibleStockAlertCount {get;private set;}
        void DrawStockAlerts()
        {
            VisibleStockAlertCount=0;
            cardScreenRects.Clear();if(HasRestockStaff)RecordCardRect(World.CharacterRoot);
            RecordCardRect(cashier?.transform);RecordCardRect(guide?.transform);RecordCardRect(extraRestocker?.transform);RecordCardRect(extraGuide?.transform);
            foreach(var g in guests)RecordCardRect(g.view.transform);foreach(var actor in performers)RecordCardRect(actor.transform);
            foreach(var shelf in Economy.shelves)
            {
                if(shelf.quantity!=0||!World.TryFacilityLabelAnchor(shelf.instanceId,out var anchor))continue;
                var point=World.WorldCamera.WorldToScreenPoint(anchor);if(point.z<=0)continue;
                float x=point.x/uiScale,y=(Screen.height-point.y)/uiScale,width=hideWorldText?32:92;
                if(x<0||x>Screen.width/uiScale||y<0||y>Screen.height/uiScale)continue;
                var initial=new Rect(Mathf.Clamp(x-width/2,8,Screen.width/uiScale-width-8),Mathf.Clamp(y-36,154,Screen.height/uiScale-161),width,32);
                Rect rect=initial;bool placed=false;
                foreach(float dy in new[]{0f,-38f,38f,-76f})
                {
                    foreach(float dx in new[]{0f,-96f,96f})
                    {
                        var candidate=new Rect(initial.x+dx,initial.y+dy,width,32);
                        if(candidate.yMin<154||candidate.yMax>Screen.height/uiScale-125||candidate.xMin<8||candidate.xMax>Screen.width/uiScale-8||uiRegions.Exists(v=>v.Overlaps(candidate))||roleRects.Exists(v=>v.Overlaps(candidate))||cardScreenRects.Exists(v=>v.Overlaps(candidate)))continue;
                        rect=candidate;placed=true;break;
                    }
                    if(placed)break;
                }
                // Crowd avoidance must never silently remove an empty shelf's warning.
                if(!placed&&uiRegions.Exists(v=>v.Overlaps(rect)))continue;
                roleRects.Add(rect);VisibleStockAlertCount++;
                if(!hideWorldText)NineSlice(rect,WorldIcon("bubble-neutral"),new RectOffset(24,24,24,28));
                var icon=WorldIcon("out-of-stock");if(icon!=null)GUI.DrawTexture(new Rect(rect.x+3,rect.y+2,28,28),icon);
                if(!hideWorldText)GUI.Label(new Rect(rect.x+33,rect.y+2,56,28),"缺货",small);
            }
        }

    }
    public sealed partial class ShopWorldView
    {
        public bool TryFacilityLabelAnchor(string id,out Vector3 anchor)
        {var label=labels.Find(x=>x.instanceId==id);anchor=label?.position??Vector3.zero;return label!=null;}
    }
}
