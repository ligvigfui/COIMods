using Mafi.Core.Game;
using Mafi.Core.Mods;

namespace GhostBuildOption;

public sealed class GhostBuildOption : IMod
{
    public string Name => "Ghost Build Option";

    public int Version => 1;

    public bool IsUiOnly => false;

    public Option<IConfig> ModConfig => throw new NotImplementedException();
    
    public void EarlyInit(DependencyResolver resolver)
    {
        Logger.Info($"EarlyInit called for {Name} mod.");
        var harmony = new Harmony("com.faszom.project.product");
        harmony.PatchAll();
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        Logger.Info($"Initialize called for {Name} mod.");
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
    {
        Logger.Info($"RegisterDependencies called for {Name} mod.");
    }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
        Logger.Info($"RegisterPrototypes called for {Name} mod.");
    }
}
