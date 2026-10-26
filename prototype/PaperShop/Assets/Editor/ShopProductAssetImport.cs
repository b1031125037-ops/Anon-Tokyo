using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using AnonTokyo.Shop;

public static class ShopProductAssetImport
{
    public static void Import(ShopCatalog catalog)
    {
        foreach(string id in ShopProductSet.ProductIds)
        {
            string folder="Assets/Shop/ImportedProducts/"+id+"/",path=folder+"model.prefab";
            if(!File.Exists(path))
            {
                var mesh=new Mesh{name=id,indexFormat=IndexFormat.UInt32};
                using(var reader=new BinaryReader(File.OpenRead(folder+"geometry.bytes")))
                {
                    int count=reader.ReadInt32(),indices=reader.ReadInt32();
                    var points=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
                    for(int i=0;i<count;i++){points[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());normals[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());}
                    var faces=new int[indices];for(int i=0;i<indices;i++)faces[i]=reader.ReadInt32();
                    mesh.vertices=points;mesh.normals=normals;mesh.uv=uv;mesh.triangles=faces;mesh.RecalculateBounds();mesh.RecalculateTangents();
                }
                if(mesh.triangles.Length/3>100000)throw new Exception("Product mesh exceeds runtime budget: "+id);
                AssetDatabase.CreateAsset(mesh,folder+"mesh.asset");
                foreach(string name in new[]{"base","normal","metallic"})
                {
                    var ti=(TextureImporter)AssetImporter.GetAtPath(folder+name+".png");
                    ti.textureType=name=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
                    ti.sRGBTexture=name=="base";ti.mipmapEnabled=true;ti.maxTextureSize=1024;ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.SaveAndReimport();
                }
                var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=id};
                mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"base.png"));
                mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"normal.png"));
                mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"metallic.png"));
                mat.SetFloat("_Metallic",1);mat.SetFloat("_Smoothness",1);mat.EnableKeyword("_NORMALMAP");mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                AssetDatabase.CreateAsset(mat,folder+"material.mat");
                var root=new GameObject(id);var child=new GameObject("Product");child.transform.SetParent(root.transform,false);
                child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<MeshRenderer>().sharedMaterial=mat;
                // All products have a ground-centred origin; dimensions remain in delivered units.
                child.transform.localPosition=new Vector3(-mesh.bounds.center.x,-mesh.bounds.min.y,-mesh.bounds.center.z);
                PrefabUtility.SaveAsPrefabAsset(root,path);UnityEngine.Object.DestroyImmediate(root);
            }
            var product=catalog.products.Find(p=>p.id==id);if(product==null)throw new Exception("Unknown product "+id);
            product.modelPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        foreach(string id in new[]{"mygo","mujica"})
        {
            var ai=(AudioImporter)AssetImporter.GetAtPath("Assets/Resources/Audio/Stage/"+id+".wav");
            var settings=ai.defaultSampleSettings;settings.loadType=AudioClipLoadType.CompressedInMemory;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.8f;settings.preloadAudioData=true;
            ai.defaultSampleSettings=settings;ai.forceToMono=false;ai.SaveAndReimport();
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(ai.assetPath);
            if(Mathf.Abs(clip.length-ShopEconomy.PerformanceLength(id=="mygo"?"MyGO!!!!!":"Ave Mujica"))>.05f)throw new Exception("Stage audio duration mismatch "+id);
        }
        EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
    }
}
