using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A small, fixed-command bridge for an already-open Editor. Commands are processed on
/// the Editor main thread; no command text is evaluated or passed to a shell.
/// </summary>
[InitializeOnLoad]
public static class ShopEditorBridge
{
    private const string ScenePath = "Assets/Scenes/ShopFoundation.unity";
    private const string LastIdKey = "AnonTokyo.ShopEditorBridge.LastId";
    private const string ActiveKey = "AnonTokyo.ShopEditorBridge.Active";
    private const string ResultKey = "AnonTokyo.ShopEditorBridge.PendingResult";
    private static double nextPoll;
    private static bool buildCallActive;
    private static string lastReadProblem;
    private static string lastWriteProblem;

    [Serializable]
    private sealed class Command
    {
        public string id;
        public string action;
    }

    [Serializable]
    private sealed class Pending
    {
        public string id;
        public string action;
        public string phase;
        public double phaseStartedAt;
    }

    [Serializable]
    private sealed class Result
    {
        public string id;
        public string status;
        public string message;
    }

    private static string BridgeDirectory
    {
        get
        {
            var project = Directory.GetParent(Application.dataPath);
            if (project == null || project.Parent == null)
                throw new InvalidOperationException("Cannot locate the prototype directory.");
            return project.Parent.FullName;
        }
    }

