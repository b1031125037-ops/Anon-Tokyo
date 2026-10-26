using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopUserAssetChecks
{
    [Serializable] class Report {public bool passed;public int checks;public string[] results;}
    public static void Run(ShopCatalog catalog,string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new InvalidOperationException("USER ASSET CHECK: "+label);results.Add(label);};
        foreach(string id in new[]{"top","bottom","outfit","shoes","head","bag","accessory","checkout","warehouse"})
        {
            var d=catalog.Find(id);check(d.modelPrefab!=null,"Delivered prefab assigned: "+id);
            var go=UnityEngine.Object.Instantiate(d.modelPrefab);
            try{
                check(go.transform.localScale==Vector3.one&&go.transform.position==Vector3.zero&&go.transform.rotation==Quaternion.identity,"Neutral prefab root: "+id);
                check(go.GetComponentsInChildren<Collider>().Length==0,"Imported geometry does not override grid picking: "+id);
                var renderer=go.GetComponentInChildren<Renderer>();var b=renderer.bounds;
                check(Mathf.Abs(b.min.y)<.001f&&Mathf.Abs(b.center.x)<.001f&&Mathf.Abs(b.center.z)<.001f,"Ground centered imported mesh: "+id);
                if(id!="warehouse")for(int rotation=0;rotation<4;rotation++){
                    go.transform.rotation=Quaternion.Euler(0,rotation*90,0);b=renderer.bounds;var footprint=ShopModel.Size(d,rotation);
                    check(b.size.x<=footprint.x-.1f&&b.size.z<=footprint.y-.1f,"Mesh fits rotated footprint: "+id+" / "+rotation);}
                else check(b.size.x<=1.07f&&b.size.z<=.24f&&b.size.y>2&&b.size.y<=2.19f,"Warehouse is a thin full height wall door");
                check(renderer.sharedMaterial.shader.name=="Universal Render Pipeline/Lit"&&renderer.sharedMaterial.GetTexture("_BaseMap")!=null&&renderer.sharedMaterial.GetTexture("_BumpMap")!=null&&renderer.sharedMaterial.GetTexture("_MetallicGlossMap")!=null,"URP PBR maps are complete: "+id);
            }finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        var levels=new List<double>();
        foreach(string name in new[]{"register","clothes"}){
            var clip=Resources.Load<AudioClip>("Audio/Effects/"+name);check(clip!=null,"Supplied sound is bundled: "+name);
            var data=new float[clip.samples*clip.channels];clip.LoadAudioData();check(clip.GetData(data,0),"Supplied sound decodes: "+name);
            check(Mathf.Abs(clip.length-(name=="register"?3:1.32f))<.025f,"Supplied sound keeps complete duration: "+name);
            double peak=0,maxPower=0;int window=clip.frequency/20*clip.channels;var powers=new List<double>();
            for(int s=0;s<data.Length;s+=window){double energy=0;int n=Math.Min(window,data.Length-s);for(int k=s;k<s+n;k++){checkFinite(data[k]);energy+=data[k]*data[k];peak=Math.Max(peak,Math.Abs(data[k]));}powers.Add(energy/n);maxPower=Math.Max(maxPower,energy/n);}
            double active=0;int count=0;foreach(var p in powers)if(p>maxPower*.01){active+=p;count++;}
            double db=10*Math.Log10(active/count);levels.Add(db);
            check(peak>.1&&peak<.8,"Supplied sound is audible and has peak headroom: "+name);
            check(Math.Abs(db+20)<1,"Supplied sound active loudness is near target: "+name+" / "+db.ToString("F2")+" dBFS");
        }
        check(Math.Abs(levels[0]-levels[1])<1,"Register and cloth active loudness match within one decibel");
        File.WriteAllText(Path.Combine(output,"user-assets-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
        Debug.Log("SHOP_USER_ASSET_CHECKS_OK "+results.Count);
    }
    static void checkFinite(float v){if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidOperationException("Nonfinite audio sample");}
}
