using System.IO;

namespace MultiplayerARPG
{
    public partial class CharacterBuff
    {
        // =========================
        // FILE SERIALIZATION
        // =========================
        public void Write(BinaryWriter writer)
        {
            writer.Write(id ?? string.Empty);
            writer.Write((byte)type);
            writer.Write(dataId);
            writer.Write(level);
            writer.Write(buffRemainsDuration);
        }

        public void Read(BinaryReader reader)
        {
            id = reader.ReadString();
            type = (BuffType)reader.ReadByte();
            dataId = reader.ReadInt32();
            level = reader.ReadInt32();
            buffRemainsDuration = reader.ReadSingle();

            // force cache rebuild on next access
            MakeAsCached();
        }
    }
}