    static ShopEditorBridge()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    /// <summary>
    /// Entry point for Unity -executeMethod ShopEditorBridge.OpenPreview.
    /// Run without -quit, -batchmode or -nographics to keep the visible Editor open.
    /// </summary>
    public static void OpenPreview()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(ActiveKey, string.Empty)))
        {
            Debug.LogWarning("Shop Editor bridge already has a pending command; OpenPreview was not queued.");
            return;
        }
        Accept(new Command { id = "open-preview-" + Guid.NewGuid().ToString("N"), action = "play" }, false);
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        FlushResult();
        try
        {
            string json = SessionState.GetString(ActiveKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                ReadCommand();
                return;
            }
            var pending = JsonUtility.FromJson<Pending>(json);
            if (pending == null || string.IsNullOrEmpty(pending.id) || !KnownAction(pending.action))
            {
                SessionState.EraseString(ActiveKey);
                Debug.LogError("Shop Editor bridge rejected an invalid saved command state.");
                return;
            }
            try { Advance(pending); }
            catch (Exception exception)
            {
                Complete(pending, "failed", exception.GetType().Name + ": " + exception.Message);
                Debug.LogException(exception);
            }
        }
        catch (Exception exception)
        {
            ReportReadProblem("Shop Editor bridge: " + exception.Message);
        }
    }

    private static void ReadCommand()
    {
        string path = Path.Combine(BridgeDirectory, "editor-command.json");
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > 8192)
        {
            ReportReadProblem("Shop Editor bridge rejected a command file larger than 8 KiB.");
            return;
        }
        Command command;
        try { command = JsonUtility.FromJson<Command>(File.ReadAllText(path, Encoding.UTF8)); }
        catch (Exception exception)
        {
            // A writer may be replacing this file. Retry next poll without accepting its ID.
            ReportReadProblem("Shop Editor bridge is waiting for a complete command JSON: " + exception.Message);
            return;
        }
        if (command == null || string.IsNullOrWhiteSpace(command.id) || command.id.Length > 128)
        {
            ReportReadProblem("Shop Editor bridge command requires a non-empty id of at most 128 characters.");
            return;
        }
        if (command.id == SessionState.GetString(LastIdKey, string.Empty)) return;
        lastReadProblem = null;
        Accept(command);
    }

    private static void Accept(Command command, bool externalCommand = true)
    {
        // Record acceptance before any side effects, including refresh/play domain reloads.
        if (externalCommand) SessionState.SetString(LastIdKey, command.id);
        if (!KnownAction(command.action))
        {
            WriteResult(command.id, "failed", "Unsupported action. Use build, play, stop or refresh.");
            return;
        }
        var pending = new Pending { id = command.id, action = command.action };
        SetPhase(pending, "queued");
        WriteResult(command.id, "pending", "Command accepted: " + command.action);
    }

    private static void Advance(Pending pending)
    {
        if (pending.phase == "building")
        {
            if (buildCallActive || BuildPipeline.isBuildingPlayer) return;
            // A domain reload interrupted the call. Never automatically build a second time.
            Complete(pending, "failed", "Build was interrupted by a domain reload; check the Console and submit a new command id.");
            return;
        }
        if (pending.phase == "refreshing")
        {
            if (EditorBusy() || EditorApplication.timeSinceStartup - pending.phaseStartedAt < 2.0) return;
            Complete(pending, "success", "Asset refresh completed; the Editor remains open.");
            return;
        }
        if (pending.phase == "entering-play")
        {
            if (EditorApplication.isPlaying)
            {
                FocusGameView();
                Complete(pending, "success", "Shop Foundation is playing in the Game view.");
            }
            else if (!EditorBusy() && !EditorApplication.isPlayingOrWillChangePlaymode &&
                     EditorApplication.timeSinceStartup - pending.phaseStartedAt > 20.0)
            {
                Complete(pending, "failed", "Play mode did not start. Check the Console for compilation errors or a cancelled transition.");
            }
            return;
        }

        if (pending.action == "play" && EditorApplication.isPlaying && IsFoundationSceneOnly())
        {
            EditorApplication.isPaused = false;
            FocusGameView();
            Complete(pending, "success", "Shop Foundation is already playing.");
            return;
        }
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        if (EditorBusy()) return;

        switch (pending.action)
        {
            case "stop":
                Complete(pending, "success", "Play mode stopped; the Editor remains open.");
                break;
            case "refresh":
                SetPhase(pending, "refreshing");
                WriteResult(pending.id, "running", "Refreshing assets and waiting for imports/compilation.");
                AssetDatabase.Refresh();
                break;
            case "build":
                EnsureNoDirtyScenes();
                SetPhase(pending, "building");
                WriteResult(pending.id, "running", "Building the Windows foundation preview.");
                buildCallActive = true;
                try { BuildShopFoundation.Build(); }
                finally { buildCallActive = false; }
                Complete(pending, "success", "Windows foundation build completed; the Editor remains open.");
                break;
            case "play":
                // A dirty foundation scene can be played without reopening or saving it.
                // Replacing a different/additive scene setup is permitted only when all scenes are clean.
                if (!IsFoundationSceneOnly())
                {
                    EnsureNoDirtyScenes();
                    string project = Directory.GetParent(Application.dataPath).FullName;
                    if (File.Exists(Path.Combine(project, ScenePath)))
                        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    else
                        BuildShopFoundation.Prepare();
                }
                FocusGameView();
                SetPhase(pending, "entering-play");
                WriteResult(pending.id, "running", "Entering Play mode.");
                EditorApplication.isPaused = false;
                EditorApplication.isPlaying = true;
                break;
        }
    }

    private static bool EditorBusy()
    {
        return EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer;
    }

    private static bool IsFoundationSceneOnly()
    {
        return SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == ScenePath;
    }

    private static void EnsureNoDirtyScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException("Unsaved scene changes were preserved: " +
                    (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path) +
                    ". Save or resolve them before rebuilding or opening another scene.");
        }
    }

    private static void FocusGameView()
    {
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView") ??
                            Type.GetType("UnityEditor.GameView, UnityEditor");
        if (gameViewType == null) return;
        EditorWindow window = EditorWindow.GetWindow(gameViewType);
        window.Show();
        window.Focus();
    }

    private static bool KnownAction(string action)
    {
        return action == "build" || action == "play" || action == "stop" || action == "refresh";
    }

    private static void SetPhase(Pending pending, string phase)
    {
        pending.phase = phase;
        pending.phaseStartedAt = EditorApplication.timeSinceStartup;
        SessionState.SetString(ActiveKey, JsonUtility.ToJson(pending));
    }

    private static void Complete(Pending pending, string status, string message)
    {
        SessionState.EraseString(ActiveKey);
        WriteResult(pending.id, status, message);
    }

    private static void WriteResult(string id, string status, string message)
    {
        SessionState.SetString(ResultKey, JsonUtility.ToJson(new Result { id = id, status = status, message = message }, true));
        FlushResult();
    }

    private static void FlushResult()
    {
        string json = SessionState.GetString(ResultKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return;
        string temporaryPath = null;
        try
        {
            string path = Path.Combine(BridgeDirectory, "editor-result.json");
            temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
            SessionState.EraseString(ResultKey);
            lastWriteProblem = null;
        }
        catch (Exception exception)
        {
            string problem = "Shop Editor bridge could not write its result; it will retry: " + exception.Message;
            if (problem != lastWriteProblem) Debug.LogError(problem);
            lastWriteProblem = problem;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void ReportReadProblem(string problem)
    {
        if (problem != lastReadProblem) Debug.LogWarning(problem);
        lastReadProblem = problem;
    }
}
