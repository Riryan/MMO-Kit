using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG
{
    public partial class MeleeDamageInfo
    {
        // Client-only damage support: manually scan known client-only damageables
        private void TryHitClientEntities(Vector3 damagePosition, float hitDistance)
        {
            foreach (var entity in GameInstance.ClientDamageableEntities)
            {
                if (entity == null || entity.IsDead)
                    continue;

                float dist = Vector3.Distance(entity.EntityTransform.position, damagePosition);
                if (dist > hitDistance)
                    continue;

                if (!IsInFov(entity.EntityTransform.position))
                    continue;

                entity.ApplyDamage(25); // Replace with real resolved damage
            }
        }

        private bool IsInFov(Vector3 targetPos)
        {
            // Replace with real FOV test later
            return true;
        }
    }
}
