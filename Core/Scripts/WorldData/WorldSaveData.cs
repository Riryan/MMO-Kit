using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayerARPG
{
    [System.Serializable]
    public class WorldSaveData
    {
        public List<BuildingSaveData> buildings = new List<BuildingSaveData>();

        // =========================
        // MAIN THREAD INITIALIZED
        // =========================
        private static string _persistentPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitPersistentPath()
        {
            _persistentPath = Application.persistentDataPath;
        }

        private static string GetNewPath(string id, string map)
        {
            if (string.IsNullOrEmpty(_persistentPath))
                throw new System.InvalidOperationException("WorldSaveData persistent path not initialized");

            return _persistentPath + "/" + id + "_world_" + map + ".wsd";
        }

        private static string GetLegacyPath(string id, string map)
        {
            if (string.IsNullOrEmpty(_persistentPath))
                throw new System.InvalidOperationException("WorldSaveData persistent path not initialized");

            return _persistentPath + "/" + id + "_world_" + map + ".sav";
        }

        // =========================
        // SAVE
        // =========================
        public void SavePersistentData(string id, string map)
        {
            string path = GetNewPath(id, map);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(buildings.Count);
                for (int i = 0; i < buildings.Count; ++i)
                    buildings[i].Write(writer);
            }
        }

        // =========================
        // LOAD
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

            string legacyPath = GetLegacyPath(id, map);
            if (File.Exists(legacyPath))
            {
                LoadLegacyBinaryFormatter(legacyPath);
                SavePersistentData(id, map);
                File.Delete(legacyPath);
            }
        }

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

        private void LoadLegacyBinaryFormatter(string path)
        {
#pragma warning disable SYSLIB0011
            using (FileStream stream = File.OpenRead(path))
            {
                var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                var data = (WorldSaveData)formatter.Deserialize(stream);

                buildings.Clear();
                if (data.buildings != null && data.buildings.Count > 0)
                    buildings.AddRange(data.buildings);
            }
#pragma warning restore SYSLIB0011
        }
    }
}
