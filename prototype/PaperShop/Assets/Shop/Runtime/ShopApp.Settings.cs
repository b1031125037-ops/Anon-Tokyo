using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public static readonly string[] MusicTitles = { "ANON TOKYO", "BangDream OST - 主界面" };
        public static readonly string[] MusicResources = { "Audio/AnonTokyo", "Audio/BangDreamMain" };
        public ShopUserSettings UserSettings { get; private set; }
        public AudioSource BackgroundMusic { get; private set; }
        public bool IsMusicPaused => musicPaused;
        public UniversalRenderPipelineAsset SettingsRenderPipeline => settingsPipeline;
        public bool UsesIsolatedSettings => verifyMode;
        public IReadOnlyList<Vector2Int> DisplayChoices => displayChoices;
        public string SettingsMessage => settingsMessage;

        readonly List<Vector2Int> displayChoices = new List<Vector2Int>();
        AudioClip[] musicClips;
        UniversalRenderPipelineAsset settingsPipeline;
        RenderPipelineAsset previousDefaultPipeline, previousQualityPipeline;
        bool settingsPending, musicPaused;
        float settingsSaveAt;
        int settingsTab, draftResolution,draftDisplayMode;
        string settingsMessage = "";
        static bool nativeFullscreenApplied;

        void InitializeSettings()
        {
            bool saved = !verifyMode && !isMenuVerification && (File.Exists(ShopUserSettings.SavePath) || File.Exists(ShopUserSettings.SavePath + ".bak"));
            UserSettings = (verifyMode||isMenuVerification) ? ShopUserSettings.Defaults() : ShopUserSettings.Load(ShopUserSettings.SavePath, out settingsMessage);
            previousDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
            previousQualityPipeline = QualitySettings.renderPipeline;
            var source = (previousQualityPipeline != null ? previousQualityPipeline : previousDefaultPipeline) as UniversalRenderPipelineAsset;
            if (source != null)
            {
                settingsPipeline = Instantiate(source);
                settingsPipeline.name = "Shop runtime graphics preferences";
                GraphicsSettings.defaultRenderPipeline = settingsPipeline;
                QualitySettings.renderPipeline = settingsPipeline;
            }
            musicClips = new AudioClip[MusicResources.Length];
            for (int i = 0; i < musicClips.Length; i++) musicClips[i] = Resources.Load<AudioClip>(MusicResources[i]);
            BackgroundMusic = gameObject.AddComponent<AudioSource>();
            BackgroundMusic.playOnAwake = false;
            BackgroundMusic.loop = true;
            BackgroundMusic.spatialBlend = 0;
            BackgroundMusic.pitch = 1f;
            BackgroundMusic.priority = 128;
            BackgroundMusic.ignoreListenerPause = false;
            if (World.WorldCamera.GetComponent<AudioListener>() == null)
                World.WorldCamera.gameObject.AddComponent<AudioListener>();
            ApplyGraphicsSettings();
            ApplyMusicSelection();
            RebuildDisplayChoices();
            if (saved) ApplyDisplaySettings(UserSettings.windowWidth, UserSettings.windowHeight, UserSettings.DisplayMode);
            if(!verifyMode&&!isMenuVerification&&!nativeFullscreenApplied&&Array.IndexOf(Environment.GetCommandLineArgs(),"--native-fullscreen")>=0)
            {nativeFullscreenApplied=true;var native=NativeDisplaySize();ApplyDisplaySettings(native.x,native.y,1);}
            SyncDisplayDraft();
            if(saved) SettingsChanged();
        }

        void TickSettings()
        {
            if (UserSettings == null || BackgroundMusic == null) return;
            // Manual pause and renovation only affect shop interaction. Audio
            // pauses solely while the application itself is inactive.
            BackgroundMusic.pitch = 1f;
            bool paused = (!frontEnd&&Economy.IsPerforming);
            if (paused && !musicPaused) { BackgroundMusic.Pause(); musicPaused = true; }
            else if (!paused && musicPaused) { BackgroundMusic.UnPause(); musicPaused = false; }
            TickStageMusic();
            if (settingsPending && Time.unscaledTime >= settingsSaveAt) SaveUserSettings();
        }

        void SettingsChanged()
        {
            settingsPending = true;
            settingsSaveAt = Time.unscaledTime + .45f;
            settingsMessage = (verifyMode||isMenuVerification) ? "验证设置：不写入玩家设置文件。" : "设置已生效，正在自动保存。";
        }

        void SaveUserSettings()
        {
            if (!settingsPending || UserSettings == null) return;
            settingsPending = false;
            if (verifyMode||isMenuVerification) return;
            settingsMessage = UserSettings.Save(ShopUserSettings.SavePath, out string reason)
                ? "设置已自动保存。" : reason;
        }

        void ApplyGraphicsSettings()
        {
            if (UserSettings == null) return;
            int quality = UserSettings.graphicsQuality;
            if (settingsPipeline != null)
            {
                settingsPipeline.renderScale = quality == 0 ? .7f : quality == 1 ? .85f : 1f;
                settingsPipeline.msaaSampleCount = quality == 0 ? 1 : quality == 1 ? 2 : 4;
                settingsPipeline.mainLightShadowmapResolution = quality == 0 ? 1024 : quality == 1 ? 2048 : 4096;
                settingsPipeline.shadowDistance = quality == 0 ? 30 : 45;
            }
            World.ApplyVisualSettings(UserSettings);
        }

        public bool SetGraphicsQuality(int value)
        {
            if (UserSettings == null || value < 0 || value > 2) return false;
            UserSettings.graphicsQuality = value; ApplyGraphicsSettings(); SettingsChanged(); return true;
        }
        public bool SetShadows(bool value)
        {
            if (UserSettings == null) return false;
            UserSettings.shadows = value; ApplyGraphicsSettings(); SettingsChanged(); return true;
        }
        public bool SetShadowStrength(float value)
        {
            if (UserSettings == null || float.IsNaN(value) || value < 0 || value > 1) return false;
            UserSettings.shadowStrength = value; ApplyGraphicsSettings(); SettingsChanged(); return true;
        }
        public bool SetLightingIntensity(float value)
        {
            if (UserSettings == null || float.IsNaN(value) || value < .5f || value > 1.5f) return false;
            UserSettings.lightingIntensity = value; ApplyGraphicsSettings(); SettingsChanged(); return true;
        }

        void ApplyMusicSelection()
        {
            if (BackgroundMusic == null) return;
            BackgroundMusic.Stop();
            BackgroundMusic.clip = musicClips[UserSettings.musicTrack];
            BackgroundMusic.loop = true;
            BackgroundMusic.pitch = 1f;
            BackgroundMusic.volume = UserSettings.musicVolume;
            BackgroundMusic.mute = UserSettings.musicMuted;
            musicPaused = false;
            if (BackgroundMusic.clip != null)
            {
                BackgroundMusic.Play();
                if ((!frontEnd&&Economy.IsPerforming)) { BackgroundMusic.Pause(); musicPaused = true; }
            }
            else settingsMessage = "音乐资源未加载，请重新导入音频。";
        }

        public bool SetMusicTrack(int track)
        {
            if (UserSettings == null || track < 0 || track >= MusicResources.Length || musicClips[track] == null) return false;
            if (UserSettings.musicTrack == track) return true;
            UserSettings.musicTrack = track; ApplyMusicSelection(); SettingsChanged(); return true;
        }
        public bool SetMusicVolume(float value)
        {
            if (UserSettings == null || float.IsNaN(value) || value < 0 || value > 1) return false;
            UserSettings.musicVolume = value; BackgroundMusic.volume = value; SettingsChanged(); return true;
        }
        public void SetMusicMuted(bool value)
        {
            if (UserSettings == null) return;
            UserSettings.musicMuted = value; BackgroundMusic.mute = value; SettingsChanged();
        }

        void RebuildDisplayChoices()
        {
            displayChoices.Clear();
            Action<int, int> add = (w, h) =>
            {
                var size = new Vector2Int(w, h);
                if (w >= 960 && h >= 540 && w <= 7680 && h <= 4320 && !displayChoices.Contains(size))
                    displayChoices.Add(size);
            };
            foreach(var size in ShopDisplayModes.CommonSizes)add(size.x,size.y);
            foreach (Resolution resolution in Screen.resolutions) add(resolution.width, resolution.height);
            add(Screen.width, Screen.height);
            var native=NativeDisplaySize();add(native.x,native.y);add(UserSettings.windowWidth,UserSettings.windowHeight);
            displayChoices.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        }

        void SyncDisplayDraft()
        {
            RebuildDisplayChoices();draftDisplayMode = UserSettings.DisplayMode;
            draftResolution = displayChoices.IndexOf(new Vector2Int(UserSettings.windowWidth, UserSettings.windowHeight));
            if (draftResolution < 0) draftResolution = Mathf.Max(0, displayChoices.IndexOf(new Vector2Int(Screen.width, Screen.height)));
        }

        public bool ApplyDisplaySettings(int width, int height, bool fullscreen)
            =>ApplyDisplaySettings(width,height,fullscreen?1:0);
        public static Vector2Int NativeDisplaySize()
        {
            var info=Screen.mainWindowDisplayInfo;
            return new Vector2Int(info.width>0?info.width:Screen.currentResolution.width,info.height>0?info.height:Screen.currentResolution.height);
        }
        public bool ApplyDisplaySettings(int width, int height, int mode)
        {
            if(UserSettings==null||mode<0||mode>2||!displayChoices.Contains(new Vector2Int(width,height)))return false;
            if(mode==1){var native=NativeDisplaySize();width=native.x;height=native.y;}
            if(mode==2&&!Array.Exists(Screen.resolutions,r=>r.width==width&&r.height==height))
            {settingsMessage="此独占分辨率不受显示器支持，请选原生分辨率或无边框全屏。";return false;}
            UserSettings.windowWidth=width;UserSettings.windowHeight=height;UserSettings.fullscreen=mode!=0;UserSettings.displayMode=mode;
            // Automation validates the preference contract without resizing the desktop.
            if ((!verifyMode&&!isMenuVerification)||(isMenuVerification&&Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-display")>=0))
                Screen.SetResolution(width,height,ShopDisplayModes.UnityMode(mode));
            draftResolution=Mathf.Max(0,displayChoices.IndexOf(new Vector2Int(width,height)));draftDisplayMode=mode;settingsMessage="";
            SettingsChanged();
            return true;
        }

        public void RestoreDefaultSettings()
        {
            UserSettings = ShopUserSettings.Defaults();
            ApplyGraphicsSettings(); ApplyMusicSelection();
            if (!verifyMode) Screen.SetResolution(UserSettings.windowWidth, UserSettings.windowHeight, FullScreenMode.Windowed);
            SyncDisplayDraft(); SettingsChanged();
        }

        public void OpenSettings()
        {
            SyncDisplayDraft();
            if (panelKey != "settings") OpenPanel("settings");
        }

        public void ShowMusicSettingsForVerification()
        {
            settingsTab = 1;
            OpenSettings();
        }

        void DrawSettingsPopup(Rect box)
        {
            if(UserSettings==null)return;
            float x=box.x+22,y=box.y+61;
            string[] tabs={"画面","音量","帮助",frontEnd?"退出游戏":"返回主菜单"};
            for(int i=0;i<4;i++)if(ArtButton(new Rect(x+i*144,y,134,33),tabs[i],settingsTab==i?activeButton:button))
            {if(i==3){if(frontEnd)ExitWithSave();else if(!ReturnToTitle())settingsMessage=LastMessage;}else settingsTab=i;}
            y+=50;
            if(settingsTab==0)
            {
                GUI.Label(new Rect(x,y+6,120,25),"画质",body);
                for(int i=0;i<3;i++)if(ArtButton(new Rect(x+140+i*144,y,136,32),new[]{"低","中","高"}[i],UserSettings.graphicsQuality==i?activeButton:button))SetGraphicsQuality(i);
                y+=40;
                if(ArtButton(new Rect(x,y,570,30),UserSettings.shadows?"阴影：开":"阴影：关",button))SetShadows(!UserSettings.shadows);y+=39;
                SettingSlider(x,ref y,"阴影强度",UserSettings.shadowStrength,0,1,v=>SetShadowStrength(v));
                SettingSlider(x,ref y,"光照亮度",UserSettings.lightingIntensity,.5f,1.5f,v=>SetLightingIntensity(v));
                SettingSlider(x,ref y,"镜头仰俯角",UserSettings.cameraPitch,-10,85,v=>{UserSettings.cameraPitch=v;ApplyGraphicsSettings();SettingsChanged();});
                SettingSlider(x,ref y,"光源方向",UserSettings.lightYaw,-180,180,v=>{UserSettings.lightYaw=v;ApplyGraphicsSettings();SettingsChanged();});
                SettingSlider(x,ref y,"光源高度",UserSettings.lightPitch,10,85,v=>{UserSettings.lightPitch=v;ApplyGraphicsSettings();SettingsChanged();});
                if(ArtButton(new Rect(x,y,160,32),ShopDisplayModes.Names[draftDisplayMode],button))draftDisplayMode=(draftDisplayMode+1)%3;
                if(ArtButton(new Rect(x+174,y,38,32),"‹",button))draftResolution=(draftResolution+displayChoices.Count-1)%displayChoices.Count;
                var size=displayChoices[draftResolution];GUI.Label(new Rect(x+222,y+6,180,26),size.x+" × "+size.y,body);
                if(ArtButton(new Rect(x+398,y,38,32),"›",button))draftResolution=(draftResolution+1)%displayChoices.Count;
                if(ArtButton(new Rect(x+448,y,122,32),"应用",activeButton))ApplyDisplaySettings(size.x,size.y,draftDisplayMode);
                y+=38;var native=NativeDisplaySize();
                if(ArtButton(new Rect(x,y,160,28),"原生分辨率",button))draftResolution=Mathf.Max(0,displayChoices.IndexOf(native));
                GUI.Label(new Rect(x+174,y+3,396,26),draftDisplayMode==1?"全屏铺满屏幕 · "+native.x+" × "+native.y:displayChoices.Count+" 种分辨率 · "+(draftResolution+1)+" / "+displayChoices.Count,small);
            }
            else if(settingsTab==1)
            {
                for(int i=0;i<MusicTitles.Length;i++)if(ArtButton(new Rect(x,y+i*45,570,36),MusicTitles[i],UserSettings.musicTrack==i?activeButton:button))SetMusicTrack(i);
                y+=108;
                SettingSlider(x,ref y,"音乐音量",UserSettings.musicVolume,0,1,v=>SetMusicVolume(v));
                SettingSlider(x,ref y,"音效音量",UserSettings.effectsVolume,0,1,v=>{UserSettings.effectsVolume=v;SettingsChanged();});
                if(ArtButton(new Rect(x,y,570,35),UserSettings.musicMuted?"音乐：静音":"音乐：播放",button))SetMusicMuted(!UserSettings.musicMuted);
                GUI.Label(new Rect(x,y+55,570,45),"当前背景音乐单曲循环",small);
            }
            else
            {
                GUI.Label(new Rect(x,y,570,300),"左键：查看展柜和岗位；员工会自动工作。\n左键拖动：平移画面；中键拖动：旋转视角；方向键：平移。\n滚轮：缩放。\n在画面设置中调整仰俯角和光源方向。\n\n标题旁开关营业。关店后，等顾客付款离开再装修。\n装修时左键摆放、R 旋转、右键取消，箭头标明正面。\n展柜低于 50% 自动补货。商品页面进货并查看配送。\n左上角升级店铺；商店购买柜台、岗位和装修材料。\nEsc 打开设置；返回主菜单会自动保存。关闭窗口会询问保存。",body);
            }
            if(settingsTab==2&&!frontEnd&&ArtButton(new Rect(x,y+310,570,35),"爱音开店指南",button))ReviewTutorial();
            if(ArtButton(new Rect(x,box.yMax-66,570,33),"恢复默认设置",button))RestoreDefaultSettings();
            if(!string.IsNullOrEmpty(settingsMessage))GUI.Label(new Rect(x,box.yMax-30,570,25),settingsMessage,small);
        }
        void SettingSlider(float x,ref float y,string label,float value,float low,float high,Action<float> setter)
        {
            GUI.Label(new Rect(x,y,190,27),label+"  "+value.ToString("0.##"),body);
            float changed=GUI.HorizontalSlider(new Rect(x+200,y+8,370,22),value,low,high);
            if(Mathf.Abs(changed-value)>.001f){setter(changed);PlaySfx("move");}y+=39;
        }

        static string FormatMusicTime(float seconds) => ((int)seconds / 60).ToString("00") + ":" + ((int)seconds % 60).ToString("00");

        void DisposeSettings()
        {
            SaveUserSettings();
            if (BackgroundMusic != null) BackgroundMusic.Stop();
            if (settingsPipeline != null)
            {
                if (GraphicsSettings.defaultRenderPipeline == settingsPipeline) GraphicsSettings.defaultRenderPipeline = previousDefaultPipeline;
                if (QualitySettings.renderPipeline == settingsPipeline) QualitySettings.renderPipeline = previousQualityPipeline;
                Destroy(settingsPipeline);
            }
        }
    }
}
