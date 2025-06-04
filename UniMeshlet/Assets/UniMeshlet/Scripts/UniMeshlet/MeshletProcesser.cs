using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet
{
    public static class MeshletProcessor
    {
        public struct Configuration
        {
            public int MaxVerticesPerMeshlet;
            public int MaxTrianglesPerMeshlet;
            public float ConeWeight;
            public int PartitionSizeForHierarchy;
            public int MinRootSizeForHierarchy;
            public IndexFormat PreferredBakedMeshIndexFormat; // Default to UInt16
        }

        public static MeshletData ProcessMeshletData(Mesh sourceMesh, Configuration config, string meshName = "BakedMeshletRenderData",
            System.Action<string, float> progressCallback = null)
        {
            var outputData = new MeshletData();
            
            progressCallback?.Invoke("Acquiring and Optimizing Mesh Data...", 0.1f);
            
            using var srcMeshDataArray = Mesh.AcquireReadOnlyMeshData(sourceMesh);
            var srcMeshData = srcMeshDataArray[0];
            var subMeshDesc = srcMeshData.GetSubMesh(0);
            int sourceIndexCount = subMeshDesc.indexCount;
            int sourceVertexCount = srcMeshData.vertexCount;
            
            using var tmpIndicesInt = new NativeArray<int>(sourceIndexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            ReadIndices(srcMeshData, 0, tmpIndicesInt);
            
            using var tmpPositions = new NativeArray<Vector3>(sourceVertexCount, Allocator.Temp);
            if(srcMeshData.HasVertexAttribute(VertexAttribute.Position))
                srcMeshData.GetVertices(tmpPositions);
            else
                Debug.LogWarning("Mesh does not have vertex positions. Initializing positions to zero.");
            
            using var tmpNormals = new NativeArray<Vector3>(sourceVertexCount, Allocator.Temp);
            if(srcMeshData.HasVertexAttribute(VertexAttribute.Normal))
                srcMeshData.GetNormals(tmpNormals);
            else
                Debug.LogWarning("Mesh does not have vertex normals. Initializing normals to zero.");
            
            using var tmpTangents = new NativeArray<Vector4>(sourceVertexCount, Allocator.Temp);
            if(srcMeshData.HasVertexAttribute(VertexAttribute.Tangent))
                srcMeshData.GetTangents(tmpTangents);
            else
                Debug.LogWarning("Mesh does not have vertex tangents. Initializing tangents to zero.");
            
            using var tmpUVs = new NativeArray<Vector2>(sourceVertexCount, Allocator.Temp);
            if(srcMeshData.HasVertexAttribute(VertexAttribute.TexCoord0))
                srcMeshData.GetUVs(0, tmpUVs);
            else
                Debug.LogWarning("Mesh does not have UVs. Initializing UVs to zero.");
            
            using var remapTable = new NativeArray<uint>(sourceVertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var streams = new NativeArray<MeshoptStream>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            unsafe
            {
                streams[0] = new MeshoptStream
                {
                    Data = tmpPositions.GetUnsafePtr(),
                    Size = (uint)Marshal.SizeOf<Vector3>(),
                    Stride = (uint)Marshal.SizeOf<Vector3>()
                };
                streams[1] = new MeshoptStream
                {
                    Data = tmpNormals.GetUnsafePtr(),
                    Size = (uint)Marshal.SizeOf<Vector3>(),
                    Stride = (uint)Marshal.SizeOf<Vector3>()
                };
                streams[2] = new MeshoptStream
                {
                    Data = tmpTangents.GetUnsafePtr(),
                    Size = (uint)Marshal.SizeOf<Vector4>(),
                    Stride = (uint)Marshal.SizeOf<Vector4>()
                };
                streams[3] = new MeshoptStream
                {
                    Data = tmpUVs.GetUnsafePtr(),
                    Size = (uint)Marshal.SizeOf<Vector2>(),
                    Stride = (uint)Marshal.SizeOf<Vector2>()
                };
            }
            var uniqueVertexCount = Meshoptimizer.GenerateVertexRemapMulti(remapTable, tmpIndicesInt, sourceIndexCount,
                sourceVertexCount, streams);
             
            using var finalIndices = new NativeArray<int>(sourceIndexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            Meshoptimizer.RemapIndexBuffer(finalIndices, tmpIndicesInt, sourceIndexCount, remapTable);
            
            using var finalPositions = new NativeArray<Vector3>(uniqueVertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            Meshoptimizer.RemapVertexBuffer(finalPositions, tmpPositions, sourceVertexCount, Marshal.SizeOf<Vector3>(), remapTable);
            using var finalNormals = new NativeArray<Vector3>(uniqueVertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            Meshoptimizer.RemapVertexBuffer(finalNormals, tmpNormals, sourceVertexCount, Marshal.SizeOf<Vector3>(), remapTable);
            using var finalTangents = new NativeArray<Vector4>(uniqueVertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            Meshoptimizer.RemapVertexBuffer(finalTangents, tmpTangents, sourceVertexCount, Marshal.SizeOf<Vector4>(), remapTable);
            using var finalUVs = new NativeArray<Vector2>(uniqueVertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            Meshoptimizer.RemapVertexBuffer(finalUVs, tmpUVs, sourceVertexCount, Marshal.SizeOf<Vector2>(), remapTable);
            
            progressCallback?.Invoke("Building Meshlets...", 0.4f);
            GenerateMeshletsInternal(out outputData.Meshlets, out outputData.MeshletVertices, out outputData.MeshletTriangles,
                finalIndices, finalPositions,
                config.MaxVerticesPerMeshlet, config.MaxTrianglesPerMeshlet, config.ConeWeight);
            
            if (!outputData.Meshlets.IsCreated || outputData.Meshlets.Length == 0)
            {
                Debug.LogError($"Meshlet generation failed for {sourceMesh.name}.");
                // outputData.Dispose() will be called by the caller in case of failure if needed
                return outputData; // Return invalid data
            }
            
            progressCallback?.Invoke("Calculating Meshlet Bounds...", 0.6f);
            GenerateMeshletBoundsInternal(out outputData.MeshletBounds, outputData.Meshlets, outputData.MeshletVertices, outputData.MeshletTriangles, finalPositions);

            progressCallback?.Invoke("Building Cluster Hierarchy...", 0.7f); ;

            outputData.HierarchyInfo = ClusterHierarchyBuilder.Build(
                ref outputData.Meshlets, ref outputData.MeshletVertices, ref outputData.MeshletTriangles, ref outputData.MeshletBounds,
                uniqueVertexCount, config.PartitionSizeForHierarchy,
                out outputData.ClusterNodes,
                config.MinRootSizeForHierarchy
            );
            
            progressCallback?.Invoke("Generating Meshlet Index Table...", 0.8f);
            GenerateMeshletIndexTableInternal(out outputData.MeshletIndexTable, out outputData.TableFormat,
                outputData.Meshlets, outputData.ClusterNodes, outputData.HierarchyInfo, -1);

            progressCallback?.Invoke("Creating Baked Mesh for Rendering...", 0.9f);
            if (uniqueVertexCount >= ushort.MaxValue)
            {
                config.PreferredBakedMeshIndexFormat = IndexFormat.UInt32; // Force to UInt32 if too many vertices
            }
            outputData.BakedMesh = CreateBakedMesh(uniqueVertexCount, finalPositions, finalNormals, finalTangents, finalUVs,
                outputData.Meshlets, outputData.MeshletVertices, outputData.MeshletTriangles,
                config.PreferredBakedMeshIndexFormat, meshName);
            
            return outputData;
        }
        
        // --- Helper methods (static, as they don't rely on instance state of MeshletProcessor) ---
        // These are identical to the ones previously in MeshletGeneratorEditor
        private static void ReadIndices(Mesh.MeshData meshData, int submeshIndex, NativeArray<int> outIndices)
        {
            if (meshData.indexFormat == IndexFormat.UInt16)
            {
                using var tempIndices16 = new NativeArray<ushort>(outIndices.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                meshData.GetIndices(tempIndices16, submeshIndex);
                for (int i = 0; i < outIndices.Length; ++i) outIndices[i] = tempIndices16[i];
            }
            else
            {
                meshData.GetIndices(outIndices, submeshIndex);
            }
        }
        
        private static void GenerateMeshletsInternal(out NativeArray<MeshoptMeshlet> dstMeshlets, out NativeArray<uint> dstMeshletVertices, out NativeArray<byte> dstMeshletTriangles, NativeArray<int> indices, NativeArray<Vector3> vertexPositions, int maxVertices, int maxTriangles, float coneWeight)
        {
            var maxMeshletCount = Meshoptimizer.BuildMeshletsBound(indices.Length, maxVertices, maxTriangles);
            var tempMeshlets = new NativeArray<MeshoptMeshlet>(maxMeshletCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var tempVertices = new NativeArray<uint>(maxMeshletCount * maxVertices, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var tempTriangles = new NativeArray<byte>(maxMeshletCount * maxTriangles * 3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var actualMeshletCount = Meshoptimizer.BuildMeshlets(
                tempMeshlets, tempVertices, tempTriangles,
                indices, indices.Length,
                vertexPositions, vertexPositions.Length, Marshal.SizeOf<Vector3>(),
                maxVertices, maxTriangles, coneWeight
            );

            if (actualMeshletCount > 0)
            {
                var lastMeshlet = tempMeshlets[actualMeshletCount - 1];
                dstMeshlets = new NativeArray<MeshoptMeshlet>(actualMeshletCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory); // Persistent as it's part of output
                dstMeshletVertices = new NativeArray<uint>((int)(lastMeshlet.VertexOffset + lastMeshlet.VertexCount), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                dstMeshletTriangles = new NativeArray<byte>((int)(lastMeshlet.TriangleOffset + ((lastMeshlet.TriangleCount * 3 + 3) & ~3)), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                NativeArray<MeshoptMeshlet>.Copy(tempMeshlets, dstMeshlets, actualMeshletCount);
                NativeArray<uint>.Copy(tempVertices, dstMeshletVertices, dstMeshletVertices.Length);
                NativeArray<byte>.Copy(tempTriangles, dstMeshletTriangles, dstMeshletTriangles.Length);
            }
            else
            {
                dstMeshlets = new NativeArray<MeshoptMeshlet>(0, Allocator.Persistent);
                dstMeshletVertices = new NativeArray<uint>(0, Allocator.Persistent);
                dstMeshletTriangles = new NativeArray<byte>(0, Allocator.Persistent);
            }
            tempMeshlets.Dispose();
            tempVertices.Dispose();
            tempTriangles.Dispose();
        }
        
        private static void GenerateMeshletBoundsInternal( out NativeArray<MeshoptBounds> dstBounds, NativeArray<MeshoptMeshlet> meshlets, NativeArray<uint> meshletVertices, NativeArray<byte> meshletTriangles, NativeArray<Vector3> vertexPositions)
        {
            dstBounds = new NativeArray<MeshoptBounds>(meshlets.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory); // Persistent
            for (int i = 0; i < meshlets.Length; ++i)
            {
                var m = meshlets[i];
                dstBounds[i] = Meshoptimizer.ComputeMeshletBounds(
                    meshletVertices, (int)m.VertexOffset,
                    meshletTriangles, (int)m.TriangleOffset, (int)m.TriangleCount,
                    vertexPositions, (int)m.VertexCount,
                    Marshal.SizeOf<Vector3>()
                );
            }
        }
        
        private static void GenerateMeshletIndexTableInternal(out NativeArray<uint> dstMeshletIndexTable, out MeshletIndexTableFormat tableFormat, NativeArray<MeshoptMeshlet> meshlets, NativeArray<ClusterNode> clusterNodes, List<HierarchyInfo> hierarchyInfo, int targetDepth)
        {
            int meshletCount = meshlets.Length;
            long totalTriangleCountLong = 0;
            for (int i = 0; i < meshletCount; ++i) totalTriangleCountLong += meshlets[i].TriangleCount;
            int totalTriangleCount = (int)totalTriangleCountLong;

            bool useHierarchyForTable = hierarchyInfo is { Count: > 0 } &&
                                        targetDepth >= 0 && targetDepth < hierarchyInfo.Count &&
                                        clusterNodes is { IsCreated: true, Length: > 0 };

            int countForFormat = useHierarchyForTable ? hierarchyInfo[targetDepth].Count : meshletCount;
            tableFormat = countForFormat <= byte.MaxValue ? MeshletIndexTableFormat.U8 :
                          countForFormat <= ushort.MaxValue ? MeshletIndexTableFormat.U16 : MeshletIndexTableFormat.U32;

            int strideBytes = (int)tableFormat;
            int tableSizeBytes = totalTriangleCount * strideBytes;
            int tableLengthUInts = (tableSizeBytes + sizeof(uint) - 1) / sizeof(uint);

            dstMeshletIndexTable = new NativeArray<uint>(tableLengthUInts, Allocator.Persistent, NativeArrayOptions.ClearMemory); // Persistent
            unsafe
            {
                byte* currentWritePtr = (byte*)dstMeshletIndexTable.GetUnsafePtr();
                if (useHierarchyForTable)
                {
                    using var clusterMap = new NativeArray<uint>(meshletCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    ClusterHierarchyBuilder.GenerateClusterMap(clusterNodes, hierarchyInfo[targetDepth].FirstIndex, hierarchyInfo[targetDepth].Count, targetDepth, hierarchyInfo.Count - 1, clusterMap);
                    for (int mId = 0; mId < meshletCount; ++mId)
                    {
                        uint idToWrite = clusterMap[mId];
                        for (int t = 0; t < meshlets[mId].TriangleCount; ++t)
                        {
                            if (strideBytes == 1) *(byte*)currentWritePtr = (byte)idToWrite;
                            else if (strideBytes == 2) *(ushort*)currentWritePtr = (ushort)idToWrite;
                            else *(uint*)currentWritePtr = idToWrite;
                            currentWritePtr += strideBytes;
                        }
                    }
                }
                else
                {
                    for (uint mId = 0; mId < meshletCount; ++mId)
                    {
                        for (int t = 0; t < meshlets[(int)mId].TriangleCount; ++t)
                        {
                            if (strideBytes == 1) *(byte*)currentWritePtr = (byte)mId;
                            else if (strideBytes == 2) *(ushort*)currentWritePtr = (ushort)mId;
                            else *(uint*)currentWritePtr = mId;
                            currentWritePtr += strideBytes;
                        }
                    }
                }
            }
        }
        
        private static Mesh CreateBakedMesh(
        int uniqueVertexCount,
        NativeArray<Vector3> positions, NativeArray<Vector3> normals,
        NativeArray<Vector4> tangents, NativeArray<Vector2> uvs,
        NativeArray<MeshoptMeshlet> meshlets,
        NativeArray<uint> meshletVertices, NativeArray<byte> meshletTriangles,
        IndexFormat indexFormat, string meshName = "BakedMeshletRenderData")
        {
            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var meshData = meshDataArray[0];

            var attributes = new NativeArray<VertexAttributeDescriptor>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
            attributes[2] = new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4);
            attributes[3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);
            meshData.SetVertexBufferParams(uniqueVertexCount, attributes);
            attributes.Dispose();

            var fullVertexData = meshData.GetVertexData<Vertex>(); // Assumes Meshlet.Vertex struct is available
            for (int i = 0; i < uniqueVertexCount; ++i)
            {
                fullVertexData[i] = new Vertex
                {
                    Position = positions[i],
                    Normal = normals.IsCreated && i < normals.Length ? normals[i] : Vector3.up,
                    Tangent = tangents.IsCreated && i < tangents.Length ? tangents[i] : new Vector4(1,0,0,1),
                    UV = uvs.IsCreated && i < uvs.Length ? uvs[i] : Vector2.zero
                };
            }

            long totalIndexCountLong = 0;
            for (int i = 0; i < meshlets.Length; ++i) totalIndexCountLong += meshlets[i].TriangleCount * 3;
            int totalIndexCount = (int)totalIndexCountLong;

            meshData.SetIndexBufferParams(totalIndexCount, indexFormat);
            if (indexFormat == IndexFormat.UInt16)
                WriteCombinedIndices(meshData.GetIndexData<ushort>(), meshlets, meshletVertices, meshletTriangles);
            else
                WriteCombinedIndices(meshData.GetIndexData<uint>(), meshlets, meshletVertices, meshletTriangles);

            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, totalIndexCount), MeshUpdateFlags.DontRecalculateBounds);

            Mesh newMesh = new Mesh { name = meshName };
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, newMesh);
            newMesh.RecalculateBounds();
            return newMesh;
        }

        private static void WriteCombinedIndices<T>(NativeArray<T> combinedIndices,
            NativeArray<MeshoptMeshlet> meshlets, NativeArray<uint> meshletVertices, NativeArray<byte> meshletTriangles)
            where T : unmanaged
        {
            int currentIndex = 0;
            foreach (var m in meshlets)
            {
                for (int tri = 0; tri < m.TriangleCount; ++tri)
                {
                    uint v0 = meshletVertices[
                        (int)(m.VertexOffset + meshletTriangles[(int)(m.TriangleOffset + tri * 3 + 0)])];
                    uint v1 = meshletVertices[
                        (int)(m.VertexOffset + meshletTriangles[(int)(m.TriangleOffset + tri * 3 + 1)])];
                    uint v2 = meshletVertices[
                        (int)(m.VertexOffset + meshletTriangles[(int)(m.TriangleOffset + tri * 3 + 2)])];
                    if (typeof(T) == typeof(ushort))
                    {
                        combinedIndices[currentIndex++] = (T)(object)(ushort)v0;
                        combinedIndices[currentIndex++] = (T)(object)(ushort)v1;
                        combinedIndices[currentIndex++] = (T)(object)(ushort)v2;
                    }
                    else // uint
                    {
                        combinedIndices[currentIndex++] = (T)(object)v0;
                        combinedIndices[currentIndex++] = (T)(object)v1;
                        combinedIndices[currentIndex++] = (T)(object)v2;
                    }
                }
            }
        }

    }
}