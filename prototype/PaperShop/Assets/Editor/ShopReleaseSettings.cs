using UnityEditor;
using UnityEngine;

public static class ShopReleaseSettings
{
    public static void Configure()
    {
        // Keep source resolution and alpha. BC7 stores the PC card/UI textures
        // at one byte per pixel instead of uncompressed four-byte RGBA.
        int changed=0;
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null||importer.textureType==TextureImporterType.NormalMap)continue;
            var settings=importer.GetPlatformTextureSettings("Standalone");
            if(settings.overridden&&settings.format==TextureImporterFormat.BC7)continue;
            settings.name="Standalone";settings.overridden=true;
            settings.maxTextureSize=importer.maxTextureSize;
            settings.format=TextureImporterFormat.BC7;settings.compressionQuality=100;
            settings.crunchedCompression=false;
            importer.SetPlatformTextureSettings(settings);importer.SaveAndReimport();changed++;
        }
        Debug.Log("SHOP_RELEASE_BC7_TEXTURES "+changed);
    }
}
