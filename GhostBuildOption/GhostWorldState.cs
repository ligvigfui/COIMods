using Mafi.Collections;
using Mafi.Core.Entities.Static;
using System.Collections.Concurrent;
using EntityId = Mafi.Core.EntityId;

namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsEverything, false, false)]
[ManuallyWrittenSerialization]
public sealed class GhostWorldState : ICommandProcessor<SetGhostBuildModeCmd>, IAction<SetGhostBuildModeCmd>
{
    private const int Version = 2;
    private Dict<EntityId, bool> m_ghosts = new Dict<EntityId, bool>();
    private ConcurrentDictionary<EntityId, GhostConversionFailure> m_failures =
        new ConcurrentDictionary<EntityId, GhostConversionFailure>();

    public static GhostWorldState? Current { get; private set; }
    internal IEnumerable<EntityId> GhostIds => m_ghosts.Keys;
    internal IEnumerable<KeyValuePair<EntityId, GhostConversionFailure>> Failures => m_failures.ToArray();
    internal bool TryGetFailure(EntityId id, out GhostConversionFailure failure) => m_failures.TryGetValue(id, out failure);
    internal void SetFailure(EntityId id, GhostConversionFailure failure) => m_failures[id] = failure;

    [DoNotSave]
    public bool IsModeEnabled { get; private set; }

    public void Bind()
    {
        Current = this;
        IsModeEnabled = false;
    }

    public static void Unbind() => Current = null;

    public void Invoke(SetGhostBuildModeCmd cmd)
    {
        IsModeEnabled = cmd.IsEnabled;
        cmd.SetResultSuccess();
    }

    public void Add(EntityId id) => m_ghosts[id] = true;

    public void Remove(EntityId id)
    {
        m_ghosts.TryRemove(id, out _);
        m_failures.TryRemove(id, out _);
    }

    public static bool IsGhost(IStaticEntity entity) =>
        IsGhost(entity.Id);

    public static bool IsGhost(EntityId id) =>
        Current != null && Current.m_ghosts.ContainsKey(id);

    private void SerializeData(BlobWriter writer)
    {
        writer.WriteInt(Version);
        Dict<EntityId, bool>.Serialize(m_ghosts, writer);
        writer.WriteInt(m_failures.Count);
        foreach (var pair in m_failures.OrderBy(pair => pair.Key))
        {
            EntityId.Serialize(pair.Key, writer);
            pair.Value.Serialize(writer);
        }
    }

    private void DeserializeData(BlobReader reader)
    {
        int version = reader.ReadInt();
        if (version < 1 || version > Version)
            throw new InvalidOperationException($"Unsupported Ghost Build state version: {version}");
        m_ghosts = Dict<EntityId, bool>.Deserialize(reader);
        m_failures = new ConcurrentDictionary<EntityId, GhostConversionFailure>();
        if (version >= 2)
        {
            int count = reader.ReadInt();
            if (count < 0)
                throw new InvalidOperationException("Invalid ghost conversion failure count.");
            for (int i = 0; i < count; i++)
            {
                var id = EntityId.Deserialize(reader);
                m_failures[id] = GhostConversionFailure.Deserialize(reader);
            }
        }
        Current = this;
        IsModeEnabled = false;
    }

    private static readonly Action<object, BlobWriter> s_write =
        (value, writer) => ((GhostWorldState)value).SerializeData(writer);
    private static readonly Action<object, BlobReader> s_read =
        (value, reader) => ((GhostWorldState)value).DeserializeData(reader);

    public static void Serialize(GhostWorldState value, BlobWriter writer)
    {
        if (writer.TryStartClassSerialization(value))
            writer.EnqueueDataSerialization(value, s_write);
    }

    public static GhostWorldState Deserialize(BlobReader reader)
    {
        if (reader.TryStartClassDeserialization(out GhostWorldState value, null))
            reader.EnqueueDataDeserialization(value, s_read);
        return value;
    }
}
