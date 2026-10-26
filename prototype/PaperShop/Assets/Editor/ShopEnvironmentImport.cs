using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using AnonTokyo.Shop;

public static class ShopEnvironmentImport
{
    const string Root="Assets/Shop/ImportedEnvironment/";
    public static void Preview()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Resources/Shop/DefaultCatalog.asset");
        Import(catalog);var output=Path.GetFullPath("../EnvironmentV011Preview");Directory.CreateDirectory(output);
        foreach(string id in new[]{"stage_mygo","stage_mujica","entrance","decor_flamingo","decor_ottoman"})
            for(int angle=0;angle<4;angle++)Render(AssetDatabase.LoadAssetAtPath<GameObject>(Root+id+"/model.prefab"),Path.Combine(output,id+"-"+angle+".png"),768,angle*90,false);
        GenerateIcons(catalog);AssetDatabase.SaveAssets();
    }
    public static void Import(ShopCatalog catalog)
    {
        ShopEnvironmentAssets.Ensure(catalog);
        foreach(string id in new[]{"stage_mygo","stage_mujica","entrance","decor_flamingo","decor_ottoman"})
        {
            string folder=Root+id+"/",path=folder+"model.prefab";
            if(!File.Exists(path))
            {
                var mesh=new Mesh{name=id,indexFormat=IndexFormat.UInt32};
                using(var r=new BinaryReader(File.OpenRead(folder+"geometry.bytes")))
                {
                    int nv=r.ReadInt32(),ni=r.ReadInt32();var v=new Vector3[nv];var n=new Vector3[nv];var uv=new Vector2[nv];
                    for(int i=0;i<nv;i++){v[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());n[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());}
                    var t=new int[ni];for(int i=0;i<ni;i++)t[i]=r.ReadInt32();mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=t;mesh.RecalculateBounds();mesh.RecalculateTangents();
                }
                AssetDatabase.CreateAsset(mesh,folder+"mesh.asset");
                foreach(string name in new[]{"base","normal","metallic"})
                {var ti=(TextureImporter)AssetImporter.GetAtPath(folder+name+".png");ti.textureType=name=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;ti.sRGBTexture=name=="base";ti.maxTextureSize=2048;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.SaveAndReimport();}
                var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=id};
                mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"base.png"));mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"normal.png"));mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"metallic.png"));mat.SetFloat("_Metallic",1);mat.SetFloat("_Smoothness",.65f);mat.EnableKeyword("_NORMALMAP");mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                AssetDatabase.CreateAsset(mat,folder+"material.mat");
                var root=new GameObject(id);var pivot=new GameObject("Orientation");pivot.transform.SetParent(root.transform,false);
                var child=new GameObject("Delivered mesh");child.transform.SetParent(pivot.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<MeshRenderer>().sharedMaterial=mat;
                var size=mesh.bounds.size;
                Vector3 fit=id.StartsWith("stage")?new Vector3(3.85f,2.45f,2.85f):id=="entrance"?new Vector3(1.96f,2.46f,.22f):id=="decor_flamingo"?new Vector3(.82f,1.3f,.82f):new Vector3(.88f,.65f,.88f);
                float scale=Mathf.Min(fit.x/size.x,fit.y/size.y,fit.z/size.z);
                child.transform.localScale=Vector3.one*scale;child.transform.localPosition=new Vector3(-mesh.bounds.center.x,-mesh.bounds.min.y,-mesh.bounds.center.z)*scale;
                if(id.StartsWith("stage"))pivot.transform.localRotation=Quaternion.Euler(0,90,0);
                PrefabUtility.SaveAsPrefabAsset(root,path);UnityEngine.Object.DestroyImmediate(root);
            }
            if(id=="entrance")OpenEntrance(path,folder);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(id=="entrance")catalog.entrancePrefab=prefab;else catalog.Find(id=="stage_mygo"?"stage":id).modelPrefab=prefab;
            string iconPath="Assets/Resources/EnvironmentIcons/"+id+".png";
            Directory.CreateDirectory(Path.GetDirectoryName(iconPath));
            if(!File.Exists(iconPath))Render(prefab,iconPath,256,id.StartsWith("stage")?90:0,true);
        }
        EditorUtility.SetDirty(catalog);GenerateIcons(catalog);
        AssetDatabase.Refresh();
        foreach(string folder in new[]{"Assets/Resources/UI/World","Assets/Resources/EnvironmentIcons"})
        foreach(string file in Directory.GetFiles(folder,"*.png"))
        {var ti=(TextureImporter)AssetImporter.GetAtPath(file);ti.alphaIsTransparency=true;ti.mipmapEnabled=false;ti.wrapMode=TextureWrapMode.Clamp;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.SaveAndReimport();}
        AssetDatabase.SaveAssets();
    }
    static void OpenEntrance(string path,string folder)
    {
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            if(root.transform.Find("Orientation/Delivered mesh/Left open leaf")!=null)return;
            var holder=root.GetComponentInChildren<MeshFilter>();var source=holder.sharedMesh;
            var v=source.vertices;var n=source.normals;var uv=source.uv;var t=source.triangles;
            var groups=new System.Collections.Generic.List<int>[] {new System.Collections.Generic.List<int>(),new System.Collections.Generic.List<int>(),new System.Collections.Generic.List<int>()};
            for(int i=0;i<t.Length;i+=3)
            {
                var c=(v[t[i]]+v[t[i+1]]+v[t[i+2]])/3;
                int group=Mathf.Abs(c.x)<.805f&&c.y>-.908f&&c.y<.836f?(c.x<0?1:2):0;
                groups[group].Add(t[i]);groups[group].Add(t[i+1]);groups[group].Add(t[i+2]);
            }
            for(int group=0;group<3;group++)
            {
                var hinge=group==0?Vector3.zero:new Vector3(group==1?-.805f:.805f,0,0);
                var vertices=new Vector3[v.Length];for(int i=0;i<v.Length;i++)vertices[i]=v[i]-hinge;
                var mesh=new Mesh{name="Entrance part "+group,indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.normals=n;mesh.uv=uv;mesh.triangles=groups[group].ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh,folder+"open-part-"+group+".asset");
                if(group==0)holder.sharedMesh=mesh;
                else
                {var leaf=new GameObject(group==1?"Left open leaf":"Right open leaf");leaf.transform.SetParent(holder.transform,false);leaf.transform.localPosition=hinge;leaf.transform.localRotation=Quaternion.Euler(0,group==1?-80:80,0);leaf.AddComponent<MeshFilter>().sharedMesh=mesh;leaf.AddComponent<MeshRenderer>().sharedMaterial=holder.GetComponent<MeshRenderer>().sharedMaterial;}
            }
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void GenerateIcons(ShopCatalog catalog)
    {
        string folder="Assets/Resources/ProductIcons/";Directory.CreateDirectory(folder);
        foreach(var product in ShopProductSet.CurrentProducts(catalog))
        {
            string path=folder+product.id+".png";
            if(!File.Exists(path))Render(product.modelPrefab,path,256,0,true);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.mipmapEnabled=false;ti.maxTextureSize=256;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.SaveAndReimport();
            product.icon=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
    static void Render(GameObject prefab,string output,int pixels,float yaw,bool transparent)
    {
        if(prefab==null)throw new Exception("Missing icon source "+output);
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var go=UnityEngine.Object.Instantiate(prefab);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
        var bounds=go.GetComponentInChildren<Renderer>().bounds;foreach(var renderer in go.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
        var cameraObject=new GameObject("Preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=transparent?Color.clear:new Color(.72f,.74f,.72f);camera.orthographic=true;
        var offset=Quaternion.Euler(0,yaw,0)*new Vector3(.45f,.4f,-2);camera.transform.position=bounds.center+offset*5;camera.transform.LookAt(bounds.center);
        camera.orthographicSize=Mathf.Max(bounds.size.y,Mathf.Sqrt(bounds.size.x*bounds.size.x+bounds.size.z*bounds.size.z))*.65f;
        var lightObject=new GameObject("Preview key");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,yaw-30,0);
        var rt=new RenderTexture(pixels,pixels,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;var old=RenderTexture.active;camera.Render();RenderTexture.active=rt;
        var image=new Texture2D(pixels,pixels,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,pixels,pixels),0,0);image.Apply();File.WriteAllBytes(output,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active=old;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    }
}
