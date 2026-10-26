using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using AnonTokyo.Shop;

// Import only the verified static mesh delivery converted by Import-UserModels.py.
public static class ShopUserAssetImport
{
    const string Root="Assets/Shop/ImportedModels/";
    static readonly string[] Sources={"display_tower","garment_bench","outfit_rack","shoes","accessory_stand","stepped_shelving","jewelry_pedestal","checkout","warehouse"};
    static readonly string[] Ids={"top","bottom","outfit","shoes","head","bag","accessory","checkout","warehouse"};
    public static void Preview()
    {
        Import(AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Resources/Shop/DefaultCatalog.asset"));
        var output=Path.GetFullPath("../ModelV006Preview");Directory.CreateDirectory(output);
        for(int i=0;i<Sources.Length;i++) RenderViews(Sources[i],output);
        AssetDatabase.SaveAssets();
    }
    public static void Import(ShopCatalog catalog)
    {
        for(int i=0;i<Sources.Length;i++)
        {
            string folder=Root+Sources[i]+"/";
            string prefabPath=folder+"model.prefab";
            if(!File.Exists(folder+"geometry.bytes"))throw new InvalidOperationException("Missing delivered model: "+Sources[i]);
            if(!File.Exists(prefabPath))
            {
                Mesh mesh=new Mesh{name=Sources[i],indexFormat=IndexFormat.UInt32};
                using(var r=new BinaryReader(File.OpenRead(folder+"geometry.bytes")))
                {
                    int nv=r.ReadInt32(),ni=r.ReadInt32();
                    var v=new Vector3[nv];var n=new Vector3[nv];var uv=new Vector2[nv];
                    for(int j=0;j<nv;j++){v[j]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());n[j]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());uv[j]=new Vector2(r.ReadSingle(),r.ReadSingle());}
                    var t=new int[ni];for(int j=0;j<ni;j++)t[j]=r.ReadInt32();
                    mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=t;mesh.RecalculateBounds();mesh.RecalculateTangents();
                }
                AssetDatabase.CreateAsset(mesh,folder+"mesh.asset");
                foreach(string name in new[]{"base","normal","metallic"})
                {
                    var ti=(TextureImporter)AssetImporter.GetAtPath(folder+name+".png");
                    ti.textureType=name=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
                    ti.sRGBTexture=name=="base";ti.mipmapEnabled=true;ti.maxTextureSize=2048;
                    ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.wrapMode=TextureWrapMode.Repeat;ti.SaveAndReimport();
                }
                var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=Sources[i]+" PBR"};
                mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"base.png"));
                mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"normal.png"));
                mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"metallic.png"));
                mat.SetFloat("_Metallic",1);mat.SetFloat("_Smoothness",1);mat.SetFloat("_Cull",2);
                mat.EnableKeyword("_NORMALMAP");mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                AssetDatabase.CreateAsset(mat,folder+"material.mat");
                var root=new GameObject(Sources[i]);var child=new GameObject("Delivered mesh");child.transform.SetParent(root.transform,false);
                child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<MeshRenderer>().sharedMaterial=mat;
                var d=catalog.Find(Ids[i]);var size=mesh.bounds.size;
                float maxHeight=Ids[i]=="warehouse"?2.18f:Ids[i]=="checkout"?1.02f:1.65f;
                float width=Ids[i]=="warehouse"?1.06f:d.width-.14f;
                float depth=Ids[i]=="warehouse"?.24f:d.depth-.14f;
                float scale=Mathf.Min(width/size.x,depth/size.z,maxHeight/size.y);
                child.transform.localScale=Vector3.one*scale;
                child.transform.localPosition=new Vector3(-mesh.bounds.center.x,-mesh.bounds.min.y,-mesh.bounds.center.z)*scale;
                PrefabUtility.SaveAsPrefabAsset(root,prefabPath);UnityEngine.Object.DestroyImmediate(root);
            }
            catalog.Find(Ids[i]).modelPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }
        EditorUtility.SetDirty(catalog);ImportAudio();AssetDatabase.SaveAssets();
    }
    static void ImportAudio()
    {
        Directory.CreateDirectory("Assets/Resources/Audio/Effects");
        var report=new List<string>();
        foreach(string name in new[]{"register","clothes"})
        {
            string source="Assets/Shop/ImportedAudio/"+name+(name=="register"?".wav":".mp3");
            string dest="Assets/Resources/Audio/Effects/"+name+".wav";
            if(File.Exists(dest))continue;
            var importer=(AudioImporter)AssetImporter.GetAtPath(source);
            var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=AudioCompressionFormat.PCM;settings.preloadAudioData=true;
            importer.defaultSampleSettings=settings;importer.forceToMono=true;importer.SaveAndReimport();
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(source);clip.LoadAudioData();
            var samples=new float[clip.samples*clip.channels];
            if(!clip.GetData(samples,0))throw new InvalidOperationException("Cannot decode supplied sound: "+source);
            // Match active 50 ms windows, not file-length RMS (which counts silence).
            int window=Math.Max(1,clip.frequency/20)*clip.channels;double peak=0,maxRms=0;
            var powers=new List<double>();
            for(int s=0;s<samples.Length;s+=window){double energy=0;int count=Math.Min(window,samples.Length-s);
                for(int k=s;k<s+count;k++){energy+=samples[k]*samples[k];peak=Math.Max(peak,Math.Abs(samples[k]));}
                powers.Add(energy/count);maxRms=Math.Max(maxRms,Math.Sqrt(energy/count));}
            double sum=0;int active=0;foreach(double power in powers)if(power>maxRms*maxRms*.01){sum+=power;active++;}
            double rms=Math.Sqrt(sum/Math.Max(1,active));
            // Gentle peak limiting allows both supplied transients to reach the same -20 dBFS active RMS.
            double low=0,high=100;
            for(int pass=0;pass<40;pass++){double gain=(low+high)*.5,energy=0;int count=0;
                for(int s=0,w=0;s<samples.Length;s+=window,w++)if(powers[w]>maxRms*maxRms*.01)
                    for(int k=s;k<Math.Min(s+window,samples.Length);k++){double v=.78*Math.Tanh(samples[k]*gain/.78);energy+=v*v;count++;}
                if(Math.Sqrt(energy/Math.Max(1,count))<.1)low=gain;else high=gain;}
            double finalGain=(low+high)*.5;for(int s=0;s<samples.Length;s++)samples[s]=(float)(.78*Math.Tanh(samples[s]*finalGain/.78));
            int fade=Math.Min(clip.frequency/200,samples.Length/2);for(int s=0;s<fade;s++){samples[s]*=s/(float)fade;samples[samples.Length-1-s]*=s/(float)fade;}
            using(var w=new BinaryWriter(File.Create(dest))){int bytes=samples.Length*2;w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(bytes+36);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)clip.channels);w.Write(clip.frequency);w.Write(clip.frequency*clip.channels*2);w.Write((short)(clip.channels*2));w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(bytes);foreach(float v in samples)w.Write((short)Mathf.RoundToInt(v*32767));}
            AssetDatabase.ImportAsset(dest,ImportAssetOptions.ForceSynchronousImport);
            report.Add(name+": seconds="+clip.length+", original active RMS="+rms+", gain="+finalGain+", target=-20 dBFS active RMS, soft peak ceiling=0.78");
        }
        if(report.Count>0)File.WriteAllLines("../audio-import-v006.txt",report);
    }
    static void RenderViews(string name,string output)
    {
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+name+"/model.prefab"));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
        var bounds=go.GetComponentInChildren<Renderer>().bounds;
        var cameraGo=new GameObject("Asset camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
        var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.68f,.69f,.67f);camera.orthographic=true;camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.y)*.7f;
        var lightGo=new GameObject("Asset light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo,scene);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;
        var rt=new RenderTexture(512,512,24);camera.targetTexture=rt;var old=RenderTexture.active;
        for(int view=0;view<4;view++){
            var offset=Quaternion.Euler(0,view*90,0)*new Vector3(0,1.2f,-4);camera.transform.position=bounds.center+offset;camera.transform.LookAt(bounds.center);
            light.transform.rotation=Quaternion.Euler(35,view*90-30,0);camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(512,512,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+"-"+view+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        RenderTexture.active=old;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    }
}
