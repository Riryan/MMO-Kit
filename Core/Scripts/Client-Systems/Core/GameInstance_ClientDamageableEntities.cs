using System.Collections.Generic;

namespace MultiplayerARPG
{
    public partial class GameInstance
    {
        public static readonly HashSet<IClientDamageableEntity> ClientDamageableEntities = new();

        public static void AddClientDamageableEntity(IClientDamageableEntity entity)
        {
            if (entity != null)
                ClientDamageableEntities.Add(entity);
        }

        public static void RemoveClientDamageableEntity(IClientDamageableEntity entity)
        {
            if (entity != null)
                ClientDamageableEntities.Remove(entity);
        }
    }
}
