using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using GhostBuildOption;
using HarmonyLib;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Economy;
using Mafi.Core.Entities.Static;
using Mafi.Core.Input;
using Mafi.Core.Terrain;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Buildings.Cargo;
using Mafi.Base.Prototypes.Machines;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Mods;
using Mafi.Core.Research;
using Mafi.Core.UnlockingTree;
using Mafi.Core.PropertiesDb;
using Mafi.Core.Terrain.Generation;
using Mafi.Localization;
using Mafi.Core.Factory.WellPumps;
using Mafi.Core.Vehicles;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.Zippers;
using Mafi.Serialization;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using Mafi.Unity.InputControl.Factory;
using Mafi.Unity.Ui;
using Mafi.Unity.Audio;
using Mafi.Core.Ports;
using Mafi.Core.Ports.Io;
using Mafi.Unity.Ports.Io;

internal static class Program
{
    private static void Main(string[] args)
    {
        string managed = Path.Combine(args[0], @"Captain of Industry_Data\Managed");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        Assembly assembly = typeof(GhostWorldState).Assembly;
        var harmony = new Harmony("GhostBuildOption.PlacementChecks");
        try
        {
            var unityOnly = new HashSet<string> { "GhostNegativeHeightPopup", "GhostShowBuildingHeight", "GhostHideBuildingHeight",
                "GhostAppearanceSettings", "GhostShortcutSettings", "GhostConstructionQuickActions", "GhostConstructionPause",
                "GhostUnityTool" };
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception) when (exception.LoaderExceptions.All(error =>
                error is TypeLoadException && error.Message.Contains("UnityEngine.TextCore.Text.FontAsset")))
            {
                if (exception.Types.Count(type => type == null) != 1)
                    throw;
                Console.WriteLine("UNITY-ONLY: Native pause-tool base/controller cannot load on CoreCLR (Unity FontAsset).");
                types = exception.Types.OfType<Type>().ToArray();
            }
            foreach (Type type in types)
                if (!unityOnly.Contains(type.Name))
                    harmony.CreateClassProcessor(type).Patch();
            Console.WriteLine("UNITY-ONLY: Height, settings, construction UI and Unity-tool hooks require in-game validation (Unity FontAsset cannot load in CoreCLR).");
            Check(harmony.GetPatchedMethods().Count() >= 20, "All simulation and elevation-control patches install");
            CheckResearchUnlocks();
            CheckShortcutLayout(assembly);
            RuntimeHelpers.RunClassConstructor(assembly.GetType("GhostBuildOption.GhostIcons", true)!.TypeHandle);
            Check(true, "Custom icons bind the native byte-array PNG decoder without Span dependencies");
            var state = new GhostWorldState();
            state.Bind();
            var ghost = (Transport)RuntimeHelpers.GetUninitializedObject(typeof(Transport));
            var normal = (Transport)RuntimeHelpers.GetUninitializedObject(typeof(Transport));
            FieldInfo id = typeof(Entity).GetField("m_id", BindingFlags.Instance | BindingFlags.NonPublic)!;
            id.SetValue(ghost, new EntityId(700001));
            id.SetValue(normal, new EntityId(700002));
            state.Add(ghost.Id);
            Check(GhostWorldState.IsGhost(ghost) && !GhostWorldState.IsGhost(normal), "Ghost membership is per entity");
            CheckGhostColors(assembly, ghost.Id, normal.Id);
            PortChecks.Run(assembly, harmony, state);
            ConnectorChecks.Run(harmony, state);
            FailureChecks.Run(state);
            TerrainFailureChecks.Run(assembly, harmony);
            TransportSplitChecks.Run(assembly, harmony, state);

            Type context = assembly.GetType("GhostBuildOption.GhostPlacementContext")!;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo enter = context.GetMethod("Enter", flags)!;
            MethodInfo exit = context.GetMethod("Exit", flags)!;
            bool Read(string name) => (bool)context.GetProperty(name, flags)!.GetValue(null)!;
            object outer = enter.Invoke(null, new object[] { true, false })!;
            Check(Read("IgnoreTerrain") && !Read("CreateGhosts"), "Preview cannot mark real ghosts");
            object inner = enter.Invoke(null, new object[] { true, true })!;
            Check(Read("CreateGhosts"), "Placement enables ghost registration");
            exit.Invoke(null, new[] { inner });
            Check(Read("IgnoreTerrain") && !Read("CreateGhosts"), "Nested scope restores preview state");
            Check(TransportHelper.GetLowestNonCollidingHeight(default).Value == HeightTilesI.MinValue.Value,
                "Ghost transport cursor is not snapped back above terrain");
            foreach (Type validator in new[] { typeof(FarmFertileGroundValidator),
                typeof(WellInjectionPumpAdditionValidator), typeof(UnstableTerrainValidator),
                typeof(FloatingEntityAdditionValidator) })
            {
                object instance = RuntimeHelpers.GetUninitializedObject(validator);
                var result = (Mafi.Core.Entities.Validators.EntityValidationResult)validator.GetMethod("CanAdd")!
                    .Invoke(instance, new object?[] { null })!;
                Check(result.IsSuccess, validator.Name + " is deferred in GB");
            }
            CheckCoastalAndDepositValidation();
            var helper = (TransportsConstructionHelper)RuntimeHelpers.GetUninitializedObject(typeof(TransportsConstructionHelper));
            Check(helper.CheckTerrainCollision(null!), "Ghost terrain checks bypass in scope");
            object?[] pillars = { null, null, null, 1, 1, false };
            Check((bool)typeof(TransportsConstructionHelper).GetMethod("TryFindPillarsForTrajectory")!.Invoke(helper, pillars)!,
                "Ghost support validation succeeds");
            Check(((ImmutableArray<Tile3i>)pillars[2]!).IsEmpty && (int)pillars[3]! == 0 && (int)pillars[4]! == 0,
                "Ghost support validation creates no supports");
            exit.Invoke(null, new[] { outer });
            Check(!Read("IgnoreTerrain") && !Read("CreateGhosts"), "Normal placement rules restored outside scope");
            ConversionChecks.Run(assembly, harmony, state);
            ReverseRegistrationChecks.Run(harmony, state);
            Check(!harmony.GetPatchedMethods().Any(method => method.DeclaringType == typeof(TerrainOccupancyManager)
                && method.Name != "GetHeightClearance"), "Native building collision checks are not patched");
            CheckClearance(ghost.Id, normal.Id);
            CheckGhostSurfaces(ghost);
            CheckModes();
            CheckElevation(assembly);
            CheckBuildingHeightControls(assembly, harmony);
            var construction = (ConstructionManager)RuntimeHelpers.GetUninitializedObject(typeof(ConstructionManager));
            Check(!construction.TrySetConstructionPause(ghost, false), "Ghost cannot be unpaused by native controls");
            object?[] normalPause = { normal, false, false };
            Check((bool)assembly.GetType("GhostBuildOption.GhostPreventUnpause")!.GetMethod("Prefix", flags)!
                .Invoke(null, normalPause)!, "Normal construction controls are unchanged");

            var preview = (StaticTransportPreview)RuntimeHelpers.GetUninitializedObject(typeof(StaticTransportPreview));
            typeof(StaticTransportPreview).GetField("<ReplacementStatus>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(preview, new ReplacementStatus(normal, false, false));
            SetMode(true);
            Check(!preview.ReplacementStatus.HasValue, "Ghost preview cannot request replacement of an existing transport");
            Check(preview.ValidationResult.HasValue && !preview.ValidationResult.Value.IsSuccess,
                "Existing transport replacement is shown as a collision");
            SetMode(false);
            Check(preview.ReplacementStatus.HasValue, "Normal transport replacement is unchanged");

            state.Invoke(new SetGhostBuildModeCmd(true));
            using var stream = new MemoryStream();
            var writer = new BlobWriter(stream);
            GhostWorldState.Serialize(state, writer);
            writer.FinalizeSerialization();
            writer.Flush();
            stream.Position = 0;
            var reader = new BlobReader(stream, 9999, ImmutableArray<ISpecialSerializerFactory>.Empty);
            GhostWorldState loaded = GhostWorldState.Deserialize(reader);
            reader.FinalizeLoading(Option<DependencyResolver>.None);
            loaded.Bind();
            Check(GhostWorldState.IsGhost(ghost) && !GhostWorldState.IsGhost(normal), "Saved ghost membership round trips");
            Check(!loaded.IsModeEnabled, "Loading resets the placement toggle");
        }
        finally
        {
            SetMode(false);
            GhostWorldState.Unbind();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static class TransportSplitChecks
    {
        private static readonly List<Transport> s_created = new();
        private static Transport s_source = null!;
        private static bool s_throwOnBuild;
        private static bool s_expectedGhost;
        private static bool s_membershipBeforeConstruction;
        private static int s_nextId = 760001;
        private static bool s_connectorGhostScope;

        private static bool SkipStorage() => false;
        private static bool AddEntity(EntitiesManager __instance, IEntity entity, EntityAddReason reasonToAdd)
        {
            AccessTools.Method(typeof(EntitiesManager), "addEntityInternal").Invoke(__instance,
                new object[] { entity, reasonToAdd, false });
            return false;
        }
        private static bool RemoveEntity(EntitiesManager __instance, IEntity entity, EntityRemoveReason reasonToRemove)
        {
            AccessTools.Method(typeof(EntitiesManager), "removeEntityInternal").Invoke(__instance,
                new object[] { entity, reasonToRemove });
            return false;
        }
        private static bool Price(ref AssetValue __result) { __result = AssetValue.Empty; return false; }
        private static bool ClearProducts(ref Quantity __result) { __result = Quantity.Zero; return false; }
        private static bool BuildConnector(ref Option<MiniZipper> __result)
        {
            var context = typeof(GhostWorldState).Assembly.GetType("GhostBuildOption.GhostPlacementContext")!;
            s_connectorGhostScope = (bool)context.GetProperty("CreateGhosts", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
            __result = Option<MiniZipper>.None;
            return false;
        }
        private static bool Build(TransportTrajectory __0, ref Transport __result)
        {
            if (s_throwOnBuild) throw new InvalidOperationException("Split fixture failure");
            __result = Create(__0);
            s_created.Add(__result);
            return false;
        }
        private static bool Progress(StaticEntity __instance, ref Option<IEntityConstructionProgress> __result)
        {
            if (!ReferenceEquals(__instance, s_source) && !s_created.Contains(__instance))
                return true;
            __result = Option<IEntityConstructionProgress>.None;
            return false;
        }
        private static bool RestoreConstruction(Transport newTransport, AssetValue availableMaterials, ref AssetValue __result)
        {
            s_membershipBeforeConstruction &= GhostWorldState.IsGhost(newTransport) == s_expectedGhost;
            __result = availableMaterials;
            return false;
        }
        private static Transport Create(TransportTrajectory trajectory)
        {
            var transport = (Transport)RuntimeHelpers.GetUninitializedObject(typeof(Transport));
            AccessTools.Field(typeof(Entity), "m_id").SetValue(transport, new EntityId(s_nextId++));
            AccessTools.Field(typeof(StaticEntity), "m_proto").SetValue(transport, trajectory.TransportProto);
            AccessTools.Field(typeof(StaticEntity), "m_constructionState").SetValue(transport, ConstructionState.InConstruction);
            SetField(transport, "m_trajectory", trajectory);
            SetField(transport, "m_products", new Queueue<TransportedProductMutable>());
            return transport;
        }

        internal static void Run(Assembly assembly, Harmony harmony, GhostWorldState state)
        {
            var add = AccessTools.Method(typeof(EntitiesManager), "addEntityInternal");
            var remove = AccessTools.Method(typeof(EntitiesManager), "removeEntityInternal");
            var storage = (EntitiesManager)RuntimeHelpers.GetUninitializedObject(typeof(EntitiesManager));
            SetField(storage, "m_entities", new Set<IEntity>());
            var manager = (TransportsManager)RuntimeHelpers.GetUninitializedObject(typeof(TransportsManager));
            SetField(manager, "m_entitiesManager", storage);
            SetField(manager, "m_transportsBuilder", RuntimeHelpers.GetUninitializedObject(typeof(TransportsBuilder)));
            SetField(manager, "m_constructionManager", Proxy<IConstructionManager>((method, _) =>
                method.Name == "ResetConstructionAnimationState" ? null : throw new InvalidOperationException(method.Name)));
            SetField(manager, "m_resolver", Proxy<IResolver>((method, _) =>
                method.Name == "Resolve" ? Proxy<IAssetTransactionManager>((_, _) => throw new InvalidOperationException()) :
                    throw new InvalidOperationException(method.Name)));
            AccessTools.Field(typeof(TransportsManager), "<ProductsManager>k__BackingField").SetValue(manager,
                Proxy<IProductsManager>((method, _) =>
                    method.Name == "ProductDestroyed" ? null : throw new InvalidOperationException(method.Name)));
            var proto = (TransportProto)RuntimeHelpers.GetUninitializedObject(typeof(TransportProto));
            var trajectory = (TransportTrajectory)RuntimeHelpers.GetUninitializedObject(typeof(TransportTrajectory));
            AccessTools.Field(typeof(TransportTrajectory), "TransportProto").SetValue(trajectory, proto);
            AccessTools.Field(typeof(TransportTrajectory), "Pivots").SetValue(trajectory,
                ImmutableArray.Create(new Tile3i(0, 0, 1), new Tile3i(4, 0, 1)));
            var geometryField = AccessTools.Field(typeof(TransportTrajectory), "m_geometry");
            object geometry = RuntimeHelpers.GetUninitializedObject(geometryField.FieldType);
            AccessTools.Field(geometry.GetType(), "RelativeWaypoints").SetValue(geometry,
                ImmutableArray.Create(default(TransportWaypoint)));
            geometryField.SetValue(trajectory, geometry);
            var hooks = new[]
            {
                (add, nameof(SkipStorage)),
                (remove, nameof(SkipStorage)),
                (AccessTools.Method(typeof(EntitiesManager), "AddEntityNoChecks"), nameof(AddEntity)),
                (AccessTools.Method(typeof(EntitiesManager), "RemoveAndDestroyEntityNoChecks"), nameof(RemoveEntity)),
                (AccessTools.Method(typeof(TransportProto), "GetPriceFor"), nameof(Price)),
                (AccessTools.Method(typeof(Transport), "ClearTransportedProducts"), nameof(ClearProducts)),
                (AccessTools.Method(typeof(TransportsBuilder), "FromTrajectory"), nameof(Build)),
                (AccessTools.Method(typeof(TransportsManager), "buildMiniZipperAt"), nameof(BuildConnector)),
                (AccessTools.PropertyGetter(typeof(StaticEntity), "ConstructionProgress"), nameof(Progress)),
                (AccessTools.Method(typeof(TransportsManager), "updateConstructionStateOfNewTransport"), nameof(RestoreConstruction))
            };
            foreach (var (method, prefix) in hooks)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(TransportSplitChecks), prefix) { priority = Priority.Last });
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var context = assembly.GetType("GhostBuildOption.GhostPlacementContext")!;
            var split = AccessTools.Method(typeof(TransportsManager), "cutOutTransport");
            bool Read(string property) => (bool)context.GetProperty(property, flags)!.GetValue(null)!;
            void RunCase(bool ghost, bool bothEnds, bool middle)
            {
                s_created.Clear();
                s_source = Create(trajectory);
                s_expectedGhost = ghost;
                s_membershipBeforeConstruction = true;
                if (ghost) state.Add(s_source.Id);
                var result = new CanCutOutTransportResult(default, default, s_source, trajectory,
                    middle ? trajectory : Option<TransportTrajectory>.None,
                    bothEnds ? trajectory : Option<TransportTrajectory>.None);
                object?[] args = { result, middle, null, null, null, null };
                try
                {
                    split.Invoke(manager, args);
                    Check(s_created.Count == 1 + (bothEnds ? 1 : 0) + (middle ? 1 : 0) &&
                        s_created.All(piece => GhostWorldState.IsGhost(piece) == ghost) && s_membershipBeforeConstruction &&
                        !GhostWorldState.IsGhost(s_source),
                        ghost ? "Partial deletion preserves ghost membership on every replacement segment" :
                            "Splitting ordinary transports does not make their replacements ghosts");
                }
                finally
                {
                    state.Remove(s_source.Id);
                    foreach (var piece in s_created) state.Remove(piece.Id);
                }
            }
            try
            {
                RunCase(true, true, true);
                RunCase(true, false, false);
                Check(!Read("CreateGhosts") && !Read("IgnoreTerrain"), "Native split restores placement context");
                object ambient = context.GetMethod("Enter", flags)!.Invoke(null, new object[] { true, true })!;
                try
                {
                    RunCase(false, true, false);
                    Check(Read("CreateGhosts"), "Normal split restores an enclosing ghost placement scope");
                }
                finally { context.GetMethod("Exit", flags)!.Invoke(null, new[] { ambient }); }
                object plans = context.GetMethod("EnterPlans", flags)!.Invoke(null, null)!;
                try { RunCase(true, true, false); }
                finally { context.GetMethod("ExitPlans", flags)!.Invoke(null, new[] { plans }); }

                s_created.Clear();
                s_source = Create(trajectory);
                s_expectedGhost = true;
                s_membershipBeforeConstruction = true;
                s_connectorGhostScope = true;
                state.Add(s_source.Id);
                try
                {
                    var connector = new CanPlaceMiniZipperAtResult(
                        new CanCutOutTransportAtResult(new Tile3i(2, 0, 1), s_source, trajectory, trajectory),
                        (MiniZipperProto)RuntimeHelpers.GetUninitializedObject(typeof(MiniZipperProto)));
                    var build = new CanBuildTransportResult(trajectory.Pivots, null, null, trajectory, false,
                        AssetValue.Empty, ImmutableArray<Tile3i>.Empty, null, null, connector, null, null, null, default, default);
                    AccessTools.Method(typeof(TransportsManager), "buildOrJoinTransport").Invoke(manager,
                        new object?[] { build, null, null, null });
                    Check(s_created.Count == 3 && !GhostWorldState.IsGhost(s_created[0]) &&
                        s_created.Skip(1).All(GhostWorldState.IsGhost) && !GhostWorldState.IsGhost(s_source) &&
                        s_membershipBeforeConstruction,
                        "Connecting a new normal branch preserves both existing ghost pieces and the normal new branch");
                    Check(!s_connectorGhostScope && !Read("CreateGhosts") && !Read("IgnoreTerrain"),
                        "Connection splitting restores normal placement before creating its new connector");
                }
                finally
                {
                    state.Remove(s_source.Id);
                    foreach (var piece in s_created) state.Remove(piece.Id);
                }

                s_throwOnBuild = true;
                try
                {
                    RunCase(true, true, false);
                    throw new InvalidOperationException("Expected split failure");
                }
                catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException error &&
                    error.Message == "Split fixture failure")
                {
                    Check(!Read("CreateGhosts") && !Read("IgnoreTerrain"), "Failed split restores context through its finalizer");
                }
            }
            finally
            {
                s_throwOnBuild = false;
                foreach (var (method, prefix) in hooks)
                    harmony.Unpatch(method, AccessTools.Method(typeof(TransportSplitChecks), prefix));
            }
        }
    }

