using Mafi.Unity.InputControl.Factory;
using Mafi.Core.Terrain;
using Mafi.Unity.Terrain;
using UnityEngine.Rendering;

namespace GhostBuildOption;

internal sealed class GhostFailureGrid : IDisposable
{
    private readonly Mesh m_mesh;
    private readonly Material m_material;
    private readonly TerrainRenderer m_terrain;
    private readonly Bounds m_bounds;
    private readonly uint[] m_args;
    private readonly ComputeBuffer m_argsBuffer;
    private ComputeBuffer? m_tilesBuffer;
    private GhostConversionFailure? m_shown;

    internal GhostFailureGrid(AssetsDb assets, TerrainManager terrain, TerrainRenderer terrainRenderer)
    {
        m_terrain = terrainRenderer;
        m_mesh = assets.GetSharedPrefabOrThrow("Assets/Core/Construction/ValidationTile.prefab").GetComponent<MeshFilter>().sharedMesh;
        m_material = assets.GetClonedMaterial("Assets/Core/Construction/TileValidityOverlayInstanced.mat");
        m_material.SetVector("_TerrainInvSize", new Vector4(1f / (terrain.TerrainWidth * 2), 1f / (terrain.TerrainHeight * 2)));
        m_bounds = new Bounds(new Vector3(terrain.TerrainWidth, 0, terrain.TerrainHeight),
            new Vector3(terrain.TerrainWidth * 2, 10000, terrain.TerrainHeight * 2));
        m_args = new[] { m_mesh.GetIndexCount(0), 0u, m_mesh.GetIndexStart(0), m_mesh.GetBaseVertex(0), 0u };
        m_argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    internal void Render(GhostConversionFailure? failure)
    {
        if (!ReferenceEquals(failure, m_shown))
        {
            m_shown = failure;
            int count = failure?.Tiles.Count ?? 0;
            m_args[1] = (uint)count;
            if (count > 0)
            {
                if (m_tilesBuffer == null || m_tilesBuffer.count < count)
                {
                    m_tilesBuffer?.Dispose();
                    m_tilesBuffer = new ComputeBuffer(count, 12);
                    m_material.SetBuffer("_PerInstanceData", m_tilesBuffer);
                }
                var tiles = failure!.Tiles.Select(tile =>
                    new TileValidityInstanceData(tile.X, tile.Y, ColorRgba.Red, 0, false)).ToArray();
                m_tilesBuffer.SetData(tiles);
            }
            m_argsBuffer.SetData(m_args);
        }
        if (m_args[1] == 0)
            return;
        m_material.SetTexture("_HeightTex", m_terrain.HeightTexture);
        Graphics.DrawMeshInstancedIndirect(m_mesh, 0, m_material, m_bounds, m_argsBuffer,
            0, null, ShadowCastingMode.Off, false, Layer.Custom14TerrainOverlays.ToId());
    }

    public void Dispose()
    {
        m_tilesBuffer?.Dispose();
        m_argsBuffer.Dispose();
        UnityEngine.Object.Destroy(m_material);
    }
}
