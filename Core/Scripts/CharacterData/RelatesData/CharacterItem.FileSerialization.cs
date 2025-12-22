using System.IO;
using System.Collections.Generic;

namespace MultiplayerARPG
{
    public partial class CharacterItem
    {
        // =========================
        // FILE SERIALIZATION
        // =========================
        public void Write(BinaryWriter writer)
        {
            writer.Write(id ?? string.Empty);
            writer.Write(dataId);
            writer.Write(level);
            writer.Write(amount);
            writer.Write(equipSlotIndex);
            writer.Write(durability);
            writer.Write(exp);
            writer.Write(lockRemainsDuration);
            writer.Write(expireTime);
            writer.Write(randomSeed);
            writer.Write(ammoDataId);
            writer.Write(ammo);

            // sockets
            if (sockets == null || sockets.Count == 0)
            {
                writer.Write((ushort)0);
            }
            else
            {
                writer.Write((ushort)sockets.Count);
                for (int i = 0; i < sockets.Count; ++i)
                {
                    writer.Write(sockets[i]);
                }
            }

            writer.Write(version);
        }

        public void Read(BinaryReader reader)
        {
            id = reader.ReadString();
            dataId = reader.ReadInt32();
            level = reader.ReadInt32();
            amount = reader.ReadInt32();
            equipSlotIndex = reader.ReadByte();
            durability = reader.ReadSingle();
            exp = reader.ReadInt32();
            lockRemainsDuration = reader.ReadSingle();
            expireTime = reader.ReadInt64();
            randomSeed = reader.ReadInt32();
            ammoDataId = reader.ReadInt32();
            ammo = reader.ReadInt32();

            // sockets
            ushort socketCount = reader.ReadUInt16();
            if (socketCount > 0)
            {
                if (sockets == null)
                    sockets = new List<int>(socketCount);
                else
                    sockets.Clear();

                for (int i = 0; i < socketCount; ++i)
                {
                    sockets.Add(reader.ReadInt32());
                }
            }
            else
            {
                sockets?.Clear();
            }

            version = reader.ReadByte();

            // ensure caches rebuild
            MakeAsCached();
        }
    }
}
