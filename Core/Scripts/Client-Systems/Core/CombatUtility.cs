using UnityEngine;

namespace MultiplayerARPG
{
    public static class CombatUtility
    {
        /// <summary>
        /// Applies damage only to IClientDamageableEntity objects. 
        /// Networked damage should be handled by the main combat systems.
        /// </summary>
        public static void TryApplyClientDamageOnly(object target, IDamageInfo damageInfo)
        {
            if (target is IClientDamageableEntity localTarget)
            {
                localTarget.ReceiveDamage(damageInfo);
            }
            else
            {
                Debug.LogWarning($"[CombatUtility] Tried to apply local damage to unsupported type: {target?.GetType().Name ?? "null"}");
            }
        }
    }
}
