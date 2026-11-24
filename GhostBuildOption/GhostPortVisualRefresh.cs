using HarmonyLib;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Unity.Ports.Io;

namespace GhostBuildOption;

internal sealed class GhostPortVisualRefresh : IDisposable
{
    private static readonly Action<IoPortsRenderer, IStaticEntity> s_refresh =
        AccessTools.MethodDelegate<Action<IoPortsRenderer, IStaticEntity>>(
            AccessTools.GetDeclaredMethods(typeof(IoPortsRenderer)).Single(method =>
                method.Name.StartsWith("<initState>g__updateAllPorts|", StringComparison.Ordinal)));

    private readonly IEntitiesManager m_entities;
    private readonly IoPortsRenderer m_renderer;

    public GhostPortVisualRefresh(IEntitiesManager entities, IoPortsRenderer renderer)
    {
        m_entities = entities;
        m_renderer = renderer;
        m_entities.OnEntityVisualChanged.AddNonSaveable(this, OnEntityVisualChanged);
    }

    private void OnEntityVisualChanged(IEntity entity)
    {
        if (entity is IStaticEntity building)
            s_refresh(m_renderer, building);
    }

    public void Dispose() => m_entities.OnEntityVisualChanged.RemoveNonSaveable(this, OnEntityVisualChanged);
}
