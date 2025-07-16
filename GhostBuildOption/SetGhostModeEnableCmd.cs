namespace GhostBuildOption;

[GenerateSerializer(false, null, 0)]
public class SetGhostModeEnableCmd : InputCommand
{
    public readonly bool IsEnabled;

    private static readonly Action<object, BlobWriter> s_serializeDataDelayedAction;

    private static readonly Action<object, BlobReader> s_deserializeDataDelayedAction;

    public SetGhostModeEnableCmd(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public static void Serialize(SetGhostModeEnableCmd value, BlobWriter writer)
    {
        if (writer.TryStartClassSerialization(value))
        {
            writer.EnqueueDataSerialization(value, s_serializeDataDelayedAction);
        }
    }

    protected override void SerializeData(BlobWriter writer)
    {
        base.SerializeData(writer);
        writer.WriteBool(IsEnabled);
    }

    public new static SetGhostModeEnableCmd Deserialize(BlobReader reader)
    {
        if (reader.TryStartClassDeserialization(out SetGhostModeEnableCmd obj, (Func<BlobReader, Type, SetGhostModeEnableCmd>)null))
        {
            reader.EnqueueDataDeserialization(obj, s_deserializeDataDelayedAction);
        }
        return obj;
    }

    protected override void DeserializeData(BlobReader reader)
    {
        base.DeserializeData(reader);
        reader.SetField(this, "IsEnabled", reader.ReadBool());
    }

    static SetGhostModeEnableCmd()
    {
        s_serializeDataDelayedAction = delegate (object obj, BlobWriter writer)
        {
            ((SetGhostModeEnableCmd)obj).SerializeData(writer);
        };
        s_deserializeDataDelayedAction = delegate (object obj, BlobReader reader)
        {
            ((SetGhostModeEnableCmd)obj).DeserializeData(reader);
        };
    }
}