    private static class ConversionChecks
    {
        private static bool s_fertile;
        private static bool FarmableSoil(ref ThicknessTilesF __result, ref bool surfaceInWay)
        {
            __result = s_fertile ? new ThicknessTilesF(100) : ThicknessTilesF.Zero;
            surfaceInWay = false;
            return false;
        }

        private sealed class Preparation : IEntityPreAddValidator
        {
            private readonly Action m_apply;
            public EntityValidatorPriority Priority => default;
            public Preparation(Action apply) => m_apply = apply;
            public void PrepareForAdd() => m_apply();
        }

        internal static void Run(Assembly assembly, Harmony harmony, GhostWorldState state)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            Type context = assembly.GetType("GhostBuildOption.GhostPlacementContext")!;
            object Scope(string name, params object[] args) => context.GetMethod(name, flags)!.Invoke(null, args)!;
            bool Flag(string name) => (bool)context.GetProperty(name, flags)!.GetValue(null)!;
            var progress = Proxy<IEntityConstructionProgress>((method, _) => method.Name switch
            {
                "get_IsPaused" => true,
                "get_Buffers" => ImmutableArray<IProductBufferReadOnly>.Empty,
                _ => throw new InvalidOperationException(method.Name)
            });
            var unpausedProgress = Proxy<IEntityConstructionProgress>((method, _) =>
                method.Name == "get_IsPaused" ? false : throw new InvalidOperationException(method.Name));
            var layout = EntityLayout.CreateEmpty(default, new RelTile2i(1, 1));
            var proto = (FarmProto)RuntimeHelpers.GetUninitializedObject(typeof(FarmProto));
            typeof(LayoutEntityProto).GetField("<Layout>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(proto, layout);
            typeof(Proto).GetField("<Strings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(proto, new Proto.Str(Loc.Str("GhostBuildOption.Tests.Farm", "Test farm", "test entity")));
            var farm = (FarmFertileGroundValidator)RuntimeHelpers.GetUninitializedObject(typeof(FarmFertileGroundValidator));
            SetField(farm, "m_farmableManager", RuntimeHelpers.GetUninitializedObject(typeof(FarmableManager)));
            MethodInfo soil = AccessTools.Method(typeof(FarmableManager), "GetFarmableThickness");
            harmony.Patch(soil, prefix: new HarmonyMethod(typeof(ConversionChecks), nameof(FarmableSoil)));
            var entities = new Dictionary<EntityId, IStaticEntity>();
            ILayoutEntity Ghost(int number, bool selected, bool unpaused = false, bool plan = false, bool destroyed = false,
                IEntityConstructionProgress? customProgress = null)
            {
                var id = new EntityId(710000 + number);
                var entity = Proxy<ILayoutEntity>((method, _) => method.Name switch
                {
                    "get_Id" => id,
                    "get_Prototype" => proto,
                    "get_DefaultTitle" => (LocStrFormatted)proto.Strings.Name,
                    "get_IsDestroyed" => destroyed,
                    "get_ConstructionState" => unpaused || plan ? ConstructionState.InConstruction : ConstructionState.Constructed,
                    "get_ConstructionProgress" => customProgress != null ? Option<IEntityConstructionProgress>.Some(customProgress) :
                        unpaused ? Option<IEntityConstructionProgress>.Some(unpausedProgress) :
                        plan ? Option<IEntityConstructionProgress>.Some(progress) : Option<IEntityConstructionProgress>.None,
                    "get_Transform" => new TileTransform(new Tile3i(number, 0, 0), default, false),
                    "get_CenterTile" => new Tile3i(number, 0, 0),
                    "get_OccupiedTiles" => ImmutableArray<OccupiedTileRelative>.Empty,
                    "get_VehicleSurfaceHeights" => ImmutableArray<KeyValuePair<Tile2i, HeightTilesF>>.Empty,
                    "IsSelected" => selected,
                    _ => throw new InvalidOperationException(method.Name)
                });
                entities.Add(id, entity);
                state.Add(id);
                return entity;
            }
            var valid = Ghost(1, true);
            var infertile = Ghost(2, true);
            var suppressed = Ghost(3, true);
            var unselected = Ghost(4, false, unpaused: true);
            var neighbour = Ghost(7, false);
            var inspectorProgress = Proxy<IEntityConstructionProgress>((method, _) =>
                method.Name == "get_Entity" ? valid : throw new InvalidOperationException(method.Name));
            MethodInfo getGhost = assembly.GetType("GhostBuildOption.GhostConstructionActions")!
                .GetMethod("GetGhost", flags)!;
            Check(ReferenceEquals(getGhost.Invoke(null, new object[] { inspectorProgress }), valid),
                "Ghost construction controls follow the inspected entity, independent of placement mode");
            Check(getGhost.Invoke(null, new[] { RuntimeHelpers.GetUninitializedObject(
                typeof(Mafi.Unity.Ui.Inspectors.GroupedConstructionProgressForUi)) }) == null,
                "Single-entity ghost controls do not access the invalid entity getter of grouped train progress");
            var support = Proxy<IStaticEntity>((method, _) =>
                method.Name == "get_Id" ? new EntityId(719999) : throw new InvalidOperationException(method.Name));
            int prepared = 0, visualUpdates = 0;
            bool repaired = false, failPreparation = false;
            var manager = Proxy<IEntitiesManager>((method, args) =>
            {
                switch (method.Name)
                {
                    case "TryGetEntity":
                        bool found = entities.TryGetValue((EntityId)args![0]!, out var entity);
                        args[1] = entity;
                        return found;
                    case "CanAdd":
                        var request = (LayoutEntityAddRequest)args![0]!;
                        var ownId = new EntityId(710000 + request.Origin.X);
                        Check(Flag("CreatePlans") && !Flag("IgnoreTerrain") && !Flag("CreateGhosts") &&
                            request.ReasonToAdd == EntityAddReason.Move && !request.IgnoreOceanValidation &&
                            request.SuppressFlags == ValidationSuppressFlag.None &&
                            request.IgnoreForCollisions.Value(ownId) &&
                            !request.IgnoreForCollisions.Value(new EntityId(719999)),
                            "Conversion uses strict native validation with only its own entity excluded");
                        ((Lyst<IEntityPreAddValidator>)args[2]!).Add(new Preparation(() =>
                        {
                            if (failPreparation)
                                throw new InvalidOperationException("test preparation failure");
                            Check(GhostWorldState.IsGhost(ownId), "Ghost identity remains registered until preparation succeeds");
                            object?[] pause = { support, null };
                            assembly.GetType("GhostBuildOption.GhostPauseConstruction")!.GetMethod("Prefix", flags)!.Invoke(null, pause);
                            Check((bool?)pause[1] == true && !Flag("CreateGhosts"),
                                "Conversion-created supports are paused normal plans, not ghosts");
                            prepared++;
                        }));
                        if (ownId == suppressed.Id && !repaired)
                            return EntityValidationResult.CreateSuppressedError(Mafi.Core.Tr.AdditionError__TerrainTooHigh);
                        s_fertile = ownId != infertile.Id || repaired;
                        SetField(request, "<OccupiedTiles>k__BackingField",
                            ImmutableArray.Create(default(OccupiedTileRelative)).AsReadOnlyArray);
                        SetField(request, "<OccupiedVertices>k__BackingField",
                            ImmutableArray.Create(default(OccupiedVertexRelative)).AsReadOnlyArray);
                        SetField(request, "<RecordTileErrorsAndMetadata>k__BackingField", false);
                        return farm.CanAdd(request);
                    case "InvokeOnEntityVisualChanged":
                        Check(ReferenceEquals(entities[((IStaticEntity)args![0]!).Id], args[0]),
                            "Conversion preserves the original entity instance and ID");
                        visualUpdates++;
                        return null;
                    default:
                        throw new InvalidOperationException(method.Name);
                }
            });
            var processor = new GhostConversionProcessor(state, manager, new LayoutEntityAddRequestFactory(null!),
                null!, null!, (VehicleSurfaceProvider)RuntimeHelpers.GetUninitializedObject(typeof(VehicleSurfaceProvider)),
                null!, null!, null!, null!);
            var area = new RectangleTerrainArea2i(new Tile2i(0, 0), new RelTile2i(10, 10));
            ConvertGhostsCmd RoundTrip(ConvertGhostsCmd input)
            {
                using var stream = new MemoryStream();
                var writer = new BlobWriter(stream);
                ConvertGhostsCmd.Serialize(input, writer);
                writer.FinalizeSerialization();
                writer.Flush();
                stream.Position = 0;
                var reader = new BlobReader(stream, 9999, ImmutableArray<ISpecialSerializerFactory>.Empty);
                var result = ConvertGhostsCmd.Deserialize(reader);
                reader.FinalizeLoading(Option<DependencyResolver>.None);
                return result;
            }
            object outer = Scope("Enter", true, true);
            try
            {
                var cmd = RoundTrip(new ConvertGhostsCmd(area));
                Check(cmd.Area.Equals(area), "Conversion selection survives native command serialization");
                processor.Invoke(cmd);
                Check(cmd.HasError && cmd.Result == 1 && prepared == 1 && visualUpdates == 1 &&
                    !GhostWorldState.IsGhost(valid) && GhostWorldState.IsGhost(infertile) &&
                    GhostWorldState.IsGhost(suppressed) && GhostWorldState.IsGhost(unselected),
                    "Mixed selection converts only valid ghosts; native fertility and suppressed errors retain failed ghosts");
                Check(!GhostWorldState.IsGhost(valid), "A ghost with native Constructed state and no construction progress can convert");
                Check(getGhost.Invoke(null, new object[] { inspectorProgress }) == null,
                    "Converted entities stop matching the ghost construction-control predicate");
                var synced = RoundTrip(cmd);
                Check(synced.HasError && synced.Result == 1 && synced.ErrorMessage.Contains("Test farm"),
                    "Conversion counts and native failure details survive result synchronization");
                Check(Flag("CreateGhosts") && Flag("IgnoreTerrain") && !Flag("CreatePlans"),
                    "Conversion restores the active GB scope after strict validation");
                repaired = true;
                var retry = new ConvertGhostsCmd(area);
                processor.Invoke(retry);
                Check(!retry.HasError && retry.Result == 2 && prepared == 3 && visualUpdates == 3 &&
                    !GhostWorldState.IsGhost(infertile) && !GhostWorldState.IsGhost(suppressed) &&
                    GhostWorldState.IsGhost(unselected),
                    "Repaired ghosts can be retried without converting unselected items or recreating converted entities");
                var empty = new ConvertGhostsCmd(area);
                processor.Invoke(empty);
                Check(!empty.HasError && empty.Result == 0 && prepared == 3, "Repeated selections do not duplicate support preparation");
                var target = new ConvertGhostCmd(unselected.Id);
                using (var stream = new MemoryStream())
                {
                    var writer = new BlobWriter(stream);
                    ConvertGhostCmd.Serialize(target, writer);
                    writer.FinalizeSerialization();
                    writer.Flush();
                    stream.Position = 0;
                    var reader = new BlobReader(stream, 9999, ImmutableArray<ISpecialSerializerFactory>.Empty);
                    target = ConvertGhostCmd.Deserialize(reader);
                    reader.FinalizeLoading(Option<DependencyResolver>.None);
                }
                Check(target.Target == unselected.Id, "Single-entity conversion preserves its exact ID through serialization");
                processor.Invoke(target);
                Check(!target.HasError && target.Result == 1 && prepared == 4 && !GhostWorldState.IsGhost(unselected) &&
                    GhostWorldState.IsGhost(neighbour),
                    "An unpaused ghost converts by exact ID without changing nearby ghosts");
                var alreadyConverted = new ConvertGhostCmd(unselected.Id);
                processor.Invoke(alreadyConverted);
                Check(!alreadyConverted.HasError && alreadyConverted.Result == 0 && prepared == 4,
                    "Non-ghost entities are ignored rather than offered conversion");
                var missing = new ConvertGhostCmd(new EntityId(719998));
                processor.Invoke(missing);
                Check(missing.HasError && missing.Result == 0, "A removed inspector target reports an explicit conversion failure");
                SoundChecks.Run(assembly, harmony, synced, target, missing, empty);
                var plan = Ghost(8, true, plan: true);
                var activeConstruction = Ghost(9, true, unpaused: true);
                var destroyed = Ghost(10, true, plan: true, destroyed: true);
                int delivered = 1;
                var emptyBuffer = Proxy<IProductBufferReadOnly>((method, _) =>
                    method.Name == "get_Quantity" ? Quantity.Zero : throw new InvalidOperationException(method.Name));
                var suppliedBuffer = Proxy<IProductBufferReadOnly>((method, _) =>
                    method.Name == "get_Quantity" ? new Quantity(delivered) : throw new InvalidOperationException(method.Name));
                var suppliedProgress = Proxy<IEntityConstructionProgress>((method, _) => method.Name switch
                {
                    "get_IsPaused" => true,
                    "get_Buffers" => ImmutableArray.Create(emptyBuffer, suppliedBuffer),
                    _ => throw new InvalidOperationException(method.Name)
                });
                var suppliedPlan = Ghost(11, true, plan: true, customProgress: suppliedProgress);
                foreach (var entity in new[] { plan, activeConstruction, destroyed, suppliedPlan })
                    state.Remove(entity.Id);
                ToggleGhostsCmd Toggle(params EntityId[] ids) => new ToggleGhostsCmd(ImmutableArray.Create(ids));
                ToggleGhostsCmd RoundTripToggle(ToggleGhostsCmd input)
                {
                    using var stream = new MemoryStream();
                    var writer = new BlobWriter(stream);
                    ToggleGhostsCmd.Serialize(input, writer);
                    writer.FinalizeSerialization();
                    writer.Flush();
                    stream.Position = 0;
                    var reader = new BlobReader(stream, 9999, ImmutableArray<ISpecialSerializerFactory>.Empty);
                    var result = ToggleGhostsCmd.Deserialize(reader);
                    reader.FinalizeLoading(Option<DependencyResolver>.None);
                    return result;
                }
                var mixed = RoundTripToggle(Toggle(plan.Id, neighbour.Id, neighbour.Id));
                processor.Invoke(mixed);
                Check(!mixed.HasError && mixed.Result == 1 && !GhostWorldState.IsGhost(neighbour) &&
                    !GhostWorldState.IsGhost(plan) && prepared == 5,
                    "Planner selections prefer ghosts, deduplicate IDs and leave normal plans unchanged");
                var reverse = RoundTripToggle(Toggle(plan.Id, plan.Id, valid.Id, activeConstruction.Id,
                    destroyed.Id, suppliedPlan.Id, new EntityId(719998)));
                processor.Invoke(reverse);
                Check(!reverse.HasError && reverse.Result == 1 && GhostWorldState.IsGhost(plan) &&
                    !GhostWorldState.IsGhost(valid) && !GhostWorldState.IsGhost(activeConstruction) &&
                    !GhostWorldState.IsGhost(destroyed) && !GhostWorldState.IsGhost(suppliedPlan) && prepared == 5,
                    "With no ghosts, only empty paused plans become ghosts; supplied, built, active, destroyed and missing items are ignored");
                Check(RoundTripToggle(reverse).Result == 1, "Reverse planner result survives command synchronization");
                var single = Toggle(plan.Id);
                processor.Invoke(single);
                Check(!single.HasError && single.Result == 1 && !GhostWorldState.IsGhost(plan) && prepared == 6,
                    "A single selected ghost validates back into a plan in place");
                var reverseAgain = Toggle(plan.Id);
                processor.Invoke(reverseAgain);
                Check(!reverseAgain.HasError && reverseAgain.Result == 1 && GhostWorldState.IsGhost(plan),
                    "A second single-item selection toggles the paused plan back into a ghost");
                state.Remove(plan.Id);
                var isPlan = typeof(GhostConversionProcessor).GetMethod("IsPlan", BindingFlags.Static | BindingFlags.NonPublic)!;
                Check(!(bool)isPlan.Invoke(null, new object[] { suppliedPlan })!,
                    "One delivered item in any construction buffer excludes a plan from hover and click selection");
                delivered = 0;
                Check((bool)isPlan.Invoke(null, new object[] { suppliedPlan })!,
                    "A paused plan with only empty construction buffers is selectable");
                delivered = 1;
                var suppliedOnly = Toggle(suppliedPlan.Id);
                processor.Invoke(suppliedOnly);
                Check(!suppliedOnly.HasError && suppliedOnly.Result == 0 && !GhostWorldState.IsGhost(suppliedPlan),
                    "Simulation rechecks deliveries and refuses a now-supplied plan even if it was previously selectable");
                delivered = 0;
                var emptied = Toggle(suppliedPlan.Id);
                processor.Invoke(emptied);
                Check(!emptied.HasError && emptied.Result == 1 && GhostWorldState.IsGhost(suppliedPlan),
                    "Plan-to-ghost conversion succeeds when every material buffer is empty");
                state.Remove(suppliedPlan.Id);
                state.Add(infertile.Id);
                repaired = false;
                var failedMixed = Toggle(infertile.Id, plan.Id);
                processor.Invoke(failedMixed);
                Check(failedMixed.HasError && failedMixed.Result == 0 && GhostWorldState.IsGhost(infertile) &&
                    !GhostWorldState.IsGhost(plan),
                    "Failed ghost validation never falls through to reverse conversion of normal plans");
                state.Remove(infertile.Id);
                repaired = true;
                CheckUnityTool(assembly, plan, valid);
                var interrupted = Ghost(6, true);
                failPreparation = true;
                try
                {
                    processor.Invoke(new ConvertGhostsCmd(area));
                    throw new InvalidOperationException("Expected preparation failure was swallowed.");
                }
                catch (InvalidOperationException exception) when (exception.Message == "test preparation failure")
                {
                    Check(GhostWorldState.IsGhost(interrupted) && Flag("CreateGhosts") && !Flag("CreatePlans"),
                        "Unexpected preparation errors propagate, retain ghost membership and restore the outer scope");
                }

                CheckTransportConversion(assembly, harmony, state, progress);
            }
            finally
            {
                Scope("Exit", outer);
                harmony.Unpatch(soil, AccessTools.Method(typeof(ConversionChecks), nameof(FarmableSoil)));
                foreach (var id in entities.Keys)
                    state.Remove(id);
            }
        }

        private static void CheckUnityTool(Assembly assembly, IStaticEntity plan, IStaticEntity built)
        {
            MethodInfo filter = assembly.GetType("GhostBuildOption.GhostUnityTool", true)!
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
            GhostWorldState.Current!.Add(plan.Id);
            try
            {
                object?[] ghostArgs = { plan, true };
                Check(!(bool)filter.Invoke(null, ghostArgs)! && !(bool)ghostArgs[1]!,
                    "Unity-tool matching filter rejects ghosts before cost and highlight evaluation");
            }
            finally
            {
                GhostWorldState.Current.Remove(plan.Id);
            }
            object?[] args = { built, false };
            Check((bool)filter.Invoke(null, args)!,
                "Unity tool retains native matching for non-ghost entities");
        }

        private static Transport s_conversionTransport = null!;
        private static Option<IEntityConstructionProgress> s_conversionProgress;
        private static int s_transportFailure;
        private static readonly List<Tile3i> s_builtSupports = new();
        private static readonly ImmutableArray<Tile3i> s_requiredSupports =
            ImmutableArray.Create(new Tile3i(5, 6, 3), new Tile3i(7, 8, 4));

        private static bool ConversionProgress(StaticEntity __instance, ref Option<IEntityConstructionProgress> __result)
        {
            if (!ReferenceEquals(__instance, s_conversionTransport))
                return true;
            __result = s_conversionProgress;
            return false;
        }

        private static bool ConversionCollision(Predicate<EntityId> __1, ref LocStrFormatted __2,
            ref Option<IStaticEntity> __3, ref bool __result)
        {
            Check(__1(s_conversionTransport.Id) && !__1(new EntityId(719999)),
                "Transport conversion ignores its own occupancy, not other buildings");
            __2 = LocStrFormatted.Empty;
            __3 = Option<IStaticEntity>.None;
            __result = true;
            return false;
        }

        private static bool ConversionTerrain(ref bool __result)
        {
            __result = s_transportFailure != 1 && s_transportFailure != 3;
            return false;
        }

        private static bool ConversionPorts(ref LocStrFormatted __2, ref bool __result)
        {
            __2 = LocStrFormatted.Empty;
            __result = true;
            return false;
        }

        private static bool ConversionPillars(ref ImmutableArray<Tile3i> __2, ref int __3, ref int __4, ref bool __result)
        {
            __2 = s_requiredSupports;
            __3 = s_requiredSupports.Length;
            __4 = 0;
            __result = s_transportFailure != 2 && s_transportFailure != 3;
            return false;
        }

        private static bool RecordPillar(Tile2i __0, HeightTilesI __1)
        {
            s_builtSupports.Add(new Tile3i(__0, __1.Value));
            return false;
        }

        private static void CheckTransportConversion(Assembly assembly, Harmony harmony, GhostWorldState state,
            IEntityConstructionProgress progress)
        {
            s_conversionTransport = (Transport)RuntimeHelpers.GetUninitializedObject(typeof(Transport));
            s_conversionProgress = Option<IEntityConstructionProgress>.Some(progress);
            AccessTools.Field(typeof(Entity), "m_id").SetValue(s_conversionTransport, new EntityId(710005));
            AccessTools.Field(typeof(StaticEntity), "m_constructionState")
                .SetValue(s_conversionTransport, ConstructionState.InConstruction);
            var proto = (TransportProto)RuntimeHelpers.GetUninitializedObject(typeof(TransportProto));
            AccessTools.Field(typeof(StaticEntity), "m_proto").SetValue(s_conversionTransport, proto);
            var trajectory = (TransportTrajectory)RuntimeHelpers.GetUninitializedObject(typeof(TransportTrajectory));
            typeof(TransportTrajectory).GetField("TransportProto")!.SetValue(trajectory, proto);
            SetField(trajectory, "m_occupiedTiles", ImmutableArray<OccupiedTileRange>.Empty);
            SetField(s_conversionTransport, "m_trajectory", trajectory);
            var transports = (TransportsManager)RuntimeHelpers.GetUninitializedObject(typeof(TransportsManager));
            SetField(transports, "<IgnorePillarsPredicate>k__BackingField", new Predicate<EntityId>(_ => false));
            int visualUpdates = 0;
            var entities = Proxy<IEntitiesManager>((method, _) =>
            {
                if (method.Name != "InvokeOnEntityVisualChanged")
                    throw new InvalidOperationException(method.Name);
                visualUpdates++;
                return null;
            });
            var processor = new GhostConversionProcessor(state, entities, null!, transports,
                (TransportsConstructionHelper)RuntimeHelpers.GetUninitializedObject(typeof(TransportsConstructionHelper)),
                (VehicleSurfaceProvider)RuntimeHelpers.GetUninitializedObject(typeof(VehicleSurfaceProvider)),
                null!, null!, null!, null!);
            var hooks = new[]
            {
                (AccessTools.PropertyGetter(typeof(StaticEntity), "ConstructionProgress"), nameof(ConversionProgress)),
                (AccessTools.Method(typeof(TransportsManager), "canBuildTransport"), nameof(ConversionCollision)),
                (AccessTools.Method(typeof(TransportsConstructionHelper), "CheckTerrainCollision"), nameof(ConversionTerrain)),
                (AccessTools.Method(typeof(TransportsConstructionHelper), "validateConnectedPorts"), nameof(ConversionPorts)),
                (AccessTools.Method(typeof(TransportsConstructionHelper), "TryFindPillarsForTrajectory"), nameof(ConversionPillars)),
                (AccessTools.Method(typeof(TransportsManager), "BuildOrExtendPillarNoChecks"), nameof(RecordPillar))
            };
            foreach (var (method, prefix) in hooks)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ConversionChecks), prefix));
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            Type context = assembly.GetType("GhostBuildOption.GhostPlacementContext")!;
            object scope = context.GetMethod("EnterPlans", flags)!.Invoke(null, null)!;
            var convert = typeof(GhostConversionProcessor).GetMethod("TryConvert", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool Convert() => (bool)convert.Invoke(processor, new object?[] { s_conversionTransport, null })!;
            state.Add(s_conversionTransport.Id);
            s_builtSupports.Clear();
            try
            {
                s_transportFailure = 1;
                Check(!Convert() && GhostWorldState.IsGhost(s_conversionTransport) && s_builtSupports.Count == 0,
                    "Terrain-blocked transport remains a ghost without creating supports");
                s_transportFailure = 2;
                Check(!Convert() && GhostWorldState.IsGhost(s_conversionTransport) && s_builtSupports.Count == 0,
                    "Unsupported transport remains a ghost without creating even partially returned supports");
                s_transportFailure = 3;
                Check(!Convert() && FailureChecks.Errors(FailureChecks.Failure(state, s_conversionTransport.Id)!).Length == 2 &&
                    s_builtSupports.Count == 0 && GhostWorldState.IsGhost(s_conversionTransport),
                    "Transport conversion collects terrain and support errors together without creating supports");
                s_transportFailure = 0;
                Check(Convert() && !GhostWorldState.IsGhost(s_conversionTransport) && visualUpdates == 1 &&
                    s_builtSupports.SequenceEqual(s_requiredSupports.ToArray()) && ReferenceEquals(s_conversionTransport.Trajectory, trajectory),
                    "Supported transport converts in place and builds exactly the native required pillars at their native heights");
            }
            finally
            {
                context.GetMethod("ExitPlans", flags)!.Invoke(null, new[] { scope });
                state.Remove(s_conversionTransport.Id);
                foreach (var (method, prefix) in hooks)
                    harmony.Unpatch(method, AccessTools.Method(typeof(ConversionChecks), prefix));
            }
        }

    }

    private static class PortChecks
    {
        private static readonly List<ColorRgba> s_colors = new();

        private static bool CapturePort(IoPortVisual __0, ref uint __result)
        {
            s_colors.Add(__0.BlueprintColor);
            __result = 1;
            return false;
        }

        private static bool SkipGpu() => false;

        private static bool NoOverlay(ref ColorRgba __1, ref bool __result)
        {
            __1 = default;
            __result = false;
            return false;
        }

        internal static void Run(Assembly assembly, Harmony harmony, GhostWorldState state)
        {
            var id = new EntityId(730001);
            var proto = (FarmProto)RuntimeHelpers.GetUninitializedObject(typeof(FarmProto));
            AccessTools.Field(typeof(LayoutEntityProto), "<Layout>k__BackingField")
                .SetValue(proto, EntityLayout.CreateEmpty(default, new RelTile2i(1, 1)));
            var progress = Proxy<IEntityConstructionProgress>((method, _) => method.Name switch
            {
                "get_IsPaused" => true,
                "get_IsDeconstruction" or "get_IsNearlyFinished" => false,
                _ => throw new InvalidOperationException(method.Name)
            });
            var ports = ImmutableArray<IoPort>.Empty;
            var entity = Proxy<PortTestEntity>((method, _) => method.Name switch
            {
                "get_Id" => id,
                "get_Prototype" => proto,
                "get_Transform" => new TileTransform(new Tile3i(0, 0, 0), default, false),
                "get_IsDestroyed" => false,
                "get_ConstructionState" => ConstructionState.InConstruction,
                "get_ConstructionProgress" => Option<IEntityConstructionProgress>.Some(progress),
                "get_OccupiedTiles" => ImmutableArray<OccupiedTileRelative>.Empty,
                "get_VehicleSurfaceHeights" => ImmutableArray<KeyValuePair<Tile2i, HeightTilesF>>.Empty,
                "get_Ports" => ports,
                _ => throw new InvalidOperationException(method.Name)
            });
            IoPort Port(bool end)
            {
                var port = (IoPort)RuntimeHelpers.GetUninitializedObject(typeof(IoPort));
                typeof(IoPort).GetField("OwnerEntity")!.SetValue(port, entity);
                typeof(IoPort).GetField("Spec")!.SetValue(port, RuntimeHelpers.GetUninitializedObject(typeof(PortSpec)));
                AccessTools.Field(typeof(IoPort), "<IsEndPort>k__BackingField").SetValue(port, end);
                return port;
            }
            ports = ImmutableArray.Create(Port(false), Port(true));
            var renderer = (IoPortsRenderer)RuntimeHelpers.GetUninitializedObject(typeof(IoPortsRenderer));
            FieldInfo events = AccessTools.Field(typeof(IoPortsRenderer), "m_newEvents");
            object queue = Activator.CreateInstance(events.FieldType, new object[] { 4 })!;
            events.SetValue(renderer, queue);
            Action<IEntity>? changed = null;
            var visualEvent = Proxy<IEventNonSaveable<IEntity>>((method, args) =>
            {
                if (method.Name == "AddNonSaveable")
                    changed = (Action<IEntity>)args![1]!;
                else if (method.Name == "RemoveNonSaveable")
                    changed = null;
                else
                    throw new InvalidOperationException(method.Name);
                return null;
            });
            var manager = Proxy<IEntitiesManager>((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_OnEntityVisualChanged": return visualEvent;
                    case "TryGetEntity": args![1] = entity; return true;
                    case "CanAdd": return EntityValidationResult.Success;
                    case "InvokeOnEntityVisualChanged": changed!((IEntity)args![0]!); return null;
                    default: throw new InvalidOperationException(method.Name);
                }
            });
            var refresh = (IDisposable)Activator.CreateInstance(
                assembly.GetType("GhostBuildOption.GhostPortVisualRefresh")!, manager, renderer)!;
            Type chunkType = typeof(IoPortsRenderer).GetNestedType("PortsChunkStandard", BindingFlags.NonPublic)!;
            Type baseType = chunkType.BaseType!;
            object chunk = RuntimeHelpers.GetUninitializedObject(chunkType);
            AccessTools.Field(baseType, "IoPortsRenderer").SetValue(chunk, renderer);
            FieldInfo overlay = AccessTools.Field(typeof(IoPortsRenderer), "m_infoOverlayColors");
            overlay.SetValue(renderer, RuntimeHelpers.GetUninitializedObject(overlay.FieldType));
            var showGpu = AccessTools.Method(baseType, "ShowPortVisual", new[] { typeof(IoPortVisual), typeof(ColorRgba) });
            var hideGpu = AccessTools.Method(baseType, "HidePortVisual");
            var colorOverlay = AccessTools.Method(overlay.FieldType, "TryGetColor");
            var hooks = new[]
            {
                (showGpu, nameof(CapturePort)), (hideGpu, nameof(SkipGpu)), (colorOverlay, nameof(NoOverlay))
            };
            foreach (var (method, prefix) in hooks)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(PortChecks), prefix));
            var show = AccessTools.Method(chunkType, "ShowPort");
            var hide = AccessTools.Method(chunkType, "HidePort");
            state.Add(id);
            s_colors.Clear();
            try
            {
                foreach (var port in ports)
                    show.Invoke(chunk, new object[] { port });
                ColorRgba ghostColor = s_colors[0];
                var processor = new GhostConversionProcessor(state, manager, new LayoutEntityAddRequestFactory(null!),
                    null!, null!, (VehicleSurfaceProvider)RuntimeHelpers.GetUninitializedObject(typeof(VehicleSurfaceProvider)),
                    null!, null!, null!, null!);
                var command = new ConvertGhostCmd(id);
                processor.Invoke(command);
                int count = (int)events.FieldType.GetProperty("Count")!.GetValue(queue)!;
                Check(!command.HasError && count == 4,
                    "Successful conversion queues native Hide/Show rebuilds for every visible port");
                s_colors.Clear();
                for (int i = 0; i < count; i++)
                {
                    object item = events.FieldType.GetMethod("get_Item", new[] { typeof(int) })!.Invoke(queue, new object[] { i })!;
                    var port = (IoPort)item.GetType().GetProperty("Key")!.GetValue(item)!;
                    string action = item.GetType().GetProperty("Value")!.GetValue(item)!.ToString()!;
                    (action == "Hide" ? hide : show).Invoke(chunk, new object[] { port });
                }
                var nativePlanColor = Mafi.Unity.Entities.Static.InstancedChunkBasedLayoutEntitiesRenderer.BLUEPRINT_PAUSED_COLOR;
                Check(s_colors.Count == 2 && s_colors.All(color => color == nativePlanColor && color != ghostColor),
                    "Native port rebuild replaces ghost yellow with the normal plan RGB and rendering alpha on both endpoints");
            }
            finally
            {
                refresh.Dispose();
                state.Remove(id);
                foreach (var (method, prefix) in hooks)
                    harmony.Unpatch(method, AccessTools.Method(typeof(PortChecks), prefix));
            }
            Check(changed == null, "Port visual refresh unsubscribes during mod disposal");
        }
    }

    private static class TerrainFailureChecks
    {
        private static bool Valid(ref bool __result) { __result = true; return false; }
        private static bool NoOcean(ref bool __result) { __result = false; return false; }
        private static bool Blocked(ref bool __result) { __result = true; return false; }
        private static bool Height(Tile2i __0, ref HeightTilesF __result)
        { __result = new HeightTilesF(__0.X == 0 ? -10 : 10); return false; }
        private static bool Index(ref Tile2iIndex __result) { __result = default; return false; }

        internal static void Run(Assembly assembly, Harmony harmony)
        {
            var terrain = (TerrainManager)RuntimeHelpers.GetUninitializedObject(typeof(TerrainManager));
            var ground = (StaticEntitiesTerrainInteractionManager)RuntimeHelpers.GetUninitializedObject(typeof(StaticEntitiesTerrainInteractionManager));
            SetField(ground, "m_terrainManager", terrain);
            var validator = new LayoutEntityTerrainValidator(terrain);
            var proto = (FarmProto)RuntimeHelpers.GetUninitializedObject(typeof(FarmProto));
            AccessTools.Field(typeof(LayoutEntityProto), "<Layout>k__BackingField")
                .SetValue(proto, EntityLayout.CreateEmpty(default, new RelTile2i(1, 1)));
            var request = LayoutEntityAddRequest.GetPooledInstanceToCreateEntity(proto,
                new EntityAddRequestData(default, false, null!, true, false, false), EntityAddReason.Move, false);
            SetField(request, "<OccupiedTiles>k__BackingField", ImmutableArray.Create(default(OccupiedTileRelative)).AsReadOnlyArray);
            SetField(request, "m_tileErrors", new BitMap(1));
            OccupiedVertexRelative Vertex(int x, LayoutTileConstraint constraint)
            {
                object vertex = default(OccupiedVertexRelative);
                AccessTools.Field(typeof(OccupiedVertexRelative), "RelativeX").SetValue(vertex, (short)x);
                AccessTools.Field(typeof(OccupiedVertexRelative), "m_constraintSlim").SetValue(vertex, (ushort)constraint);
                return (OccupiedVertexRelative)vertex;
            }
            var hooks = new[]
            {
                (AccessTools.Method(typeof(TerrainManager), "IsValidCoord", new[] { typeof(Tile2i) }), nameof(Valid)),
                (AccessTools.Method(typeof(TerrainManager), "GetHeight", new[] { typeof(Tile2i) }), nameof(Height)),
                (AccessTools.Method(typeof(TerrainManager), "GetTileIndex", new[] { typeof(Tile2i) }), nameof(Index)),
                (AccessTools.Method(typeof(TerrainManager), "IsBlockingBuildings", new[] { typeof(Tile2iIndex) }), nameof(Blocked)),
                (AccessTools.Method(typeof(TerrainManager), "IsOcean", new[] { typeof(Tile2iIndex) }), nameof(NoOcean))
            };
            foreach (var (method, prefix) in hooks)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(TerrainFailureChecks), prefix));
            Type errorsType = assembly.GetType("GhostBuildOption.GhostConversionErrors")!;
            object Errors() => Activator.CreateInstance(errorsType, nonPublic: true)!;
            IDisposable Capture(object errors) => (IDisposable)Activator.CreateInstance(
                assembly.GetType("GhostBuildOption.GhostValidationCapture+Capture")!,
                BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { request, errors }, null)!;
            object Snapshot(object errors) => errorsType.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(errors, null)!;
            try
            {
                SetField(request, "<OccupiedVertices>k__BackingField",
                    ImmutableArray.Create(Vertex(0, LayoutTileConstraint.Ground), Vertex(1, LayoutTileConstraint.Ground)).AsReadOnlyArray);
                var errors = Errors();
                using (Capture(errors))
                    ground.CanAdd(request);
                var messages = FailureChecks.Errors(Snapshot(errors)).Select(error => error.Value).ToArray();
                Check(messages.Contains(Mafi.Core.Tr.AdditionError__TerrainTooLow.TranslatedString) &&
                    messages.Contains(Mafi.Core.Tr.AdditionError__TerrainTooHigh.TranslatedString),
                    "One native ground validator retains both too-low and too-high errors instead of only its last result");
                SetField(request, "<OccupiedVertices>k__BackingField",
                    ImmutableArray.Create(Vertex(0, LayoutTileConstraint.Ocean)).AsReadOnlyArray);
                errors = Errors();
                using (Capture(errors))
                    ((IEntityAdditionValidator<ILayoutEntityAddRequest>)validator).CanAdd(request);
                messages = FailureChecks.Errors(Snapshot(errors)).Select(error => error.Value).ToArray();
                Check(messages.Contains(Mafi.Core.Tr.AdditionError__SomethingInWay.TranslatedString) &&
                    messages.Contains(Mafi.Core.Tr.AdditionError__NeedsOcean.TranslatedString),
                    "Terrain diagnostics include blocked tiles and missing ocean even when native validation stops after the tile error");
            }
            finally
            {
                request.ReturnToPool();
                foreach (var (method, prefix) in hooks)
                    harmony.Unpatch(method, AccessTools.Method(typeof(TerrainFailureChecks), prefix));
            }
        }
    }

    private static class FailureChecks
    {
        private sealed class Validator : IEntityAdditionValidator<LayoutEntityAddRequest>, IEntityPreAddValidator
        {
            private readonly Func<LayoutEntityAddRequest, EntityValidationResult> m_validate;
            private readonly Action m_prepare;
            public EntityValidatorPriority Priority => default;
            public Validator(Func<LayoutEntityAddRequest, EntityValidationResult> validate, Action prepare)
            { m_validate = validate; m_prepare = prepare; }
            public EntityValidationResult CanAdd(LayoutEntityAddRequest request) => m_validate(request);
            public void PrepareForAdd() => m_prepare();
        }

        internal static object? Failure(GhostWorldState state, EntityId id)
        {
            object?[] args = { id, null };
            return (bool)typeof(GhostWorldState).GetMethod("TryGetFailure", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(state, args)! ? args[1] : null;
        }
        internal static LocStrFormatted[] Errors(object failure) =>
            ((IEnumerable<LocStrFormatted>)failure.GetType().GetField("Errors", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(failure)!).ToArray();
        private static Tile2i[] Tiles(object failure) =>
            ((IEnumerable<Tile2i>)failure.GetType().GetField("Tiles", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(failure)!).ToArray();
        private static string Text(object failure) => ((LocStrFormatted)failure.GetType()
            .GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(failure)!).Value;

        private static GhostWorldState RoundTrip(GhostWorldState state, bool legacy = false)
        {
            using var stream = new MemoryStream();
            var writer = new BlobWriter(stream);
            if (!legacy)
                GhostWorldState.Serialize(state, writer);
            else if (writer.TryStartClassSerialization(state))
                writer.EnqueueDataSerialization(state, (value, output) =>
                {
                    output.WriteInt(1);
                    var ghosts = (Dict<EntityId, bool>)AccessTools.Field(typeof(GhostWorldState), "m_ghosts").GetValue(value)!;
                    Dict<EntityId, bool>.Serialize(ghosts, output);
                });
            writer.FinalizeSerialization();
            writer.Flush();
            stream.Position = 0;
            var reader = new BlobReader(stream, 9999, ImmutableArray<ISpecialSerializerFactory>.Empty);
            var result = GhostWorldState.Deserialize(reader);
            reader.FinalizeLoading(Option<DependencyResolver>.None);
            result.Bind();
            return result;
        }

        internal static void Run(GhostWorldState state)
        {
            var id = new EntityId(750001);
            bool failing = true;
            int prepared = 0, checkedCount = 0;
            var first = Mafi.Core.Tr.AdditionError__TerrainTooHigh;
            var second = Mafi.Core.Tr.AdditionError__NotFarmable;
            var validators = new EntityValidators(new AllImplementationsOf<IEntityAdditionValidator>(new IEntityAdditionValidator[]
            {
                new Validator(request =>
                {
                    checkedCount++;
                    if (!failing) return EntityValidationResult.Success;
                    request.SetTileError(0);
                    request.GetAdditionalErrorTilesStorage().Add(new Tile2i(105, 106));
                    var ocean = new Lyst<Tile2i>();
                    ocean.Add(new Tile2i(107, 108));
                    request.AddMetadata(new OceanAreaValidationMetadata(default, ColorRgba.Red, 1, 0, default, 1, ocean));
                    return EntityValidationResult.CreateError(first);
                }, () => prepared++),
                new Validator(_ => { checkedCount++; return failing ? EntityValidationResult.CreateError(second) :
                    EntityValidationResult.Success; }, () => prepared++),
                new Validator(_ => { checkedCount++; return failing ? EntityValidationResult.CreateSuppressedError(first) :
                    EntityValidationResult.Success; }, () => prepared++)
            }), AllImplementationsOf<IEntityRemovalValidator>.Empty);
            var proto = (FarmProto)RuntimeHelpers.GetUninitializedObject(typeof(FarmProto));
            AccessTools.Field(typeof(LayoutEntityProto), "<Layout>k__BackingField")
                .SetValue(proto, EntityLayout.CreateEmpty(default, new RelTile2i(1, 1)));
            var entity = Proxy<ILayoutEntity>((method, _) => method.Name switch
            {
                "get_Id" => id,
                "get_Prototype" => proto,
                "get_Transform" => new TileTransform(new Tile3i(100, 101, 0), default, false),
                "get_VehicleSurfaceHeights" => ImmutableArray<KeyValuePair<Tile2i, HeightTilesF>>.Empty,
                _ => throw new InvalidOperationException(method.Name)
            });
            var manager = Proxy<IEntitiesManager>((method, args) =>
            {
                if (method.Name == "CanAdd")
                {
                    var request = (LayoutEntityAddRequest)args![0]!;
                    SetField(request, "<OccupiedTiles>k__BackingField", ImmutableArray.Create(default(OccupiedTileRelative)).AsReadOnlyArray);
                    SetField(request, "m_tileErrors", new BitMap(1));
                    return validators.CallAllAddValidatorsFor(request, (bool)args[1]!, (Lyst<IEntityPreAddValidator>)args[2]!);
                }
                if (method.Name == "InvokeOnEntityVisualChanged") return null;
                throw new InvalidOperationException(method.Name);
            });
            var processor = new GhostConversionProcessor(state, manager, new LayoutEntityAddRequestFactory(null!),
                null!, null!, (VehicleSurfaceProvider)RuntimeHelpers.GetUninitializedObject(typeof(VehicleSurfaceProvider)),
                null!, null!, null!, null!);
            var convert = typeof(GhostConversionProcessor).GetMethod("TryConvert", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool Convert() => (bool)convert.Invoke(processor, new object?[] { entity, null })!;
            state.Add(id);
            try
            {
                Check(!Convert() && checkedCount == 3 && prepared == 0 && GhostWorldState.IsGhost(id),
                    "Failed conversion runs all native validators, including suppressed errors, without preparing anything");
                var failure = Failure(state, id)!;
                Check(Errors(failure).Length == 2 && Text(failure).Contains(first.TranslatedString) &&
                    Text(failure).Contains(second.TranslatedString),
                    "Shared hover/inspector diagnostics retain every distinct validation error, not only the native summary");
                Check(Tiles(failure).ToHashSet().SetEquals(new[] { new Tile2i(100, 101), new Tile2i(105, 106), new Tile2i(107, 108) }),
                    "Diagnostics snapshot exact layout, additional and ocean error tiles before returning the request to its pool");
                var saved = RoundTrip(state);
                var savedFailure = Failure(saved, id)!;
                Check(GhostWorldState.IsGhost(id) && Text(savedFailure) == Text(failure) &&
                    Tiles(savedFailure).SequenceEqual(Tiles(failure)),
                    "All conversion errors and affected tiles survive native save/load");
                var old = RoundTrip(state, legacy: true);
                Check(GhostWorldState.IsGhost(id) && Failure(old, id) == null,
                    "Version-one ghost saves load without requiring conversion diagnostics");
                state.Bind();
                failing = false;
                Check(Convert() && Failure(state, id) == null && !GhostWorldState.IsGhost(id) && prepared == 3,
                    "Successful retry clears the complete warning and prepares each native validator exactly once");
                state.Add(id);
                failing = true;
                Check(!Convert() && Failure(state, id) != null, "A new failed attempt replaces diagnostics after retry");
                state.Remove(id);
                Check(Failure(state, id) == null, "Removing a ghost also removes its conversion warning");
            }
            finally { state.Bind(); state.Remove(id); }
        }
    }

    private static class ConnectorChecks
    {
        private static readonly List<IStaticEntity> s_occupants = new();
        private static bool s_offLimits, s_ocean;
        private static int s_cutOuts;
        private static MiniZipperProto s_proto = null!;

        private static bool OffLimits(ref bool __result) { __result = s_offLimits; return false; }
        private static bool Ocean(ref bool __result) { __result = s_ocean; return false; }
        private static bool TileIndex(ref Tile2iIndex __result) { __result = default; return false; }

        private static bool TransportInsertion(ref CanPlaceMiniZipperAtResult __2,
            ref LocStrFormatted __3, ref bool __result)
        {
            __2 = new CanPlaceMiniZipperAtResult(default, s_proto);
            __3 = LocStrFormatted.Empty;
            __result = true;
            return false;
        }

        private static bool CutOut() { s_cutOuts++; return false; }

        internal static void Run(Harmony harmony, GhostWorldState state)
        {
            s_proto = (MiniZipperProto)RuntimeHelpers.GetUninitializedObject(typeof(MiniZipperProto));
            AccessTools.Field(typeof(LayoutEntityProto), "<Layout>k__BackingField").SetValue(s_proto, EntityLayout.CreateEmpty(default, new RelTile2i(1, 1)));
            AccessTools.Field(typeof(Proto), "<Strings>k__BackingField").SetValue(s_proto,
                new Proto.Str(Loc.Str("GhostBuildOption.Tests.Connector", "Test connector", "test entity")));
            var connector = (MiniZipper)RuntimeHelpers.GetUninitializedObject(typeof(MiniZipper));
            AccessTools.Field(typeof(Entity), "m_id").SetValue(connector, new EntityId(740001));
            AccessTools.Field(typeof(Entity), "<Prototype>k__BackingField").SetValue(connector, s_proto);
            AccessTools.Field(typeof(StaticEntity), "m_proto").SetValue(connector, s_proto);
            AccessTools.Field(typeof(LayoutEntityBase), "m_proto").SetValue(connector, s_proto);
            AccessTools.Field(typeof(MiniZipper), "Prototype").SetValue(connector, s_proto);
            AccessTools.Field(typeof(LayoutEntityBase), "<Transform>k__BackingField").SetValue(connector, new TileTransform(new Tile3i(20, 20, 0), default, false));
            AccessTools.Field(typeof(LayoutEntityBase), "m_vehicleSurfaceHeightsCache").SetValue(connector, ImmutableArray<KeyValuePair<Tile2i, HeightTilesF>>.Empty);
            var transports = (TransportsManager)RuntimeHelpers.GetUninitializedObject(typeof(TransportsManager));
            SetField(transports, "<IgnorePillarsPredicate>k__BackingField", new Predicate<EntityId>(id => id == new EntityId(740099)));
            SetField(transports, "m_terrain", RuntimeHelpers.GetUninitializedObject(typeof(TerrainManager)));
            SetField(transports, "m_occupancyManager", RuntimeHelpers.GetUninitializedObject(typeof(TerrainOccupancyManager)));
            SetField(transports, "m_constructionHelper", RuntimeHelpers.GetUninitializedObject(typeof(TransportsConstructionHelper)));
            var validator = new MiniZipperValidator(transports);
            var occupancy = (TerrainOccupancyManager)AccessTools.Field(typeof(TransportsManager), "m_occupancyManager").GetValue(transports)!;
            AccessTools.Field(typeof(TerrainOccupancyManager), "TerrainManager").SetValue(occupancy,
                AccessTools.Field(typeof(TransportsManager), "m_terrain").GetValue(transports));
            void UpdateOccupancy()
            {
                var dataType = typeof(TerrainOccupancyManager).GetNestedType("OccupancyData", BindingFlags.NonPublic)!;
                object Data(int index) => Activator.CreateInstance(dataType,
                    new object[] { s_occupants[index].Id, (short)0, (ushort)1, index + 1 < s_occupants.Count ? index : -1 })!;
                var data = Array.CreateInstance(dataType, 1);
                data.SetValue(Data(0), 0);
                SetField(occupancy, "m_data", data);
                var overflowField = AccessTools.Field(typeof(TerrainOccupancyManager), "m_overflowData");
                object overflow = Activator.CreateInstance(overflowField.FieldType, new object[] { 4 })!;
                var add = overflowField.FieldType.GetMethod("Add", new[] { dataType })!;
                for (int i = 1; i < s_occupants.Count; i++)
                    add.Invoke(overflow, new[] { Data(i) });
                overflowField.SetValue(occupancy, overflow);
            }
            bool throwValidation = false;
            int visualUpdates = 0;
            var manager = Proxy<IEntitiesManager>((method, args) =>
            {
                if (method.Name == "TryGetEntity")
                {
                    var entity = s_occupants.FirstOrDefault(candidate => candidate.Id == (EntityId)args![0]!);
                    args![1] = entity;
                    return entity != null;
                }
                if (method.Name == "CanAdd")
                {
                    if (throwValidation)
                        throw new InvalidOperationException("test connector validation exception");
                    ((Lyst<IEntityPreAddValidator>)args![2]!).Add(validator);
                    return validator.CanAdd((LayoutEntityAddRequest)args[0]!);
                }
                if (method.Name == "InvokeOnEntityVisualChanged") { visualUpdates++; return null; }
                throw new InvalidOperationException(method.Name);
            });
            SetField(occupancy, "m_entitiesManager", manager);
            var processor = new GhostConversionProcessor(state, manager, new LayoutEntityAddRequestFactory(null!),
                transports, null!, (VehicleSurfaceProvider)RuntimeHelpers.GetUninitializedObject(typeof(VehicleSurfaceProvider)),
                null!, null!, null!, null!);
            var convert = typeof(GhostConversionProcessor).GetMethod("TryConvert", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool Convert() => (bool)convert.Invoke(processor, new object?[] { connector, null })!;
            var hooks = new[]
            {
                (AccessTools.Method(typeof(TerrainManager), "IsOffLimitsOrInvalid", new[] { typeof(Tile2i) }), nameof(OffLimits)),
                (AccessTools.Method(typeof(TerrainManager), "IsOcean", new[] { typeof(Tile2iIndex) }), nameof(Ocean)),
                (AccessTools.Method(typeof(TerrainManager), "GetTileIndex", new[] { typeof(Tile2i) }), nameof(TileIndex)),
                (AccessTools.Method(typeof(TransportsConstructionHelper), "CanPlaceMiniZipperAt"), nameof(TransportInsertion)),
                (AccessTools.Method(typeof(TransportsManager), "CutOutTransportForMiniZipper"), nameof(CutOut))
            };
            foreach (var (method, prefix) in hooks)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ConnectorChecks), prefix));
            var ordinaryRequest = LayoutEntityAddRequest.GetPooledInstanceToCreateEntity(s_proto,
                new EntityAddRequestData(((ILayoutEntity)connector).Transform, false, id => id == connector.Id, true, false, false),
                EntityAddReason.Move, false);
            try
            {
                s_occupants.Clear();
                s_occupants.Add(connector);
                UpdateOccupancy();
                s_offLimits = s_ocean = false;
                s_cutOuts = 0;
                Check(!validator.CanAdd(ordinaryRequest).IsSuccess,
                    "Native connector validation reproduces self-collision outside ghost conversion");
                state.Add(connector.Id);
                Check(Convert() && !GhostWorldState.IsGhost(connector) && visualUpdates == 1 && s_cutOuts == 0,
                    "Real connector validation converts an existing connector without colliding with itself");
                Check(!validator.CanAdd(ordinaryRequest).IsSuccess,
                    "Connector self-exclusion does not leak into ordinary placement after conversion");
                state.Add(connector.Id);
                var blocker = Proxy<IStaticEntity>((method, _) => method.Name switch
                {
                    "get_Id" => new EntityId(740002),
                    "get_DefaultTitle" => (LocStrFormatted)s_proto.Strings.Name,
                    _ => throw new InvalidOperationException(method.Name)
                });
                s_occupants.Add(blocker);
                UpdateOccupancy();
                Check(!Convert() && GhostWorldState.IsGhost(connector) && visualUpdates == 1 && s_cutOuts == 0,
                    "Connector conversion still detects a different building behind its own occupancy");
                s_occupants.Remove(blocker);
                UpdateOccupancy();
                s_offLimits = true;
                Check(!Convert() && GhostWorldState.IsGhost(connector), "Connector conversion preserves native map restrictions");
                s_offLimits = false;
                s_ocean = true;
                Check(!Convert() && GhostWorldState.IsGhost(connector), "Connector conversion preserves native ocean restrictions");
                s_ocean = false;
                throwValidation = true;
                try { Convert(); throw new InvalidOperationException("Expected validation exception"); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException inner &&
                    inner.Message == "test connector validation exception")
                { }
                throwValidation = false;
                Check(!validator.CanAdd(ordinaryRequest).IsSuccess && GhostWorldState.IsGhost(connector),
                    "Connector self-exclusion is restored even when validation throws");
                var transport = (Transport)RuntimeHelpers.GetUninitializedObject(typeof(Transport));
                AccessTools.Field(typeof(Entity), "m_id").SetValue(transport, new EntityId(740003));
                s_occupants.Add(transport);
                UpdateOccupancy();
                Check(Convert() && !GhostWorldState.IsGhost(connector) && visualUpdates == 2 && s_cutOuts == 1,
                    "Connector conversion retains native transport insertion checks and prepares its cut only after validation");
            }
            finally
            {
                ordinaryRequest.ReturnToPool();
                state.Remove(connector.Id);
                s_occupants.Clear();
                foreach (var (method, prefix) in hooks)
                    harmony.Unpatch(method, AccessTools.Method(typeof(ConnectorChecks), prefix));
            }
        }
    }

    private static class ReverseRegistrationChecks
    {
        private static readonly List<string> s_calls = new();
        private static EntityId s_target;
        private static RectangleTerrainArea2i s_area;

        private static bool RemoveSurface(IStaticEntity entity)
        {
            if (entity.Id != s_target)
                return true;
            Check(!GhostWorldState.IsGhost(entity), "Plan surfaces are unregistered before ghost hooks would skip removal");
            s_calls.Add("surface");
            return false;
        }

        private static bool RemoveOcean(IStaticEntityWithReservedOceanV2 __0)
        {
            if (__0.Id != s_target)
                return true;
            Check(!GhostWorldState.IsGhost(__0), "Plan ocean reservations are removed while still registered as normal");
            s_calls.Add("ocean");
            return false;
        }

        private static bool RefreshDocks(IStaticEntityWithReservedOceanV2 owner, RectangleTerrainArea2i blockedArea)
        {
            if (owner.Id != s_target)
                return true;
            Check(GhostWorldState.IsGhost(owner) && blockedArea.Equals(s_area),
                "Neighbouring dock reservations are reevaluated after ghost exclusion using the original blocked area");
            s_calls.Add("docks");
            return false;
        }

        internal static void Run(Harmony harmony, GhostWorldState state)
        {
            s_target = new EntityId(725001);
            s_area = new RectangleTerrainArea2i(new Tile2i(3, 4), new RelTile2i(5, 6));
            s_calls.Clear();
            var progress = Proxy<IEntityConstructionProgress>((method, _) => method.Name switch
            {
                "get_IsPaused" => true,
                "get_Buffers" => ImmutableArray<IProductBufferReadOnly>.Empty,
                _ => throw new InvalidOperationException(method.Name)
            });
            var coastal = Proxy<IStaticEntityWithReservedOceanV2>((method, _) => method.Name switch
            {
                "get_Id" => s_target,
                "get_IsDestroyed" => false,
                "get_ConstructionState" => ConstructionState.InConstruction,
                "get_ConstructionProgress" => Option<IEntityConstructionProgress>.Some(progress),
                "get_OceanAreaBlocked" => GhostWorldState.IsGhost(s_target) ? default : s_area,
                "get_OccupiedTiles" => ImmutableArray<OccupiedTileRelative>.Empty,
                _ => throw new InvalidOperationException(method.Name)
            });
            var entities = Proxy<IEntitiesManager>((method, args) =>
            {
                if (method.Name == "TryGetEntity")
                {
                    args![1] = coastal;
                    return true;
                }
                if (method.Name == "InvokeOnEntityVisualChanged")
                {
                    Check(ReferenceEquals(args![0], coastal) && GhostWorldState.IsGhost(coastal),
                        "Reverse conversion refreshes entity and cached port colors only after changing membership");
                    s_calls.Add("visual");
                    return null;
                }
                throw new InvalidOperationException(method.Name);
            });
            var hooks = new[]
            {
                (AccessTools.Method(typeof(VehicleSurfaceProvider), "entityRemoved"), nameof(RemoveSurface)),
                (AccessTools.Method(typeof(StaticEntityOceanReservationManagerV2), "removeRegisteredEntity"), nameof(RemoveOcean)),
                (AccessTools.Method(typeof(StaticEntityOceanReservationManagerV2), "reevaluateOtherDocksForBlockedArea"), nameof(RefreshDocks))
            };
            foreach (var (method, prefix) in hooks)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ReverseRegistrationChecks), prefix));
            try
            {
                var processor = new GhostConversionProcessor(state, entities, null!, null!, null!,
                    (VehicleSurfaceProvider)RuntimeHelpers.GetUninitializedObject(typeof(VehicleSurfaceProvider)),
                    (StaticEntityOceanReservationManagerV2)RuntimeHelpers.GetUninitializedObject(typeof(StaticEntityOceanReservationManagerV2)),
                    null!, null!, null!);
                var command = new ToggleGhostsCmd(ImmutableArray.Create(s_target));
                processor.Invoke(command);
                Check(!command.HasError && command.Result == 1 &&
                    s_calls.SequenceEqual(new[] { "surface", "ocean", "docks", "visual" }),
                    "Reversing a coastal plan preserves the entity and safely removes its native registrations");
            }
            finally
            {
                state.Remove(s_target);
                foreach (var (method, prefix) in hooks)
                    harmony.Unpatch(method, AccessTools.Method(typeof(ReverseRegistrationChecks), prefix));
            }
        }
    }

    private static class SoundChecks
    {
        private static readonly List<UiSound> s_played = new();

        private static bool Record(UiSound __0)
        {
            s_played.Add(__0);
            return false;
        }

        internal static void Run(Assembly assembly, Harmony harmony, params InputCommand<int>[] commands)
        {
            var play = AccessTools.Method(typeof(UiSoundsPlayer), nameof(UiSoundsPlayer.Play), new[] { typeof(UiSound) });
            var record = AccessTools.Method(typeof(SoundChecks), nameof(Record));
            harmony.Patch(play, prefix: new HarmonyMethod(record));
            try
            {
                var sounds = RuntimeHelpers.GetUninitializedObject(typeof(UiSoundsPlayer));
                var complete = assembly.GetType("GhostBuildOption.GhostConversionSounds", true)!.GetMethod("PlayCompletionSound",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                s_played.Clear();
                foreach (var command in commands)
                    complete.Invoke(null, new[] { sounds, command });
                Check(s_played.SequenceEqual(new[] { UiSound.InvalidOp, UiSound.AssignClick, UiSound.InvalidOp, UiSound.AssignClick }),
                    "Completion plays native Unity-tool failure sound for any errors and success sound for every error-free batch");
            }
            finally
            {
                harmony.Unpatch(play, record);
            }
        }
    }

    private static void SetMode(bool enabled) =>
        typeof(GhostModeManager).GetProperty(nameof(GhostModeManager.IsGhostModeEnabled))!.SetValue(null, enabled);

    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static void CheckGhostColors(Assembly assembly, EntityId ghostId, EntityId normalId)
    {
        if (!ModJsonConfig.TryLoadFromFile(Path.Combine(AppContext.BaseDirectory, "config.json"), "GhostBuildOption",
            out var config, out string errors) || !string.IsNullOrEmpty(errors))
            throw new InvalidOperationException("Failed to load GB color setting: " + errors);
        MethodInfo configure = assembly.GetType("GhostBuildOption.GhostColors")!
            .GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Configure() => configure.Invoke(null, new object[] { config });
        if (!config.TrySetValue("ghost_tint_opacity", 40, out errors))
            throw new InvalidOperationException("Failed to set test tint opacity: " + errors);
        Configure();
        var progress = Proxy<IEntityConstructionProgress>((method, _) => method.Name switch
        {
            "get_IsPaused" => true,
            "get_IsDeconstruction" or "get_IsNearlyFinished" => false,
            _ => throw new InvalidOperationException(method.Name)
        });
        IStaticEntity Entity(EntityId id) => Proxy<IStaticEntity>((method, _) => method.Name switch
        {
            "get_Id" => id,
            "get_ConstructionState" => ConstructionState.InConstruction,
            "get_ConstructionProgress" => Option<IEntityConstructionProgress>.Some(progress),
            "get_IsDestroyed" => false,
            _ => throw new InvalidOperationException(method.Name)
        });
        IStaticEntity ghost = Entity(ghostId);
        IStaticEntity normal = Entity(normalId);
        bool BuildingColor(IStaticEntity entity, out ColorRgba color) =>
            Mafi.Unity.Entities.Static.InstancedChunkBasedLayoutEntitiesRenderer.GetBlueprintColor(entity, out color);
        MethodInfo transportColor = typeof(UiContext).Assembly
            .GetType("Mafi.Unity.Factory.Transports.InstancedChunkBasedTransportsRenderer")!
            .GetMethod("GetBlueprintColor", BindingFlags.Static | BindingFlags.Public)!;
        bool TransportColor(IStaticEntity entity, out ColorRgba color)
        {
            object?[] args = { entity, null };
            bool result = (bool)transportColor.Invoke(null, args)!;
            color = (ColorRgba)args[1]!;
            return result;
        }

        BuildingColor(normal, out var normalBuilding);
        TransportColor(normal, out var normalTransport);
        var neutral = Mafi.Unity.Entities.Static.InstancedChunkBasedLayoutEntitiesRenderer.BLUEPRINT_PAUSED_COLOR;
        var softYellow = new ColorRgba((byte)((neutral.R * 60 + 255 * 40 + 50) / 100),
            (byte)((neutral.G * 60 + 255 * 40 + 50) / 100), (byte)((neutral.B * 60 + 50) / 100), neutral.A);
        SetMode(false);
        Check(BuildingColor(ghost, out var building) && TransportColor(ghost, out var transport) &&
            building.ToHexRgb() == softYellow.ToHexRgb() && transport.ToHexRgb() == softYellow.ToHexRgb() &&
            building.A == normalBuilding.A && transport.A == normalTransport.A,
            "Configured GB tint is a 40-percent yellow overlay with native rendering alpha, even with GB mode off");
        var cubeType = typeof(Mafi.Unity.Entities.Static.ConstructionCubesRenderer)
            .GetNestedType("CubesForEntityData", BindingFlags.NonPublic)!;
        object cubes = RuntimeHelpers.GetUninitializedObject(cubeType);
        cubeType.GetField("Entity")!.SetValue(cubes, ghost);
        MethodInfo cubeColor = cubeType.GetMethod("getCubesColor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(((ColorRgba)cubeColor.Invoke(cubes, null)!).ToHexRgb() == softYellow.ToHexRgb(),
            "GB construction cubes use the ghost color too");
        Check(config.TrySetValue("ghost_tint_opacity", 0, out errors), "Tint opacity accepts zero");
        Configure();
        Check(BuildingColor(ghost, out building) && building.ToHexRgb() == neutral.ToHexRgb() &&
            building.A == normalBuilding.A, "Zero tint opacity restores the native planning color without changing render style");
        Check(!config.TrySetValue("ghost_tint_opacity", -1, out errors) && !string.IsNullOrEmpty(errors) &&
            !config.TrySetValue("ghost_tint_opacity", 101, out errors) && !string.IsNullOrEmpty(errors),
            "Tint opacity rejects percentages outside zero through one hundred");
        Check(config.TrySetValue("ghost_tint_opacity", 25.5, out errors), "Tint opacity accepts fractional percentages");
        Configure();
        Check(BuildingColor(ghost, out building) &&
            building.R == (byte)Math.Round(neutral.R * 0.745 + 255 * 0.255, MidpointRounding.AwayFromZero) &&
            building.A == normalBuilding.A, "Fractional opacity is applied through the native numeric configuration");
        Check(config.TrySetValue("ghost_tint_opacity", 100, out errors), "Tint opacity accepts one hundred");
        Check(config.TrySetValue("ghost_color", "#12ab34", out errors), "Native mod settings accept a custom hex color");
        Configure();
        Check(BuildingColor(ghost, out building) && TransportColor(ghost, out transport) &&
            building.ToHexRgb() == "12AB34" && transport.ToHexRgb() == "12AB34" &&
            ((ColorRgba)cubeColor.Invoke(cubes, null)!).ToHexRgb() == "12AB34",
            "Full tint opacity applies the exact custom color to buildings, transports and construction cubes");
        Check(!config.TrySetValue("ghost_color", "#GG00FF", out errors) && !string.IsNullOrEmpty(errors) &&
            !config.TrySetValue("ghost_color", "#12345", out errors) && !string.IsNullOrEmpty(errors),
            "Native settings reject malformed ghost colors with an error");
        SetMode(true);
        Check(BuildingColor(normal, out building) && building == normalBuilding &&
            TransportColor(normal, out transport) && transport == normalTransport,
            "Ordinary paused plans keep their native colors even with GB mode enabled");
        cubeType.GetField("Entity")!.SetValue(cubes, normal);
        Check(((ColorRgba)cubeColor.Invoke(cubes, null)!).ToHexRgb() != "12AB34",
            "Ordinary construction cubes are not tinted as ghosts");

        ColorRgba Preview(string type, ColorRgba color, bool special)
        {
            object?[] args = type == "GhostTransportPreviewColor"
                ? new object?[] { (ColorRgba?)color, special } : new object?[] { color, special };
            assembly.GetType("GhostBuildOption." + type)!.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, args);
            return (ColorRgba)args[0]!;
        }
        Check(Preview("GhostBuildingPreviewColor", ColorRgba.Green, false).ToHexRgb() == "12AB34" &&
            Preview("GhostTransportPreviewColor", HighlightColors.OK_GREEN, false).ToHexRgb() == "12AB34",
            "Valid building and transport GB previews use the custom color");
        Check(Preview("GhostBuildingPreviewColor", ColorRgba.Red, false) == ColorRgba.Red &&
            Preview("GhostBuildingPreviewColor", ColorRgba.Green, true) == ColorRgba.Green &&
            Preview("GhostTransportPreviewColor", HighlightColors.ERROR_RED, false) == HighlightColors.ERROR_RED &&
            Preview("GhostTransportPreviewColor", HighlightColors.OK_GREEN, true) == HighlightColors.OK_GREEN,
            "Placement errors, removal previews and existing-transport highlights keep their native colors");
        SetMode(false);
        Check(Preview("GhostBuildingPreviewColor", ColorRgba.Green, false) == ColorRgba.Green &&
            Preview("GhostTransportPreviewColor", HighlightColors.OK_GREEN, false) == HighlightColors.OK_GREEN,
            "Turning GB off restores normal placement preview colors");
        config.ResetToDefaults();
        Configure();
    }

    private static void CheckShortcutLayout(Assembly assembly)
    {
        var toggle = MyShortcutsStorage.ToggleGhostMode;
        var convert = MyShortcutsStorage.ConvertGhostsToPlans;
        Check(toggle.Group == KbGroups.Tools && convert.Group == KbGroups.Tools,
            "Both rebindable ghost shortcuts belong to Tools");
        Check(toggle.OwnerId == "GhostBuildOption" && convert.OwnerId == "GhostBuildOption",
            "Tools grouping preserves mod ownership");
        var upgrade = KbHandleBuilder.Start(nameof(Shortcuts.ToggleUpgradeTool), "Upgrade tool").SetGroup(KbGroups.Tools).Build();
        var planning = KbHandleBuilder.Start(nameof(Shortcuts.TogglePlanningMode), "Toggle planning mode").SetGroup(KbGroups.Tools).Build();
        var nativeTools = new Lyst<KbHandle> { upgrade, planning };
        var sections = new Lyst<KeyValuePair<KbGroup, Lyst<KbHandle>>>
        {
            new(KbGroups.Tools, nativeTools)
        };
        var arrange = assembly.GetType("GhostBuildOption.GhostShortcutLayout")!
            .GetMethod("Arrange", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Arrange(string owner, Lyst<KeyValuePair<KbGroup, Lyst<KbHandle>>> value) =>
            (bool)arrange.Invoke(null, new object[] { owner, value, new[] { toggle, convert } })!;
        Check(Arrange("", sections) && nativeTools.Count == 4 &&
            nativeTools[0] == upgrade && ReferenceEquals(nativeTools[1], convert) &&
            nativeTools[2] == planning && ReferenceEquals(nativeTools[3], toggle),
            "Validation shortcut appears below Upgrade while Ghost Build remains below Planning, preserving original handles");
        Check(Arrange("", sections) && nativeTools.Count == 4, "Shortcut layout does not add duplicate rows");
        var ghostSections = new Lyst<KeyValuePair<KbGroup, Lyst<KbHandle>>>
        {
            new(KbGroups.Tools, new Lyst<KbHandle> { toggle, convert })
        };
        Check(!Arrange("GhostBuildOption", ghostSections) && Arrange("OtherMod", ghostSections),
            "Duplicate GB owner block is hidden without affecting another mod's block");
    }

    private static void CheckResearchUnlocks()
    {
        var db = new ProtosDb();
        var registrator = (ProtoRegistrator)RuntimeHelpers.GetUninitializedObject(typeof(ProtoRegistrator));
        typeof(ProtoRegistrator).GetField(nameof(ProtoRegistrator.PrototypesDb))!.SetValue(registrator, db);
        var mod = (GhostBuildOption.GhostBuildOption)RuntimeHelpers.GetUninitializedObject(typeof(GhostBuildOption.GhostBuildOption));
        mod.RegisterPrototypes(registrator);
        var ghostBuild = db.GetOrThrow<TechnologyProto>(GhostBuildOption.Ids.Technology.GhostTool);
        var ghostPlans = db.GetOrThrow<TechnologyProto>(GhostBuildOption.Ids.Technology.GhostPlansTool);

        var planning = new TechnologyProto(IdsCore.Technology.PlanningTool, Proto.Str.Empty,
            new TechnologyProto.Gfx("Assets/Unity/UserInterface/Toolbar/Planning.svg"));
        var existing = new ProtoWithIconUnlock(planning);
        var tools = db.Add(new ResearchNodeProto(Mafi.Base.Ids.Research.Tools, Proto.Str.Empty,
            ImmutableArray<ResearchNodeProto>.Empty, ImmutableArray.Create<IUnlockNodeUnit>(existing),
            ImmutableArray<IResearchNodeUnlockingCondition>.Empty, 1,
            ImmutableArray<KeyValuePair<PropertyId<Percent>, Percent>>.Empty, Duration.Zero, 0,
            default, false, new ResearchNodeProto.Gfx(ImmutableArray<string>.Empty, ImmutableArray<IProtoWithIcon>.Empty)));
        var actions = (Lyst<(Option<IMod>, Action<ProtosDb>)>)typeof(ProtosDb)
            .GetField("m_preLockActions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(db)!;
        foreach (var action in actions)
            action.Item2(db);

        var cards = tools.Units.OfType<ProtoWithIconUnlock>().ToArray();
        Check(cards.Length == 3 && cards.Contains(existing) && cards.Any(card => ReferenceEquals(card.Proto, ghostBuild)) &&
            cards.Any(card => ReferenceEquals(card.Proto, ghostPlans)),
            "Tools research retains planning and adds both GB unlocks even when registered later");
        Check(cards.Where(card => !ReferenceEquals(card, existing))
            .All(card => !card.HideInUI && card.IconPath.HasValue && !string.IsNullOrEmpty(card.IconPath.Value)),
            "Both GB research unlocks are visible icon cards, not hidden unlock bindings");
        var iconPaths = cards.Where(card => !ReferenceEquals(card, existing)).Select(card => card.IconPath.Value).ToArray();
        Check(iconPaths.Distinct().Count() == 2 && iconPaths.All(path => path.StartsWith("Assets/GhostBuildOption/") &&
            File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", Path.GetFileName(path)))),
            "Both custom research icons resolve to distinct packaged ghost textures");
        foreach (string path in iconPaths)
        {
            byte[] png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", Path.GetFileName(path)));
            Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) == 256 &&
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) == 256,
                $"Packaged {Path.GetFileName(path)} is a 256x256 PNG");
        }
    }

    private static LayoutEntityAddRequest Request(ILayoutEntityProto proto, int height)
    {
        var request = (LayoutEntityAddRequest)RuntimeHelpers.GetUninitializedObject(typeof(LayoutEntityAddRequest));
        SetField(request, "<Proto>k__BackingField", proto);
        SetField(request, "<Transform>k__BackingField", new TileTransform(new Tile3i(0, 0, height), default, false));
        SetField(request, "<Origin>k__BackingField", new Tile3i(0, 0, height));
        return request;
    }

    private static void CheckCoastalAndDepositValidation()
    {
        var ocean = (ProtoWithReservedOceanValidator)RuntimeHelpers.GetUninitializedObject(typeof(ProtoWithReservedOceanValidator));
        foreach (Type type in new[] { typeof(TradeDockProto), typeof(CargoDepotProto), typeof(ShipyardProto), typeof(OceanWaterPumpProto) })
        {
            var proto = (IProtoWithReservedOceanV2)RuntimeHelpers.GetUninitializedObject(type);
            if (proto is OceanWaterPumpProto)
            {
                SetField(proto, "<MinGroundHeight>k__BackingField", new HeightTilesI(1));
                SetField(proto, "<MaxGroundHeight>k__BackingField", new HeightTilesI(11));
            }
            int minimum = proto.MinGroundHeight.Value;
            int maximum = proto.MaxGroundHeight.Value;
            Check(!ocean.CanAdd(Request(proto, minimum - 1)).IsSuccess &&
                !ocean.CanAdd(Request(proto, maximum + 1)).IsSuccess,
                type.Name + " rejects elevations outside its native limits");
            Check(ocean.CanAdd(Request(proto, minimum)).IsSuccess &&
                ocean.CanAdd(Request(proto, maximum)).IsSuccess,
                type.Name + " accepts both height boundaries without querying water");
        }
        var outlet = (OceanLiquidDumpProto)RuntimeHelpers.GetUninitializedObject(typeof(OceanLiquidDumpProto));
        Check(ocean.CanAdd(Request(outlet, -5)).IsSuccess && ocean.CanAdd(Request(outlet, 13)).IsSuccess,
            "The restored coastal height check does not restrict unrelated ocean structures");

        var product = (FluidProductProto)RuntimeHelpers.GetUninitializedObject(typeof(FluidProductProto));
        typeof(Proto).GetField("<Strings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(product, new Proto.Str(Loc.Str("GhostBuildOption.Tests.Water", "Water", "test resource")));
        var resource = (VirtualResourceProductProto)RuntimeHelpers.GetUninitializedObject(typeof(VirtualResourceProductProto));
        typeof(VirtualResourceProductProto).GetField("Product")!.SetValue(resource, product);
        typeof(VirtualResourceProductProto).GetField("IsResourceFinal")!.SetValue(resource, true);
        var pump = (WellPumpProto)RuntimeHelpers.GetUninitializedObject(typeof(WellPumpProto));
        typeof(WellPumpProto).GetField("MinedProduct")!.SetValue(pump, resource);
        var deposits = ImmutableArray<IVirtualTerrainResource>.Empty;
        var manager = Proxy<IVirtualResourceManager>((method, _) =>
            method.Name == "RetrieveResourcesAt" ? deposits : throw new InvalidOperationException(method.Name));
        var well = (WellPumpAdditionValidator)RuntimeHelpers.GetUninitializedObject(typeof(WellPumpAdditionValidator));
        SetField(well, "m_virtualResourceManager", manager);
        Check(!well.CanAdd(Request(pump, 0)).IsSuccess, "Ghost groundwater pump requires a matching deposit");
        Quantity available = new Quantity(10);
        deposits = ImmutableArray.Create<IVirtualTerrainResource>(Proxy<IVirtualTerrainResource>((method, _) =>
            method.Name switch
            {
                "get_Product" => resource,
                "get_Quantity" => available,
                _ => throw new InvalidOperationException(method.Name)
            }));
        Check(well.CanAdd(Request(pump, 0)).IsSuccess, "Ghost groundwater pump accepts a usable deposit");
        available = Quantity.Zero;
        Check(!well.CanAdd(Request(pump, 0)).IsSuccess, "Ghost groundwater pump rejects an exhausted final deposit");
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        T instance = DispatchProxy.Create<T, NativeProxy>();
        ((NativeProxy)(object)instance).Call = call;
        return instance;
    }

    private static void CheckModes()
    {
        var commands = new List<InputCommand>();
        var controllers = new Lyst<IUnityInputController>();
        var gb = (GhostModeInputController)RuntimeHelpers.GetUninitializedObject(typeof(GhostModeInputController));
        IInputScheduler scheduler = Proxy<IInputScheduler>((method, args) =>
        {
            if (method.Name != "ScheduleInputCmd") throw new InvalidOperationException(method.Name);
            commands.Add((InputCommand)args![0]!);
            return args[0];
        });
        IUnityInputMgr input = Proxy<IUnityInputMgr>((method, args) =>
        {
            if (method.Name == "get_ActiveControllers") return controllers;
            if (method.Name != "DeactivateController") throw new InvalidOperationException(method.Name);
            var controller = (IUnityInputController)args![0]!;
            controllers.Remove(controller);
            controller.Deactivate();
            return null;
        });
        SetField(gb, "m_inputManager", input);
        SetField(gb, "m_inputScheduler", scheduler);
        Type native = typeof(UiContext).Assembly.GetType("Mafi.Unity.Ui.Controllers.Tools.PlanningModeInputController")!;
        var planning = (IUnityInputController)RuntimeHelpers.GetUninitializedObject(native);
        SetField(planning, "m_inputScheduler", scheduler);
        controllers.Add(planning);
        gb.Activate();
        Check(controllers.Count == 0 && commands[0] is SetPlanningModeEnabledCmd { IsEnabled: false } &&
            commands[1] is SetGhostBuildModeCmd { IsEnabled: true }, "GB deactivates planning before enabling itself");
        controllers.Add(gb);
        typeof(GhostModeInputController).GetMethod("OnControllerActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(gb, new object[] { planning });
        Check(!GhostModeManager.IsGhostModeEnabled && commands.Last() is SetGhostBuildModeCmd { IsEnabled: false },
            "Activating planning deactivates GB");

        var access = (GhostPlanningAccess)RuntimeHelpers.GetUninitializedObject(typeof(GhostPlanningAccess));
        SetField(gb, "m_planningAccess", access);
        Check(!gb.IsVisible && gb.DeactivateShortcutsIfNotVisible,
            "GB placement and its shortcut are hidden while planning is locked");
        typeof(GhostPlanningAccess).GetProperty(nameof(GhostPlanningAccess.IsUnlocked))!.SetValue(access, true);
        Check(gb.IsVisible, "GB placement becomes available with planning research");
    }

    private static IEnumerable<CodeInstruction> IsolateLoweringKeyboard(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType?.FullName == "UnityEngine.Input" &&
                method.Name == "GetKey")
                instruction.operand = AccessTools.Method(typeof(Program), nameof(NoKeyPressed));
            yield return instruction;
        }
    }

    private static bool NoKeyPressed(int key) => false;

    private static void CheckBuildingHeightControls(Assembly assembly, Harmony harmony)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var canLower = assembly.GetType("GhostBuildOption.GhostLowerInvalidBuilding")!.GetMethod("CanLower", flags)!;
        var invalid = Mafi.Core.Entities.Validators.EntityValidationResult.CreateError(LocStrFormatted.Empty);
        bool CanLower(bool removal, Mafi.Core.Entities.Validators.EntityValidationResult result) =>
            (bool)canLower.Invoke(null, new object[] { result, removal })!;
        SetMode(true);
        Check(CanLower(false, invalid), "Invalid ghosts can be lowered below terrain to reach their coastal elevation range");
        Check(!CanLower(true, invalid), "GB does not bypass lowering validity in removal mode");
        SetMode(false);
        Check(!CanLower(false, invalid) && CanLower(false, Mafi.Core.Entities.Validators.EntityValidationResult.Success),
            "Normal lowering still requires valid placement");

        var placerType = typeof(Mafi.Unity.Ui.Controllers.LayoutEntityPlacing.StaticEntityMassPlacer);
        var lower = AccessTools.Method(placerType, "lowerEntity");
        harmony.Patch(lower, transpiler: new HarmonyMethod(typeof(Program), nameof(IsolateLoweringKeyboard)));
        var placer = RuntimeHelpers.GetUninitializedObject(placerType);
        var cursor = (TerrainCursor)RuntimeHelpers.GetUninitializedObject(typeof(TerrainCursor));
        SetField(cursor, "m_isActivated", true);
        var invalidPreview = Proxy<IStaticEntityPreview>((method, _) => method.Name switch
        {
            "get_ValidationResult" => invalid,
            "CanMoveUpDownIfValid" => true,
            _ => throw new InvalidOperationException(method.Name)
        });
        SetField(placer, "m_terrainCursor", cursor);
        SetField(placer, "m_entityPreviews", new Lyst<KeyValuePair<IStaticEntityPreview, EntityConfigData>>
        {
            new(invalidPreview, null!)
        });
        SetMode(true);
        Check((bool?)lower.Invoke(placer, null) == true && cursor.RelativeHeight.Value == -1,
            "Actual native lowering moves an invalid ghost below zero terrain-relative offset");
        SetMode(false);
        cursor.RelativeHeight = ThicknessTilesI.Zero;
        Check((bool?)lower.Invoke(placer, null) == false && cursor.RelativeHeight.Value == 0,
            "Actual native lowering keeps the original invalid-placement restriction outside GB");

        var height = assembly.GetType("GhostBuildOption.GhostPlacementHeight")!.GetMethod("Get", flags)!;
        var preview = (LayoutEntityPreview)RuntimeHelpers.GetUninitializedObject(typeof(LayoutEntityPreview));
        var anchor = new TileTransform(new Tile3i(0, 0, 15), default, false);
        foreach (int worldHeight in new[] { -2, 1, 2, 5, 11 })
        {
            SetField(preview, "<Transform>k__BackingField", new TileTransform(new Tile3i(0, 0, worldHeight), default, false));
            var displayed = (HeightTilesI)height.Invoke(null, new object[] { preview, anchor })!;
            Check(displayed.Value == worldHeight, $"Building popup reports world elevation {worldHeight}, not terrain-relative or anchor height");
        }
        Check(((HeightTilesI)height.Invoke(null, new object?[] { null, anchor })!).Value == 15,
            "Whole-blueprint popup reports its placement anchor's world elevation");
    }

    private static void CheckElevation(Assembly assembly)
    {
        Type limits = assembly.GetType("GhostBuildOption.GhostTransportHeightLimits")!;
        object Apply(string method, int value, int bound) => limits.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { new ThicknessTilesI(value), new ThicknessTilesI(bound) })!;
        SetMode(true);
        Check(((ThicknessTilesI)Apply("Min", 6, 5)).Value == 6 &&
            ((ThicknessTilesI)Apply("Max", -1, 0)).Value == -1, "GB transport elevation crosses both old limits");
        SetMode(false);
        Check(((ThicknessTilesI)Apply("Min", 6, 5)).Value == 5 &&
            ((ThicknessTilesI)Apply("Max", -1, 0)).Value == 0, "Normal transport elevation limits are unchanged");
    }

    private static void CheckClearance(EntityId ghost, EntityId normal)
    {
        var occupancy = (TerrainOccupancyManager)RuntimeHelpers.GetUninitializedObject(typeof(TerrainOccupancyManager));
        SetField(occupancy, "m_entitiesManager", Proxy<IEntitiesManager>((method, _) =>
            method.Name == "IsEntityInvalid" ? false : throw new InvalidOperationException(method.Name)));
        Type recordType = typeof(TerrainOccupancyManager).GetNestedType("OccupancyData", BindingFlags.NonPublic)!;
        object Record(EntityId id, short height, int next)
        {
            object record = Activator.CreateInstance(recordType)!;
            recordType.GetField("EntityId")!.SetValue(record, id);
            recordType.GetField("FromRaw")!.SetValue(record, height);
            recordType.GetField("VerticalSizeRaw")!.SetValue(record, (ushort)2);
            recordType.GetField("OverflowIndex")!.SetValue(record, next);
            return record;
        }
        void Records(object head, params object[] tail)
        {
            Array data = Array.CreateInstance(recordType, 1);
            data.SetValue(head, 0);
            SetField(occupancy, "m_data", data);
            Type listType = typeof(TerrainOccupancyManager).GetField("m_overflowData", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType;
            object overflow = Activator.CreateInstance(listType, new object[] { 4 })!;
            foreach (object record in tail) listType.GetMethod("Add", new[] { recordType })!.Invoke(overflow, new[] { record });
            SetField(occupancy, "m_overflowData", overflow);
        }
        int Clearance() => occupancy.GetHeightClearance(new Tile2iIndex(0), new HeightTilesI(0)).Value;
        Records(Record(ghost, 0, 0), Record(normal, 5, -1));
        Check(Clearance() == 5, "A ghost at ground level does not hide a real obstacle above it");
        Check(occupancy.IsOccupiedAt(new Tile2iIndex(0), new HeightTilesI(0), null),
            "Ghost buildings still participate in building collisions");
        Records(Record(normal, -3, 0), Record(ghost, 0, 1), Record(normal, 7, -1));
        Check(Clearance() == 7, "Ghost overflow records do not obstruct vehicles or hide later real obstacles");
        Records(Record(ghost, 0, 0), Record(ghost, 3, -1));
        Check(Clearance() == int.MaxValue, "A column containing only ghosts is fully passable");
        Records(Record(normal, 0, -1));
        Check(Clearance() == 0, "Normal ground-level buildings still block vehicles");
    }

    private static void CheckGhostSurfaces(Transport ghost)
    {
        var surfaces = (VehicleSurfaceProvider)RuntimeHelpers.GetUninitializedObject(typeof(VehicleSurfaceProvider));
        foreach (string method in new[] { "entityAdded", "entityRemoved" })
            typeof(VehicleSurfaceProvider).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(surfaces, new object[] { ghost });
        var dock = (Shipyard)RuntimeHelpers.GetUninitializedObject(typeof(Shipyard));
        typeof(Entity).GetField("m_id", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dock, ghost.Id);
        Check(dock.OceanAreaBlocked.IsEmpty && !dock.IsOceanAreaBlocked, "Ghost docks do not reserve or obstruct shipping water");
        var ocean = (StaticEntityOceanReservationManagerV2)RuntimeHelpers.GetUninitializedObject(typeof(StaticEntityOceanReservationManagerV2));
        foreach (string method in new[] { "registerNewEntity", "removeRegisteredEntity" })
            typeof(StaticEntityOceanReservationManagerV2).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ocean, new object[] { dock });
        Console.WriteLine("PASS: Ghost surfaces and ocean reservations skip native registration and removal");
        Type navigation = typeof(GhostWorldState).Assembly.GetType("GhostBuildOption.GhostShipClearance")!;
        var ownDock = new EntityId(700003);
        var ignore = (Predicate<EntityId>)navigation.GetMethod("CombineIgnoredIds", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { new Predicate<EntityId>(id => id == ownDock) })!;
        Check(ignore(ghost.Id) && ignore(ownDock) && !ignore(new EntityId(700002)),
            "Ship navigation ignores ghosts and its own dock, but not other real buildings");
    }
}

public interface PortTestEntity : ILayoutEntity, IEntityWithPorts { }

public class NativeProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Call { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
}
