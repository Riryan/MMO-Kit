using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayerARPG
{
    [System.Serializable]
    public class SummonBuffsSaveData
    {
        public List<CharacterBuff> summonBuffs = new List<CharacterBuff>();

        // =========================
        // MAIN THREAD CACHED PATH
        // =========================
        private static string _persistentPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitPersistentPath()
        {
            _persistentPath = Application.persistentDataPath;
        }

        private static string GetNewPath(string id)
        {
            if (string.IsNullOrEmpty(_persistentPath))
                throw new System.InvalidOperationException("SummonBuffsSaveData persistent path not initialized");

            return _persistentPath + "/" + id + "_summon_buffs.sbsd";
        }

        private static string GetLegacyPath(string id)
        {
            if (string.IsNullOrEmpty(_persistentPath))
                throw new System.InvalidOperationException("SummonBuffsSaveData persistent path not initialized");

            return _persistentPath + "/" + id + "_summon_buffs.sav";
        }

        // =========================
        // SAVE (NEW FORMAT)
        // =========================
        public void SavePersistentData(string id)
        {
            string path = GetNewPath(id);

            using (FileStream stream = File.Open(path, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(summonBuffs.Count);
                for (int i = 0; i < summonBuffs.Count; ++i)
                {
                    summonBuffs[i].Write(writer);
                }
            }
        }

        // =========================
        // LOAD (NEW → LEGACY)
        // =========================
        public void LoadPersistentData(string id)
        {
            summonBuffs.Clear();

            string newPath = GetNewPath(id);
            if (File.Exists(newPath))
            {
                LoadNew(newPath);
                return;
            }

            string legacyPath = GetLegacyPath(id);
            if (File.Exists(legacyPath))
            {
                LoadLegacyBinaryFormatter(legacyPath);

                // migrate immediately
                SavePersistentData(id);
                File.Delete(legacyPath);
            }
        }

        // =========================
        // NEW FORMAT LOADER
        // =========================
        private void LoadNew(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int count = reader.ReadInt32();
                summonBuffs.Capacity = count;

                for (int i = 0; i < count; ++i)
                {
                    CharacterBuff buff = new CharacterBuff();
                    buff.Read(reader);
                    summonBuffs.Add(buff);
                }
            }
        }

        // =========================
        // LEGACY LOADER (ONE-TIME)
        // =========================
        private void LoadLegacyBinaryFormatter(string path)
        {
#pragma warning disable SYSLIB0011
            using (FileStream stream = File.OpenRead(path))
            {
                var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                var data = (SummonBuffsSaveData)formatter.Deserialize(stream);

                summonBuffs.Clear();
                if (data.summonBuffs != null && data.summonBuffs.Count > 0)
                {
                    summonBuffs.AddRange(data.summonBuffs);
                }
            }
#pragma warning restore SYSLIB0011
        }
    }
}
