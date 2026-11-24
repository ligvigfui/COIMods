namespace GhostBuildOption;

[ManuallyWrittenSerialization]
public sealed class SetGhostBuildModeCmd : InputCommand
{
    public bool IsEnabled { get; private set; }

    public SetGhostBuildModeCmd(bool enabled) => IsEnabled = enabled;

    protected override void SerializeData(BlobWriter writer)
    {
        base.SerializeData(writer);
        writer.WriteBool(IsEnabled);
    }

    protected override void DeserializeData(BlobReader reader)
    {
        base.DeserializeData(reader);
        IsEnabled = reader.ReadBool();
    }

    private static readonly Action<object, BlobWriter> s_write =
        (value, writer) => ((SetGhostBuildModeCmd)value).SerializeData(writer);
    private static readonly Action<object, BlobReader> s_read =
        (value, reader) => ((SetGhostBuildModeCmd)value).DeserializeData(reader);

    public static void Serialize(SetGhostBuildModeCmd value, BlobWriter writer)
    {
        if (writer.TryStartClassSerialization(value))
            writer.EnqueueDataSerialization(value, s_write);
    }

    public new static SetGhostBuildModeCmd Deserialize(BlobReader reader)
    {
        if (reader.TryStartClassDeserialization(out SetGhostBuildModeCmd value, null))
            reader.EnqueueDataDeserialization(value, s_read);
        return value;
    }
}
