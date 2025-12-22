using System.Collections.Generic;

namespace MultiplayerARPG
{
    public sealed class WorldSaveSnapshot
    {
        public readonly List<BuildingSaveData> buildings;
        public readonly List<StorageCharacterItem> storageItems;
        public readonly List<CharacterBuff> summonBuffs;

        public WorldSaveSnapshot(
            List<BuildingSaveData> buildings,
            List<StorageCharacterItem> storageItems,
            List<CharacterBuff> summonBuffs)
        {
            this.buildings = Copy(buildings);
            this.storageItems = Copy(storageItems);
            this.summonBuffs = CopyBuffs(summonBuffs);
        }

        private static List<T> Copy<T>(List<T> source)
        {
            if (source == null || source.Count == 0)
                return new List<T>();

            return new List<T>(source);
        }

        private static List<CharacterBuff> CopyBuffs(List<CharacterBuff> source)
        {
            if (source == null || source.Count == 0)
                return new List<CharacterBuff>();

            List<CharacterBuff> result = new List<CharacterBuff>(source.Count);
            for (int i = 0; i < source.Count; ++i)
            {
                result.Add(source[i].Clone(false));
            }
            return result;
        }
    }
}
