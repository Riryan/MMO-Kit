using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayerARPG
{
    [System.Serializable]
    public class StorageSaveData
    {
        public List<StorageCharacterItem> storageItems = new List<StorageCharacterItem>();

        private static string GetNewPath(string id)
        {
            return Application.persistentDataPath + "/" + id + "_storage.ssd";
        }

        private static string GetLegacyPath(string id)
        {
            return Application.persistentDataPath + "/" + id + "_storage.sav";
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
                writer.Write(storageItems.Count);
                for (int i = 0; i < storageItems.Count; ++i)
                {
                    storageItems[i].Write(writer);
                }
            }
        }

        // =========================
        // LOAD (NEW → LEGACY)
        // =========================
        public void LoadPersistentData(string id)
        {
            storageItems.Clear();

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
                storageItems.Capacity = count;

                for (int i = 0; i < count; ++i)
                {
                    StorageCharacterItem item = new StorageCharacterItem();
                    item.Read(reader);
                    storageItems.Add(item);
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
                var data = (StorageSaveData)formatter.Deserialize(stream);

                storageItems.Clear();
                storageItems.AddRange(data.storageItems);
            }
#pragma warning restore SYSLIB0011
        }
    }
}
