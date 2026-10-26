using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        readonly ShopSimulationClock simulationClock = new ShopSimulationClock();
        public bool IsEditingLayout => renovationMode || IsPaintingSurface || panelKey == "build" || !string.IsNullOrEmpty(placingDefinition);

        public int GameSpeed => simulationClock.Speed;
        public double SimulationElapsedSeconds => simulationClock.ElapsedSeconds;
        public bool IsApplicationInactive => simulationClock.IsApplicationInactive;
        public bool IsSimulationPaused => simulationClock.IsPaused(IsPaused||panelKey=="settings");
        public string SimulationPauseReason => simulationClock.PauseReason(IsPaused||panelKey=="settings");
        public string ClockStatus => IsSimulationPaused
            ? "暂停 · " + SimulationPauseReason
            : (IsEditingLayout ? "装修中 · " : "游戏速度 ") + GameSpeed + "×";

        public bool SetGameSpeed(int speed) => (verifyMode || speed==1) && (Economy==null || !Economy.IsPerforming || speed==1) && simulationClock.SetSpeed(speed);
        public float CalculateSimulationDelta(float sourceDelta)
        {
            if(!verifyMode || (Economy!=null && Economy.IsPerforming))simulationClock.SetSpeed(1);
            return simulationClock.Step(sourceDelta, IsPaused||panelKey=="settings");
        }

        // Explicit inputs are also used by regression checks. Verification ignores
        // real window changes so foreground tooling cannot interrupt an automated run.
        public void ApplyApplicationFocus(bool focused) { simulationClock.ApplyFocus(focused); TickSettings(); OnOfflineActivityChanged(); }
        public void ApplyApplicationPause(bool paused) { simulationClock.ApplyApplicationPause(paused); TickSettings(); OnOfflineActivityChanged(); }

        void OnApplicationFocus(bool focused)
        {
            if(!focused)panGesture=false;
            if (!verifyMode && !isMenuVerification) ApplyApplicationFocus(focused);
        }

        void OnApplicationPause(bool paused)
        {
            if (!verifyMode && !isMenuVerification) ApplyApplicationPause(paused);
        }

        void DrawTimeControls(float width)
        {
            Rect box = new Rect(width * .5f - 182f,82f,364f,32f);PanelBox(box);
            GUI.Label(new Rect(box.x+15,box.y+6,334,24),IsSimulationPaused?"暂停 · "+SimulationPauseReason:IsEditingLayout?"装修中 · 时间照常推进":"营业时钟 · 正常速度",body);
        }
    }
}
