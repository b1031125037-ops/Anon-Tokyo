using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    [Serializable]
    public sealed class SurfaceFinish
    {
        public string kind, styleId;
        public int x, z;
    }

    public static class ShopSurfaces
    {
        public static readonly string[] WallIds={"wall_default","wall_cream","wall_sage","wall_rose"};
        public static readonly string[] FloorIds={"floor_default","floor_wood","floor_tile"};
        public static readonly string[] WallNames={"原色墙面","奶油墙纸","鼠尾草墙纸","浅粉墙纸"};
        public static readonly string[] FloorNames={"原色地板","浅木色地板","浅色石砖"};
        public static string Default(string kind)=>kind=="floor"?"floor_default":"wall_default";
        public static bool ValidCell(ShopLayout layout,string kind,int x,int z)
            =>kind=="floor"?x>=0&&x<layout.width&&z>=0&&z<layout.depth:
              kind=="north"?x>=0&&x<layout.width&&z==0:kind=="east"&&x==0&&z>=0&&z<layout.depth;
        public static bool ValidStyle(string kind,string id)
            =>Array.IndexOf(kind=="floor"?FloorIds:WallIds,id)>=0;
        public static string Get(ShopLayout layout,string kind,int x,int z)
            =>layout.finishes?.Find(f=>f.kind==kind&&f.x==x&&f.z==z)?.styleId??Default(kind);
        public static bool Apply(ShopLayout layout,string kind,int x,int z,string style,out string reason)
        {
            if(!ValidCell(layout,kind,x,z)||!ValidStyle(kind,style))
            {reason="请选择店内地板格或有效墙面列。";return false;}
            if(!Validate(layout,out reason))return false;
            layout.surfaceVersion=1;
            layout.finishes.RemoveAll(f=>f.kind==kind&&f.x==x&&f.z==z);
            if(style!=Default(kind))layout.finishes.Add(new SurfaceFinish{kind=kind,x=x,z=z,styleId=style});
            reason=kind=="floor"?"已更换这一格地板。":"已更换这一列墙纸。";return true;
        }
        public static bool Validate(ShopLayout layout,out string reason)
        {
            reason="";
            if(layout.surfaceVersion<0||layout.surfaceVersion>1||layout.finishes==null||
               (layout.surfaceVersion==0&&layout.finishes.Count!=0))
            {reason="墙地装修记录版本无效。";return false;}
            var seen=new HashSet<string>();
            foreach(var f in layout.finishes)
                if(f==null||!ValidCell(layout,f.kind,f.x,f.z)||!ValidStyle(f.kind,f.styleId)||
                   !seen.Add(f.kind+":"+f.x+":"+f.z))
                {reason="墙地装修记录重复、越界或材料无效。";return false;}
            return true;
        }
        public static List<SurfaceFinish> Copy(List<SurfaceFinish> source)
            =>source==null?null:source.ConvertAll(f=>f==null?null:new SurfaceFinish{kind=f.kind,x=f.x,z=f.z,styleId=f.styleId});
        public static Color ColorFor(string id)
        {
            switch(id)
            {
                case "wall_cream":return new Color(.87f,.83f,.72f);
                case "wall_sage":return new Color(.65f,.73f,.63f);
                case "wall_rose":return new Color(.81f,.68f,.67f);
                case "floor_wood":return new Color(.70f,.57f,.40f);
                case "floor_tile":return new Color(.87f,.86f,.82f);
                case "floor_default":return new Color(.79f,.78f,.74f);
                default:return new Color(.75f,.73f,.68f);
            }
        }
    }
}
