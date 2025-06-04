using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UniMeshlet.Common;
using Unity.Collections;
using UnityEngine;

namespace UniMeshlet.Generation
{
    public static class ClusterHierarchyBuilder
    {
        private const int MaxDepth = 10;
        private sealed class Cluster
        {
            public NativeArray<int> Indices;
            
            public Vector3 Center;
            public float Radius;

            // leaf node only
            public int MeshletID = -1;
           
            // internal node only
            public List<Cluster> Children;
            
            public uint GlobalIndex;
        }

        /// <summary>
        /// 木構造を生成し、並べ替え済み meshlet 配列＋ ClusterNode 配列を返す
        /// 返値のバグあり、refの方を利用すべき
        /// </summary>
        /// <param name="meshlets">meshopt_buildMeshlets() の結果</param>
        /// <param name="meshletVertices">頂点 index プール (uint)</param>
        /// <param name="meshletTriangles">ローカル triangle index プール (byte)</param>
        /// <param name="meshletBounds">meshlet 境界球</param>
        /// <param name="vertexCount">元メッシュ頂点数 (PartitionClusters 用)</param>
        /// <param name="targetPartitionSize">PartitionClusters のクラスタ目安サイズ</param>
        /// <param name="outNodes">GPU 送信用 ClusterNode 配列</param>
        /// <param name="outMeshlets">並べ替え後 meshlet 行テーブル</param>
        /// <param name="outMeshletVertices">並べ替え後 Vertices プール</param>
        /// <param name="outMeshletTriangles">並べ替え後 Triangles プール</param>
        /// <param name="outMeshletBounds">並べ替え後 Bounds テーブル</param>
        /// <param name="minRootCount">ルート層のノードがこの数以下になったら打切り</param>
        /// <param name="minRelativeReduction"></param>
        /// <param name="minAbsoluteReduction"></param>
        /// <param name="activeAbsoluteCheckThreshold"></param>
        /// <returns>木の深さ (root=depth-(return-1))</returns>
        public static List<HierarchyInfo> Build(
            NativeArray<MeshoptMeshlet> meshlets,
            NativeArray<uint> meshletVertices,
            NativeArray<byte> meshletTriangles,
            NativeArray<MeshoptBounds> meshletBounds,
            int vertexCount,
            int targetPartitionSize,
            out NativeArray<ClusterNode> outNodes,
            out NativeArray<MeshoptMeshlet> outMeshlets,
            out NativeArray<uint> outMeshletVertices,
            out NativeArray<byte> outMeshletTriangles,
            out NativeArray<MeshoptBounds> outMeshletBounds,
            int minRootCount = 32,
            float minRelativeReduction = 0.2f,
            int minAbsoluteReduction = 32,
            int activeAbsoluteCheckThreshold = 128
            )
        {
            var meshletCount = meshlets.Length;
            
            // ルート層のノード数が minRootCount より少ない場合は、木構造を作らずに返す
            if (meshletCount < minRootCount)
            {
                Debug.LogWarning("Meshlet count is less than minRootCount; hierarchy skipped.");
                outNodes = new NativeArray<ClusterNode>(0, Allocator.Persistent);
                outMeshlets = meshlets;
                outMeshletVertices = meshletVertices;
                outMeshletTriangles = meshletTriangles;
                outMeshletBounds = meshletBounds;
                return new List<HierarchyInfo>();
            }
            
            //──────────────────────────────────────────────────────
            // 0) Leaf (= meshlet) を Cluster に変換
            //──────────────────────────────────────────────────────
            var leafLevel = new List<Cluster>(meshletCount);

            for (int m = 0; m < meshletCount; m++)
            {
                var meshlet = meshlets[m];
                var triangleCount = (int)meshlet.TriangleCount;
                var idxArr = new NativeArray<int>(triangleCount * 3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                
                var triangleOffset = (int)meshlet.TriangleOffset;
                var vertexOffset = (int)meshlet.VertexOffset;
                var idx = 0;
                for (int t = 0; t < triangleCount; t++)
                {
                    var v0 = meshletVertices[vertexOffset + meshletTriangles[triangleOffset + t * 3]];
                    var v1 = meshletVertices[vertexOffset + meshletTriangles[triangleOffset + t * 3 + 1]];
                    var v2 = meshletVertices[vertexOffset + meshletTriangles[triangleOffset + t * 3 + 2]];
                    idxArr[idx++] = (int)v0;
                    idxArr[idx++] = (int)v1;
                    idxArr[idx++] = (int)v2;
                }

                var bound = meshletBounds[m];
                
                var cluster = new Cluster
                {
                    Indices = idxArr,
                    Center =  bound.Center,
                    Radius = bound.Radius,
                    MeshletID = m,
                    Children = null
                };
                leafLevel.Add(cluster);
            }

            var levels = new List<List<Cluster>>{ leafLevel };
            var currLevel = leafLevel;

            //──────────────────────────────────────────────────────
            // 1) PartitionClusters を再帰使用して親クラスタを作る
            //──────────────────────────────────────────────────────
            while (currLevel.Count > minRootCount && levels.Count < MaxDepth)
            {
                var clusterCount = currLevel.Count;
                
                // 1-A) Flatten to index array
                var clusterIdxCounts = new NativeArray<uint>(clusterCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var totalIdxCount = 0;
                for (int i = 0; i < clusterCount; i++)
                {
                    var cluster = currLevel[i];
                    var idxCount = cluster.Indices.Length;
                    totalIdxCount += idxCount;
                    clusterIdxCounts[i] = (uint)idxCount;
                }
                
                var clusterIndices = new NativeArray<int>(totalIdxCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int dst = 0;
                for (int i = 0; i < clusterCount; i++)
                {
                    var srcIdx = currLevel[i].Indices;
                    var length = srcIdx.Length;
                    NativeArray<int>.Copy(srcIdx, 0, clusterIndices, dst, length);
                    dst += length;
                }
                
                var partitionIndices = new NativeArray<uint>(clusterCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                var partitionCount = Meshoptimizer.PartitionClusters(
                    partitionIndices,
                    clusterIndices,
                    totalIdxCount,
                    clusterIdxCounts,
                    clusterCount,
                    vertexCount,
                    targetPartitionSize
                );
                
                clusterIdxCounts.Dispose();
                clusterIndices.Dispose();
    
                // =================================================
                // 1-B) 削減率を計算して打切り判定
                // =================================================
                var actualAbsoluteReduction = currLevel.Count - partitionCount;
                var actualRelativeReduction = (float)actualAbsoluteReduction / currLevel.Count;
                var stopCheck = false;
                if (currLevel.Count > activeAbsoluteCheckThreshold)
                {
                    if(actualRelativeReduction < minRelativeReduction)
                        stopCheck = true;
                }
                else
                {
                    if(actualAbsoluteReduction < minAbsoluteReduction)
                        stopCheck = true;
                }
                if (stopCheck)
                {
                    Debug.Log($"Stop partitioning: {currLevel.Count} -> {partitionCount}, " +
                              $"actualRelativeReduction: {actualRelativeReduction}, " +
                              $"actualAbsoluteReduction: {actualAbsoluteReduction}");
                    partitionIndices.Dispose();
                    break;
                }
                
                // 1-B) dest ID ごとに子クラスタをまとめる
                var bucketMap = new Dictionary<uint, List<Cluster>>();
                for (int i = 0; i < clusterCount; i++)
                {
                    uint pid = partitionIndices[i];
                    if(!bucketMap.TryGetValue(pid, out var lst))
                        bucketMap[pid] = lst = new List<Cluster>();
                    lst.Add(currLevel[i]);
                }
                
                var parentLevel = new List<Cluster>(bucketMap.Count);

                foreach (var kv in bucketMap)
                {
                    List<Cluster> children = kv.Value;
                    
                    // 子 index 連結
                    var parentIndexCount = children.Sum(c => c.Indices.Length);
                    var parentIndices = new NativeArray<int>(parentIndexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    dst = 0;
                    foreach (var child in children)
                    {
                        var srcIdx = child.Indices;
                        var length = srcIdx.Length;
                        NativeArray<int>.Copy(srcIdx, 0, parentIndices, dst, length);
                        srcIdx.Dispose(); // 子のインデックスを解放
                        dst += length;
                    }
                    
                    // --- 親境界球 --
                    var childrenCount = children.Count;
                    var childBoundCenters = new NativeArray<Vector3>(childrenCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    var childBoundRadii = new NativeArray<float>(childrenCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < childrenCount; i++)
                    {
                        var child = children[i];
                        childBoundCenters[i] = child.Center;
                        childBoundRadii[i] = child.Radius;
                    }

                    var parentBound = Meshoptimizer.ComputeSphereBounds(
                        childBoundCenters,
                        childrenCount,
                        Marshal.SizeOf<Vector3>(),
                        childBoundRadii,
                        sizeof(float)
                    );
                    
                    var parent = new Cluster
                    {
                        Indices = parentIndices,
                        Center = parentBound.Center,
                        Radius = parentBound.Radius,
                        MeshletID = -1,
                        Children = children
                    };
                    parentLevel.Add(parent);

                    childBoundCenters.Dispose();
                    childBoundRadii.Dispose();
                }
                
                levels.Add(parentLevel);
                currLevel = parentLevel;
                
                partitionIndices.Dispose();
            }
            
            // rootのリソース解放
            foreach (var cluster in currLevel)
            {
                if(cluster.Indices.IsCreated)
                    cluster.Indices.Dispose();
            }
            
            if (levels.Count < 2)
            {
                Debug.Log("No hierarchy created; returning original meshlets.");
                outNodes = new NativeArray<ClusterNode>(0, Allocator.Persistent);
                outMeshlets = meshlets;
                outMeshletVertices = meshletVertices;
                outMeshletTriangles = meshletTriangles;
                outMeshletBounds = meshletBounds;
                return new List<HierarchyInfo>();
            }
            

            // =================================================================
            // 2) 幅優先インデックス (root〜leafCluster) を付与
            // =================================================================
            var roots = levels[^1];
            var bfsQueue = new Queue<Cluster>(roots);
            uint running = 0;
            while (bfsQueue.Count > 0)
            {
                var node = bfsQueue.Dequeue();
                node.GlobalIndex = running++;
                if(node.MeshletID == -1)
                    foreach (var child in node.Children)
                        bfsQueue.Enqueue(child);
            }
            
            //──────────────────────────────────────────────────────
            // 3) leafCluster 順に meshlet を並べ替え
            //──────────────────────────────────────────────────────
            var level1 = levels[1];
            var newOrder = new NativeArray<int>(meshletCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var mid = 0;
            foreach (var lc in level1)
            {
                foreach (var child in lc.Children!)
                {
                    newOrder[mid++] = child.MeshletID;
                }
            }
            
            var newMeshlets = new NativeArray<MeshoptMeshlet>(meshletCount, Allocator.Persistent);
            var newMeshletBounds = new NativeArray<MeshoptBounds>(meshletCount, Allocator.Persistent);
            var lastMeshletOld = meshlets[meshletCount - 1];
            var newMeshletVertices = new NativeArray<uint>((int)(lastMeshletOld.VertexOffset + lastMeshletOld.VertexCount), Allocator.Persistent);
            var newMeshletTriangles = new NativeArray<byte>((int)(lastMeshletOld.TriangleOffset + ((lastMeshletOld.TriangleCount * 3 + 3) & ~3)), Allocator.Persistent);


            var vOffset = 0;
            var tOffset = 0;
            for (int newID = 0; newID < meshletCount; ++newID)
            {
                int oldID = newOrder[newID];
                var oldMeshlet = meshlets[oldID];

                var vStart = vOffset;
                for(int i = 0; i < oldMeshlet.VertexCount; ++i)
                    newMeshletVertices[vOffset++] = meshletVertices[(int)oldMeshlet.VertexOffset + i];
                
                int  tStart    = tOffset;
                for (int i = 0; i < oldMeshlet.TriangleCount * 3; ++i)
                    newMeshletTriangles[tOffset++] = meshletTriangles[(int)oldMeshlet.TriangleOffset + i];

                newMeshlets[newID] = new MeshoptMeshlet
                {
                    VertexOffset = (uint)vStart,
                    TriangleOffset = (uint)tStart,
                    VertexCount = oldMeshlet.VertexCount,
                    TriangleCount = oldMeshlet.TriangleCount
                };
                newMeshletBounds[newID] = meshletBounds[oldID];
            }
            
            var idRemap = new Dictionary<int, int>(meshletCount);
            for(int newID = 0; newID < meshletCount; ++newID)
            {
                int oldID = newOrder[newID];
                idRemap[oldID] = newID;
            }
            newOrder.Dispose();

            foreach (var lc in level1)
                foreach (var ch in lc.Children!)
                    ch.MeshletID = idRemap[ch.MeshletID];
            
            //──────────────────────────────────────────────────────
            // 4) ClusterNode 配列書き出し (root〜level2 + leafCluster)
            //──────────────────────────────────────────────────────
            var nodeArr = new NativeArray<ClusterNode>((int)running, Allocator.Persistent);

            // 4-A) internal ノード (depth ≥2)
            for (int d = levels.Count - 1; d >= 2; --d)
            {
                foreach (var n in levels[d])
                {
                    var firstChild = n.Children![0].GlobalIndex;
                    nodeArr[(int)n.GlobalIndex] = new ClusterNode
                    {
                        Center = n.Center,
                        Radius = n.Radius,
                        FirstIndex = firstChild,
                        Count = (uint)n.Children.Count
                    };
                }
            }

            // 4-B) leafCluster ノード
            foreach (var lc in level1)
            {
                // uint meshStart = (uint)idRemap[ lc.Children![0].MeshletID ];
                uint meshStart = (uint)lc.Children![0].MeshletID;
                if (meshStart == 8866)
                {
                    Debug.Log($"GlobalIndex: {lc.GlobalIndex} Children: {lc.Children.Count} MeshletID: {lc.Children[0].MeshletID} MeshStart: {meshStart}");
                }
                nodeArr[(int)lc.GlobalIndex] = new ClusterNode
                {
                    Center = lc.Center,
                    Radius = lc.Radius,
                    FirstIndex = meshStart,
                    Count = (uint)lc.Children.Count
                };
            }
            
            var hierarchyInfo = new List<HierarchyInfo>();
            for (int i = levels.Count - 1; i >= 1; --i)
            {
                hierarchyInfo.Add(
                    new HierarchyInfo {
                        FirstIndex = (int)levels[i][0].GlobalIndex,
                        Count = levels[i].Count
                    });
            }

            //──────────────────────────────────────────────────────
            // 5) 出力
            //──────────────────────────────────────────────────────
            outNodes = nodeArr;
            outMeshlets = newMeshlets;
            outMeshletVertices = newMeshletVertices;
            outMeshletTriangles = newMeshletTriangles;
            outMeshletBounds = newMeshletBounds;
            
            /*var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Cluster Hierarchy: {levels.Count} levels");
            for (int i = levels.Count - 1; i >= 0; i--)
            {
                var levelNodes = levels[i].Count;
                var totalChildren = levels[i].Sum(c => c.Children?.Count ?? 0);
                var averageChildren = levelNodes > 0 ? (float)totalChildren / levelNodes : 0;
                sb.AppendLine($"L{i}: {levelNodes}, AvgChildren: {averageChildren}");
            }
            Debug.Log(sb.ToString());*/

            return hierarchyInfo;
        }
        
        public static List<HierarchyInfo> Build(
            ref NativeArray<MeshoptMeshlet> meshlets,
            ref NativeArray<uint> meshletVertices,
            ref NativeArray<byte> meshletTriangles,
            ref NativeArray<MeshoptBounds> meshletBounds,
            int vertexCount,
            int targetPartitionSize,
            out NativeArray<ClusterNode> outNodes,
            int minRootCount = 32,
            float minRelativeReduction = 0.2f,
            int minAbsoluteReduction = 32,
            int activeAbsoluteCheckThreshold = 128
        )
        {
            var result = Build(
                meshlets,
                meshletVertices,
                meshletTriangles,
                meshletBounds,
                vertexCount,
                targetPartitionSize,
                out outNodes,
                out var newMeshlets,
                out var newMeshletVertices,
                out var newMeshletTriangles,
                out var newMeshletBounds,
                minRootCount,
                minRelativeReduction,
                minAbsoluteReduction,
                activeAbsoluteCheckThreshold
            );

            // 古い配列を解放
            if (result.Count > 0)
            {
                if (meshlets.IsCreated) meshlets.Dispose();
                if (meshletVertices.IsCreated) meshletVertices.Dispose();
                if (meshletTriangles.IsCreated) meshletTriangles.Dispose();
                if (meshletBounds.IsCreated) meshletBounds.Dispose();
            }

            // 参照を新しい配列に更新
            meshlets = newMeshlets;
            meshletVertices = newMeshletVertices;
            meshletTriangles = newMeshletTriangles;
            meshletBounds = newMeshletBounds;

            return result;
        }
        
        public static void GenerateClusterMap(NativeArray<ClusterNode> nodes, int firstIndex, int clusterCount, int targetDepth, int maxDepth, NativeArray<uint> clusterMap)
        {
            if (targetDepth > maxDepth)
            {
                Debug.LogError("Target depth exceeded max depth");
                return;
            }
            
            for (var i = 0; i < clusterCount; ++i)
            {
                RecursiveClusterMap(
                    (int)firstIndex + i,
                    (uint)i,
                    targetDepth,
                    maxDepth,
                    nodes,
                    clusterMap
                    );
            }
        }

        private static void RecursiveClusterMap(
            int nodeIdx,
            uint clusterIdx,
            int curDepth,
            int maxDepth,
            NativeArray<ClusterNode> nodes,
            NativeArray<uint> clusterMap
        )
        {
            var n = nodes[nodeIdx];
            if (curDepth == maxDepth)
            {
                int firstIndex = (int)n.FirstIndex;
                int count = (int)n.Count;
                for (int i = 0; i < count; ++i)
                { 
                    clusterMap[firstIndex + i] = clusterIdx;
                }
                return;
            }
            
            for(int i = 0; i < n.Count; ++i)
            {
                RecursiveClusterMap((int)n.FirstIndex + i, clusterIdx, curDepth + 1, maxDepth, nodes, clusterMap);
            }

        }
        
    }
}