using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        bool tutorialCollapsed;
        int tutorialReview=-1, tutorialFocusedStep=-1;
        public int TutorialStep=>Economy.tutorialStep;
        public bool TutorialChoicePending=>!frontEnd&&Economy.tutorialVersion>=1&&TutorialStep==0;
        public bool TutorialVisible=>!frontEnd&&(TutorialChoicePending||tutorialReview>=0||
            Economy.tutorialVersion>=1&&TutorialStep>0&&TutorialStep!=22&&TutorialStep<=29&&TutorialStep!=10&&TutorialStep!=14&&TutorialStep!=17);
        string TutorialProduct=>Economy.shelves.Count>0?Economy.shelves[0].productId:ShopProductSet.CurrentProducts(catalog)[0].id;
        FacilityKind TutorialRole=>TutorialStep==3?FacilityKind.Restock:TutorialStep==4?FacilityKind.Checkout:FacilityKind.Reception;
        public bool ChooseTutorial(bool learn)
        {
            if(!TutorialChoicePending)return false;
            Economy.tutorialStep=learn?(Economy.tutorialVersion==2?3:1):-1;Economy.tutorialDeferred=false;tutorialCollapsed=false;
            Economy.tutorialTransactionBaseline=Economy.transactions;Economy.tutorialUpgradeBaseline=Economy.areaExpansionLevel;
            if(learn){Economy.shopOpen=false;InitializeBusinessState();}
            dirty=true;SaveTrackedSession(false);return true;
        }
        void TutorialAdvance(int step)
        {
            Economy.tutorialStep=step;Economy.tutorialDeferred=false;tutorialCollapsed=step==8;
            if(step>=3&&step<=6){panelKey="";Select(null);}
            dirty=true;SaveTrackedSession(false);
        }
        public void SkipTutorial(){if(TutorialChoicePending)ChooseTutorial(false);}
        public void DeferTutorial(){Economy.tutorialDeferred=true;tutorialCollapsed=true;dirty=true;SaveTrackedSession(false);}
        public void ResumeTutorial(){tutorialCollapsed=false;Economy.tutorialDeferred=false;dirty=true;}
        public void ReviewTutorial(){tutorialReview=0;tutorialCollapsed=false;panelKey="";}
        PlacedFacility TutorialFacility()
        {
            if(TutorialStep>=3&&TutorialStep<=5)return Layout.facilities.Find(f=>catalog.Find(f.definitionId)?.kind==TutorialRole);
            if(TutorialStep==6)return Layout.facilities.Find(f=>Economy.Shelf(f.instanceId)?.productId==TutorialProduct);
            if(TutorialStep==20)return Layout.facilities.Find(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Stage);
            return null;
        }
        void FocusTutorialFacility()
        {var f=TutorialFacility();if(f!=null)World.FocusFacility(f.instanceId);}
        void TickTutorial()
        {
            if(Economy.tutorialVersion<1||TutorialStep<1||TutorialStep==22)return;
            if(Economy.tutorialVersion==2)TickTutorialV2();
            if(tutorialFocusedStep!=TutorialStep){tutorialFocusedStep=TutorialStep;FocusTutorialFacility();}
            int step=TutorialStep;var stock=Economy.Stock(TutorialProduct);
            if(step==1&&stock.contracted)TutorialAdvance(2);
            else if(step==2&&(stock.purchased>0||Economy.orders.Exists(o=>o.productId==TutorialProduct)||Economy.shelves.Exists(s=>s.productId==TutorialProduct&&s.quantity>0)))TutorialAdvance(Economy.tutorialVersion==2?26:3);
            else if(step==6&&panelKey=="shelf"&&Economy.Shelf(selectedId)?.productId==TutorialProduct&&Economy.Shelf(selectedId).quantity>0)TutorialAdvance(7);
            else if(step==7&&Economy.shopOpen)TutorialAdvance(8);
            else if(step==8&&Economy.transactions>Economy.tutorialTransactionBaseline)TutorialAdvance(9);
            else if(step==10&&(Economy.areaExpansionLevel>Economy.tutorialUpgradeBaseline||Economy.areaExpansionLevel==4))TutorialAdvance(13);
            else if(step==10&&!CashierBusy&&!Economy.IsPerforming&&Economy.areaExpansionLevel<4&&
                Economy.ExpansionQualifiedPopularity>=Economy.NextExpansionPopularity&&
                (Economy.testMode||Economy.cash>=Economy.NextExpansionCost))TutorialAdvance(11);
            else if(step==11&&panelKey=="bonuses")TutorialAdvance(12);
            else if((step==11||step==12)&&Economy.areaExpansionLevel>Economy.tutorialUpgradeBaseline)TutorialAdvance(13);
            else if(step==14)TutorialAdvance(Economy.areaExpansionLevel>0?15:10);
            else if(step==15&&Economy.tutorialWallClaimed)TutorialAdvance(16);
            else if(step==16&&Economy.tutorialFloorClaimed)TutorialAdvance(Economy.tutorialVersion==2?29:17);
            else if(step==17&&PerformanceMissingConditions("MyGO!!!!!",false,false).Count==0)
            {performanceBandChoice="MyGO!!!!!";TutorialAdvance(Economy.stageOwned?19:18);}
            else if(step==17&&PerformanceMissingConditions("Ave Mujica",false,false).Count==0)
            {performanceBandChoice="Ave Mujica";TutorialAdvance(Economy.stageOwned?19:18);}
            else if(step==18&&Economy.stageOwned)TutorialAdvance(19);
            else if(step==19&&Layout.facilities.Exists(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Stage))TutorialAdvance(20);
            else if(step==20&&Economy.IsPerforming)TutorialAdvance(21);
            else if(step==21&&!Economy.IsPerforming)TutorialAdvance(22);
        }
        public void TutorialAction()
        {
            if(tutorialReview>=0){if(++tutorialReview>7)tutorialReview=-1;return;}
            // Dialogue never opens a gameplay window or confirms a player's assignment.
            if(TutorialStep==9){TutorialAdvance(Economy.tutorialVersion==2?28:10);panelKey="";}
            else if(TutorialStep==24){TutorialAdvance(25);panelKey="";}
            else if(TutorialStep==13){TutorialAdvance(15);panelKey="";}
            else FocusTutorialFacility();
        }
        string TutorialSpeech(int step)
        {
            var extra=TutorialExtraSpeech(step);if(extra!=null)return extra;
            switch(step)
            {
                case 1:return "我们先卖这款上装吧！看看成本和售价，点第一款商品的签约按钮，就能进货啦。";
                case 2:return "先订一批试试看。下单后，这里会告诉我们货送到哪儿了。";
                case 3:return "我们先安排好岗位，货到后就有人送到展柜。点补货位安排伙伴，我来帮忙也可以哦！";
                case 4:return "接着安排收银员，客人挑好商品就能来付款了。";
                case 5:return "再请一位伙伴接待客人，帮他们找到喜欢的东西吧！";
                case 6:return "等伙伴把商品摆好，点开展柜看看售价和库存吧！";
                case 7:return "准备好了！点标题旁的开店按钮，迎接第一位客人吧！";
                case 8:return "开门啦！等客人慢慢挑选，我们一起期待第一笔生意吧。";
                case 9:return "卖出去了！第一位客人喜欢我们的店呢。接下来就交给你啦！";
                case 11:return "我们的店越来越受欢迎啦！人气和金币都够了，点左上角看看升级吧！";
                case 12:return CanRenovate?"看一下升级费用和新增空间，准备好就确认吧！":
                    "先点标题旁的关店按钮，等客人逛完离开，我们就能开始扩建。";
                case 13:return "变宽敞了！接下来到商店领取墙纸和地板，把新空间装扮起来吧！";
                case 15:return "点底部的商店，再选墙纸，领取一种喜欢的颜色。领到的材料会放进装修库存。";
                case 16:return "再到商店的地板分类领取材料吧！关店后打开装修，选库存中的材料就能铺到店里。";
                case 18:return "大家已经准备好演出啦！点商店的设施分类，把舞台带回店里吧！";
                case 19:return "MyGO 的舞台已经在库存里啦！先关店等客人离开，再到装修的设施分类摆放。箭头就是舞台正面。";
                case 20:return Economy.shopOpen?"点店里的舞台，选好乐队，再点开始表演！还缺什么可以在 FEVER 里查看。":"舞台就位啦！结束装修，重新开店，再点舞台开始表演吧！";
                case 21:return "开演啦！一起听听大家的演奏吧，伙伴们会照看好店里的生意。";
                default:return "欢迎来到我们的店！要我陪你一起准备开业吗？";
            }
        }
        string TutorialActionLabel()
        {
            if(tutorialReview>=0)return tutorialReview==7?"看完啦":"下一页";
            if(TutorialStep==9||TutorialStep==13||TutorialStep==24)return "好耶！";
            if(TutorialFacility()!=null)return "定位设施";
            return "请点击高亮位置";
        }
        void TutorialFrame(Rect box,string resource)
        {
            var texture=Ui("V5/components/"+resource);
            if(texture==null)PanelBox(box);
            else{uiRegions.Add(box);NineSlice(box,texture,new RectOffset(22,22,20,22));}
        }
        void DrawTutorial(float width,float height)
        {
            if(!TutorialVisible||panelKey=="settings"||panelKey=="save"||quitPrompt)return;
            if(TutorialChoicePending)
            {
                Color old=GUI.color;GUI.color=new Color(0,0,0,.25f);GUI.DrawTexture(new Rect(0,0,width,height),Texture2D.whiteTexture);GUI.color=old;
                var box=new Rect(width*.5f-340,height*.5f-170,680,340);TutorialFrame(box,"welcome-panel");
                var portrait=Ui("V5/anon-talk")??Resources.Load<Texture2D>("Characters/anon/idle");
                if(portrait!=null)GUI.DrawTexture(new Rect(box.x+15,box.y+20,170,290),portrait,ScaleMode.ScaleToFit);
                GUI.Label(new Rect(box.x+200,box.y+35,425,32),"千早爱音",heading);
                GUI.Label(new Rect(box.x+200,box.y+91,425,90),TutorialSpeech(0),body);
                if(ArtButton(new Rect(box.x+200,box.y+207,435,43),"跟爱音学开店",activeButton))ChooseTutorial(true);
                if(ArtButton(new Rect(box.x+200,box.y+264,435,39),"跳过，直接经营",button))ChooseTutorial(false);
                return;
            }
            if(tutorialCollapsed||Economy.tutorialDeferred)
            {
                var tab=new Rect(24,height-171,160,43);uiRegions.Add(tab);
                if(ArtButton(tab,"爱音 · 继续引导",button))ResumeTutorial();return;
            }
            bool compact=!string.IsNullOrEmpty(panelKey)||renovationMode||Selected()!=null;
            var card=compact?new Rect(24,170,280,430):new Rect(width-548,height-355,520,225);
            TutorialFrame(card,compact?"dialogue-compact":"dialogue-wide");
            var art=Ui("V5/anon-talk")??Resources.Load<Texture2D>("Characters/anon/idle");
            if(art!=null)GUI.DrawTexture(compact?new Rect(card.x+15,card.y+45,90,135):new Rect(card.x+8,card.y+20,115,175),art,ScaleMode.ScaleToFit);
            GUI.Label(new Rect(card.x+(compact?22:135),card.y+18,200,30),"千早爱音",heading);
            int speech=tutorialReview>=0?new[]{1,2,3,4,5,7,9,11}[tutorialReview]:TutorialStep;
            GUI.Label(compact?new Rect(card.x+20,card.y+185,240,108):new Rect(card.x+135,card.y+58,360,72),TutorialSpeech(speech),body);
            if(ArtButton(new Rect(card.xMax-57,card.y+15,37,26),"—",button)){tutorialCollapsed=true;return;}
            bool hasAction=tutorialReview>=0||TutorialStep==9||TutorialStep==13||TutorialStep==24||TutorialFacility()!=null;
            var actionRect=new Rect(card.x+(compact?20:135),card.yMax-98,compact?240:360,40);
            if(hasAction){if(ArtButton(actionRect,TutorialActionLabel(),activeButton))TutorialAction();}
            else GUI.Label(actionRect,TutorialStep==8||TutorialStep==21?"一起等一会儿吧":"请点击高亮位置",small);
            GUI.enabled=true;
            if(tutorialReview>=0&&ArtButton(new Rect(card.x+(compact?20:135),card.yMax-48,compact?240:360,29),"关闭指南",button))tutorialReview=-1;
            if(tutorialReview<0)DrawTutorialTarget(width,height);
        }
        void DrawTutorialTarget(float width,float height)
        {
            Rect target=new Rect();bool worldObject=false;LastTutorialCircle=new Rect();
            if(TutorialStep==1||TutorialStep==2||TutorialStep==26||TutorialStep==27)target=panelKey=="goods"&&(productCategory==-1||productCategory==0)&&scroll.y<1?
                new Rect(width*.5f+77,height-488,177,42):new Rect((width-838)*.5f+210,height-112,98,82);
            else if(TutorialStep==7||TutorialStep==12&&!CanRenovate)target=new Rect(350,20,112,72);
            else if(TutorialStep==11||TutorialStep==12)target=new Rect(24,20,316,72);
            else if(TutorialStep>=3&&TutorialStep<=6)
            {
                if(panelKey=="post")target=new Rect(CompactPanelRect.x+18,CompactPanelRect.yMax-125,CompactPanelRect.width-36,34);
                else if(!string.IsNullOrEmpty(panelKey))return;
                else
                {
                    var facility=TutorialStep==6?Layout.facilities.Find(f=>Economy.Shelf(f.instanceId)?.productId==TutorialProduct):
                        Layout.facilities.Find(f=>catalog.Find(f.definitionId).kind==TutorialRole);
                    if(facility!=null){target=TutorialObjectRect(facility);worldObject=true;}
                    if(uiRegions.Exists(r=>r.Overlaps(target)))return;
                }
            }
            if(TutorialStep==15||TutorialStep==16||TutorialStep==18)
            {
                if(!IsStorePanel(panelKey))target=new Rect((width-838)*.5f+105,height-112,98,82);
                else if(TutorialStep==18&&panelKey=="poststore")target=new Rect(width*.5f+82,height-692+367,204,42);
                else if(panelKey=="materials"&&surfaceStoreFloor==(TutorialStep==16))
                {var ids=surfaceStoreFloor?ShopSurfaces.FloorIds:ShopSurfaces.WallIds;int row=System.Array.FindIndex(ids,id=>Economy.MaterialRoom(Layout,id)>0);if(row>=0)target=new Rect(width*.5f+82,height-692+168+row*48,204,33);}
                else target=new Rect(width*.5f-284+(TutorialStep==15?4:TutorialStep==16?3:2)*116,height-692+54,106,32);
            }
            if(TutorialStep==19)target=new Rect((width-838)*.5f,height-112,98,82);
            if(TutorialStep==20){var f=TutorialFacility();if(f!=null&&panelKey==""){target=TutorialObjectRect(f);worldObject=true;}}
            if(tutorialDynamicTarget.width>0)target=tutorialDynamicTarget;
            if((TutorialStep==26||TutorialStep==27)&&panelKey=="goods"&&tutorialDynamicTarget.width==0&&SecondTutorialProduct!=null)
            {int index=System.Array.IndexOf(ShopProductSet.ProductIds,SecondTutorialProduct);target=new Rect(width*.5f-286+(index+1)*72,height-549,66,31);}
            if(TutorialStep==25&&!string.IsNullOrEmpty(placingDefinition))target=new Rect(width*.5f-30,height*.5f-30,60,60);
            if(TutorialStep==23&&panelKey!="store")target=new Rect((width-838)*.5f+105,height-112,98,82);
            if(TutorialStep==25&&!renovationMode)target=new Rect((width-838)*.5f,height-112,98,82);
            if(TutorialStep==28&&panelKey!="growth")target=new Rect((width-838)*.5f+525,height-112,98,82);
            if(TutorialStep==29&&!IsEditingLayout)target=new Rect(350,20,112,72);
            if(target.width==0)return;
            DrawAttentionArrow(target);
            DrawTutorialRing(target,worldObject);
        }
    }
}
