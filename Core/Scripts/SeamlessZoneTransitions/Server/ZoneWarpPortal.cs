using UnityEngine;
using MultiplayerARPG;
using MultiplayerARPG.MMO;

namespace SeamlessZoneTransitions.Server
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class ZoneWarpPortal : MonoBehaviour
    {
        [Header("Target Map")]
        public BaseMapInfo targetMap;

        [Header("Spawn")]
        public Vector3 warpToPosition;
        public bool overrideRotation;
        public Vector3 warpToRotation;

        void Reset()
        {
            var c = GetComponent<Collider>();
            c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
#if !UNITY_SERVER
            // CLIENT: activate preloaded visuals at the exact moment of warp
            var preloader = SeamlessZoneTransitions.Client.ClientZonePreloader.Instance;
            preloader?.ActivateNow();
#endif

#if UNITY_SERVER || UNITY_EDITOR
            var player = other.GetComponentInParent<BasePlayerCharacterEntity>();
            if (player == null || player.IsWarping || targetMap == null)
                return;

            var mgr = MapNetworkManager.Singleton;
            if (mgr == null) return;

            mgr.WarpCharacter(
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
