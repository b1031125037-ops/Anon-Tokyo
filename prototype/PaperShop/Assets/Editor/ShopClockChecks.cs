using System;
using System.Collections.Generic;
using System.IO;
using AnonTokyo.Shop;
using UnityEngine;

public static class ShopClockChecks
{
    [Serializable] sealed class Report { public bool passed; public int checks; public string[] results; }

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<string>();
        Action<bool, string> check = (ok, label) =>
        {
            if (!ok) throw new InvalidOperationException("CLOCK CHECK FAILED: " + label);
            results.Add(label);
        };
        var clock = new ShopSimulationClock();
        check(clock.Speed == 1 && clock.ElapsedSeconds == 0 && !clock.IsPaused(false),
            "A new session begins at one-times speed with no elapsed simulation or pause");
        check(Near(clock.Step(.02f, false), .02), "One-times advances by the supplied frame duration");
        check(clock.SetSpeed(2) && Near(clock.Step(.02f, false), .04),
            "Two-times doubles ordinary simulation deltas");
        check(Near(clock.ElapsedSeconds, .06), "Elapsed simulation time includes both speed settings");
        check(!clock.SetSpeed(0) && !clock.SetSpeed(-1) && !clock.SetSpeed(3) && clock.Speed == 2,
            "Unsupported speed edits fail atomically without changing the current rate");
        check(Near(clock.Step(30f, false), .2),
            "The source delta is limited before applying two-times speed");
        check(clock.SetSpeed(1) && Near(clock.Step(30f, false), .1),
            "A long frame at one-times contributes at most a tenth of a simulation second");
        double before = clock.ElapsedSeconds;
        check(clock.Step(0, false) == 0 && clock.Step(-1, false) == 0 &&
              clock.Step(float.NaN, false) == 0 && clock.Step(float.PositiveInfinity, false) == 0 &&
              clock.Step(float.NegativeInfinity, false) == 0 && clock.ElapsedSeconds == before,
            "Zero, negative and non-finite input cannot advance or poison elapsed time");
        check(clock.IsPaused(true) && clock.PauseReason(true) == "手动暂停" &&
              clock.Step(.05f, true) == 0 && clock.ElapsedSeconds == before,
            "Manual pause holds simulation time and identifies the active reason");
        check(!clock.IsApplicationInactive && clock.IsPaused(true),
            "Manual simulation pause does not report the application inactive or silence background music");
        check(Near(clock.Step(.05f, false), .05),
            "Leaving manual pause resumes from the current frame without accumulating paused ticks");

        clock.SetSpeed(2);
        before=clock.ElapsedSeconds;clock.ApplyFocus(false);
        check(clock.IsApplicationInactive&&!clock.IsPaused(false)&&Near(clock.Step(.02f,false),.04)&&clock.ElapsedSeconds>before,
            "Background frames keep advancing live simulation instead of creating offline income");
        clock.ApplyFocus(true);
        check(Near(clock.Step(.02f,false),.04),"Returning focus preserves continuous simulation");
        before=clock.ElapsedSeconds;clock.ApplyFocus(false);
        check(clock.Step(.02f,true)==0&&clock.ElapsedSeconds==before,"Settings pause also freezes an unfocused game");
        clock.ApplyFocus(true);check(clock.Step(.02f,true)==0,"Focus cannot dismiss the settings pause");
        check(Near(clock.Step(.02f,false),.04),"Closing settings resumes at the current frame");
        clock.ApplyApplicationPause(true);clock.ApplyApplicationPause(false);
        check(clock.Step(120f,false)==0&&Near(clock.Step(.02f,false),.04),"An OS suspension cannot produce a giant simulation catch-up frame");

        var one = new ShopSimulationClock();
        var two = new ShopSimulationClock();
        two.SetSpeed(2);
        for (int i = 0; i < 250; i++)
        {
            one.Step(.02f, false);
            two.Step(.02f, false);
        }
        check(Near(one.ElapsedSeconds, 5) && Near(two.ElapsedSeconds, 10),
            "Equal frame sequences produce five versus ten simulation seconds at the two supported speeds");
        float timeScale = Time.timeScale, unscaledDelta = Time.unscaledDeltaTime;
        var soundObject = new GameObject("Clock audio isolation check");
        try
        {
            var source = soundObject.AddComponent<AudioSource>();
            source.pitch = .85f;
            two.SetSpeed(1);
            two.Step(.08f, false);
            two.SetSpeed(2);
            two.Step(.08f, false);
            check(Time.timeScale == timeScale && Time.unscaledDeltaTime == unscaledDelta && Near(source.pitch, .85),
                "Simulation speed edits leave global time scale, unscaled UI delta and audio pitch unchanged");
        }
        finally { UnityEngine.Object.DestroyImmediate(soundObject); }

        File.WriteAllText(Path.Combine(output, "clock-verification.json"), JsonUtility.ToJson(
            new Report { passed = true, checks = results.Count, results = results.ToArray() }, true));
        Debug.Log("SHOP_CLOCK_CHECKS_OK " + results.Count);
    }

    static bool Near(double value, double expected) => Math.Abs(value - expected) < .00001;
}
