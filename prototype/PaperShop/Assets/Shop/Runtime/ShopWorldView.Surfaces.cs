using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopWorldView
    {
        readonly System.Collections.Generic.List<Mesh> finishMeshes=new System.Collections.Generic.List<Mesh>();
        void ClearFinishMeshes(){foreach(var mesh in finishMeshes)if(mesh!=null)Destroy(mesh);finishMeshes.Clear();}
        GameObject SurfaceCube(string name,Vector3 center,Vector3 size,Material material,string kind,int columnX,int columnZ)
        {
            var obj=Cube(name,mapRoot,center,size,material);
            var filter=obj.GetComponent<MeshFilter>();
            var mesh=Instantiate(filter.sharedMesh);mesh.name=name+" aligned UV";
            var vertices=mesh.vertices;var uv=new Vector2[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 p=center+Vector3.Scale(vertices[i],size);
                uv[i]=kind=="floor"?new Vector2(p.x+layout.width*.5f-columnX,p.z+layout.depth*.5f-columnZ):
                    kind=="north"?new Vector2(p.x+layout.width*.5f-columnX,p.y/WallHeight):
                    new Vector2(p.z+layout.depth*.5f-columnZ,p.y/WallHeight);
            }
            mesh.uv=uv;filter.sharedMesh=mesh;finishMeshes.Add(mesh);return obj;
        }
        // Every column shares one full-height UV space, including door lintels.
        Material FinishMaterial(string kind,int x,int z,float height=2.72f,float baseY=0)
        {
            string id=ShopSurfaces.Get(layout,kind,x,z);
            var texture=Resources.Load<Texture2D>("ShopTextures/"+id);
            if(texture==null)return MaterialFor(ShopSurfaces.ColorFor(id),kind=="floor"?.29f:.07f);
            string key="finish:"+id;
            if(surfaceMaterials.TryGetValue(key,out var existing))return existing;
            var material=new Material(shader){name=key};
            material.SetColor("_BaseColor",Color.white);
            material.SetTexture("_BaseMap",texture);
            material.SetTextureScale("_BaseMap",Vector2.one);
            material.SetTextureOffset("_BaseMap",Vector2.zero);
            material.SetFloat("_Smoothness",kind=="floor"?.29f:.07f);
            surfaceMaterials[key]=material;materials.Add(material);return material;
        }
        void BuildFloorCells()
        {
            for(int x=0;x<layout.width;x++)for(int z=0;z<layout.depth;z++)
            {
                var center=CellCenter(new Vector2Int(x,z));center.y=-.04f;
                SurfaceCube("Floor cell "+x+","+z,center,new Vector3(1,.08f,1),FinishMaterial("floor",x,z),"floor",x,z);
            }
        }
        void BuildNorthColumns()
        {
            for(int x=0;x<layout.width;x++)
            {
                float bottom=x==ShopModel.EntranceColumn(layout)-1||x==ShopModel.EntranceColumn(layout)?2.1f:0;
                TrackBackWall(SurfaceCube("North wallpaper column "+x,
                    new Vector3(x-layout.width*.5f+.5f,(WallHeight+bottom)*.5f,layout.depth*.5f+WallThickness*.5f),
                    new Vector3(1,WallHeight-bottom,WallThickness),FinishMaterial("north",x,0),"north",x,0));
            }
        }
        void BuildEastColumns(Material trim)
        {
            float half=layout.depth*.5f;
            bool hasDoor=FindWarehouse(out var warehouse,out var definition)&&warehouse.x>=layout.width;
            float center=hasDoor?Mathf.Clamp(CellCenter(ShopModel.ServiceCell(warehouse,definition)).z,-half+.6f,half-.6f):float.MaxValue;
            float min=center-.5f,max=center+.5f;
            for(int z=0;z<layout.depth;z++)
            {
                float start=z-half,end=start+1;
                var material=FinishMaterial("east",0,z);
                SurfaceEastSegment(z,start,Mathf.Min(end,min),material,trim);
                if(end>max)SurfaceEastSegment(z,Mathf.Max(start,max),end,material,trim);
                float a=Mathf.Max(start,min),b=Mathf.Min(end,max);
                if(b>a)SurfaceCube("Warehouse lintel column "+z,
                    new Vector3(layout.width*.5f+WallThickness*.5f,(WallHeight+2.12f)*.5f,(a+b)*.5f),
                    new Vector3(WallThickness,WallHeight-2.12f,b-a),material,"east",0,z);
            }
        }
        void SurfaceEastSegment(int column,float a,float b,Material material,Material trim)
        {
            if(b-a<=.001f)return;
            SurfaceCube("East wallpaper column "+column,new Vector3(layout.width*.5f+WallThickness*.5f,WallHeight*.5f,(a+b)*.5f),
                new Vector3(WallThickness,WallHeight,b-a),material,"east",0,column);
            Cube("East skirting",mapRoot,new Vector3(layout.width*.5f-.012f,.055f,(a+b)*.5f),new Vector3(.024f,.11f,b-a),trim);
        }
        public bool PickWallColumn(Vector2 screen,out string kind,out int column)
        {
            kind="";column=-1;
            if(WorldCamera==null||layout==null)return false;
            Ray ray=WorldCamera.ScreenPointToRay(screen);float nearest=float.MaxValue;
            for(int side=0;side<2;side++)
            {
                var plane=side==0?new Plane(Vector3.back,new Vector3(0,0,layout.depth*.5f)):
                    new Plane(Vector3.left,new Vector3(layout.width*.5f,0,0));
                if(!plane.Raycast(ray,out float distance)||distance>=nearest)continue;
                Vector3 p=ray.GetPoint(distance);
                int c=Mathf.FloorToInt(side==0?p.x+layout.width*.5f:p.z+layout.depth*.5f);
                if(p.y<0||p.y>WallHeight||c<0||c>=(side==0?layout.width:layout.depth))continue;
                if(side==0&&(c==ShopModel.EntranceColumn(layout)-1||c==ShopModel.EntranceColumn(layout))&&p.y<2.1f)continue;
                if(side==1&&FindWarehouse(out var warehouse,out var definition))
                {
                    float doorZ=Mathf.Clamp(CellCenter(ShopModel.ServiceCell(warehouse,definition)).z,-layout.depth*.5f+.6f,layout.depth*.5f-.6f);
                    if(Mathf.Abs(p.z-doorZ)<.5f&&p.y<2.12f)continue;
                }
                kind=side==0?"north":"east";column=c;nearest=distance;
            }
            return column>=0;
        }
    }
}
