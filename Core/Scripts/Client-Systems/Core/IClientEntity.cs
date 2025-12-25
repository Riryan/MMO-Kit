using UnityEngine;

namespace MultiplayerARPG
{
    public interface IClientEntity
    {
        string LocalId { get; } // Could be instance ID
        //Transform EntityTransform { get; }
        bool IsDestroyed { get; }
    }
}
