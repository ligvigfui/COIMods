using Mafi.Collections.ImmutableCollections;
using EntityId = Mafi.Core.EntityId;

namespace GhostBuildOption;

[ManuallyWrittenSerialization]
public sealed class ToggleGhostsCmd : InputCommand<int>
{
    public ImmutableArray<EntityId> Targets { get; private set; }

    public ToggleGhostsCmd(ImmutableArray<EntityId> targets) => Targets = targets;

    protected override void SerializeData(BlobWriter writer)
    {
        base.SerializeData(writer);
        ImmutableArray<EntityId>.Serialize(Targets, writer);
    }

    protected override void DeserializeData(BlobReader reader)
    {
        base.DeserializeData(reader);
        Targets = ImmutableArray<EntityId>.Deserialize(reader);
    }

    private static readonly Action<object, BlobWriter> s_write =
        (value, writer) => ((ToggleGhostsCmd)value).SerializeData(writer);
    private static readonly Action<object, BlobReader> s_read =
        (value, reader) => ((ToggleGhostsCmd)value).DeserializeData(reader);

    public static void Serialize(ToggleGhostsCmd value, BlobWriter writer)
    {
        if (writer.TryStartClassSerialization(value))
            writer.EnqueueDataSerialization(value, s_write);
    }

    public static ToggleGhostsCmd Deserialize(BlobReader reader)
    {
        if (reader.TryStartClassDeserialization(out ToggleGhostsCmd value, null))
            reader.EnqueueDataDeserialization(value, s_read);
        return value;
    }
}
