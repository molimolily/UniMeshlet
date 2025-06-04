using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UniMeshlet.Common
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
    
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ClusterNode
    {
        public Vector3 Center;
        public float Radius;
        public uint FirstIndex;
        public uint Count;
    }
    
    [Serializable]
    public struct HierarchyInfo
    {
        public int FirstIndex;
        public int Count;
    }
}