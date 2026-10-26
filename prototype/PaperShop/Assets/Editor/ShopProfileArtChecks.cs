using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopProfileArtChecks
{
    [Serializable] class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("PROFILE ART CHECK FAILED: "+label);results.Add(label);};
        string originalRoot=ShopProfiles.VerificationRoot;
        try
        {
            ShopProfiles.VerificationRoot=output;
            var paths=new HashSet<string>();
            for(int slot=1;slot<=4;slot++)check(paths.Add(ShopProfiles.PathFor(slot)),"Independent path for slot "+slot);
            bool invalid=false;try{ShopProfiles.PathFor(0);}catch(ArgumentOutOfRangeException){invalid=true;}
            check(invalid,"Invalid slot cannot construct a save path");
            for(int slot=1;slot<=4;slot++)
            {
                var session=ShopProfiles.NewGame(catalog,slot==4);
                check(session.economy.tutorialVersion==2&&session.economy.tutorialStep==0,"Fresh slot requests tutorial choice "+slot);
                check(session.economy.Validate(catalog,session.layout,out _)&&ShopModel.ValidateCheckoutAccess(session.layout,catalog,out _),"Valid fresh layout and economy "+slot);
                check(session.economy.shelves.Count==1&&session.economy.testMode==(slot==4)&&session.economy.cash==800,"One initial display and correct finite/infinite mode "+slot);
                check(session.layout.finishes.Count==(slot==4?168:80)&&ShopSurfaces.Get(session.layout,"floor",0,0)=="floor_wood","New store uses imported finishes "+slot);
                session.economy.cash=2000+slot;
                ShopSession.SaveToPath(session.layout,session.economy,catalog,ShopProfiles.PathFor(slot));
            }
            for(int slot=1;slot<=4;slot++)check(ShopProfiles.Read(slot,catalog,out _)?.economy.cash==2000+slot,"Slot progress remains isolated "+slot);
            var protectedSlot=File.ReadAllBytes(ShopProfiles.PathFor(1));
            File.WriteAllText(Path.Combine(output,"shop-user-settings-v1.json"),"preserve-settings");
            string deletePath=ShopProfiles.PathFor(3);File.Copy(deletePath,deletePath+".bak",true);File.WriteAllText(deletePath+".tmp","pending");
            string interruptedSave=deletePath+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllText(interruptedSave,"interrupted write");
            check(!ShopProfiles.Delete(0,out _),"Invalid deletion slot is rejected");
            check(ShopProfiles.Delete(3,out _)&&!ShopProfiles.Exists(3)&&!File.Exists(deletePath+".tmp")&&!File.Exists(interruptedSave),"Deletion removes only chosen primary, backup and pending files");
            check(Convert.ToBase64String(File.ReadAllBytes(ShopProfiles.PathFor(1)))==Convert.ToBase64String(protectedSlot)&&ShopProfiles.Exists(2)&&ShopProfiles.Exists(4),"Deleting one slot preserves other slots");
            check(File.ReadAllText(Path.Combine(output,"shop-user-settings-v1.json"))=="preserve-settings","Slot deletion preserves shared preferences");
            check(ShopProfiles.Delete(3,out _),"Deleting an empty slot is harmless");
            check(ShopProfiles.Delete(4,out _)&&!ShopProfiles.Exists(4),"Infinite slot may also be deleted independently");
            var wrong=ShopProfiles.NewGame(catalog,true);
            ShopSession.SaveToPath(wrong.layout,wrong.economy,catalog,ShopProfiles.PathFor(1));
            check(ShopProfiles.Read(1,catalog,out _)==null,"Infinite state cannot load into a normal slot");
            var e=ShopProfiles.NewGame(catalog,false).economy;
            var tutorial=ShopProfiles.NewGame(catalog,false);
            tutorial.economy.tutorialStep=11;tutorial.economy.tutorialTransactionBaseline=3;
            tutorial.economy.tutorialUpgradeBaseline=1;tutorial.economy.tutorialDeferred=true;
            string tutorialPath=Path.Combine(output,"tutorial-roundtrip.json");
            ShopSession.SaveToPath(tutorial.layout,tutorial.economy,catalog,tutorialPath);
            var restored=ShopSession.LoadFromPath(catalog,tutorialPath,out _).economy;
            check(restored.tutorialVersion==2&&restored.tutorialStep==11&&restored.tutorialDeferred&&restored.tutorialTransactionBaseline==3&&restored.tutorialUpgradeBaseline==1,"All tutorial state survives transactional save decoding");
            tutorial.economy.tutorialVersion=0;tutorial.economy.tutorialStep=0;tutorial.economy.tutorialDeferred=false;
            ShopSession.SaveToPath(tutorial.layout,tutorial.economy,catalog,tutorialPath);
            check(ShopSession.LoadFromPath(catalog,tutorialPath,out _).economy.tutorialVersion==0,"Existing legacy saves do not opt into tutorial");
            foreach(string ui in new[]{"dialogue-wide","dialogue-compact","welcome-panel","renovation-drawer"})
                check(Resources.Load<Texture2D>("ShopUI/V5/components/"+ui)!=null,"Onboarding UI asset loads "+ui);
            foreach(var post in e.staffPosts)
                check(!ShopApp.IsEmployeeOffDuty(e,post.staffId),"Assigned employee excluded from visits "+post.staffId);
            check(!ShopApp.IsEmployeeOffDuty(e,"uika"),"Unsigned employees excluded from visits");
            check(ShopApp.IsEmployeeOffDuty(e,"tomori")||ShopApp.IsEmployeeOffDuty(e,"rana"),"Unassigned initial employees can visit");
            foreach(var id in new[]{"anon","tomori","soyo","taki","rana","uika","mutsumi","umiri","nyamu","sakiko"})
            {
                string folder=Path.Combine(Application.dataPath,"Resources/Characters",id);
                check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder,"carry.png")))==
                    Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder,"shopping.png"))),
                    id+" customer carry uses the reviewed shopping-bag card");
                check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder,"carry.png")))!=
                    Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder,"restock.png"))),
                    id+" customer carry never aliases employee restocking");
            }
            check(ShopApp.GuestCardPose(ShopApp.GuestPhase.ShoppingQueue,true,2)=="walk",
                "Walking to a second display stays empty-handed despite an existing basket");
            check(ShopApp.GuestCardPose(ShopApp.GuestPhase.ShoppingQueue,false,2)=="think",
                "Waiting at a second display stays empty-handed despite an existing basket");
            check(ShopApp.GuestCardPose(ShopApp.GuestPhase.Browsing,false,2)=="interact",
                "Selecting more items uses the empty-handed interaction card");
            check(ShopApp.GuestCardPose(ShopApp.GuestPhase.CheckoutQueue,true,2)=="carry"&&
                ShopApp.GuestCardPose(ShopApp.GuestPhase.CheckoutQueue,false,2)=="carry",
                "A completed basket uses a shopping bag while approaching and waiting at checkout");
            check(ShopApp.GuestCardPose(ShopApp.GuestPhase.Paying,false,2)=="pay",
                "Payment takes precedence over a carried basket");
            check(ShopApp.GuestCardPose(ShopApp.GuestPhase.Leaving,true,2)=="carry"&&
                ShopApp.GuestCardPose(ShopApp.GuestPhase.Leaving,true,0)=="walk",
                "Departure distinguishes purchased goods from an empty or returned basket");
            foreach(var id in new[]{"anon","tomori","soyo","taki","rana","uika","mutsumi","umiri","nyamu","sakiko"})
            foreach(var pose in new[]{"idle","walk","cashier","welcome","think","restock","interact","pay","carry"})
            {
                var image=Resources.Load<Texture2D>("Characters/"+id+"/"+pose);
                check(image!=null&&image.width==512&&image.height==768,id+" "+pose+" card loads with fixed anchor canvas");
            }
            foreach(var id in new[]{"guest01","guest02","guest03","guest04","guest05"})
            foreach(var pose in new[]{"idle","walk","think","interact","pay","carry"})
            {
                var card=Resources.Load<Texture2D>("Characters/"+id+"/"+pose);
                check(card!=null&&card.width==512&&card.height==768,"Guest action imported "+id+" / "+pose);
            }
            foreach(string key in new[]{"open-normal","open-hover","open-pressed","closing-normal","closed-normal","closed-hover","closed-pressed"})
                check(Resources.Load<Texture2D>("ShopUI/V3/components/business-"+key)!=null,"Three-state business plaque loads: "+key);
            foreach(var id in new[]{"wall_cream","wall_sage","floor_wood","floor_tile"})
            {
                var image=Resources.Load<Texture2D>("ShopTextures/"+id);
                check(image!=null&&image.wrapMode==TextureWrapMode.Repeat,"Repeat texture loaded "+id);
            }
            foreach(var id in new[]{"components/panel","components/primary-normal","components/menu-start-normal","components/save-slot-normal","icons/currency","icons/storage-capacity"})
                check(Resources.Load<Texture2D>("ShopUI/"+id)!=null,"Fabric UI resource loaded "+id);
        }
        finally{ShopProfiles.VerificationRoot=originalRoot;}
        File.WriteAllText(Path.Combine(output,"profile-art-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_PROFILE_ART_CHECKS_OK "+results.Count);
    }
}
