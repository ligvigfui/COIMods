using EntityId = Mafi.Core.EntityId;

namespace GhostBuildOption;

[ManuallyWrittenSerialization]
public sealed class ConvertGhostCmd : InputCommand<int>
{
    public EntityId Target { get; private set; }

    public ConvertGhostCmd(EntityId target) => Target = target;

    protected override void SerializeData(BlobWriter writer)
    {
        base.SerializeData(writer);
        EntityId.Serialize(Target, writer);
    }

    protected override void DeserializeData(BlobReader reader)
    {
        base.DeserializeData(reader);
        Target = EntityId.Deserialize(reader);
    }

    private static readonly Action<object, BlobWriter> s_write =
        (value, writer) => ((ConvertGhostCmd)value).SerializeData(writer);
    private static readonly Action<object, BlobReader> s_read =
        (value, reader) => ((ConvertGhostCmd)value).DeserializeData(reader);

    public static void Serialize(ConvertGhostCmd value, BlobWriter writer)
    {
        if (writer.TryStartClassSerialization(value))
            writer.EnqueueDataSerialization(value, s_write);
    }

    public static ConvertGhostCmd Deserialize(BlobReader reader)
    {
        if (reader.TryStartClassDeserialization(out ConvertGhostCmd value, null))
            reader.EnqueueDataDeserialization(value, s_read);
        return value;
    }
}
