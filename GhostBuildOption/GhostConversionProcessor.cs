using HarmonyLib;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Factory.Transports;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain;
using Mafi.Core.Vehicles;
using Mafi.Localization;
using EntityId = Mafi.Core.EntityId;

namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsEverything)]
public sealed class GhostConversionProcessor : ICommandProcessor<ConvertGhostsCmd>, IAction<ConvertGhostsCmd>,
    ICommandProcessor<ConvertGhostCmd>, IAction<ConvertGhostCmd>,
    ICommandProcessor<ToggleGhostsCmd>, IAction<ToggleGhostsCmd>
{
    private delegate bool CheckTransport(TransportsManager manager, TransportTrajectory trajectory,
        Predicate<EntityId> ignored, out LocStrFormatted error, out Option<IStaticEntity> blocker);
    private delegate bool CheckPorts(TransportsConstructionHelper helper, TransportTrajectory trajectory,
        Mafi.Core.Ports.Io.IoPortShapeProto shape, out LocStrFormatted error);

    private static readonly CheckTransport s_checkTransport = AccessTools.MethodDelegate<CheckTransport>(
        AccessTools.Method(typeof(TransportsManager), "canBuildTransport"));
    private static readonly CheckPorts s_checkPorts = AccessTools.MethodDelegate<CheckPorts>(
        AccessTools.Method(typeof(TransportsConstructionHelper), "validateConnectedPorts"));
    private static readonly Action<LayoutEntityAddRequest> s_markActualAdd =
        AccessTools.MethodDelegate<Action<LayoutEntityAddRequest>>(
            AccessTools.Method(typeof(LayoutEntityAddRequest), "MarkAsActualAdd"));
    private static readonly Action<VehicleSurfaceProvider, IStaticEntity> s_addSurface =
        AccessTools.MethodDelegate<Action<VehicleSurfaceProvider, IStaticEntity>>(
            AccessTools.Method(typeof(VehicleSurfaceProvider), "entityAdded"));
    private static readonly Action<StaticEntityOceanReservationManagerV2, IStaticEntityWithReservedOceanV2> s_addOcean =
        AccessTools.MethodDelegate<Action<StaticEntityOceanReservationManagerV2, IStaticEntityWithReservedOceanV2>>(
            AccessTools.Method(typeof(StaticEntityOceanReservationManagerV2), "registerNewEntity"));
    private static readonly Action<VehicleSurfaceProvider, IStaticEntity> s_removeSurface =
        AccessTools.MethodDelegate<Action<VehicleSurfaceProvider, IStaticEntity>>(
            AccessTools.Method(typeof(VehicleSurfaceProvider), "entityRemoved"));
    private static readonly Action<StaticEntityOceanReservationManagerV2, IStaticEntityWithReservedOceanV2> s_removeOcean =
        AccessTools.MethodDelegate<Action<StaticEntityOceanReservationManagerV2, IStaticEntityWithReservedOceanV2>>(
            AccessTools.Method(typeof(StaticEntityOceanReservationManagerV2), "removeRegisteredEntity"));
    private static readonly Action<StaticEntityOceanReservationManagerV2, IStaticEntityWithReservedOceanV2, RectangleTerrainArea2i> s_refreshOtherDocks =
        AccessTools.MethodDelegate<Action<StaticEntityOceanReservationManagerV2, IStaticEntityWithReservedOceanV2, RectangleTerrainArea2i>>(
            AccessTools.Method(typeof(StaticEntityOceanReservationManagerV2), "reevaluateOtherDocksForBlockedArea"));

    private readonly GhostWorldState m_state;
    private readonly IEntitiesManager m_entities;
    private readonly LayoutEntityAddRequestFactory m_requests;
    private readonly TransportsManager m_transports;
    private readonly TransportsConstructionHelper m_transportChecks;
    private readonly VehicleSurfaceProvider m_surfaces;
    private readonly StaticEntityOceanReservationManagerV2 m_ocean;
    private readonly ClearancePathabilityProvider m_pathability;
    private readonly ShipsClearancePathabilityProvider m_ships;
    private readonly TerrainManager m_terrain;

    public GhostConversionProcessor(GhostWorldState state, IEntitiesManager entities,
        LayoutEntityAddRequestFactory requests, TransportsManager transports, TransportsConstructionHelper transportChecks,
        VehicleSurfaceProvider surfaces, StaticEntityOceanReservationManagerV2 ocean,
        ClearancePathabilityProvider pathability, ShipsClearancePathabilityProvider ships, TerrainManager terrain)
    {
        m_state = state;
        m_entities = entities;
        m_requests = requests;
        m_transports = transports;
        m_transportChecks = transportChecks;
        m_surfaces = surfaces;
        m_ocean = ocean;
        m_pathability = pathability;
        m_ships = ships;
        m_terrain = terrain;
    }

    public void Invoke(ConvertGhostsCmd cmd) => Convert(cmd, m_state.GhostIds.OrderBy(id => id).Where(id =>
        m_entities.TryGetEntity<IStaticEntity>(id, out var entity) && !entity.IsDestroyed && entity.IsSelected(cmd.Area)).ToArray(), false);

    public void Invoke(ConvertGhostCmd cmd) => Convert(cmd, new[] { cmd.Target }, true);

    internal static bool IsPlan(IStaticEntity entity)
    {
        if (entity.IsDestroyed || entity is IConstructionChildEntity ||
            entity.ConstructionState != ConstructionState.InConstruction || !entity.ConstructionProgress.HasValue)
            return false;
        var progress = entity.ConstructionProgress.Value;
        if (!progress.IsPaused)
            return false;
        foreach (var buffer in progress.Buffers)
            if (buffer.Quantity.IsNotZero)
                return false;
        return true;
    }

    public void Invoke(ToggleGhostsCmd cmd)
    {
        var selected = cmd.Targets.ToArray().Distinct().OrderBy(id => id)
            .Select(id => m_entities.TryGetEntity<IStaticEntity>(id, out var entity) ? entity : null)
            .OfType<IStaticEntity>().Where(entity => !entity.IsDestroyed).ToArray();
        var ghosts = selected.Where(GhostWorldState.IsGhost).Select(entity => entity.Id).ToArray();
        if (ghosts.Length > 0)
        {
            Convert(cmd, ghosts, false);
            return;
        }

        var changed = new List<IStaticEntity>();
        foreach (var entity in selected)
        {
            if (!IsPlan(entity))
                continue;
            var coastal = entity as IStaticEntityWithReservedOceanV2;
            var blockedArea = coastal?.OceanAreaBlocked ?? default;
            // Unregister while still normal: the ghost hooks intentionally skip these native methods.
            s_removeSurface(m_surfaces, entity);
            if (coastal != null)
                s_removeOcean(m_ocean, coastal);
            m_state.Remove(entity.Id);
            m_state.Add(entity.Id);
            if (coastal != null)
                s_refreshOtherDocks(m_ocean, coastal, blockedArea);
            m_entities.InvokeOnEntityVisualChanged(entity);
            changed.Add(entity);
        }
        GhostVehicleNavigation.RefreshEntities(changed, m_pathability, m_ships, m_terrain);
        Logger.Info($"Ghost planner changed {changed.Count} plan(s) into ghosts.");
        cmd.SetResultSuccess(changed.Count);
    }

    private void Convert(InputCommand<int> cmd, IEnumerable<EntityId> candidates, bool reportMissing)
    {
        var previous = GhostPlacementContext.EnterPlans();
        try
        {
            var converted = new List<IStaticEntity>();
            int failed = 0;
            string firstError = "";
            foreach (EntityId id in candidates)
            {
                if (!m_entities.TryGetEntity<IStaticEntity>(id, out var entity) || entity.IsDestroyed)
                {
                    if (reportMissing)
                    {
                        failed++;
                        firstError = Tr.GhostConversionMissing.ToString();
                        Logger.Warning($"Ghost conversion failed for {id}: {firstError}");
                    }
                    continue;
                }
                if (!GhostWorldState.IsGhost(entity))
                    continue;
                LocStrFormatted error;
                if (TryConvert(entity, out error))
                    converted.Add(entity);
                else
                {
                    failed++;
                    string detail = $"{entity.GetTitle()} ({id}, {entity.CenterTile}): {error.Value}";
                    Logger.Warning("Ghost conversion failed: " + detail);
                    if (firstError.Length == 0)
                        firstError = detail;
                }
            }
            GhostVehicleNavigation.RefreshEntities(converted, m_pathability, m_ships, m_terrain);
            string summary = Tr.GhostConversionSummary.Format(converted.Count.ToString(), failed.ToString()).Value;
            Logger.Info(summary);
            if (failed > 0)
                cmd.SetResultError(converted.Count, summary + "\n" + firstError);
            else
                cmd.SetResultSuccess(converted.Count);
        }
        finally
        {
            GhostPlacementContext.ExitPlans(previous);
        }
    }

    internal bool TryConvert(IStaticEntity entity, out LocStrFormatted error)
    {
        if (!GhostWorldState.IsGhost(entity))
        {
            error = Tr.GhostConversionNotGhost;
            return false;
        }
        var errors = new GhostConversionErrors();
        if (entity is Transport transport)
        {
            TryPrepareTransport(transport, errors);
        }
        else if (entity is ILayoutEntity layout)
        {
            // Move validates an existing unique building without counting it as a new duplicate.
            var request = m_requests.CreateRequestFor<LayoutEntityProto>(layout.Prototype,
                new EntityAddRequestData(layout.Transform, false, id => id == entity.Id, true, false, false),
                EntityAddReason.Move, false);
            try
            {
                s_markActualAdd(request);
                var preparation = new Lyst<IEntityPreAddValidator>();
                EntityValidationResult validation;
                using (new GhostValidationCapture.Capture(request, errors))
                    validation = GhostConnectorValidation.Validate(m_entities, layout, request, preparation);
                errors.Add(validation);
                errors.ReadTiles(request);
                if (!errors.Any)
                {
                    foreach (var validator in preparation)
                        validator.PrepareForAdd();
                }
            }
            finally
            {
                request.ReturnToPool();
            }
        }
        else
        {
            errors.Add(Tr.GhostConversionUnsupported);
        }

        if (errors.Any)
        {
            var failure = errors.Snapshot();
            m_state.SetFailure(entity.Id, failure);
            error = failure.Text;
            return false;
        }
        m_state.Remove(entity.Id);
        s_addSurface(m_surfaces, entity);
        if (entity is IStaticEntityWithReservedOceanV2 coastal)
            s_addOcean(m_ocean, coastal);
        m_entities.InvokeOnEntityVisualChanged(entity);
        error = LocStrFormatted.Empty;
        return true;
    }

    private void TryPrepareTransport(Transport transport, GhostConversionErrors errors)
    {
        var trajectory = transport.Trajectory;
        if (!s_checkTransport(m_transports, trajectory,
            id => id == transport.Id || m_transports.IgnorePillarsPredicate(id), out var error, out _))
        {
            errors.Add(error);
            errors.AddTiles(trajectory.OccupiedTiles.Select(tile => new Tile2i(tile.Position.X, tile.Position.Y)));
        }
        if (!m_transportChecks.CheckTerrainCollision(trajectory))
        {
            errors.Add(Mafi.Core.Tr.TrAdditionError__TerrainCollision);
            errors.AddTiles(trajectory.OccupiedTiles
                .Where(tile => tile.From < TransportHelper.GetLowestNonCollidingHeight(m_terrain[tile.Position]))
                .Select(tile => new Tile2i(tile.Position.X, tile.Position.Y)));
        }
        if (!s_checkPorts(m_transportChecks, trajectory, transport.Prototype.PortsShape, out error))
            errors.Add(error);
        if (!m_transportChecks.TryFindPillarsForTrajectory(trajectory, Set<Tile2i>.Empty,
            out ImmutableArray<Tile3i> supports, out _, out _))
        {
            errors.Add(Mafi.Core.Tr.TrAdditionError__NoPillars);
            errors.AddTiles(trajectory.OccupiedTiles.Select(tile => new Tile2i(tile.Position.X, tile.Position.Y)));
        }
        if (!errors.Any)
            foreach (var support in supports)
                m_transports.BuildOrExtendPillarNoChecks(support.Xy, support.Height, true);
    }
}
