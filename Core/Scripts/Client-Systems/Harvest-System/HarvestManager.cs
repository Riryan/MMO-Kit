using LiteNetLibManager;
using UnityEngine;
using System.Collections.Generic;

namespace MultiplayerARPG
{
    public class HarvestManager : MonoBehaviour
    {
        public static HarvestManager Instance { get; private set; }

        [SerializeField]
        private List<HarvestableLite> harvestablesList = new(); // Drag-and-drop these in the Inspector

        private readonly Dictionary<int, HarvestableLite> Harvestables = new();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Harvestables.Clear();

            foreach (var harvestable in harvestablesList)
            {
                if (harvestable != null && !Harvestables.ContainsKey(harvestable.dataId))
                {
                    Harvestables[harvestable.dataId] = harvestable;
                    Debug.Log($"[HarvestManager] Registered (Manual): {harvestable.title} (ID: {harvestable.dataId})");
                }
            }

            // Optional fallback to auto-load
            /*
            foreach (var harvestable in Resources.LoadAll<HarvestableLite>(""))
            {
                if (harvestable != null && !Harvestables.ContainsKey(harvestable.dataId))
                {
                    Harvestables[harvestable.dataId] = harvestable;
                    Debug.Log($"[HarvestManager] Registered (Auto): {harvestable.title} (ID: {harvestable.dataId})");
                }
            }
            */
        }

        [ServerRpc]
        public void ServerReceiveHarvestReward(
            long connectionId,
            int harvestableDataId,
            int weaponDataId,
            int skillDataId,
            int level,
            float damage)
        {
            if (!BaseGameNetworkManager.Singleton.IsServer)
                return;

            if (!Harvestables.TryGetValue(harvestableDataId, out var harvestable) || harvestable == null)
            {
                Debug.LogWarning($"[HarvestManager] Invalid harvestable ID: {harvestableDataId}");
                return;
            }

            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(connectionId, out BasePlayerCharacterEntity player))
            {
                Debug.LogWarning($"[HarvestManager] No player found for connectionId: {connectionId}");
                return;
            }

            WeaponType equippedType = null;

            if (weaponDataId > 0 &&
                GameInstance.Items.TryGetValue(weaponDataId, out var item) &&
                item is IWeaponItem weaponItem)
            {
                equippedType = weaponItem.WeaponType;
            }

            if (harvestable.requiredToolType != null &&
                harvestable.requiredToolType != equippedType)
            {
                Debug.LogWarning($"[HarvestManager] Tool mismatch. Required: {harvestable.requiredToolType?.Title ?? "None"}, Equipped: {equippedType?.Title ?? "Unarmed"}");
                return;
            }

            var drops = harvestable.GetValidDrops();
            if (drops.Count == 0)
            {
                Debug.LogWarning($"[HarvestManager] No valid item drop config found for: {harvestable.title}");
                return;
            }

            foreach (var drop in drops)
            {
                int itemId = drop.item.DataId;
                int amount = Mathf.Max(1, drop.amount);

                bool dropToGround = player.IncreasingItemsWillOverwhelming(itemId, amount);

                if (!dropToGround)
                {
                    player.IncreaseItems(
                        CharacterItem.Create(itemId, 1, amount),
                        item => player.OnRewardItem(RewardGivenType.Harvestable, item));
                }
                else
                {
                    ItemDropEntity.Drop(
                        player,
                        RewardGivenType.Harvestable,
                        CharacterItem.Create(itemId, 1, amount),
                        new string[0]);
                }
            }

            if (harvestable.expReward > 0)
            {
                player.RewardExp(harvestable.expReward, 1, RewardGivenType.Harvestable, 1, 1);
            }
        }
    }
}
