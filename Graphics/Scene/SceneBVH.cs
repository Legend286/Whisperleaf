using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Whisperleaf.Graphics.Immediate;

namespace Whisperleaf.Graphics.Scene;

public class SceneBVH
{
    private struct Node
    {
        public Vector3 Min;
        public Vector3 Max;
        public int Left; // If -1, is leaf
        public int Right; // If > -1, InstanceIndex is in Left/Right
        public int InstanceIndex; // Only valid if Left == -1
        public int Depth; // Depth in the BVH tree
    }

    private Node[] _nodes = Array.Empty<Node>();
    private int _nodeCount = 0;
    private int _rootIndex = -1;
    private int _maxDepth = 0; // Track max depth for coloring

    // Temporary list for query results
    private readonly List<int> _queryResults = new(1024);

    private Dictionary<int, (Vector3 Min, Vector3 Max)> _cachedBounds = new();

    public void Build(List<int> indices, Func<int, (Vector3 Min, Vector3 Max)> aabbProvider)
    {
        _nodeCount = 0;
        _maxDepth = 0;
        if (indices.Count == 0)
        {
            _rootIndex = -1;
            return;
        }

        // Resize nodes array conservatively (2N - 1 nodes max)
        int maxNodes = indices.Count * 2;
        if (_nodes.Length < maxNodes)
        {
            _nodes = new Node[maxNodes];
        }

        // Pre-calculate AABBs in parallel
        _cachedBounds.Clear();
        // Since we are adding to a dictionary in parallel, we need a concurrent dictionary or array
        // Given indices are arbitrary ints, an array might be sparse if indices are large.
        // However, indices come from GltfPass which are 0..N-1 usually (meshInstances then lights).
        // Let's use a concurrent dictionary or just array if we know max index.
        // The indices passed here are from 0 to TotalInstances+Lights.
        // The caller (GltfPass) generates indices as 0..Count.
        
        // Let's assume indices are dense and small enough, or just use ConcurrentDictionary.
        // Or better: create a local array of bounds corresponding to the 'workingIndices' array positions?
        // No, the recursive build sorts indices, so we need a map from ID to Bounds.
        
        // Let's use a ConcurrentDictionary for safety and simplicity given the scope.
        // Actually, we can just use a thread-safe array if we know the max index.
        // But indices list might not be 0..N strictly if filtered?
        // In GltfPass, indices are 0..Count. So max index is Count-1.
        
        int maxIndex = 0;
        foreach(var i in indices) if (i > maxIndex) maxIndex = i;
        
        var boundsArray = new (Vector3 Min, Vector3 Max)[maxIndex + 1];
        
        Parallel.ForEach(indices, i =>
        {
            boundsArray[i] = aabbProvider(i);
        });

        // Recursively build
        // We clone the indices list because we will sort it
        int[] workingIndices = indices.ToArray();
        _rootIndex = BuildRecursive(workingIndices, 0, workingIndices.Length, boundsArray, 0);
    }

    private int BuildRecursive(int[] indices, int start, int end, (Vector3 Min, Vector3 Max)[] boundsCache, int depth)
    {
        int count = end - start;
        int nodeIndex = _nodeCount++;

        _nodes[nodeIndex].Depth = depth;
        _maxDepth = Math.Max(_maxDepth, depth);

        // Calculate AABB for this range
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);

        for (int i = start; i < end; i++)
        {
            var bounds = boundsCache[indices[i]];
            min = Vector3.Min(min, bounds.Min);
            max = Vector3.Max(max, bounds.Max);
        }

        _nodes[nodeIndex].Min = min;
        _nodes[nodeIndex].Max = max;

        if (count == 1)
        {
            // Leaf
            _nodes[nodeIndex].Left = -1;
            _nodes[nodeIndex].Right = -1;
            _nodes[nodeIndex].InstanceIndex = indices[start];
        }
        else
        {
            // Split
            // Find longest axis
            Vector3 extent = max - min;
            int axis = 0;
            if (extent.Y > extent.X) axis = 1;
            if (extent.Z > extent.Y && extent.Z > extent.X) axis = 2;

            // Sort indices based on centroids along axis
            Array.Sort(indices, start, count, new AxisComparer(axis, boundsCache));

            int mid = start + count / 2;
            _nodes[nodeIndex].Left = BuildRecursive(indices, start, mid, boundsCache, depth + 1);
            _nodes[nodeIndex].Right = BuildRecursive(indices, mid, end, boundsCache, depth + 1);
        }

