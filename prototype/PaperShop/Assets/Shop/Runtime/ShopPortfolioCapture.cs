using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>
    /// Opt-in portfolio recording. The prepared showcase is an isolated sandbox;
    /// all visible sales, staffing, construction and performance use the game's commands.
    /// This component is inert during ordinary play and never writes player preferences.
    /// </summary>
    public sealed class ShopPortfolioCapture : MonoBehaviour
    {
        [Serializable] public sealed class Moment { public float seconds; public string action; public int value; }
        [Serializable] public sealed class Clip
        {
            public string file,title; public int frames,width,height,transactionsBefore,transactionsAfter;
            public int pickupsBefore,pickupsAfter,extraRestocksBefore,extraRestocksAfter,guestsBefore,guestsAfter;
            public float seconds; public List<Moment> events=new List<Moment>();
        }
        [Serializable] public sealed class Manifest
        {
            public bool completed; public string author="白布",project="Anon Tokyo服装店";
            public string provenance="Actual Unity gameplay; isolated staged sandbox; design and balancing led by 白布 with AI assistance.";
            public string error=""; public int fps=30; public List<Clip> clips=new List<Clip>();
            public List<string> setup=new List<string>();
        }
        static bool installed;
        readonly Manifest manifest=new Manifest();
        readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        string output,ffmpeg; Process encoder; Texture2D pixels; Clip active; int activeFrame;
        readonly StringBuilder encoderLog=new StringBuilder();
        ShopApp App=>FindFirstObjectByType<ShopApp>();
        const int Fps=30;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(installed||Array.IndexOf(Environment.GetCommandLineArgs(),"--portfolio-capture")<0)return;
            installed=true;var obj=new GameObject("Portfolio capture - isolated session");
            DontDestroyOnLoad(obj);obj.AddComponent<ShopPortfolioCapture>();
        }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            output=Argument(args,"--portfolio-capture");ffmpeg=Argument(args,"--portfolio-ffmpeg");
            Directory.CreateDirectory(output);
            if(Array.IndexOf(args,"--verify-menu")<0||Array.IndexOf(args,"--portfolio-only")<0||!File.Exists(ffmpeg))
            {Fail("Required: --verify-menu <output> --portfolio-only --portfolio-ffmpeg <existing ffmpeg.exe>");yield break;}
            Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            Time.captureFramerate=Fps;
            var stack=new Stack<IEnumerator>();stack.Push(Sequence());
            while(stack.Count>0)
            {
                object next=null;bool moved=false;Exception failure=null;
                try {moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
                catch(Exception ex){failure=ex;}
                if(failure!=null){Fail(failure.ToString());yield break;}
                if(!moved){stack.Pop();continue;}
                if(next is IEnumerator routine)stack.Push(routine);else yield return next;
            }
            manifest.completed=true;WriteManifest();Time.captureFramerate=0;Time.timeScale=1;
            Application.Quit(0);
        }
        static string Argument(string[] args,string key)
        {int n=Array.IndexOf(args,key);return n>=0&&n+1<args.Length?args[n+1]:"";}
        IEnumerator Sequence()
        {
            yield return Frames(4);
            Require(App!=null&&App.IsFrontEnd,"Portfolio starts at title");
            Require(Path.GetFullPath(ShopProfiles.Root)==Path.GetFullPath(Path.Combine(Argument(Environment.GetCommandLineArgs(),"--verify-menu"),"slots")),"Isolated profiles confirmed");
            yield return Record("01-title","主界面",4,null);
            Require(App.StartSlot(4),"Start showcase sandbox");
            for(int i=0;i<300&&(App==null||App.IsFrontEnd);i++)yield return null;
            Require(App!=null&&!App.IsFrontEnd,"Gameplay loaded");
            App.ChooseTutorial(false);App.SetPaused(true);App.Economy.shopOpen=false;
            Invoke(App,"ClearCustomers");Invoke(App,"ClearRoadTraffic");
            PrepareShowcase();
            App.EndRenovation();App.ClearFacilitySelection();Set(App,"panelKey","");
            App.SetPaused(false);App.SetShopOpen(true);
            Time.timeScale=4;
            for(int i=0;i<600&&App.Economy.transactions<2;i++)yield return null;
            Time.timeScale=1;Overview();
            yield return Record("02-overview","纸片角色与立体店铺",8,f=>
            {if(f>15)App.World.Orbit(new Vector2(.10f,0));});

            Focus(new Vector3(-1.1f,.7f,2.7f),5.3f,32,42);
            yield return Record("03-business","招待、挑选与收银",14,f=>
            {if(f>90)App.World.Orbit(new Vector2(-.025f,0));});

            App.CancelRestockTask();
            foreach(var shelf in App.Economy.shelves)
            {var stock=App.Economy.Stock(shelf.productId);stock.available+=shelf.quantity;shelf.quantity=0;}
            App.World.SyncProductDisplays(App.Economy);App.RequestRestockBatch();
            Focus(new Vector3(2.1f,.8f,1.3f),5.4f,35,45);
            yield return Record("04-restock","双补货岗位协作",12,null);

            yield return CloseAndDrain();
            App.CancelRestockTask();Overview();
            Require(App.BuyCounter("top"),"Buy display for the construction clip");
            var fixture=App.Economy.storedFixtures.Find(x=>x.definitionId=="top");
            Require(fixture!=null,"Construction display is in inventory");
            var moved=new PlacedFacility{instanceId=fixture.instanceId,definitionId="top",x=2,z=0,rotation=0};
            Require(App.BuyMaterial("floor_tile",16),"Claim floor finish");
            App.ShowRenovationDrawer();App.OpenRenovationInventory(0);
            yield return Record("05-renovation","装修与空间布置",12,f=>
            {
                if(f==55){App.CollapseRenovationDrawer();App.World.ShowPlacement(moved,true);Mark("select",1);}
                if(f==85){moved.rotation=1;App.World.ShowPlacement(moved,true);Mark("rotate",1);}
                if(f==115){moved.rotation=2;App.World.ShowPlacement(moved,true);Mark("rotate",1);}
                if(f==145){Require(App.ApplyPlacement(moved,out string reason),"Place showcase display: "+reason);App.World.HidePlacement();Mark("place",1);}
                if(f>=175&&f<303&&(f-175)%8==0)
                {int n=(f-175)/8;Require(App.ApplySurface("floor",5+n%4,n/4,"floor_tile"),"Apply a floor tile");Mark("place",1);}
                if(f==315)App.EndRenovation();
            });

            App.EndRenovation();App.ClearFacilitySelection();Overview();
            App.OpenPanel("goods");
            yield return Record("06-management","商品采购与员工安排",12,f=>
            {
                if(f==60){Require(App.OrderProduct("top_tshirt"),"Place a real product order");Mark("purchase",1);}
                if(f==155){App.OpenStaffPost("reception-1");Mark("select",1);}
                if(f==205){Require(App.SelectStaffDraft("tomori"),"Choose Tomori for reception");Mark("select",1);}
                if(f==250){Require(App.ConfirmStaffSelection(),"Confirm staffing selection");Mark("select",1);}
                if(f==330){App.ClearFacilitySelection();Set(App,"panelKey","");}
            });
            // Begin with an empty shop, so no off-duty member can obstruct band assembly.
            App.Economy.testMode=true;
            Require(App.Economy.FillFeverForTesting(),"Prepare collected FEVER in showcase save");
            App.Economy.testMode=false;
            App.SetShopOpen(true);Require(App.BeginPerformance("MyGO!!!!!"),"Start real MyGO performance: "+App.LastMessage);
            Focus(new Vector3(3.7f,1.15f,-2.8f),3.7f,31,45);
            yield return Record("07-performance","FEVER 舞台表演",14,f=>
            {if(f>30)App.World.Orbit(new Vector2(.065f,0));});
            Time.timeScale=4;
            while(App.Economy.performanceRemaining>3.4f)yield return null;
            Time.timeScale=1;
            yield return Record("08-finale","舞台谢幕与庆祝",4,null);
            Require(App.Economy.Validate(App.Catalog,App.Layout,out string invalid),"Final stock and economy invariants: "+invalid);
        }
        void PrepareShowcase()
        {
            var economy=App.Economy;economy.CancelRestock();economy.ReturnAllBaskets();
            foreach(var shelf in economy.shelves){economy.Stock(shelf.productId).available+=shelf.quantity;shelf.quantity=0;}
            foreach(string id in new[]{"uika","mutsumi","umiri","nyamu","sakiko"})
                if(!economy.IsStaffUnlocked(id))Require(App.SignStaff(id),"Sign "+id);
            Require(App.BuyAdditionalStaffPost(FacilityKind.Restock),"Unlock second restock post");
            Require(App.BuyEnvironmentItem("decor_flamingo")&&App.BuyEnvironmentItem("decor_ottoman"),"Own showroom decoration");
            var room=ShopModel.Clone(App.Layout);room.entranceColumn=3;room.facilities.Clear();
            room.facilities.Add(Facility("warehouse-1","warehouse",12,4,0,11));
            room.facilities.Add(Facility("checkout-1","checkout",4,10,3));
            room.facilities.Add(Facility("reception-1","reception",2,8));
            room.facilities.Add(Facility("restock-1","restock",10,9));
            room.facilities.Add(Facility("restock-2","restock",8,9));
            room.facilities.Add(Facility("stage-1","stage",8,1));
            room.facilities.Add(Facility("flamingo-1","decor_flamingo",11,1));
            room.facilities.Add(Facility("ottoman-1","decor_ottoman",1,1));
            int[] xs={3,5,3,6,1,7,9},zs={6,6,3,3,3,6,6};
            for(int i=0;i<ShopProductSet.Categories.Length;i++)room.facilities.Add(Facility(ShopProductSet.Categories[i]+"-1",ShopProductSet.Categories[i],xs[i],zs[i]));
            Require(ShopModel.Validate(room,App.Catalog,out string invalid),"Showcase room validation: "+invalid);
            App.ReplaceLayout(room);
            Require(App.AssignStaffToPost("checkout-1","taki"),"Cashier assigned");
            Require(App.AssignStaffToPost("reception-1","soyo"),"Reception assigned");
            Require(App.AssignStaffToPost("restock-1","anon"),"Primary stock worker assigned");
            Require(App.AssignStaffToPost("restock-2","rana"),"Second stock worker assigned");
            // A staged, internally conserved stock ledger represents a progressed shop.
            // It is only written to the isolated capture profile, never any player profile.
            foreach(var shelf in App.Economy.shelves)
            {
                int n=Array.IndexOf(ShopProductSet.Categories,App.Layout.facilities.Find(x=>x.instanceId==shelf.instanceId).definitionId);
                shelf.productId=ShopProductSet.ProductIds[n];shelf.quantity=10;
                var stock=App.Economy.Stock(shelf.productId);stock.contracted=true;
                stock.available+=35;stock.purchased+=45;
            }
            App.Economy.popularity=85;App.Economy.peakPopularity=100;
            // Film the real costs and proceeds of a progressed finite economy.
            App.Economy.testMode=false;App.Economy.cash=6800;App.Economy.warehouseLevel=3;
            App.World.SetWarehouseLevel(3);
            App.Economy.RefreshDecoration(App.Layout);App.World.SyncProductDisplays(App.Economy);
            App.UserSettings.cameraPitch=33;App.UserSettings.lightingIntensity=1;
            App.UserSettings.lightPitch=48;App.UserSettings.lightYaw=60;App.UserSettings.shadowStrength=.85f;
            App.World.ApplyVisualSettings(App.UserSettings);Set(App,"hideWorldText",true);
            Require(App.Economy.Validate(App.Catalog,App.Layout,out invalid),"Showcase ledger validation: "+invalid);
            Overview();
        }
        static PlacedFacility Facility(string id,string definition,int x,int z,int rotation=0,int wall=0)
            =>new PlacedFacility{instanceId=id,definitionId=definition,x=x,z=z,rotation=rotation,wallPosition=wall};
        void Overview(){App.World.ResetCamera();App.World.Zoom(-1.1f);}
        void Focus(Vector3 point,float size,float pitch,float yaw)
        {
            Set(App.World,"guidedFocusRemaining",0f);Set(App.World,"focus",point);Set(App.World,"size",size);
            Set(App.World,"viewRotation",Quaternion.Euler(pitch,yaw,0));Invoke(App.World,"ApplyCamera");
        }
        IEnumerator CloseAndDrain()
        {
            App.SetShopOpen(false);Time.timeScale=4;
            for(int i=0;i<2400&&App.IsClosing;i++)yield return null;
            Time.timeScale=1;Require(App.CanRenovate,"All customers paid and left before renovation");
        }
        IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
        IEnumerator Record(string name,string title,float duration,Action<int> update)
        {
            int width=Screen.width,height=Screen.height,total=Mathf.RoundToInt(duration*Fps);
            active=new Clip{file=name+".mp4",title=title,width=width,height=height,frames=total,seconds=duration,
                transactionsBefore=App.Economy.transactions,pickupsBefore=App.WarehousePickups,
                extraRestocksBefore=App.ExtraRestocksCompleted,guestsBefore=App.CustomerCount};
            manifest.clips.Add(active);encoderLog.Clear();
            var info=new ProcessStartInfo{FileName=ffmpeg,UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardInput=true,RedirectStandardError=true,
                Arguments="-hide_banner -loglevel warning -y -f rawvideo -pixel_format rgb24 -video_size "+width+"x"+height+
                    " -framerate "+Fps+" -i pipe:0 -an -vf vflip -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -movflags +faststart \""+Path.Combine(output,active.file)+"\""};
            encoder=Process.Start(info);
            encoder.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(encoderLog)encoderLog.AppendLine(e.Data);};encoder.BeginErrorReadLine();
            if(pixels==null||pixels.width!=width||pixels.height!=height)
            {if(pixels!=null)Destroy(pixels);pixels=new Texture2D(width,height,TextureFormat.RGB24,false);}
            int transactions=App.Economy.transactions,pickups=App.WarehousePickups,extra=App.ExtraRestocksCompleted;
            int[] shelfQuantities=new int[App.Economy.shelves.Count];
            for(int i=0;i<shelfQuantities.Length;i++)shelfQuantities[i]=App.Economy.shelves[i].quantity;
            for(activeFrame=0;activeFrame<total;activeFrame++)
            {
                update?.Invoke(activeFrame);
                yield return new WaitForEndOfFrame();
                if(encoder.HasExited)throw new IOException("Encoder exited early: "+encoderLog);
                pixels.ReadPixels(new Rect(0,0,width,height),0,0,false);pixels.Apply(false,false);
                var data=pixels.GetRawTextureData<byte>().ToArray();encoder.StandardInput.BaseStream.Write(data,0,data.Length);
                if(activeFrame==0||activeFrame==90||activeFrame==total-1)
                    File.WriteAllBytes(Path.Combine(output,name+"-"+activeFrame.ToString("0000")+".png"),pixels.EncodeToPNG());
                if(App.Economy.transactions>transactions){Mark("payment",App.Economy.transactions-transactions);transactions=App.Economy.transactions;}
                if(App.WarehousePickups>pickups){Mark("pickup",App.WarehousePickups-pickups);pickups=App.WarehousePickups;}
                if(App.ExtraRestocksCompleted>extra){Mark("restock",App.ExtraRestocksCompleted-extra);extra=App.ExtraRestocksCompleted;}
                for(int i=0;i<Math.Min(shelfQuantities.Length,App.Economy.shelves.Count);i++)
                {int n=App.Economy.shelves[i].quantity;if(n>shelfQuantities[i])Mark("shelf_filled",n-shelfQuantities[i]);shelfQuantities[i]=n;}
                if(activeFrame%30==0)File.WriteAllText(Path.Combine(output,"progress.txt"),name+" "+activeFrame+" / "+total);
            }
            encoder.StandardInput.Close();
            while(!encoder.HasExited)yield return null;
            encoder.WaitForExit();int exit=encoder.ExitCode;encoder.Dispose();encoder=null;
            File.WriteAllText(Path.Combine(output,name+"-ffmpeg.log"),encoderLog.ToString());
            if(exit!=0)throw new IOException("Encoder exit code "+exit+": "+encoderLog);
            active.transactionsAfter=App.Economy.transactions;active.pickupsAfter=App.WarehousePickups;
            active.extraRestocksAfter=App.ExtraRestocksCompleted;active.guestsAfter=App.CustomerCount;
            WriteManifest();active=null;
        }
        void Mark(string action,int value){active?.events.Add(new Moment{seconds=activeFrame/(float)Fps,action=action,value=value});}
        void Require(bool value,string note)
        {manifest.setup.Add((value?"OK ":"FAILED ")+note);if(!value)throw new InvalidOperationException(note);}
        void Set(object target,string field,object value)
        {var f=target.GetType().GetField(field,flags);if(f==null)throw new MissingFieldException(field);f.SetValue(target,value);}
        object Invoke(object target,string method,params object[] args)
        {var m=target.GetType().GetMethod(method,flags);if(m==null)throw new MissingMethodException(method);return m.Invoke(target,args);}
        void WriteManifest()=>File.WriteAllText(Path.Combine(output,"manifest.json"),JsonUtility.ToJson(manifest,true));
        void Fail(string message)
        {
            manifest.error=message;manifest.completed=false;
            try{if(encoder!=null&&!encoder.HasExited)encoder.StandardInput.Close();WriteManifest();}catch{}
            UnityEngine.Debug.LogError("Portfolio capture failed: "+message);Time.captureFramerate=0;Time.timeScale=1;Application.Quit(2);
        }
    }
}
