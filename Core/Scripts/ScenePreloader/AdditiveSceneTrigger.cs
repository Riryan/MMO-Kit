using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Collider))]
public class AdditiveSceneTrigger : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Scene To Preload")]
    [Tooltip("Drag the target scene asset here")]
    public SceneAsset targetScene;
#endif

    [Header("Runtime")]
    [SerializeField, HideInInspector]
    private string targetSceneName;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Bake scene name at edit time (no runtime dependency on SceneAsset)
        targetSceneName = targetScene != null ? targetScene.name : string.Empty;
    }
#endif

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerTag"))
            return;

        var preloader = FindObjectOfType<ClientAdditiveScenePreloader>();
        if (preloader == null)
            return;

        preloader.BeginPreload(targetSceneName);
    }
}
