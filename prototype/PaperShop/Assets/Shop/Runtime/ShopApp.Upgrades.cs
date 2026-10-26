namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public bool UpgradeSalesCounter(string id)
        {
            bool ok=Economy.UpgradeShelf(catalog,Layout,id,out string reason);
            LastMessage=reason;dirty|=ok;PlaySfx(ok?"upgrade":"error");return ok;
        }
        public bool MoveWarehouseDoor(int wallCell)
        {
            if(wallCell<0 || wallCell>=Layout.depth){LastMessage="仓库门只能放在右侧墙面。";return false;}
            return ApplyPlacement(new PlacedFacility{instanceId="warehouse-1",definitionId="warehouse",x=Layout.width,z=4,wallPosition=wallCell+1},out _);
        }
    }
}
