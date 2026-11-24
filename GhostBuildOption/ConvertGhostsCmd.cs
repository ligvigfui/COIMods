using Mafi.Core.Terrain;

namespace GhostBuildOption;

[ManuallyWrittenSerialization]
public sealed class ConvertGhostsCmd : InputCommand<int>
{
    public RectangleTerrainArea2i Area { get; private set; }

    public ConvertGhostsCmd(RectangleTerrainArea2i area) => Area = area;

    protected override void SerializeData(BlobWriter writer)
    {
        base.SerializeData(writer);
        RectangleTerrainArea2i.Serialize(Area, writer);
    }

    protected override void DeserializeData(BlobReader reader)
    {
        base.DeserializeData(reader);
        Area = RectangleTerrainArea2i.Deserialize(reader);
    }

    private static readonly Action<object, BlobWriter> s_write =
        (value, writer) => ((ConvertGhostsCmd)value).SerializeData(writer);
    private static readonly Action<object, BlobReader> s_read =
        (value, reader) => ((ConvertGhostsCmd)value).DeserializeData(reader);

    public static void Serialize(ConvertGhostsCmd value, BlobWriter writer)
    {
        if (writer.TryStartClassSerialization(value))
            writer.EnqueueDataSerialization(value, s_write);
    }

    public static ConvertGhostsCmd Deserialize(BlobReader reader)
    {
        if (reader.TryStartClassDeserialization(out ConvertGhostsCmd value, null))
            reader.EnqueueDataDeserialization(value, s_read);
        return value;
    }
}
