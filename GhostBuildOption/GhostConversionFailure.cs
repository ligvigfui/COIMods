using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Localization;

namespace GhostBuildOption;

internal sealed class GhostConversionFailure
{
    internal readonly IReadOnlyList<LocStrFormatted> Errors;
    internal readonly IReadOnlyList<Tile2i> Tiles;
    internal LocStrFormatted ErrorList => string.Join("\n", Errors.Select(error => "- " + error.Value)).AsLoc();
    internal LocStrFormatted Text => (Tr.GhostConversionFailedHeading.ToString() + "\n" + ErrorList.Value).AsLoc();

    internal GhostConversionFailure(IEnumerable<LocStrFormatted> errors, IEnumerable<Tile2i> tiles)
    {
        Errors = Array.AsReadOnly(errors.GroupBy(error => error.Value).Select(group => group.First()).ToArray());
        Tiles = Array.AsReadOnly(tiles.Distinct().ToArray());
        if (Errors.Count == 0)
            throw new ArgumentException("A conversion failure must contain at least one error.", nameof(errors));
    }

    internal void Serialize(BlobWriter writer)
    {
        writer.WriteInt(Errors.Count);
        foreach (var error in Errors)
            LocStrFormatted.Serialize(error, writer);
        writer.WriteInt(Tiles.Count);
        foreach (var tile in Tiles)
            Tile2i.Serialize(tile, writer);
    }

    internal static GhostConversionFailure Deserialize(BlobReader reader)
    {
        int count = reader.ReadInt();
        if (count < 1)
            throw new InvalidOperationException("Invalid ghost conversion error count.");
        var errors = new LocStrFormatted[count];
        for (int i = 0; i < errors.Length; i++)
            errors[i] = LocStrFormatted.Deserialize(reader);
        count = reader.ReadInt();
        if (count < 0)
            throw new InvalidOperationException("Invalid ghost conversion error tile count.");
        var tiles = new Tile2i[count];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = Tile2i.Deserialize(reader);
        return new GhostConversionFailure(errors, tiles);
    }
}

internal sealed class GhostConversionErrors
{
    private readonly List<LocStrFormatted> m_errors = new List<LocStrFormatted>();
    private readonly HashSet<Tile2i> m_tiles = new HashSet<Tile2i>();
    internal bool Any => m_errors.Count > 0;
    internal void Add(LocStrFormatted error) =>
        m_errors.Add(error.IsEmptyOrNull ? Tr.GhostConversionUnknownError : error);
    internal void Add(EntityValidationResult result)
    {
        if (!result.IsSuccess)
            Add(result.ErrorMessageForPlayer);
    }
    internal void AddTiles(IEnumerable<Tile2i> tiles) => m_tiles.UnionWith(tiles);
    internal void ReadTiles(LayoutEntityAddRequest request)
    {
        if (!request.RecordTileErrorsAndMetadata)
            return;
        for (int i = 0; i < request.OccupiedTiles.Length; i++)
            if (request.GetTileError(i))
                m_tiles.Add(request.Origin.Xy + request.OccupiedTiles[i].RelCoord);
        if (request.HasAdditionalErrorTiles)
            m_tiles.UnionWith(request.GetAdditionalErrorTilesStorage());
        if (request.Metadata.HasValue)
            foreach (var metadata in request.Metadata.Value)
                if (metadata is OceanAreaValidationMetadata ocean)
                {
                    if (ocean.BlockedTiles.HasValue)
                        m_tiles.UnionWith(ocean.BlockedTiles.Value);
                    else if (ocean.Color.R > ocean.Color.G)
                        m_tiles.UnionWith(ocean.Area.EnumerateTiles());
                }
    }
    internal GhostConversionFailure Snapshot() => new GhostConversionFailure(m_errors, m_tiles);
}
