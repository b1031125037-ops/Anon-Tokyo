using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using AnonTokyo.Shop;

public static class ShopSettingsChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public string[] results; public float[] musicSeconds; }

    public static void ConfigureAudioResources()
    {
        foreach (string resource in ShopApp.MusicResources)
        {
            string path = "Assets/Resources/" + resource + ".ogg";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Background music missing: " + path);
            var sample = importer.defaultSampleSettings;
            bool changed = importer.forceToMono || !importer.loadInBackground || !sample.preloadAudioData ||
                sample.loadType != AudioClipLoadType.Streaming || sample.compressionFormat != AudioCompressionFormat.Vorbis ||
                sample.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate || Mathf.Abs(sample.quality - .85f) > .001f;
            if (!changed) continue;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            sample.preloadAudioData = true;
            sample.loadType = AudioClipLoadType.Streaming;
            sample.compressionFormat = AudioCompressionFormat.Vorbis;
            sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            sample.quality = .85f;
            importer.defaultSampleSettings = sample;
            importer.SaveAndReimport();
        }
    }

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (ok, label) =>
        {
            if (!ok) throw new InvalidOperationException("SETTINGS CHECK FAILED: " + label);
            results.Add(label);
        };
        var defaults = ShopUserSettings.Defaults();
        check(defaults.DisplayMode==0,"Legacy window preference resolves to window mode");
        var legacyFull=ShopUserSettings.Defaults();legacyFull.fullscreen=true;
        check(legacyFull.DisplayMode==1,"Legacy fullscreen preference resolves to borderless mode");
        legacyFull.displayMode=2;check(legacyFull.Copy().DisplayMode==2,"Exclusive mode survives preference serialization");
        legacyFull.displayMode=3;check(!legacyFull.Validate(out _),"Invalid display mode is rejected");
        check(ShopDisplayModes.CommonSizes.Length>=30,"Restored common resolution catalog has thirty or more choices");
        check(defaults.Validate(out _), "Defaults pass complete preference validation");
        check(defaults.graphicsQuality == 2 && defaults.shadows && defaults.shadowStrength == 1f &&
              defaults.lightingIntensity == 1f, "Default visual settings preserve current daylight and strong shadows");
        check(defaults.musicTrack == 0 && defaults.musicVolume == .18f && !defaults.musicMuted,
            "Default music is ANON TOKYO at eighteen percent volume");
        check(defaults.windowWidth == 1360 && defaults.windowHeight == 850 && !defaults.fullscreen,
            "Default display remains the existing window size");
        check(defaults.effectsVolume==.9f&&defaults.cameraPitch==25f&&defaults.lightYaw==60f&&defaults.lightPitch==40f,"Requested sound balance and camera/light defaults");
        var changed = defaults.Copy();
        changed.graphicsQuality = 0; changed.shadows = false; changed.shadowStrength = .36f;
        changed.lightingIntensity = 1.2f; changed.musicTrack = 1; changed.musicVolume = .67f;
        changed.musicMuted = true; changed.windowWidth = 1280; changed.windowHeight = 720; changed.fullscreen = true;
        check(changed.Validate(out _), "Independent graphics, music and display changes remain valid");
        string path = Path.Combine(output, "isolated-user-settings.json");
        string displayLegacyPath=Path.Combine(output,"legacy-display.json");
        File.WriteAllText(displayLegacyPath,JsonUtility.ToJson(changed).Replace("\"displayMode\":-1,","").Replace(",\"displayMode\":-1",""));
        check(ShopUserSettings.Load(displayLegacyPath,out _).DisplayMode==1,"Real legacy JSON without displayMode preserves fullscreen preference");
        string legacy=JsonUtility.ToJson(changed).Replace("\"presentationRevision\":2,","");
        string migrationPath=Path.Combine(output,"legacy-mix.json");File.WriteAllText(migrationPath,legacy);
        var migrated=ShopUserSettings.Load(migrationPath,out _);
        check(migrated.musicVolume==.18f&&migrated.effectsVolume==.9f&&migrated.cameraPitch==25f,
            "Legacy audio and visual preferences migrate once to the requested balance");
        check(migrated.musicMuted&&migrated.musicTrack==1&&migrated.fullscreen&&migrated.windowWidth==1280,
            "Migration retains mute, selected music, resolution and fullscreen");
        migrated.musicVolume=.27f;migrated.effectsVolume=.55f;migrated.cameraPitch=42f;migrated.Save(migrationPath,out _);
        var remigrated=ShopUserSettings.Load(migrationPath,out _);
        check(remigrated.musicVolume==.27f&&remigrated.effectsVolume==.55f&&remigrated.cameraPitch==42f,
            "Restart retains the player's later volume and camera adjustments");
        foreach(string effect in ShopSoundPalette.Keys)
        {
            var samples=ShopSoundPalette.Samples(effect);float peak=0;bool finite=true;
            foreach(float sample in samples){finite&=!float.IsNaN(sample)&&!float.IsInfinity(sample);peak=Mathf.Max(peak,Mathf.Abs(sample));}
            check(finite&&peak>.1f&&peak<1f&&samples[0]==0&&Mathf.Abs(samples[samples.Length-1])<.01f,
                "Cartoon effect is audible, finite, click-free and has sample headroom: "+effect);
        }
        check(defaults.Save(path, out _), "Default preferences save to a test-only path");
        check(changed.Save(path, out _) && File.Exists(path + ".bak"), "Atomic update preserves a previous preference backup");
        var loaded = ShopUserSettings.Load(path, out string loadReason);
        check(string.IsNullOrEmpty(loadReason) && JsonUtility.ToJson(loaded) == JsonUtility.ToJson(changed),
            "All preference fields round-trip without touching economy save data");
        File.WriteAllText(path, "damaged preferences");
        var recovered = ShopUserSettings.Load(path, out string recoveredReason);
        check(recovered.Validate(out _) && recovered.musicTrack == 0 && !string.IsNullOrEmpty(recoveredReason),
            "A damaged preference file recovers the valid backup");
        File.WriteAllText(path, "{\"version\":2}");
        var unsupported = ShopUserSettings.Load(path, out string unsupportedReason);
        check(unsupported.Validate(out _) && unsupported.version == 1 && !string.IsNullOrEmpty(unsupportedReason),
            "A parsed but unsupported preference schema also recovers the valid backup");
        File.WriteAllText(path, "{\"musicTrack\":");
        var truncated = ShopUserSettings.Load(path, out string truncatedReason);
        check(truncated.Validate(out _) && !string.IsNullOrEmpty(truncatedReason),
            "Truncated JSON is caught and recovers the valid backup");
        File.WriteAllText(path + ".bak", "damaged backup");
        var fallback = ShopUserSettings.Load(path, out string fallbackReason);
        check(fallback.Validate(out _) && fallback.graphicsQuality == 2 && !string.IsNullOrEmpty(fallbackReason),
            "Invalid primary and backup files safely restore defaults with an explicit message");
        check(ShopUserSettings.Load(Path.Combine(output, "missing-" + Guid.NewGuid().ToString("N")), out _).Validate(out _),
            "A first run starts from valid defaults without requiring an existing file");

        Action<Action<ShopUserSettings>, string> invalid = (edit, label) =>
        {
            var value = defaults.Copy(); edit(value); check(!value.Validate(out _), label);
        };
        invalid(s => s.version = 2, "Unknown preference schema version is rejected");
        invalid(s => s.graphicsQuality = -1, "Negative quality index is rejected");
        invalid(s => s.graphicsQuality = 3, "Undefined quality index is rejected");
        invalid(s => s.shadowStrength = 1.1f, "Out-of-range shadow strength is rejected");
        invalid(s => s.shadowStrength = float.NaN, "NaN shadow strength is rejected");
        invalid(s => s.lightingIntensity = float.PositiveInfinity, "Infinite daylight intensity is rejected");
        invalid(s => s.lightingIntensity = .49f, "A brightness below the supported range is rejected");
        invalid(s => s.musicTrack = 2, "A music index without an imported track is rejected");
        invalid(s => s.musicVolume = -.1f, "Negative volume is rejected");
        invalid(s => s.musicVolume = float.NaN, "NaN volume is rejected");
        invalid(s => s.windowWidth = 0, "Zero display width is rejected");
        invalid(s => s.windowHeight = 99999, "Unbounded display height is rejected");
        defaults.musicVolume = float.PositiveInfinity;
        string before = File.ReadAllText(path);
        check(!defaults.Save(path, out _) && File.ReadAllText(path) == before,
            "Rejected settings cannot overwrite any existing preference file");

        var durations = new float[ShopApp.MusicResources.Length];
        for (int i = 0; i < ShopApp.MusicResources.Length; i++)
        {
            string resource = ShopApp.MusicResources[i];
            string asset = "Assets/Resources/" + resource + ".ogg";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(asset);
            check(clip != null && clip.samples > 100000 && clip.length > 10, "Imported track has real playable samples: " + resource);
            check(clip.channels == 2 && clip.frequency == 48000, "Imported track preserves stereo and forty-eight-kilohertz rate: " + resource);
            float expected = i == 0 ? 44.245333f : 152.277f;
            check(Mathf.Abs(clip.length - expected) < .1f, "Imported duration matches the user's source audio: " + resource);
            var importer = (AudioImporter)AssetImporter.GetAtPath(asset);
            check(importer.defaultSampleSettings.loadType == AudioClipLoadType.Streaming &&
                  importer.defaultSampleSettings.compressionFormat == AudioCompressionFormat.Vorbis &&
                  !importer.forceToMono, "Music uses streamed stereo Vorbis import settings: " + resource);
            durations[i] = clip.length;
        }
        foreach(string key in new[]{"payment","income","pickup","stock"})
        {
            var clip=Resources.Load<AudioClip>("Audio/Effects/"+(key=="payment"||key=="income"?"register":"clothes"));
            clip.LoadAudioData();var samples=new float[clip.samples*clip.channels];clip.GetData(samples,0);
            using(var writer=new BinaryWriter(File.Create(Path.Combine(output,key+"-preview.wav"))))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples.Length*2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);
                writer.Write(clip.frequency);writer.Write(clip.frequency*2);writer.Write((short)2);writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples.Length*2);
                foreach(float sample in samples)writer.Write((short)(Mathf.Clamp(sample,-1,1)*32767));
            }
        }
        File.WriteAllText(Path.Combine(output, "settings-verification.json"),
            JsonUtility.ToJson(new Report { passed = true, checks = results.Count, results = results.ToArray(), musicSeconds = durations }, true));
        Debug.Log("SHOP_SETTINGS_CHECKS_OK " + results.Count);
    }
}
