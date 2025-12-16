namespace Whisperleaf.Editor;

public struct RenderStats
{
    public int DrawCalls;
    public int RenderedInstances;
    public long RenderedTriangles;
    public long RenderedVertices;
    
    public int SourceMeshes;
    public long SourceVertices;
    public long SourceTriangles;
    public int TotalInstances;
    
    public int UniqueMaterials;
    public int NodesVisited;
    public int NodesCulled;
    public int LeafsTested;
    public long TrianglesCulled;
}
