#if !UNITY_SERVER
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace SeamlessZoneTransitions.Client
{
    [DisallowMultipleComponent]
    public class ClientZonePreloader : MonoBehaviour
    {
        public static ClientZonePreloader Instance { get; private set; }

        [Header("Behavior")]
        [Tooltip("Unload non-active scenes after activation")]
        public bool unloadPreviousScenes = true;

        [Tooltip("Optional small delay before activation (seconds)")]
        [Range(0f, 0.5f)]
        public float activationDelay = 0f;

        AsyncOperation loadOp;
        string pendingScene;
        bool preloading;
        bool activated;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public bool IsPreloaded(string sceneName)
            => preloading && pendingScene == sceneName && loadOp != null && loadOp.progress >= 0.9f;

        public void BeginPreload(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (preloading && pendingScene == sceneName) return;

            // Cancel any previous preload safely
            ResetState();

            pendingScene = sceneName;
            preloading = true;
            StartCoroutine(PreloadRoutine());
        }

        IEnumerator PreloadRoutine()
        {
            Debug.Log($"[SeamlessZoneTransitions] Scene preloaded (inactive): {pendingScene}");
            loadOp = SceneManager.LoadSceneAsync(pendingScene, LoadSceneMode.Additive);
            loadOp.allowSceneActivation = false;

            while (loadOp.progress < 0.9f)
                yield return null;
        }

        /// <summary>
        /// Call immediately when server warp is triggered.
        /// Deterministic: no heuristics, no OnDisable hacks.
        /// </summary>
        public void ActivateNow()
        {
            if (activated || loadOp == null) return;
            activated = true;
            StartCoroutine(ActivateRoutine());
        }

        IEnumerator ActivateRoutine()
        {
            if (activationDelay > 0f)
                yield return new WaitForSeconds(activationDelay);

            loadOp.allowSceneActivation = true;
            while (!loadOp.isDone)
                yield return null;

            var scene = SceneManager.GetSceneByName(pendingScene);
            if (scene.IsValid())
                SceneManager.SetActiveScene(scene);

            if (unloadPreviousScenes)
            {
                for (int i = SceneManager.sceneCount - 1; i >= 0; --i)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s.IsValid() && s.name != pendingScene)
                        SceneManager.UnloadSceneAsync(s);
                }
            }

            ResetState(keepInstance:true);
        }

        void ResetState(bool keepInstance = false)
        {
            loadOp = null;
            pendingScene = null;
            preloading = false;
            activated = false;
            if (!keepInstance) Instance = this;
        }
    }
}
#endif