        return nodeIndex;
    }

    public void DrawDebug(ImmediateRenderer renderer, RgbaFloat leafColor)
    {
        if (_nodes.Length > 0 && _rootIndex != -1 && _nodeCount > 0)
        {
            DrawNode(renderer, _rootIndex, 0, leafColor);
        }
    }

    private void DrawNode(ImmediateRenderer renderer, int nodeIndex, int depth, RgbaFloat leafColor)
    {
        if (nodeIndex >= _nodeCount) return;
        
        ref var node = ref _nodes[nodeIndex];
        
        if (node.Left == -1) // Leaf
        {
            renderer.DrawAABB(node.Min, node.Max, leafColor);
        }
        else // Internal
        {
            // Depth based color
            float t = (depth % 5) / 5.0f;
            var color = new RgbaFloat(1.0f - t, 0.5f, t, 0.3f); 
            renderer.DrawAABB(node.Min, node.Max, color);
            
            if (node.Left != -1) DrawNode(renderer, node.Left, depth + 1, leafColor);
            if (node.Right != -1) DrawNode(renderer, node.Right, depth + 1, leafColor);
        }
    }

    public struct BVHStats
    {
        public int NodesVisited;
        public int NodesCulled;
        public int LeafsTested;
    }

    public int FindEnclosingNode(Vector3 min, Vector3 max)
    {
        if (_rootIndex == -1) return -1;
        
        int current = _rootIndex;
        
        // Iterative descent
        while (true)
        {
            ref var node = ref _nodes[current];
            
            // If leaf, stop
            if (node.Left == -1) return current;
            
            ref var left = ref _nodes[node.Left];
            ref var right = ref _nodes[node.Right];
            
            bool inLeft = Contains(left.Min, left.Max, min, max);
            bool inRight = Contains(right.Min, right.Max, min, max);
            
            if (inLeft && inRight)
            {
                // In both? Pick the smaller one or just left. 
                current = node.Left;
            }
            else if (inLeft)
            {
                current = node.Left;
            }
            else if (inRight)
            {
                current = node.Right;
            }
            else
            {
                return current;
            }
        }
    }

    private static bool Contains(Vector3 containerMin, Vector3 containerMax, Vector3 itemMin, Vector3 itemMax)
    {
        return itemMin.X >= containerMin.X && itemMin.Y >= containerMin.Y && itemMin.Z >= containerMin.Z &&
               itemMax.X <= containerMax.X && itemMax.Y <= containerMax.Y && itemMax.Z <= containerMax.Z;
    }

    public (List<int> Results, BVHStats Stats) Query(Frustum frustum, int startNodeIndex = -1)
    {
        _queryResults.Clear();
        var stats = new BVHStats();
        
        int root = startNodeIndex == -1 ? _rootIndex : startNodeIndex;

        if (root != -1 && root < _nodeCount)
        {
            QueryRecursive(root, ref frustum, ref stats);
        }
        return (_queryResults, stats);
    }
    
    public (List<int> Results, BVHStats Stats) Query(Frustum frustum) => Query(frustum, -1);

    private void QueryRecursive(int nodeIndex, ref Frustum frustum, ref BVHStats stats)
    {
        if (nodeIndex >= _nodeCount) return;
        
        ref var node = ref _nodes[nodeIndex];
        stats.NodesVisited++;

        if (!frustum.Intersects(node.Min, node.Max))
        {
            stats.NodesCulled++;
            return;
        }

        if (node.Left == -1)
        {
            // Leaf
            stats.LeafsTested++;
            _queryResults.Add(node.InstanceIndex);
        }
        else
        {
            QueryRecursive(node.Left, ref frustum, ref stats);
            QueryRecursive(node.Right, ref frustum, ref stats);
        }
    }

    private struct AxisComparer : IComparer<int>
    {
        private readonly int _axis;
        private readonly (Vector3 Min, Vector3 Max)[] _boundsCache;

        public AxisComparer(int axis, (Vector3 Min, Vector3 Max)[] boundsCache)
        {
            _axis = axis;
            _boundsCache = boundsCache;
        }

        public int Compare(int a, int b)
        {
            var boundsA = _boundsCache[a];
            var boundsB = _boundsCache[b];
            float cA = (boundsA.Min.Get(_axis) + boundsA.Max.Get(_axis)) * 0.5f;
            float cB = (boundsB.Min.Get(_axis) + boundsB.Max.Get(_axis)) * 0.5f;
            return cA.CompareTo(cB);
        }
    }
}

public static class VectorExtensions
{
    public static float Get(this Vector3 v, int index) => index switch
    {
        0 => v.X,
        1 => v.Y,
        2 => v.Z,
        _ => 0
    };
}
