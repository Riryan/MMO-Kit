using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class ClientAdditiveScenePreloader : MonoBehaviour
{
    [Header("Behavior")]
    public bool unloadPreviousScene = true;
    public float activationDelay = 0.2f;

    private AsyncOperation loadOp;
    private string preloadedScene;
    private bool isPreloading;
    private bool isActivated;

    /// <summary>
    /// Called by AdditiveSceneTrigger
    /// </summary>
    public void BeginPreload(string sceneName)
    {
        if (isPreloading || string.IsNullOrEmpty(sceneName))
            return;

        isPreloading = true;
        preloadedScene = sceneName;
        StartCoroutine(PreloadRoutine());
    }

    private IEnumerator PreloadRoutine()
    {
        loadOp = SceneManager.LoadSceneAsync(preloadedScene, LoadSceneMode.Additive);
        loadOp.allowSceneActivation = false;

        while (loadOp.progress < 0.9f)
            yield return null;

        Debug.Log($"[AdditivePreloader] Preloaded scene: {preloadedScene}");
    }

    /// <summary>
    /// Call when MMOWarpMessage is received
    /// </summary>
    public void ActivatePreloadedScene()
    {
        if (isActivated || loadOp == null)
            return;

        isActivated = true;
        StartCoroutine(ActivateRoutine());
    }

    private IEnumerator ActivateRoutine()
    {
        if (activationDelay > 0f)
            yield return new WaitForSeconds(activationDelay);

        loadOp.allowSceneActivation = true;

        while (!loadOp.isDone)
            yield return null;

        Scene newScene = SceneManager.GetSceneByName(preloadedScene);
        if (newScene.IsValid())
            SceneManager.SetActiveScene(newScene);

        if (unloadPreviousScene)
        {
            for (int i = SceneManager.sceneCount - 1; i >= 0; --i)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.name != preloadedScene)
                    SceneManager.UnloadSceneAsync(s);
            }
        }

        Debug.Log($"[AdditivePreloader] Activated scene: {preloadedScene}");
    }
}
