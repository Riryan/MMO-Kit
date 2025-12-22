using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayerARPG
{
    [System.Serializable]
    public class WorldSaveData
    {
        public List<BuildingSaveData> buildings = new List<BuildingSaveData>();

        private static string GetNewPath(string id, string map)
        {
            return Application.persistentDataPath + "/" + id + "_world_" + map + ".wsd";
        }

        private static string GetLegacyPath(string id, string map)
        {
            return Application.persistentDataPath + "/" + id + "_world_" + map + ".sav";
        }

        // =========================
        // SAVE (NEW FORMAT ONLY)
        // =========================
        public void SavePersistentData(string id, string map)
        {
            string path = GetNewPath(id, map);

            using (FileStream stream = File.Open(path, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(buildings.Count);
                for (int i = 0; i < buildings.Count; ++i)
                {
                    buildings[i].Write(writer);
                }
            }
        }

        // =========================
        // LOAD (NEW → LEGACY)
        // =========================
        public void LoadPersistentData(string id, string map)
        {
            buildings.Clear();

            string newPath = GetNewPath(id, map);
            if (File.Exists(newPath))
            {
                LoadNew(newPath);
                return;
            }

            // ---- legacy fallback ----
            string legacyPath = GetLegacyPath(id, map);
            if (File.Exists(legacyPath))
            {
                LoadLegacyBinaryFormatter(legacyPath);

                // migrate immediately
                SavePersistentData(id, map);
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
                buildings.Capacity = count;

                for (int i = 0; i < count; ++i)
                {
                    BuildingSaveData data = new BuildingSaveData();
                    data.Read(reader);
                    buildings.Add(data);
                }
            }
        }

        // =========================
        // LEGACY LOADER (ONE TIME)
        // =========================
        private void LoadLegacyBinaryFormatter(string path)
        {
#pragma warning disable SYSLIB0011
            using (FileStream stream = File.OpenRead(path))
            {
                var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                var data = (WorldSaveData)formatter.Deserialize(stream);

                buildings.Clear();
                buildings.AddRange(data.buildings);
            }
#pragma warning restore SYSLIB0011
        }
    }
}
