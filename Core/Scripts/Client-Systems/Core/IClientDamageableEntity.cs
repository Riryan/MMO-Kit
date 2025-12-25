using UnityEngine;

namespace MultiplayerARPG
{
    public interface IClientDamageableEntity : IClientEntity, ITargetableEntity
    {
        string LocalId { get; }           // Unique local-only ID (e.g. instance ID)
        int CurrentHp { get; }
        int MaxHp { get; }
        bool IsDead { get; }

        void ApplyDamage(int amount);
        void ReceiveDamage(IDamageInfo damageInfo);
    }
}
