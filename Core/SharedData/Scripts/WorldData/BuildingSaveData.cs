using System.IO;
using LiteNetLib.Utils;

namespace MultiplayerARPG
{
    [System.Serializable]
    public partial class BuildingSaveData : IBuildingSaveData, INetSerializable
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public int EntityId { get; set; }
        public int CurrentHp { get; set; }
        public float RemainsLifeTime { get; set; }
        public bool IsLocked { get; set; }
        public string LockPassword { get; set; }
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        public string CreatorId { get; set; }
        public string CreatorName { get; set; }
        public string ExtraData { get; set; }
        public bool IsSceneObject { get; set; }

        // =========================
        // NETWORK SERIALIZATION
        // =========================
        public void Deserialize(NetDataReader reader)
        {
            this.DeserializeBuildingSaveData(reader);
        }

        public void Serialize(NetDataWriter writer)
        {
            this.SerializeBuildingSaveData(writer);
        }

        // =========================
        // FILE SERIALIZATION (WORLD SAVE)
        // =========================
        public void Write(BinaryWriter writer)
        {
            writer.Write(Id ?? string.Empty);
            writer.Write(ParentId ?? string.Empty);
            writer.Write(EntityId);
            writer.Write(CurrentHp);
            writer.Write(RemainsLifeTime);
            writer.Write(IsLocked);
            writer.Write(LockPassword ?? string.Empty);

            writer.Write(Position.x);
            writer.Write(Position.y);
            writer.Write(Position.z);

            writer.Write(Rotation.x);
            writer.Write(Rotation.y);
            writer.Write(Rotation.z);

            writer.Write(CreatorId ?? string.Empty);
            writer.Write(CreatorName ?? string.Empty);
            writer.Write(ExtraData ?? string.Empty);
            writer.Write(IsSceneObject);
        }

        public void Read(BinaryReader reader)
        {
            Id = reader.ReadString();
            ParentId = reader.ReadString();
            EntityId = reader.ReadInt32();
            CurrentHp = reader.ReadInt32();
            RemainsLifeTime = reader.ReadSingle();
            IsLocked = reader.ReadBoolean();
            LockPassword = reader.ReadString();

            Position = new Vec3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());

            Rotation = new Vec3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());

            CreatorId = reader.ReadString();
            CreatorName = reader.ReadString();
            ExtraData = reader.ReadString();
            IsSceneObject = reader.ReadBoolean();
        }
    }
}
