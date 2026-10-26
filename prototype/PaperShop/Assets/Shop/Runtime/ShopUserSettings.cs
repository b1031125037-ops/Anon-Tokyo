using System;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Device preferences are independent of either economy save profile.</summary>
    [Serializable]
    public sealed class ShopUserSettings
    {
        public int version = 1;
        public int graphicsQuality = 2;
        public bool shadows = true;
        public float shadowStrength = 1f;
        public float lightingIntensity = 1f;
        public int musicTrack;
        public float musicVolume = .18f;
        public bool musicMuted;
        public float effectsVolume=.9f,cameraPitch=25f,lightYaw=60f,lightPitch=40f;
        public int presentationRevision=2;
        public int windowWidth = 1360, windowHeight = 850;
        public bool fullscreen;
        // -1 reads legacy fullscreen; 0 window, 1 borderless, 2 exclusive.
        public int displayMode=-1;
        public int DisplayMode=>displayMode<0?(fullscreen?1:0):displayMode;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "shop-user-settings-v1.json");
        public static ShopUserSettings Defaults() => new ShopUserSettings();
        public ShopUserSettings Copy() => JsonUtility.FromJson<ShopUserSettings>(JsonUtility.ToJson(this));

        public bool Validate(out string reason)
        {
            if (version != 1) { reason = "设置版本不受支持。"; return false; }
            if (graphicsQuality < 0 || graphicsQuality > 2) { reason = "画质档位无效。"; return false; }
            if (!InRange(shadowStrength, 0, 1) || !InRange(lightingIntensity, .5f, 1.5f))
            { reason = "光影设置超出范围。"; return false; }
            if(!InRange(effectsVolume,0,1)||!InRange(cameraPitch,-10,85)||!InRange(lightYaw,-180,180)||!InRange(lightPitch,10,85))
            {reason="声音或视角参数超出范围。";return false;}
            if (musicTrack < 0 || musicTrack >= 2 || !InRange(musicVolume, 0, 1))
            { reason = "音乐设置超出范围。"; return false; }
            if (windowWidth < 960 || windowWidth > 7680 || windowHeight < 540 || windowHeight > 4320)
            { reason = "显示分辨率超出范围。"; return false; }
            reason = "";
            if(displayMode < -1 || displayMode > 2){reason="显示模式无效。";return false;}
            return true;
        }

        static bool InRange(float value, float low, float high) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= low && value <= high;

        public static ShopUserSettings Load(string path, out string reason)
        {
            reason = "";
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return Defaults();
            string firstError = "";
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    string json = File.ReadAllText(candidate);
                    if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{", StringComparison.Ordinal))
                        throw new InvalidDataException("设置文件格式无效。");
                    var settings = JsonUtility.FromJson<ShopUserSettings>(json);
                    string invalid = "";
                    if (settings == null || !settings.Validate(out invalid))
                        throw new InvalidDataException(settings == null ? "设置文件为空。" : invalid);
                    if(!json.Contains("\"displayMode\""))settings.displayMode=settings.fullscreen?1:0;
                    // One-time requested rebalance. Subsequent player changes are preserved.
                    if (!json.Contains("\"presentationRevision\"") || settings.presentationRevision < 2)
                    {
                        settings.shadowStrength=1f;settings.lightingIntensity=1f;
                        settings.cameraPitch=25f;settings.lightYaw=60f;settings.lightPitch=40f;
                        settings.musicVolume=.18f;settings.effectsVolume=.9f;settings.presentationRevision=2;
                    }
                    reason = candidate == path ? "" : "设置文件异常，已恢复上次备份。";
                    return settings;
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException ||
                                           ex is ArgumentException)
                { if (string.IsNullOrEmpty(firstError)) firstError = ex.Message; }
            }
            reason = "设置文件无法读取，已使用默认设置：" + firstError;
            return Defaults();
        }

        public bool Save(string path, out string reason)
        {
            if (!Validate(out reason)) return false;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(this, true));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                reason = "";
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is ArgumentException)
            { reason = "设置暂未保存：" + ex.Message; return false; }
        }
    }
}
