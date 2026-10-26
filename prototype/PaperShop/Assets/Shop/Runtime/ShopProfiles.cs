using System;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public static class ShopProfiles
    {
        public static bool StartRequested;
        public static int SelectedSlot=1;
        public static string VerificationRoot;
        public static string Root=>string.IsNullOrEmpty(VerificationRoot)?Application.persistentDataPath:VerificationRoot;
        public static string PathFor(int slot)
        {
            if(slot<1||slot>4)throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(Root,slot==4?"shop-infinite-v2.json":"shop-slot-"+slot+"-v2.json");
        }
        public static string Label(int slot)=>slot==4?"无限资源模式":"存档 "+slot;
        public static bool Exists(int slot)=>File.Exists(PathFor(slot))||File.Exists(PathFor(slot)+".bak");
        public static bool Delete(int slot,out string reason)
        {
            if(slot<1||slot>4){reason="请选择要删除的存档。";return false;}
            try
            {
                // Exact slot files only; never remove the directory or shared preferences.
                string path=PathFor(slot);
                foreach(string suffix in new[]{".bak",".tmp",""})if(File.Exists(path+suffix))File.Delete(path+suffix);
                string directory=Path.GetDirectoryName(path),prefix=Path.GetFileName(path)+".tmp-";
                if(Directory.Exists(directory))foreach(string temporary in Directory.EnumerateFiles(directory,prefix+"*"))
                    if(Guid.TryParseExact(Path.GetFileName(temporary).Substring(prefix.Length),"N",out _))File.Delete(temporary);
                reason="";return true;
            }
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException)
            {reason="未能完整删除存档，请重试："+ex.Message;return false;}
        }
        public static ShopSession Read(int slot,ShopCatalog catalog,out string reason)
        {
            var session=ShopSession.LoadFromPath(catalog,PathFor(slot),out reason);
            if(session!=null&&session.economy.testMode!=(slot==4))
            {reason="存档模式不匹配，已保留文件。";return null;}
            return session;
        }
        public static void MaximizeSandbox(ShopCatalog catalog,ShopLayout layout,ShopEconomy economy)
        {
            if(!economy.testMode||economy.areaExpansionLevel>=4)return;
            bool open=economy.shopOpen;economy.shopOpen=false;
            economy.CancelRestock();economy.ReturnAllBaskets();
            while(economy.areaExpansionLevel<4)
            {
                economy.popularity=economy.PopularityCap;economy.peakPopularity=economy.popularity;
                if(!economy.TryExpandShop(catalog,layout,out string reason))throw new InvalidOperationException(reason);
            }
            economy.shopOpen=open;
        }
        public static ShopSession NewGame(ShopCatalog catalog,bool infinite)
        {
            var layout=ShopModel.CreateDefault();
            var economy=ShopEconomy.CreateDefault(catalog,layout);
            var prepared=ShopSalesTestSetup.Prepare(layout,economy,catalog);
            prepared.layout.facilities.RemoveAll(f=>catalog.Find(f.definitionId)?.kind==FacilityKind.Shelf&&f.definitionId!="top");
            prepared.economy.SyncShelves(catalog,prepared.layout,out _);
            prepared.economy.cash=800;
            prepared.economy.balanceVersion=1;
            foreach(var f in prepared.layout.facilities)prepared.economy.freeDecorationIds.Add(f.instanceId);
            prepared.economy.RefreshDecoration(prepared.layout);
            if(infinite){prepared.economy.EnableTestResources();MaximizeSandbox(catalog,prepared.layout,prepared.economy);}
            // A new normal shop starts closed so the player can order and arrange stock.
            prepared.economy.shopOpen=infinite;
            prepared.economy.InitializeFixtureStore(catalog,prepared.layout,out _);
            for(int x=0;x<prepared.layout.width;x++)
            {
                ShopSurfaces.Apply(prepared.layout,"north",x,0,"wall_cream",out _);
                for(int z=0;z<prepared.layout.depth;z++)ShopSurfaces.Apply(prepared.layout,"floor",x,z,"floor_wood",out _);
            }
            for(int z=0;z<prepared.layout.depth;z++)ShopSurfaces.Apply(prepared.layout,"east",0,z,"wall_cream",out _);
            prepared.economy.InitializeRenovationInventory(prepared.layout);
            prepared.economy.managementRulesVersion=1;
            prepared.economy.tutorialVersion=2;
            prepared.economy.tutorialStep=0;
            return prepared;
        }
    }
}
