using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Collections;
using HarmonyLib;
using Mafi.Core.Research;

namespace GhostBuildOption;

public sealed class GhostBuildOption : IMod
{
    private readonly Harmony m_harmony = new Harmony("GhostBuildOption.Placement");
    private GhostPortVisualRefresh? m_portVisualRefresh;
    private GhostConversionFeedback? m_conversionFeedback;
    private GhostModeIndicator? m_modeIndicator;

    internal static ModJsonConfig? ActiveConfig { get; private set; }

    public ModManifest Manifest { get; }

    public ModJsonConfig JsonConfig { get; }

    public bool IsUiOnly => false;

    public Option<IConfig> ModConfig => Option<IConfig>.None;

    public GhostBuildOption(ModManifest manifest)
    {
        Manifest = manifest;
        JsonConfig = new ModJsonConfig(this);
    }

    public void EarlyInit(DependencyResolver resolver)
    {
        GhostModeManager.IsGhostModeEnabled = false;
        GhostColors.Configure(JsonConfig);
        ActiveConfig = JsonConfig;
        m_harmony.PatchAll(typeof(GhostBuildOption).Assembly);
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        GhostIcons.Register(resolver.Resolve<AssetsDb>(), Manifest.RootDirectoryPath);
        var state = resolver.Resolve<GhostWorldState>();
        state.Bind();
        m_conversionFeedback = new GhostConversionFeedback(state, resolver.Resolve<UiContext>(),
            resolver.Resolve<CursorPickingManager>(), resolver.Resolve<Mafi.Unity.Entities.EntitiesIconRenderer>(),
            resolver.Instantiate<Mafi.Unity.Ui.Library.CursorMessage>(), resolver.Resolve<AssetsDb>());
        m_portVisualRefresh = new GhostPortVisualRefresh(resolver.Resolve<Mafi.Core.Entities.IEntitiesManager>(),
            resolver.Resolve<Mafi.Unity.Ports.Io.IoPortsRenderer>());
        resolver.Resolve<GhostConversionProcessor>();
        if (gameWasLoaded)
            GhostVehicleNavigation.RefreshExistingGhosts(state, resolver.Resolve<Mafi.Core.Entities.IEntitiesManager>(),
                resolver.Resolve<Mafi.Core.PathFinding.ClearancePathabilityProvider>(),
                resolver.Resolve<Mafi.Core.PathFinding.ShipsClearancePathabilityProvider>(), resolver.Resolve<Mafi.Core.Terrain.TerrainManager>());
        var registry = resolver.Resolve<IShortcutsRegistry>();
        registry.Add(this, MyShortcutsStorage.ToggleGhostMode);
        registry.Add(this, MyShortcutsStorage.ConvertGhostsToPlans);
        resolver.Resolve<GhostModeInputController>();
        resolver.Resolve<GhostConversionInputController>();
        resolver.Resolve<GhostBuildingHeightDisplay>();
        m_modeIndicator = new GhostModeIndicator(resolver.Resolve<ToolbarHud>(), resolver.Resolve<UiContext>(),
            resolver.Resolve<Mafi.Unity.UiStatic.BuildModesStripsActivator>(),
            resolver.Resolve<Mafi.Unity.Ui.Controllers.TransportBuildController>());
        Logger.Info("Ghost placement and ghost-to-plan conversion initialized.");
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
    {
    }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
        var ghostBuild = registrator.PrototypesDb.Add(new TechnologyProto(Ids.Technology.GhostTool,
            new Proto.Str(Tr.GhostBuildResearch, Tr.GhostModeActive__Tooltip),
            new TechnologyProto.Gfx(GhostIcons.Build)));
        var ghostPlans = registrator.PrototypesDb.Add(new TechnologyProto(Ids.Technology.GhostPlansTool,
            new Proto.Str(Tr.GhostPlansResearch, Tr.ConvertGhostsToPlans__Tooltip),
            new TechnologyProto.Gfx(GhostIcons.Convert)));
        registrator.AddPreLockAction(r =>
        {
            var tools = r.PrototypesDb.GetOrThrow<ResearchNodeProto>(Mafi.Base.Ids.Research.Tools);
            tools.AddProtoToUnlock(ghostBuild);
            tools.AddProtoToUnlock(ghostPlans);
        });
    }

    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues)
    {
    }

    public void Dispose()
    {
        ActiveConfig = null;
        m_modeIndicator?.Dispose();
        m_modeIndicator = null;
        m_conversionFeedback?.Dispose();
        m_conversionFeedback = null;
        m_portVisualRefresh?.Dispose();
        m_portVisualRefresh = null;
        GhostConversionInputController.Current?.DisposeForHotReload();
        GhostBuildingHeightDisplay.Current?.DisposeForHotReload();
        GhostModeManager.IsGhostModeEnabled = false;
        GhostWorldState.Unbind();
        m_harmony.UnpatchAll(m_harmony.Id);
    }
}
