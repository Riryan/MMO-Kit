using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeamlessZoneTransitions.Client
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class ZonePreloadTrigger : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("Editor")]
        [Tooltip("Scene asset to preload (client-only)")]
        public SceneAsset targetScene;
#endif
        [SerializeField, HideInInspector]
        string targetSceneName;

        void Reset()
        {
            var c = GetComponent<Collider>();
            c.isTrigger = true;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            targetSceneName = targetScene ? targetScene.name : string.Empty;
        }
#endif

void OnTriggerEnter(Collider other)
{
#if UNITY_SERVER
    return;
#else
    Debug.Log(
        $"[SeamlessZoneTransitions][PreloadTrigger] HIT by {other.name} | tag={other.tag}",
        this
    );

    ClientZonePreloader.Instance?.BeginPreload(targetSceneName);
#endif
}

    }
}
