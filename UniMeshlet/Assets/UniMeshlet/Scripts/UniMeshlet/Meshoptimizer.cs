using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UniMeshlet
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MeshoptStream
    {
        public void* Data;
        public nuint Size;
        public nuint Stride;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct MeshoptMeshlet
    {
        public uint VertexOffset;
        public uint TriangleOffset;
        public uint VertexCount;
        public uint TriangleCount;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public unsafe struct MeshoptBounds
    {
        public Vector3 Center;      // 12B
        public float Radius;               // 16B

        public Vector3 ConeApex;    // 28B
        public Vector3 ConeAxis;    // 40B
        public float ConeCutoff;           // 44B

        public fixed sbyte ConeAxis_s8[3]; // 47B
        public sbyte ConeCutoff_s8;        // 48B
    }

    internal static class Meshoptimizer
    {
        #if UNITY_IOS && !UNITY_EDITOR_OSX
        private const string MeshoptDLLName = "__Internal";
        #else
        private const string MeshoptDLLName = "meshopt_unity";
        #endif

        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe nuint Meshopt_GenerateVertexRemapMulti_Int(
            [Out] uint* destinations,
            [In] int* indices,
            nuint indexCount,
            nuint vertexCount,
            [In] MeshoptStream* streams,
            nuint streamCount
        );
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe nuint Meshopt_GenerateVertexRemapMulti_Ushort(
            [Out] uint* destinations,
            [In] ushort* indices,
            nuint indexCount,
            nuint vertexCount,
            [In] MeshoptStream* streams,
            nuint streamCount
        );
        
        public static unsafe int GenerateVertexRemapMulti(
            NativeArray<uint> destinations,
            NativeArray<int> indices,
            int indexCount,
            int vertexCount,
            NativeArray<MeshoptStream> streams
        )
        {
            return (int)Meshopt_GenerateVertexRemapMulti_Int(
                (uint*)destinations.GetUnsafePtr(),
                (int*)indices.GetUnsafeReadOnlyPtr(),
                (nuint)indexCount,
                (nuint)vertexCount,
                (MeshoptStream*)streams.GetUnsafeReadOnlyPtr(),
                (nuint)streams.Length
            );
        }
        
        public static unsafe int GenerateVertexRemapMulti(
            NativeArray<uint> destinations,
            NativeArray<ushort> indices,
            int indexCount,
            int vertexCount,
            NativeArray<MeshoptStream> streams
        )
        {
            return (int)Meshopt_GenerateVertexRemapMulti_Ushort(
                (uint*)destinations.GetUnsafePtr(),
                (ushort*)indices.GetUnsafeReadOnlyPtr(),
                (nuint)indexCount,
                (nuint)vertexCount,
                (MeshoptStream*)streams.GetUnsafeReadOnlyPtr(),
                (nuint)streams.Length
            );
        }
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void Meshopt_RemapVertexBuffer(
            [Out] void* destination,
            [In] void* vertices,
            nuint vertexCount,
            nuint vertexSize,
            [In] uint* remap
        );
        
        public static unsafe void RemapVertexBuffer<T>(
            NativeArray<T> destination,
            NativeArray<T> vertices,
            int vertexCount,
            int vertexSize,
            NativeArray<uint> remap
        ) where T : unmanaged
        {
            Meshopt_RemapVertexBuffer(
                destination.GetUnsafePtr(),
                vertices.GetUnsafeReadOnlyPtr(),
                (nuint)vertexCount,
                (nuint)vertexSize,
                (uint*)remap.GetUnsafeReadOnlyPtr()
            );
        }
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void Meshopt_RemapIndexBuffer_Int(
            [Out] int* destination,
            [In] int* indices,
            nuint indexCount,
            [In] uint* remap
        );
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void Meshopt_RemapIndexBuffer_Ushort(
            [Out] ushort* destination,
            [In] ushort* indices,
            nuint indexCount,
            [In] uint* remap
        );
        
        public static unsafe void RemapIndexBuffer(
            NativeArray<int> destination,
            NativeArray<int> indices,
            int indexCount,
            NativeArray<uint> remap
        )
        {
            Meshopt_RemapIndexBuffer_Int(
                (int*)destination.GetUnsafePtr(),
                (int*)indices.GetUnsafeReadOnlyPtr(),
                (nuint)indexCount,
                (uint*)remap.GetUnsafeReadOnlyPtr()
            );
        }
        
        public static unsafe void RemapIndexBuffer(
            NativeArray<ushort> destination,
            NativeArray<ushort> indices,
            int indexCount,
            NativeArray<uint> remap
        )
        {
            Meshopt_RemapIndexBuffer_Ushort(
                (ushort*)destination.GetUnsafePtr(),
                (ushort*)indices.GetUnsafeReadOnlyPtr(),
                (nuint)indexCount,
                (uint*)remap.GetUnsafeReadOnlyPtr()
            );
        }
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern nuint Meshopt_BuildMeshletsBound(
            nuint indexCount,
            nuint maxVertices,
            nuint maxTriangles
            );

        public static int BuildMeshletsBound(
            int indexCount,
            int maxVertices,
            int maxTriangles
        )
        {
            return (int)Meshopt_BuildMeshletsBound((nuint)indexCount, (nuint)maxVertices, (nuint)maxTriangles);
        }
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe nuint Meshopt_BuildMeshlets_Int(
            [Out] MeshoptMeshlet* meshlets,
            [Out] uint* meshletVertices,
            [Out] byte* meshletTriangles,
            [In] int* indices,
            nuint indexCount,
            [In] float* vertexPositions,
            nuint vertexCount,
            nuint vertexPositionsStride,
            nuint maxVertices,
            nuint maxTriangles,
            float coneWeight
        );
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe nuint Meshopt_BuildMeshlets_Ushort(
            [Out] MeshoptMeshlet* meshlets,
            [Out] uint* meshletVertices,
            [Out] byte* meshletTriangles,
            [In] ushort* indices,
            nuint indexCount,
            [In] float* vertexPositions,
            nuint vertexCount,
            nuint vertexPositionsStride,
            nuint maxVertices,
            nuint maxTriangles,
            float coneWeight
        );

        public static unsafe int BuildMeshlets<T>(
            NativeArray<MeshoptMeshlet> meshlets,
            NativeArray<uint> meshletVertices,
            NativeArray<byte> meshletTriangles,
            NativeArray<int> indices,
            int indexCount,
            NativeArray<T> vertexPositions,
            int vertexCount,
            int vertexPositionsStride,
            int maxVertices,
            int maxTriangles,
            float coneWeight
        ) where T : unmanaged
        {
            return (int)Meshopt_BuildMeshlets_Int(
                (MeshoptMeshlet*)meshlets.GetUnsafePtr(),
                (uint*)meshletVertices.GetUnsafePtr(),
                (byte*)meshletTriangles.GetUnsafePtr(),
                (int*)indices.GetUnsafeReadOnlyPtr(),
                (nuint)indexCount,
                (float*)vertexPositions.GetUnsafeReadOnlyPtr(),
                (nuint)vertexCount,
                (nuint)vertexPositionsStride,
                (nuint)maxVertices,
                (nuint)maxTriangles,
                coneWeight
            );
        }
        
        public static unsafe int BuildMeshlets<T>(
            NativeArray<MeshoptMeshlet> meshlets,
            NativeArray<uint> meshletVertices,
            NativeArray<byte> meshletTriangles,
            NativeArray<ushort> indices,
            int indexCount,
            NativeArray<T> vertexPositions,
            int vertexCount,
            int vertexPositionsStride,
            int maxVertices,
            int maxTriangles,
            float coneWeight
        ) where T : unmanaged
        {
            return (int)Meshopt_BuildMeshlets_Ushort(
                (MeshoptMeshlet*)meshlets.GetUnsafePtr(),
                (uint*)meshletVertices.GetUnsafePtr(),
                (byte*)meshletTriangles.GetUnsafePtr(),
                (ushort*)indices.GetUnsafeReadOnlyPtr(),
                (nuint)indexCount,
                (float*)vertexPositions.GetUnsafeReadOnlyPtr(),
                (nuint)vertexCount,
                (nuint)vertexPositionsStride,
                (nuint)maxVertices,
                (nuint)maxTriangles,
                coneWeight
            );
        }

        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe MeshoptBounds Meshopt_ComputeClusterBounds_Int(
            [In] int* indices,
            nuint indexCount,
            [In] float* vertexPositions,
            nuint vertexCount,
            nuint vertexPositionsStride
        );
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe MeshoptBounds Meshopt_ComputeClusterBounds_Ushort(
            [In] ushort* indices,
            nuint indexCount,
            [In] float* vertexPositions,
            nuint vertexCount,
            nuint vertexPositionsStride
        );
        
        public static unsafe MeshoptBounds ComputeClusterBounds(
            NativeArray<int> indices,
            int indexCount,
            NativeArray<Vector3> vertexPositions,
            int vertexCount,
            int vertexPositionsStride
        )
        {
            return Meshopt_ComputeClusterBounds_Int(
                (int*)indices.GetUnsafeReadOnlyPtr() ,
                (nuint)indexCount,
                (float*)vertexPositions.GetUnsafeReadOnlyPtr(),
                (nuint)vertexCount,
                (nuint)vertexPositionsStride
            );
        }
        
        public static unsafe MeshoptBounds ComputeClusterBounds(
            NativeArray<ushort> indices,
            int indexCount,
            NativeArray<Vector3> vertexPositions,
            int vertexCount,
            int vertexPositionsStride
        )
        {
            return Meshopt_ComputeClusterBounds_Ushort(
                (ushort*)indices.GetUnsafeReadOnlyPtr(),
                (nuint)indexCount,
                (float*)vertexPositions.GetUnsafeReadOnlyPtr(),
                (nuint)vertexCount,
                (nuint)vertexPositionsStride
            );
        }
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe MeshoptBounds Meshopt_ComputeMeshletBounds(
            [In] uint* meshletVertices,
            [In] byte* meshletTriangles,
            nuint triangleCount,
            [In] float* vertexPositions,
            nuint vertexCount,
            nuint vertexPositionsStride
        );
        
        public static unsafe MeshoptBounds ComputeMeshletBounds(
            NativeArray<uint> meshletVertices,
            int vertexOffset,
            NativeArray<byte> meshletTriangles,
            int triangleOffset,
            int triangleCount,
            NativeArray<Vector3> vertexPositions,
            int vertexCount,
            int vertexPositionsStride
        )
        {
            uint* meshletVerticesPtr = (uint*)meshletVertices.GetUnsafeReadOnlyPtr() + vertexOffset;
            byte* meshletTrianglesPtr = (byte*)meshletTriangles.GetUnsafeReadOnlyPtr() + triangleOffset * sizeof(byte);
            float* vertexPositionsPtr = (float*)vertexPositions.GetUnsafeReadOnlyPtr();
            
            return Meshopt_ComputeMeshletBounds(
                meshletVerticesPtr,
                meshletTrianglesPtr,
                (nuint)triangleCount,
                vertexPositionsPtr,
                (nuint)vertexCount,
                (nuint)vertexPositionsStride
            );
        }

        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe MeshoptBounds Meshopt_ComputeSphereBounds(
            [In] float* positions,
            nuint count,
            nuint positionsStride,
            [In] float* radii,
            nuint radiiStride
        );
        
        public static unsafe MeshoptBounds ComputeSphereBounds(
            NativeArray<Vector3> positions,
            int count,
            int positionsStride,
            NativeArray<float> radii,
            int radiiStride
        )
        {
            return Meshopt_ComputeSphereBounds(
                (float*)positions.GetUnsafeReadOnlyPtr(),
                (nuint)count,
                (nuint)positionsStride,
                (float*)radii.GetUnsafeReadOnlyPtr(),
                (nuint)radiiStride
            );
        }

        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe nuint Meshopt_PartitionClusters_Int(
            [Out] uint* destinations,
            [In] int* clusterIndices,
            nuint totalIndexCount,
            [In] uint* clusterIndexCounts,
            nuint clusterCount,
            nuint vertexCount,
            nuint targetPartitionSize
        );
        
        [DllImport(MeshoptDLLName, CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe nuint Meshopt_PartitionClusters_Ushort(
            [Out] uint* destinations,
            [In] ushort* clusterIndices,
            nuint totalIndexCount,
            [In] uint* clusterIndexCounts,
            nuint clusterCount,
            nuint vertexCount,
            nuint targetPartitionSize
        );

        public static unsafe int PartitionClusters(
            NativeArray<uint> destinations,
            NativeArray<int> clusterIndices,
            int totalIndexCount,
            NativeArray<uint> clusterIndexCounts,
            int clusterCount,
            int vertexCount,
            int targetPartitionSize
        )
        {
            return (int)Meshopt_PartitionClusters_Int(
                (uint*)destinations.GetUnsafePtr(),
                (int*)clusterIndices.GetUnsafeReadOnlyPtr(),
                (nuint)totalIndexCount,
                (uint*)clusterIndexCounts.GetUnsafeReadOnlyPtr(),
                (nuint)clusterCount,
                (nuint)vertexCount,
                (nuint)targetPartitionSize
            );
        }
        
        public static unsafe int PartitionClusters(
            NativeArray<uint> destinations,
            NativeArray<ushort> clusterIndices,
            int totalIndexCount,
            NativeArray<uint> clusterIndexCounts,
            int clusterCount,
            int vertexCount,
            int targetPartitionSize
        )
        {
            return (int)Meshopt_PartitionClusters_Ushort(
                (uint*)destinations.GetUnsafePtr(),
                (ushort*)clusterIndices.GetUnsafeReadOnlyPtr(),
                (nuint)totalIndexCount,
                (uint*)clusterIndexCounts.GetUnsafeReadOnlyPtr(),
                (nuint)clusterCount,
                (nuint)vertexCount,
                (nuint)targetPartitionSize
            );
        }
    }
}
