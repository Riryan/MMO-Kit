using UnityEngine;
using MultiplayerARPG.MMO;

namespace MultiplayerARPG
{
    [RequireComponent(typeof(Collider))]
    public class SceneWarpPortal : MonoBehaviour
    {
        [Header("Target Map")]
        public BaseMapInfo targetMap;

        [Header("Warp Settings")]
        public Vector3 warpToPosition;
        public bool overrideRotation;
        public Vector3 warpToRotation;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

private void OnTriggerEnter(Collider other)
{
    // CLIENT — activate preloaded scene NOW
#if !UNITY_SERVER
    var preloader = FindObjectOfType<ClientAdditiveScenePreloader>();
    preloader?.ActivatePreloadedScene();
#endif

#if UNITY_SERVER || UNITY_EDITOR
    var player = other.GetComponentInParent<BasePlayerCharacterEntity>();
    if (player == null || player.IsWarping || targetMap == null)
        return;

    var mapNetworkManager = MapNetworkManager.Singleton;
    if (mapNetworkManager == null)
        return;

    mapNetworkManager.WarpCharacter(
        player,
        targetMap.Id,
        warpToPosition,
        overrideRotation,
        warpToRotation
    );
#endif
}

    }
}
