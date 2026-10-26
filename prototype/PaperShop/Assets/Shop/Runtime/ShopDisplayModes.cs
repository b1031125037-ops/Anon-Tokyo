using UnityEngine;
namespace AnonTokyo.Shop
{
    public static class ShopDisplayModes
    {
        public static readonly string[] Names={"窗口模式","无边框全屏","独占全屏"};
        public static readonly Vector2Int[] CommonSizes={
            new Vector2Int(960,540),new Vector2Int(1024,640),new Vector2Int(1024,768),new Vector2Int(1152,864),
            new Vector2Int(1280,720),new Vector2Int(1280,768),new Vector2Int(1280,800),new Vector2Int(1280,960),new Vector2Int(1280,1024),
            new Vector2Int(1360,768),new Vector2Int(1360,850),new Vector2Int(1366,768),new Vector2Int(1440,900),
            new Vector2Int(1600,900),new Vector2Int(1600,1000),new Vector2Int(1600,1200),new Vector2Int(1680,1050),
            new Vector2Int(1920,1080),new Vector2Int(1920,1200),new Vector2Int(1920,1440),new Vector2Int(2048,1152),
            new Vector2Int(2560,1080),new Vector2Int(2560,1440),new Vector2Int(2560,1600),new Vector2Int(2880,1800),
            new Vector2Int(3200,1800),new Vector2Int(3440,1440),new Vector2Int(3840,1600),new Vector2Int(3840,2160),new Vector2Int(3840,2400)};
        public static FullScreenMode UnityMode(int mode)=>mode==2?FullScreenMode.ExclusiveFullScreen:mode==1?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed;
    }
}
