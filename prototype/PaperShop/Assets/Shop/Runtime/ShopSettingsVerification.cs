using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Shop
{
    /// <summary>Exercises imported audio playback and live lighting without player preference writes.</summary>
    [DefaultExecutionOrder(-430)]
    public sealed class ShopSettingsVerification : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool passed, timedOut, isolatedPreferences, bothTracksPlaying, singleTrackLooped;
            public bool manualPauseKeepsMusic, focusAudioHeld, audioResumed, systemAudioHeld;
            public bool pausedTrackSwitchPlays, renovationKeepsMusicAndTime, mutedWithoutRestart, speedDoesNotPitch;
            public bool lowQualityApplied, highQualityApplied, shadowsApplied, lightingApplied, roundTrip;
            public bool userFileUntouched, economyUntouched, invalidValuesRejected, defaultsRestored;
            public int checks, screenshots;
            public float[] durations;
            public string[] results, errors;
        }

        ShopApp app;
        Report report;
        bool active, finished;
        string output, userFileAtStart;
        readonly List<string> errors = new List<string>();
        readonly List<string> results = new List<string>();
        float deadline;

        void Awake()
        {
            active = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-settings") >= 0;
            if (active) Application.logMessageReceived += OnLog;
        }
        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message + "\n" + stack);
        }
        void Update()
        {
            if (!active || finished || report == null || Time.realtimeSinceStartup < deadline) return;
            report.timedOut = true; errors.Add("Settings verification timed out."); Finish();
        }
        void Check(bool ok, string label)
        {
            results.Add(label);
            if (!ok) errors.Add(label);
        }
        string UserFile() => File.Exists(ShopUserSettings.SavePath) ? File.ReadAllText(ShopUserSettings.SavePath) : null;

        IEnumerator Start()
        {
            if (!active) yield break;
            deadline = Time.realtimeSinceStartup + 65;
            report = new Report();
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "--verify-settings");
            if (flag + 1 >= args.Length || args[flag + 1].StartsWith("--", StringComparison.Ordinal))
            { errors.Add("--verify-settings requires an output directory."); Finish(); yield break; }
            output = Path.GetFullPath(args[flag + 1]); Directory.CreateDirectory(output);
            userFileAtStart = UserFile();
            Application.runInBackground = true;
            Time.timeScale = 1;
            yield return null;
            app = GetComponent<ShopApp>();
            if (app == null || app.UserSettings == null || app.BackgroundMusic == null)
            { errors.Add("Settings or audio source did not initialize."); Finish(); yield break; }
            report.isolatedPreferences = app.UsesIsolatedSettings;
            Check(report.isolatedPreferences, "Verification does not load or write player preference files");
            app.SetPaused(true);
            string economyBefore = JsonUtility.ToJson(app.Economy);
            app.OpenSettings();
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "01-graphics-settings.png")); report.screenshots++;

            report.lowQualityApplied = app.SetGraphicsQuality(0) && app.SettingsRenderPipeline != null &&
                Mathf.Approximately(app.SettingsRenderPipeline.renderScale, .7f) &&
                app.SettingsRenderPipeline.msaaSampleCount == 1 &&
                app.SettingsRenderPipeline.mainLightShadowmapResolution == 1024;
            Check(report.lowQualityApplied, "Low quality applies lower rendering scale, antialiasing and shadow-map resolution");
            Check(app.SetGraphicsQuality(1) && Mathf.Approximately(app.SettingsRenderPipeline.renderScale, .85f) &&
                  app.SettingsRenderPipeline.msaaSampleCount == 2, "Medium quality applies the intermediate renderer configuration");
            report.highQualityApplied = app.SetGraphicsQuality(2) && Mathf.Approximately(app.SettingsRenderPipeline.renderScale, 1) &&
                app.SettingsRenderPipeline.msaaSampleCount == 4 && app.SettingsRenderPipeline.mainLightShadowmapResolution == 4096;
            Check(report.highQualityApplied, "High quality preserves the original full-resolution four-sample rendering");
            report.shadowsApplied = app.SetShadows(false) && app.World.Daylight.shadows == LightShadows.None &&
                app.SetShadows(true) && app.World.Daylight.shadows == LightShadows.Soft &&
                app.SetShadowStrength(.43f) && Mathf.Approximately(app.World.Daylight.shadowStrength, .43f);
            Check(report.shadowsApplied, "Shadow switch and strength change the actual scene directional light");
            report.lightingApplied = app.SetLightingIntensity(1.25f) && Mathf.Approximately(app.World.Daylight.intensity, 1.3f);
            Check(report.lightingApplied, "Brightness changes the actual daylight intensity without replacing scene assets");

            string snapshot = JsonUtility.ToJson(app.UserSettings);
            report.invalidValuesRejected = !app.SetGraphicsQuality(3) && !app.SetShadowStrength(float.NaN) &&
                !app.SetLightingIntensity(float.PositiveInfinity) && !app.SetMusicTrack(2) && !app.SetMusicVolume(-.1f) &&
                !app.ApplyDisplaySettings(0, 0, true) && JsonUtility.ToJson(app.UserSettings) == snapshot;
            Check(report.invalidValuesRejected, "Unsupported settings reject without mutating the last valid preferences");
            Check(app.DisplayChoices.Count > 0, "Available display settings include a safe supported resolution");
            app.SetMusicTrack(1); app.SetMusicVolume(.31f); app.SetMusicMuted(true);
            string testSettings = Path.Combine(output, "runtime-isolated-settings.json");
            report.roundTrip = app.UserSettings.Save(testSettings, out _) &&
                JsonUtility.ToJson(ShopUserSettings.Load(testSettings, out _)) == JsonUtility.ToJson(app.UserSettings);
            Check(report.roundTrip, "Live graphics and music choices round-trip through an isolated settings file");
            report.economyUntouched = JsonUtility.ToJson(app.Economy) == economyBefore;
            Check(report.economyUntouched, "Preference changes do not modify shop inventory, orders, money or layout");
            app.RestoreDefaultSettings();
            report.defaultsRestored = JsonUtility.ToJson(app.UserSettings) == JsonUtility.ToJson(ShopUserSettings.Defaults());
            Check(report.defaultsRestored, "Restore defaults returns graphics, audio and window preferences to the documented defaults");
            app.SetPaused(false);
            yield return new WaitForSecondsRealtime(.5f);

            report.durations = new float[2];
            bool both = true;
            for (int i = 0; i < 2; i++)
            {
                Check(app.SetMusicTrack(i), "Selected imported track " + ShopApp.MusicTitles[i]);
                var source = app.BackgroundMusic;
                yield return new WaitForSecondsRealtime(.7f);
                if (source.clip == null) { both = false; continue; }
                report.durations[i] = source.clip.length;
                int before = source.timeSamples;
                yield return new WaitForSecondsRealtime(.6f);
                bool playing = source.isPlaying && source.timeSamples > before && source.clip.samples > 100000;
                both &= playing;
                Check(playing, "Actual audio sample cursor advances for " + ShopApp.MusicTitles[i]);
                Check(source.loop && Mathf.Approximately(source.pitch, 1), "Selected track loops with normal pitch");
            }
            report.bothTracksPlaying = both;

            app.SetMusicTrack(0);
            yield return new WaitForSecondsRealtime(.3f);
            var audio = app.BackgroundMusic;
            AudioClip originalClip = audio.clip;
            audio.time = audio.clip.length - .45f;
            yield return new WaitForSecondsRealtime(1.2f);
            report.singleTrackLooped = audio.isPlaying && audio.clip == originalClip && audio.time < 3 &&
                app.UserSettings.musicTrack == 0 && audio.loop;
            Check(report.singleTrackLooped, "Reaching track end wraps the same imported track without advancing the playlist");

            app.SetGameSpeed(2);
            float musicBefore = audio.time;
            yield return new WaitForSecondsRealtime(.8f);
            float musicAdvance = audio.time - musicBefore;
            report.speedDoesNotPitch = audio.pitch == 1 && musicAdvance > .5f && musicAdvance < 1.3f;
            Check(report.speedDoesNotPitch, "Double game speed leaves audio pitch and actual playback rate unchanged");
            app.SetGameSpeed(1);
            app.SetPaused(true);
            yield return new WaitForSecondsRealtime(.2f);
            int pausedSample = audio.timeSamples;
            double pausedSimulation = app.SimulationElapsedSeconds;
            yield return new WaitForSecondsRealtime(.5f);
            report.manualPauseKeepsMusic = !app.IsMusicPaused && audio.isPlaying && audio.timeSamples > pausedSample
                && app.IsSimulationPaused && app.SimulationElapsedSeconds == pausedSimulation;
            Check(report.manualPauseKeepsMusic, "Manual pause freezes shop simulation while the existing BGM cursor keeps advancing");
            report.pausedTrackSwitchPlays = app.SetMusicTrack(1);
            yield return new WaitForSecondsRealtime(.35f);
            report.pausedTrackSwitchPlays &= audio.isPlaying && !app.IsMusicPaused && audio.timeSamples > 0
                && app.UserSettings.musicTrack == 1 && app.IsSimulationPaused;
            Check(report.pausedTrackSwitchPlays, "Switching BGM while manually paused starts the newly selected track immediately");
            app.SetMusicTrack(0);
            yield return new WaitForSecondsRealtime(.3f);
            app.ApplyApplicationFocus(false);
            yield return new WaitForSecondsRealtime(.2f);
            int unfocusedSample = audio.timeSamples;
            yield return new WaitForSecondsRealtime(.4f);
            report.focusAudioHeld = app.IsMusicPaused && !audio.isPlaying && Math.Abs(audio.timeSamples - unfocusedSample) < 200;
            Check(report.focusAudioHeld, "Lost focus independently holds the music cursor");
            app.ApplyApplicationFocus(true);
            yield return new WaitForSecondsRealtime(.4f);
            report.audioResumed = audio.isPlaying && !app.IsMusicPaused && audio.timeSamples > unfocusedSample
                && app.IsPaused && app.IsSimulationPaused && app.SimulationElapsedSeconds == pausedSimulation;
            Check(report.audioResumed, "Returning focus resumes music from the held cursor even while shop simulation remains manually paused");
            app.ApplyApplicationPause(true);
            yield return new WaitForSecondsRealtime(.2f);
            int suspendedSample = audio.timeSamples;
            yield return new WaitForSecondsRealtime(.3f);
            report.systemAudioHeld = app.IsMusicPaused && !audio.isPlaying && Math.Abs(audio.timeSamples - suspendedSample) < 200;
            app.ApplyApplicationPause(false);
            yield return new WaitForSecondsRealtime(.4f);
            report.systemAudioHeld &= !app.IsMusicPaused && audio.isPlaying && audio.timeSamples > suspendedSample
                && app.IsSimulationPaused;
            Check(report.systemAudioHeld, "System suspension and recovery pause and resume BGM independently of the manual shop pause");
            app.SetPaused(false);
            Check(app.ForceResetCustomersForVerification(), "Could not prepare the isolated closed renovation fixture");
            app.OpenPanel("build");
            yield return new WaitForSecondsRealtime(.2f);
            int renovationSample = audio.timeSamples;
            double renovationTime = app.SimulationElapsedSeconds;
            yield return new WaitForSecondsRealtime(.5f);
            report.renovationKeepsMusicAndTime = app.IsEditingLayout && !app.IsSimulationPaused && !app.IsMusicPaused
                && audio.isPlaying && audio.timeSamples > renovationSample && app.SimulationElapsedSeconds > renovationTime + .2;
            Check(report.renovationKeepsMusicAndTime, "A closed shop's renovation menu keeps both BGM and simulation time advancing");
            app.OpenSettings();
            app.SetMusicMuted(true);
            int mutedBefore = audio.timeSamples;
            yield return new WaitForSecondsRealtime(.4f);
            report.mutedWithoutRestart = audio.mute && audio.isPlaying && audio.timeSamples > mutedBefore;
            Check(report.mutedWithoutRestart, "Mute silences the source while keeping playback position moving");
            app.SetMusicMuted(false);
            Check(!audio.mute && audio.isPlaying, "Unmute restores sound without selecting a new track");
            app.SetMusicVolume(.25f);
            Check(Mathf.Approximately(audio.volume, .25f), "Music volume reaches the actual AudioSource");
            app.ShowMusicSettingsForVerification();
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "02-music-settings.png")); report.screenshots++;
            yield return new WaitForSecondsRealtime(.6f);
            report.userFileUntouched = UserFile() == userFileAtStart;
            Check(report.userFileUntouched, "All playback and preference tests leave the user's real settings file untouched");
            Finish();
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            if (report == null) report = new Report();
            report.checks = results.Count;
            report.results = results.ToArray();
            report.errors = errors.ToArray();
            report.passed = errors.Count == 0 && !report.timedOut;
            if (!string.IsNullOrEmpty(output))
                File.WriteAllText(Path.Combine(output, "settings-runtime-verification.json"), JsonUtility.ToJson(report, true));
            Debug.Log("SHOP_SETTINGS_RUNTIME_" + (report.passed ? "OK" : "FAILED"));
            Application.Quit(report.passed ? 0 : 1);
        }
        void OnDestroy() { if (active) Application.logMessageReceived -= OnLog; }
    }
}
