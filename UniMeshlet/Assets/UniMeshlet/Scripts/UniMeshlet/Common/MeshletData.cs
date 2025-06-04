using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniMeshlet.Common
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector2 UV;
    }
    
    public enum MeshletIndexTableFormat : byte { U8 = 1, U16 = 2, U32 = 4 }

    public struct MeshletData : IDisposable
    {
        public NativeArray<MeshoptMeshlet> Meshlets;
        public NativeArray<uint> MeshletVertices;
        public NativeArray<byte> MeshletTriangles;
        public NativeArray<MeshoptBounds> MeshletBounds;
        public NativeArray<ClusterNode> ClusterNodes; // May not be created if no hierarchy
        public List<HierarchyInfo> HierarchyInfo;
        public NativeArray<uint> MeshletIndexTable;
        public MeshletIndexTableFormat TableFormat;
        public Mesh BakedMesh;
            
        public bool HasHierarchy => HierarchyInfo is { Count: > 0 } &&
                                    ClusterNodes is { IsCreated: true, Length: > 0 };

        public bool IsValid => BakedMesh != null &&
                               Meshlets.IsCreated &&
                               MeshletVertices.IsCreated &&
                               MeshletTriangles.IsCreated &&
                               MeshletBounds.IsCreated;
            
        public void Dispose()
        {
            if (Meshlets.IsCreated) Meshlets.Dispose();
            if (MeshletVertices.IsCreated) MeshletVertices.Dispose();
            if (MeshletTriangles.IsCreated) MeshletTriangles.Dispose();
            if (MeshletBounds.IsCreated) MeshletBounds.Dispose();
            if (ClusterNodes.IsCreated) ClusterNodes.Dispose();
            if (MeshletIndexTable.IsCreated) MeshletIndexTable.Dispose();
            if (BakedMesh != null) Object.DestroyImmediate(BakedMesh); // Or Destroy if not in Editor context
            HierarchyInfo?.Clear();
            HierarchyInfo = null;
        }
    }
    
    public enum MeshletDataBlockId : uint
    {
        BakedVertices = 1,
        BakedNormals = 2,
        BakedTangents = 3,
        BakedUVs = 4,
        BakedIndices = 5,
        // --- Baked Mesh Meta (Not a block, but info needed for BakedIndices interpretation)
        // BakedMeshIndexFormat (これはMetaData1に入る)

        Meshlets = 10,
        MeshletVertices = 11,
        MeshletTriangles = 12, // byte[]
        MeshletIndexTable = 13, // uint[]
        // --- Meshlet Index Table Meta
        // MeshletTableFormat (MetaData1に入る)

        MeshletBounds = 14,

        ClusterNodes = 20,
        HierarchyInfo = 21
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SerializableDataBlockInfo
    {
        public uint BlockId;
        public ulong BlockOffset;
        public ulong BlockSize;
        public uint ElementCount;
        public uint ElementStride;
        public uint MetaData1;
        public uint MetaData2;
    }
}