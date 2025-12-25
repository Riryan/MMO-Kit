using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace MultiplayerARPG
{
    [CreateAssetMenu(menuName = "Harvesting/HarvestableLite")]
    public class HarvestableLite : ScriptableObject
    {
        [Header("Basic Info")]
        public int dataId; // Must be unique
        public string title = "Harvestable";
        public WeaponType requiredToolType; // null = any tool or unarmed
        public int expReward = 0;

        [Header("Drop Settings")]
        public List<LiteHarvestDrop> drops = new();

        /// <summary>
        /// Filters and returns only valid drops (non-null items, amount > 0, pass chance roll)
        /// </summary>
        public List<LiteHarvestDrop> GetValidDrops()
        {
            return drops
                .Where(d => d.item != null && d.amount > 0 && Random.value <= Mathf.Clamp01(d.chance))
                .ToList();
        }
    }

    [System.Serializable]
    public struct LiteHarvestDrop
    {
        public Item item;
        public int amount;
        [Range(0f, 1f)]
        public float chance; // 1.0 = always drops, 0.5 = 50% chance
    }
}
