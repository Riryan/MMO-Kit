
#if UNITY_EDITOR
using UnityEngine;
using SeamlessZoneTransitions;

namespace SeamlessZoneTransitions.Editor
{
    [ExecuteAlways]
    public class SeamlessZoneGizmos : MonoBehaviour
    {
        public enum GizmoType
        {
            Preload,
            Warp
        }

        public GizmoType gizmoType = GizmoType.Preload;

        public Color preloadColor = new Color(0f, 0.8f, 1f, 0.25f);
        public Color warpColor = new Color(1f, 0.3f, 0.3f, 0.25f);

        void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null)
                return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = gizmoType == GizmoType.Preload ? preloadColor : warpColor;

            if (col is BoxCollider box)
            {
                Gizmos.DrawCube(box.center, box.size);
            }

            Gizmos.color = Color.white;
            Gizmos.matrix = Matrix4x4.identity;
        }

        void OnDrawGizmosSelected()
        {
            if (gizmoType != GizmoType.Preload)
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(
                transform.position,
                SeamlessZoneConstants.MaxPreloadToWarpDistanceMeters
            );
        }
    }
}
#endif
