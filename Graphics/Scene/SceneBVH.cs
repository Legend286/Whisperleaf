using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

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
    }

    private Node[] _nodes = Array.Empty<Node>();
    private int _nodeCount = 0;
    private int _rootIndex = -1;

    // Temporary list for query results
    private readonly List<int> _queryResults = new(1024);

    public void Build(List<int> indices, Func<int, (Vector3 Min, Vector3 Max)> aabbProvider)
    {
        _nodeCount = 0;
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

        // Recursively build
        // We clone the indices list because we will sort it
        int[] workingIndices = indices.ToArray();
        _rootIndex = BuildRecursive(workingIndices, 0, workingIndices.Length, aabbProvider);
    }

    private int BuildRecursive(int[] indices, int start, int end, Func<int, (Vector3 Min, Vector3 Max)> aabbProvider)
    {
        int count = end - start;
        int nodeIndex = _nodeCount++;

        // Calculate AABB for this range
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);

        for (int i = start; i < end; i++)
        {
            var bounds = aabbProvider(indices[i]);
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
            // Note: Standard sort is slow for subarrays. We implement a quick partition or sort.
            // Using Array.Sort with IComparer is easiest for now.
            Array.Sort(indices, start, count, new AxisComparer(axis, aabbProvider));

            int mid = start + count / 2;
            _nodes[nodeIndex].Left = BuildRecursive(indices, start, mid, aabbProvider);
            _nodes[nodeIndex].Right = BuildRecursive(indices, mid, end, aabbProvider);
        }

        return nodeIndex;
    }

    public List<int> Query(Frustum frustum)
    {
        _queryResults.Clear();
        if (_rootIndex != -1)
        {
            QueryRecursive(_rootIndex, ref frustum);
        }
        return _queryResults;
    }

    private void QueryRecursive(int nodeIndex, ref Frustum frustum)
    {
        // Check AABB
        // Note: Using ref node pointer would be unsafe if array resizes, but here we don't resize during query.
        ref var node = ref _nodes[nodeIndex];

        if (!frustum.Intersects(node.Min, node.Max))
        {
            return;
        }

        if (node.Left == -1)
        {
            // Leaf
            _queryResults.Add(node.InstanceIndex);
        }
        else
        {
            QueryRecursive(node.Left, ref frustum);
            QueryRecursive(node.Right, ref frustum);
        }
    }

    private struct AxisComparer : IComparer<int>
    {
        private readonly int _axis;
        private readonly Func<int, (Vector3 Min, Vector3 Max)> _provider;

        public AxisComparer(int axis, Func<int, (Vector3 Min, Vector3 Max)> provider)
        {
            _axis = axis;
            _provider = provider;
        }

        public int Compare(int a, int b)
        {
            var boundsA = _provider(a);
            var boundsB = _provider(b);
            float cA = (boundsA.Min.Get(_axis) + boundsA.Max.Get(_axis)) * 0.5f;
            float cB = (boundsB.Min.Get(_axis) + boundsB.Max.Get(_axis)) * 0.5f;
            return cA.CompareTo(cB);
        }
    }
}

// Vector3 indexer extension for convenience
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
