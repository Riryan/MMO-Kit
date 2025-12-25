using UnityEngine;

namespace MultiplayerARPG
{
    public partial class RaycastDamageInfo
    {
        private void TryHitClientEntities(Vector3 damagePosition, Vector3 damageDirection)
        {
            float fov = GetFov();
            float distance = GetDistance();

            foreach (var entity in GameInstance.ClientDamageableEntities)
            {
                if (entity == null || entity.IsDead)
                    continue;

                Vector3 toTarget = entity.EntityTransform.position - damagePosition;
                if (toTarget.magnitude > distance)
                    continue;

                if (Vector3.Angle(damageDirection, toTarget) > fov * 0.5f)
                    continue;

                entity.ApplyDamage(25); // Replace with actual calculation later
            }
        }
    }
}
