using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public bool HasGuideStaff=>!string.IsNullOrEmpty(Economy.AssignedStaffId(FacilityKind.Reception));
        public bool HasRestockStaff=>!string.IsNullOrEmpty(Economy.AssignedStaffId(FacilityKind.Restock));
        public bool HasRestocker=>HasRestockStaff;
        public bool HasCashier=>!string.IsNullOrEmpty(Economy.AssignedStaffId(FacilityKind.Checkout));
        public bool GuideAvailable=>(guideEnabled || Economy.IsPerforming) && HasGuideStaff;
        public Vector2Int GuideHomeCell=>GuideHome();
        public Vector2Int RestockHomeCell=>StaffPointCell(FacilityKind.Restock,ShopModel.SpawnCell);
        Vector2Int StaffPointCell(FacilityKind role,Vector2Int fallback)
        {
            var p=Layout.facilities.Find(f=>catalog.Find(f.definitionId).kind==role);
            return p==null?fallback:new Vector2Int(p.x,p.z);
        }
        string StaffName(FacilityKind role)=>StaffName(Economy.PostFor(role));
        string StaffName(StaffPostState post)
        {
            string id=Economy.EffectiveStaffId(post);
            return string.IsNullOrEmpty(id)?"空缺":catalog.staff.Find(s=>s.id==id)?.title??"未知员工";
        }
        public bool AssignStaffToPost(string id,string staffId)
        {
            if(IsVisitingEmployee(staffId)){LastMessage="该角色正在店内闲逛，请等她离店后再安排上岗。";return false;}
            if(Economy.IsPerforming){LastMessage="表演期间由未演出员工自动排班，请结束后调整原绑定。";return false;}
            var check=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(Economy));
            if(!check.AssignStaff(catalog,id,staffId,out string reason)){LastMessage=reason;return false;}
            if((HasCustomer || Economy.shopOpen) && string.IsNullOrEmpty(check.AssignedStaffId(FacilityKind.Checkout)))
            {LastMessage="营业或仍有客人时需保留收银员。可直接换另一名空闲员工，或关店等客人离开后解除。";return false;}
            CancelRestockTask();StopAtCurrentCell();
            if(guideGuest!=null)
            {
                var waiting=guideGuest;ReleaseGuide(false);
                if(waiting.phase==GuestPhase.ToReception || waiting.phase==GuestPhase.WaitingGuide)
                    waiting.phase=GuestPhase.WaitingGuide;
            }
            bool ok=Economy.AssignStaff(catalog,id,staffId,out reason);
            LastMessage=reason;dirty|=ok;ResetGuide();RefreshStaffVisibility();return ok;
        }
        public bool UpgradeStaffPost(string id)
        {
            bool ok=Economy.UpgradePost(id,out string reason);PlaySfx(ok?"upgrade":"error");
            LastMessage=reason;dirty|=ok;return ok;
        }
        public bool SetStaffCarryLimit(string id,int value)
        {
            bool ok=Economy.SetCarryLimit(id,value,out string reason);
            LastMessage=reason;dirty|=ok;return ok;
        }
        string staffDraftPost="", staffDraftId="", staffDraftError="";
        public string SelectedStaffDraft=>staffDraftId;
        public void OpenStaffPost(string id)
        {Select(id);panelKey="post";staffDraftPost=id;staffDraftId=Economy.Post(id)?.staffId??"";staffDraftError="";}
        public bool SelectStaffDraft(string id)
        {
            var post=Economy.Post(staffDraftPost);
            if(panelKey!="post"||post==null||Economy.IsPerforming)return false;
            if(IsVisitingEmployee(id)){staffDraftError="该角色正在店内闲逛，请等她离店后再安排。";PlaySfx("error");return false;}
            if(id!=""&&(!Economy.IsStaffUnlocked(id)||Economy.staffPosts.Exists(p=>p!=post&&p.staffId==id)))return false;
            staffDraftId=id;staffDraftError="";return true;
        }
        public bool ConfirmStaffSelection()
        {
            if(staffDraftError.Contains("正在店内闲逛")){PlaySfx("error");return false;}
            if(panelKey!="post"||selectedId!=staffDraftPost)return false;
            bool ok=AssignStaffToPost(staffDraftPost,staffDraftId);
            PlaySfx(ok?"select":"error");staffDraftError=ok?"已确认":LastMessage;
            if(ok&&TutorialStep>=3&&TutorialStep<=5&&Economy.Post(staffDraftPost)?.role==TutorialRole&&staffDraftId!="")
            {panelKey="";Select(null);TutorialAdvance(TutorialStep==5&&Economy.tutorialVersion==2?23:TutorialStep+1);}
            return ok;
        }
        void RefreshStaffVisibility()
        {
            if(guide!=null)guide.gameObject.SetActive(HasGuideStaff);
            if(cashier!=null)cashier.gameObject.SetActive(HasCashier);
            if(World?.CharacterRoot!=null)World.CharacterRoot.gameObject.SetActive(HasRestockStaff);
        }
        public void TickRestockHome(float dt)
        {
            RefreshStaffVisibility();
            if(dt<=0 || !HasRestockStaff || IsRestocking || path.Count>0 || workRemaining>0 || autoDelay>0 ||
                !string.IsNullOrEmpty(placingDefinition))return;
            var home=RestockHomeCell;
            if(ActorCell!=home && string.IsNullOrEmpty(Economy.NextRestock(catalog,Layout)))
                RouteActor(home);
        }
        void DrawStaffPost(Rect box)
        {
            var f=Selected();var post=f==null?null:Economy.Post(f.instanceId);
            if(post==null)return;
            GUI.Label(new Rect(box.x+22,box.y+60,570,32),catalog.Find(f.definitionId).title+" · Lv."+post.level,heading);
            GUI.Label(new Rect(box.x+22,box.y+103,570,30),"当前："+StaffName(post)+" · "+PostEffect(post),body);
            GUI.Label(new Rect(box.x+22,box.y+143,570,26),"更换员工",body);
            for(int i=0;i<catalog.staff.Count;i++)
            {
                var staff=catalog.staff[i];GUI.enabled=Economy.IsStaffUnlocked(staff.id)&&!IsVisitingEmployee(staff.id)&&!Economy.IsPerforming&&!Economy.staffPosts.Exists(p=>p!=post&&p.staffId==staff.id);
                if(ArtButton(new Rect(box.x+22+(i%3)*192,box.y+179+(i/3)*44,182,37),staff.title+(staffDraftId==staff.id?" ✓":""),staffDraftId==staff.id?activeButton:button))SelectStaffDraft(staff.id);
            }
            GUI.enabled=true;
            if(ArtButton(new Rect(box.x+22,box.y+367,180,35),"空置岗位",button))SelectStaffDraft("");
            if(ArtButton(new Rect(box.x+214,box.y+367,378,35),"确认安排",activeButton))ConfirmStaffSelection();
            GUI.Label(new Rect(box.x+22,box.y+406,570,29),staffDraftError!=""?staffDraftError:"待确认："+(staffDraftId==""?"空置岗位":catalog.staff.Find(s=>s.id==staffDraftId)?.title),small);
            GUI.Label(new Rect(box.x+22,box.y+444,360,54),post.level<3?"升级后："+PostEffect(post,post.level+1):"已满级",body);
            GUI.enabled=post.level<3;
            if(ArtButton(new Rect(box.x+400,box.y+444,192,35),post.level<3?"升级 · "+(Economy.testMode?"免费":ShopEconomy.PostUpgradeCost(post.level)+" 金币"):"已满级",button))UpgradeStaffPost(post.instanceId);
            GUI.enabled=true;
        }
    }
}
