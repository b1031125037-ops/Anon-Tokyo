using UnityEngine;

namespace AnonTokyo.Shop
{
    public static class ShopStageFormation
    {
        public static string[] Members(string band)=>band=="Ave Mujica"
            ?new[]{"mutsumi","uika","umiri","sakiko","nyamu"}
            :new[]{"anon","tomori","rana","soyo","taki"};
        // The 4-cell side is the local -X front. Three people span its length.
        public static Vector2 LocalPosition(int index)=>index<3
            ?new Vector2(0,index*1.5f):new Vector2(1.7f,index==3?.65f:2.35f);
        public static Vector2 GridPosition(PlacedFacility stage,FacilityDefinition definition,int index)
        {
            Vector2 o=ShopModel.TransformLocalCell(stage,definition,Vector2Int.zero);
            Vector2 x=(Vector2)ShopModel.TransformLocalCell(stage,definition,Vector2Int.right)-o;
            Vector2 z=(Vector2)ShopModel.TransformLocalCell(stage,definition,Vector2Int.up)-o;
            Vector2 local=LocalPosition(index);return o+x*local.x+z*local.y;
        }
    }
}
