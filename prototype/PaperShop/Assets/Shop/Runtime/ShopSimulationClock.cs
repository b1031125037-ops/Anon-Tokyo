using System;

namespace AnonTokyo.Shop
{
    /// <summary>Only advances shop simulation; UI, camera input and audio retain their own clocks.</summary>
    public sealed class ShopSimulationClock
    {
        public const float MaximumSourceDelta = .1f;
        public int Speed { get; private set; } = 1;
        public bool IsFocused { get; private set; } = true;
        public bool IsApplicationPaused { get; private set; }
        public bool IsApplicationInactive => !IsFocused || IsApplicationPaused;
        public double ElapsedSeconds { get; private set; }
        bool discardResumeFrame;

        public bool SetSpeed(int speed)
        {
            if (speed != 1 && speed != 2) return false;
            Speed = speed;
            return true;
        }

        public void ApplyFocus(bool focused)
        {
            if (IsFocused == focused) return;
            IsFocused = focused;
            // Focus is metadata only: the desktop player keeps simulating in the background.
        }

        public void ApplyApplicationPause(bool paused)
        {
            if (IsApplicationPaused == paused) return;
            IsApplicationPaused = paused;
            discardResumeFrame = true;
        }

        public bool IsPaused(bool manuallyPaused)
        {
            return manuallyPaused;
        }

        public string PauseReason(bool manuallyPaused)
        {
            if (manuallyPaused) return "手动暂停";

            return "";
        }

        public float Step(float sourceDelta, bool manuallyPaused)
        {
            if (IsPaused(manuallyPaused)) return 0f;
            if (discardResumeFrame)
            {
                discardResumeFrame = false;
                return 0f;
            }
            if (float.IsNaN(sourceDelta) || float.IsInfinity(sourceDelta) || sourceDelta <= 0f)
                return 0f;
            float delta = Math.Min(sourceDelta, MaximumSourceDelta) * Speed;
            ElapsedSeconds += delta;
            return delta;
        }
    }
}
