using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnonTokyo.Preview
{
    [DefaultExecutionOrder(-500)]
    public class PreviewVerification : MonoBehaviour
    {
        [Serializable]
        private class Result
        {
            public bool framesAdvanced;
            public bool flipCompleted;
            public bool characterMoved;
            public bool pauseHeld;
            public bool resumed;
            public bool passed;
            public int frameCount;
            public int flipCount;
            public float elapsed;
            public string[] errors;
        }

        private readonly List<string> errors = new List<string>();

        private void Awake()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify") >= 0)
                Application.logMessageReceived += CollectError;
        }

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--verify");
            if (index < 0 || index + 1 >= args.Length) yield break;
            var destination = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(destination);
            Application.runInBackground = true;
            var demo = GetComponent<PaperShopDemo>();
            yield return null;
            demo.SetDemoState(0);
            var firstFrame = Time.frameCount;
            var firstTime = demo.ElapsedDemoTime;
            yield return new WaitForSecondsRealtime(1.2f);
            var result = new Result { framesAdvanced = Time.frameCount > firstFrame + 10 && demo.ElapsedDemoTime > firstTime };
            yield return Capture(Path.Combine(destination, "01-idle.png"));

            var flips = demo.FlipCount;
            demo.SetDemoState(1);
            yield return new WaitForSecondsRealtime(0.8f);
            result.flipCompleted = demo.FlipCount > flips;
            yield return Capture(Path.Combine(destination, "02-flipped.png"));

            var position = demo.CharacterPosition;
            demo.SetDemoState(2);
            yield return new WaitForSecondsRealtime(1.3f);
            result.characterMoved = Vector3.Distance(position, demo.CharacterPosition) > 0.1f;
            yield return Capture(Path.Combine(destination, "03-moving.png"));
            yield return new WaitForSecondsRealtime(3.3f);
            yield return Capture(Path.Combine(destination, "03b-behind-plant.png"));

            demo.SetPaused(true);
            var pausedPosition = demo.CharacterPosition;
            var pausedTime = demo.ElapsedDemoTime;
            yield return new WaitForSecondsRealtime(0.5f);
            result.pauseHeld = Vector3.Distance(pausedPosition, demo.CharacterPosition) < 0.001f && Mathf.Abs(demo.ElapsedDemoTime - pausedTime) < 0.001f;
            demo.SetPaused(false);
            yield return new WaitForSecondsRealtime(0.5f);
            result.resumed = demo.ElapsedDemoTime > pausedTime + 0.1f;

            demo.SetDemoState(3);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Capture(Path.Combine(destination, "04-working.png"));
            result.frameCount = Time.frameCount;
            result.flipCount = demo.FlipCount;
            result.elapsed = demo.ElapsedDemoTime;
            result.errors = errors.ToArray();
            result.passed = result.framesAdvanced && result.flipCompleted && result.characterMoved && result.pauseHeld && result.resumed && errors.Count == 0;
            File.WriteAllText(Path.Combine(destination, "verification.json"), JsonUtility.ToJson(result, true));
            Debug.Log("PAPER_SHOP_VERIFICATION " + result.passed);
            Application.logMessageReceived -= CollectError;
            Application.Quit(result.passed ? 0 : 1);
        }

        private IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null)
            {
                errors.Add("No rendered frame was available for capture. Run verification with a visible graphics window.");
                yield break;
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Destroy(texture);
        }

        private void CollectError(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message + "\n" + stackTrace);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= CollectError;
        }
    }
}
