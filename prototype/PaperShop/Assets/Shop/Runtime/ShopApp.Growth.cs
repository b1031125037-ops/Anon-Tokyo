using System;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        Vector2 growthScroll;
        public bool HasClaimableTask=>Economy!=null&&Economy.GetGrowthTaskStates().Exists(t=>t.canClaim);
        void DrawClaimableTaskBadge(Rect buttonRect)
        {
            if(!HasClaimableTask)return;
            var badge=new Rect(buttonRect.center.x-35,buttonRect.y-17,70,25);
            var old=GUI.color;GUI.color=new Color(1f,.72f,.35f,.9f+.1f*Mathf.Sin(Time.unscaledTime*3));
            NineSlice(badge,activeButton.normal.background,activeButton.border);GUI.color=old;
            var style=new GUIStyle(small){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
            GUI.Label(badge,"可领取",style);
            uiRegions.Add(badge);
            if(ArtButton(badge,GUIContent.none,GUIStyle.none))OpenGrowth();
        }

        public void OpenGrowth() { if(panelKey!="growth")OpenPanel("growth"); }
        public void OpenExpansion() { if(panelKey!="expansion")OpenPanel("expansion"); }

        bool SaveGrowthDraft(ShopLayout layout, ShopEconomy state, out string reason)
        {
            reason="";
            if(verifyMode)return true;
            if(blockedSave){reason="当前存档尚未恢复，暂不能提交成长奖励或扩店。";return false;}
            if(pendingOfflineUntilUtc>0){reason="离线结算仍等待保存，请恢复存档写入后再领取或扩店。";return false;}
            if(!state.CaptureOfflineBaseline(catalog,layout,Math.Max(UtcSeconds(),state.offlineLastCheckUtcSeconds),out reason))return false;
            try{ShopSession.Save(layout,state,catalog);return true;}
            catch(Exception ex){reason="保存失败，本次资产变更未提交："+ex.Message;return false;}
        }

        public bool ClaimGrowthTask(string taskId)
        {
            var state=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            if(!state.TryClaimGrowthTask(catalog,Layout,taskId,out string message))
            {LastMessage=message;return false;}
            if(!SaveGrowthDraft(Layout,state,out string reason)){LastMessage=reason;return false;}
            Economy=state;dirty=verifyMode;LastMessage=message;PlaySfx("income");return true;
        }

        public bool ExpandShop()
        {
            if(!CanRenovate){LastMessage="请先关店，等待所有客人结清并离开后再扩店。";return false;}
            var layout=ShopModel.Clone(Layout);
            var state=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            state.CancelRestock();
            if(!state.TryExpandShop(catalog,layout,out string message)){LastMessage=message;return false;}
            if(!SaveGrowthDraft(layout,state,out string reason)){LastMessage=reason;return false;}
            CancelRestockTask();CancelPlacement();StopAtCurrentCell();
            int depthShift=layout.depth-Layout.depth,widthShift=layout.width-Layout.width;
            ActorCell+=new Vector2Int(widthShift,depthShift);
            extraRestockCell+=new Vector2Int(widthShift,depthShift);
            ClearRoadTraffic();Layout=layout;Economy=state;
            World.Rebuild(Layout,catalog);
            World.SetWarehouseLevel(Economy.warehouseLevel);
            World.CharacterRoot.position=World.CellCenter(ActorCell);
            if(extraRestocker!=null)extraRestocker.transform.position=World.CellCenter(extraRestockCell);
            ResetGuide();
            var checkout=Layout.facilities.Find(f=>f.definitionId=="checkout");
            if(cashier!=null && checkout!=null)cashier.transform.position=World.CellCenter(CashierStandCell(checkout));
            dirty=verifyMode;LastMessage=message;PlaySfx("upgrade");
            return true;
        }

        static bool IsStorePanel(string key)=>key=="store"||key=="poststore"||key=="materials"||key=="decorations";
        void DrawStoreTabs(Rect box)
        {
            string[] labels={"展柜","装饰","设施","地板","墙纸"};
            int active=panelKey=="store"?0:panelKey=="decorations"?1:panelKey=="poststore"?2:surfaceStoreFloor?3:4;
            for(int i=0;i<5;i++)
                if(ArtButton(new Rect(box.x+22+i*116,box.y+54,106,32),labels[i],active==i?activeButton:button))
                {
                    string next=i==0?"store":i==1?"decorations":i==2?"poststore":"materials";
                    if(i>=3)surfaceStoreFloor=i==3;
                    if(panelKey!=next)OpenPanel(next);
                }
        }

        void DrawGrowth(Rect box)
        {
            GUI.Label(new Rect(box.x+22,box.y+103,570,28),
                "任务 "+Economy.claimedGrowthTaskIds.Count+" / 10 · 店内已售 "+Economy.OnlineUnitsSold+" 件",heading);
            GUI.Label(new Rect(box.x+22,box.y+141,570,38),
                "按顺序领取奖励，完成条件会解锁下一阶段。已有进度计入；离线销量不代替店内成交。",small);
            var tasks=Economy.GetGrowthTaskStates();
            growthScroll=GUI.BeginScrollView(new Rect(box.x+20,box.y+188,576,323),growthScroll,new Rect(0,0,552,tasks.Count*100));
            for(int i=0;i<tasks.Count;i++)
            {
                var task=tasks[i];float y=i*100;
                if(TutorialStep==28&&task.canClaim)RegisterTutorialTarget(new Rect(box.x+421,box.y+208+y-growthScroll.y,141,40),new Rect(box.x+20,box.y+188,576,323));
                GUI.Label(new Rect(8,y+2,385,25),(i+1)+". "+task.definition.title+" · "+task.progressText,body);
                GUI.Label(new Rect(8,y+30,375,47),task.definition.description,small);
                GUI.Label(new Rect(8,y+75,375,23),"奖励 "+task.definition.reward+" 金币"+(Economy.testMode?"":""),small);
                GUI.enabled=task.canClaim;
                string action=task.claimed?"已领取":!task.unlocked?"前置未领取":task.complete?"领取奖励":"进行中";
                if(ArtButton(new Rect(401,y+20,141,40),action,task.canClaim?activeButton:button))
                    ClaimGrowthTask(task.definition.id);
                GUI.enabled=true;
            }
            GUI.EndScrollView();
        }

        void DrawExpansion(Rect box)
        {
            DrawShopUpgrade(box);
        }
    }
}
