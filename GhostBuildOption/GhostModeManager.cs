namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsEverything, false, false)]
[GenerateSerializer(false, null, 0)]
public class GhostModeManager : ICommandProcessor<SetGhostModeEnableCmd>, IAction<SetGhostModeEnableCmd>
{
    private static readonly Action<object, BlobWriter> s_serializeDataDelayedAction;

    private static readonly Action<object, BlobReader> s_deserializeDataDelayedAction;

    public bool IsGhostModeEnabled { get; private set; }

    public void Invoke(SetGhostModeEnableCmd cmd)
    {
        IsGhostModeEnabled = cmd.IsEnabled;
        cmd.SetResultSuccess();
    }

    public static void Serialize(GhostModeManager value, BlobWriter writer)
    {
        if (writer.TryStartClassSerialization(value))
        {
            writer.EnqueueDataSerialization(value, s_serializeDataDelayedAction);
        }
    }

    protected virtual void SerializeData(BlobWriter writer)
    {
        writer.WriteBool(IsGhostModeEnabled);
    }

    public static GhostModeManager Deserialize(BlobReader reader)
    {
        if (reader.TryStartClassDeserialization(out GhostModeManager obj, (Func<BlobReader, Type, GhostModeManager>)null))
        {
            reader.EnqueueDataDeserialization(obj, s_deserializeDataDelayedAction);
        }
        return obj;
    }

    protected virtual void DeserializeData(BlobReader reader)
    {
        IsGhostModeEnabled = reader.ReadBool();
    }

    public GhostModeManager() {}

    static GhostModeManager()
    {
        s_serializeDataDelayedAction = delegate (object obj, BlobWriter writer)
        {
            ((GhostModeManager)obj).SerializeData(writer);
        };
        s_deserializeDataDelayedAction = delegate (object obj, BlobReader reader)
        {
            ((GhostModeManager)obj).DeserializeData(reader);
        };
    }
}