using UnityEditor;
using UnityEngine;

public sealed class ShopArtImporter:AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        bool ui=assetPath.StartsWith("Assets/Resources/ShopUI/");
        bool card=assetPath.StartsWith("Assets/Resources/Characters/");
        bool surface=assetPath.StartsWith("Assets/Resources/ShopTextures/");
        if(!ui&&!card&&!surface)return;
        var importer=(TextureImporter)assetImporter;
        importer.textureType=TextureImporterType.Default;
        importer.sRGBTexture=true;
        importer.alphaIsTransparency=ui||card;
        importer.mipmapEnabled=surface||card;
        importer.wrapMode=surface?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
        importer.filterMode=surface?FilterMode.Trilinear:FilterMode.Bilinear;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=surface?4096:2048;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.isReadable=false;
    }
}
