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
#if UNITY_SERVER || UNITY_EDITOR
            var player = other.GetComponentInParent<BasePlayerCharacterEntity>();
            if (player == null)
                return;

            if (player.IsWarping)
                return;

            if (targetMap == null)
                return;

            // Get the active map network manager
            var mapNetworkManager = MapNetworkManager.Singleton;
            if (mapNetworkManager == null)
                return;

            // Authoritative server-side warp
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
