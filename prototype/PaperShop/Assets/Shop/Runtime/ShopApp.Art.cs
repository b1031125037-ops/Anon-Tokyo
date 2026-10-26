using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        GUIStyle menuStartStyle,slotPanelStyle,infinitePanelStyle;
        static Texture2D Ui(string name)=>Resources.Load<Texture2D>("ShopUI/"+name);
        readonly System.Collections.Generic.Dictionary<GUIStyle,GUIStyle> fabricLabels=new System.Collections.Generic.Dictionary<GUIStyle,GUIStyle>();
        static void NineSlice(Rect rect,Texture2D image,RectOffset border)
        {
            if(image==null)return;
            float factor=Mathf.Min(1,rect.width/Mathf.Max(1,border.horizontal),rect.height/Mathf.Max(1,border.vertical));
            float[] dx={rect.x,rect.x+border.left*factor,rect.xMax-border.right*factor,rect.xMax};
            float[] dy={rect.y,rect.y+border.top*factor,rect.yMax-border.bottom*factor,rect.yMax};
            // Adjacent slices share physical pixel boundaries, including scaled UI.
            float pixelsX=Mathf.Max(.01f,Mathf.Abs(GUI.matrix.m00)),pixelsY=Mathf.Max(.01f,Mathf.Abs(GUI.matrix.m11));
            for(int i=0;i<4;i++){dx[i]=Mathf.Round(dx[i]*pixelsX)/pixelsX;dy[i]=Mathf.Round(dy[i]*pixelsY)/pixelsY;}
            float[] sx={0,border.left/(float)image.width,1-border.right/(float)image.width,1};
            float[] sy={1,1-border.top/(float)image.height,border.bottom/(float)image.height,0};
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                if(dx[x+1]>dx[x]&&dy[y+1]>dy[y])
                    GUI.DrawTextureWithTexCoords(new Rect(dx[x],dy[y],dx[x+1]-dx[x],dy[y+1]-dy[y]),
                        image,new Rect(sx[x],sy[y+1],sx[x+1]-sx[x],sy[y]-sy[y+1]),true);
        }
        bool ArtButton(Rect rect,string label,GUIStyle style)=>ArtButton(rect,new GUIContent(label),style);
        bool ArtButton(Rect rect,GUIContent content,GUIStyle style)
        {
            if(style==GUIStyle.none||style.normal.background==null){ButtonSound(rect,content.text);bool hit=GUI.Button(rect,content,style);if(hit)QueueClickSound();return hit;}
            ButtonSound(rect,content.text);
            bool clicked=GUI.Button(rect,GUIContent.none,GUIStyle.none);
            if(clicked)QueueClickSound();
            bool hover=rect.Contains(Event.current.mousePosition);
            Texture2D background=GUI.enabled&&hover?(Input.GetMouseButton(0)?style.active.background:style.hover.background):style.normal.background;
            var previousColor=GUI.color;if(!GUI.enabled)GUI.color=new Color(.65f,.65f,.65f,1f);
            NineSlice(rect,background??style.normal.background,style.border);GUI.color=previousColor;
            if(!fabricLabels.TryGetValue(style,out var labelStyle))
            {
                labelStyle=new GUIStyle(style);labelStyle.border=new RectOffset();
                labelStyle.normal.background=labelStyle.hover.background=labelStyle.active.background=labelStyle.focused.background=null;
                fabricLabels[style]=labelStyle;
            }
            GUI.Label(rect,content,labelStyle);return clicked;
        }
        void ApplyFabricStyles()
        {
            var panelArt=Ui("components/panel");
            if(panelArt==null)return;
            panel.normal.background=panelArt;panel.border=new RectOffset(28,28,28,30);
            FabricButton(button,"neutral",22,20);
            FabricButton(activeButton,"primary",22,20);
            FabricButton(toolbarButton,"toolbar",24,24);
            FabricButton(toolbarActive,"toolbar",24,24,true);
            button.normal.textColor=button.hover.textColor=button.active.textColor=ink;
            activeButton.normal.textColor=activeButton.hover.textColor=activeButton.active.textColor=ink;
            toolbarButton.normal.textColor=toolbarButton.hover.textColor=ink;
            toolbarActive.normal.textColor=toolbarActive.hover.textColor=ink;
            menuStartStyle=new GUIStyle(activeButton);FabricButton(menuStartStyle,"menu-start",28,24);
            slotPanelStyle=new GUIStyle(panel);slotPanelStyle.normal.background=Ui("components/save-slot-normal");slotPanelStyle.border=new RectOffset(27,27,27,27);
            infinitePanelStyle=new GUIStyle(panel);infinitePanelStyle.normal.background=Ui("components/unlimited-mode-normal");infinitePanelStyle.border=new RectOffset(22,22,22,22);
            GUI.skin.horizontalSlider.normal.background=Ui("components/slider-track-normal");
            GUI.skin.horizontalSliderThumb.normal.background=Ui("components/slider-thumb-normal");
            GUI.skin.horizontalSliderThumb.fixedWidth=18;
            GUI.skin.horizontalSliderThumb.fixedHeight=18;
        }
        static void FabricButton(GUIStyle style,string key,int x,int y,bool selected=false)
        {
            string modern=key=="primary"?"action":key;
            style.normal.background=Ui("V2/components/"+modern+(selected?"-selected":"-normal"))??Ui("components/"+key+(selected?"-selected":"-normal"));
            style.hover.background=Ui("V2/components/"+modern+"-hover")??Ui("components/"+key+"-hover");
            style.active.background=Ui("V2/components/"+modern+"-pressed")??Ui("components/"+key+"-pressed");
            style.focused.background=style.normal.background;
            style.border=new RectOffset(x,x,y,y);
        }
        void SyncStaffCards()
        {
            BindStaffCard(World.Character,Economy.PostFor(FacilityKind.Restock),
                restockPhase==RestockPhase.Picking||restockPhase==RestockPhase.ToShelf||restockPhase==RestockPhase.Stocking
                    ?"restock":IsWalking?"walk":workRemaining>0?"interact":"idle");
            BindStaffCard(cashier,Economy.PostFor(FacilityKind.Checkout),CashierBusy?"cashier":"idle");
            BindStaffCard(guide,Economy.PostFor(FacilityKind.Reception),
                guideRoute.Count>0?"walk":guidePhase==GuidePhase.Greeting?"welcome":
                guidePhase==GuidePhase.Recommending?"interact":guidePhase==GuidePhase.Approaching?"think":"idle");
            BindStaffCard(extraRestocker,SecondPost(FacilityKind.Restock),
                extraRestockPhase==RestockPhase.Picking||extraRestockPhase==RestockPhase.ToShelf||extraRestockPhase==RestockPhase.Stocking
                    ?"restock":extraRestocker!=null&&extraRestocker.IsMoving?"walk":"idle");
            BindStaffCard(extraGuide,SecondPost(FacilityKind.Reception),
                extraGuide!=null&&extraGuide.IsMoving?"walk":extraGuide!=null&&extraGuide.IsWorking?"welcome":"idle");
        }
        void BindStaffCard(PaperCharacterView actor,StaffPostState post,string pose)
        {
            if(actor==null||post==null)return;
            actor.SetCharacter(Economy.EffectiveStaffId(post));actor.SetPose(pose);
        }
    }
}
