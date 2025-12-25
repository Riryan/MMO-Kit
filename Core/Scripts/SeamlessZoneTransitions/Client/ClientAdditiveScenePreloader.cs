using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class ClientAdditiveScenePreloader : MonoBehaviour
{
    [Header("Behavior")]
    public bool unloadPreviousScene = true;

    [Header("Debug")]
    public bool debugLogs = true;

    private AsyncOperation loadOp;
    private string targetScene;
    private bool isPreloading;

    // Optional: if RequestActivate() is called before load completes
    private bool pendingActivate;

    private void Log(string msg)
    {
        if (debugLogs) Debug.Log(msg);
    }

    private void LogW(string msg)
    {
        if (debugLogs) Debug.LogWarning(msg);
    }

    private void LogE(string msg)
    {
        Debug.LogError(msg);
    }

    /// <summary>
    /// Start additive preload for a scene. Safe to call multiple times.
    /// </summary>
    public void BeginPreload(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            LogW("[AdditivePreloader] BeginPreload skipped (empty scene name)");
            return;
        }

        // Already exists (loaded or currently loading) -> don't start another async op
        Scene existing = SceneManager.GetSceneByName(sceneName);
        if (existing.IsValid())
        {
            Log($"[AdditivePreloader] Scene already exists, skipping preload: {sceneName}");
            // If someone already requested activation, try to satisfy it now
            if (pendingActivate && existing.isLoaded)
                SetActiveAndCleanup(sceneName);
            return;
        }

        // Already preloading this exact scene
        if (isPreloading && targetScene == sceneName)
        {
            Log($"[AdditivePreloader] Already preloading, skipping duplicate: {sceneName}");
            return;
        }

        // Preloading something else -> cancel state (we can't cancel Unity op reliably, but we can ignore it)
        if (isPreloading && targetScene != sceneName)
        {
            LogW($"[AdditivePreloader] Switching preload target ({targetScene} -> {sceneName}), ignoring previous op");
            ResetState();
        }

        targetScene = sceneName;
        isPreloading = true;
        pendingActivate = false;

        Log($"[AdditivePreloader] Begin preload (Additive): {sceneName}");
        StartCoroutine(PreloadRoutine(sceneName));
    }

    /// <summary>
    /// Request to activate/switch to a scene. If it isn't loaded yet, it will activate when ready.
    /// </summary>
    public void RequestActivate(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            LogW("[AdditivePreloader] RequestActivate skipped (empty scene name)");
            return;
        }

        // If scene already exists and loaded -> activate immediately
        Scene existing = SceneManager.GetSceneByName(sceneName);
        if (existing.IsValid() && existing.isLoaded)
        {
            Log($"[AdditivePreloader] RequestActivate -> scene loaded, activating: {sceneName}");
            SetActiveAndCleanup(sceneName);
            return;
        }

        // If we're not preloading it yet, start preload now
        if (!isPreloading || targetScene != sceneName)
        {
            Log($"[AdditivePreloader] RequestActivate -> starting preload first: {sceneName}");
            pendingActivate = true;
            BeginPreload(sceneName);
            return;
        }

        // We're preloading it: mark pending and it will activate on completion
        Log($"[AdditivePreloader] RequestActivate -> will activate when ready: {sceneName}");
        pendingActivate = true;
    }

    private IEnumerator PreloadRoutine(string sceneName)
    {
        // Start the additive load (DO NOT block activation)
        loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        if (loadOp == null)
        {
            LogE($"[AdditivePreloader] LoadSceneAsync returned null: {sceneName}");
            ResetState();
            yield break;
        }

        // Wait until done
        while (!loadOp.isDone)
            yield return null;

        Log($"[AdditivePreloader] Preload completed: {sceneName}");

        // If someone requested activation, do it now
        if (pendingActivate)
        {
            Log($"[AdditivePreloader] Pending activation -> activating: {sceneName}");
            SetActiveAndCleanup(sceneName);
        }
        else
        {
            // Keep state, but mark not actively preloading
            isPreloading = false;
            loadOp = null;
        }
    }

    private void SetActiveAndCleanup(string sceneName)
    {
        Scene newScene = SceneManager.GetSceneByName(sceneName);
        if (!newScene.IsValid() || !newScene.isLoaded)
        {
            LogW($"[AdditivePreloader] SetActive skipped (scene not loaded yet): {sceneName}");
            return;
        }

        SceneManager.SetActiveScene(newScene);
        Log($"[AdditivePreloader] Scene set active: {sceneName}");

        if (unloadPreviousScene)
        {
            for (int i = SceneManager.sceneCount - 1; i >= 0; --i)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded && s.name != sceneName)
                {
                    Log($"[AdditivePreloader] Unloading scene: {s.name}");
                    SceneManager.UnloadSceneAsync(s);
                }
            }
        }

        ResetState();
    }

    private void ResetState()
    {
        loadOp = null;
        targetScene = null;
        isPreloading = false;
        pendingActivate = false;
    }
}
