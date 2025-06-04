using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet
{
    public class MeshletDataInfo : ScriptableObject
    {
        [Header("Source File Information")]
        [Tooltip("The GUID of the original source asset (e.g., Mesh) from which this data was generated.")]
        public string sourceAssetGUID; // 一意の識別子として、元のアセットのGUIDを保持
    
        [Tooltip("The name of the .meshlet binary file located in StreamingAssets.")]
        public string sourceMeshletDataFilename; // StreamingAssets内のバイナリファイル名
        public int fileVersion;

        [Header("destination File Information")]
        public static readonly string StreamingAssetsTargetSubfolder = "__RuntimeMeshlets";
        public string runtimeMeshletDataFilename;

        [Header("Baked Mesh MetaData")]
        public int bakedVertexCount;
        public IndexFormat bakedMeshIndexFormat;
        public int bakedIndexCount;
        public bool bakedMeshHasNormals;
        public bool bakedMeshHasTangents;
        public bool bakedMeshHasUVs;


        [Header("Meshlet MetaData")]
        public int meshletCount;
        public int totalMeshletVerticesCount; // MeshletVertices配列の要素数
        public int totalMeshletTrianglesByteCount; // MeshletTriangles配列のバイト長
        public int meshletIndexTableUIntCount; // MeshletIndexTable配列のuint要素数
        public MeshletIndexTableFormat meshletTableFormat; // MeshletProcessor内のenumを参照
        public int meshletBoundsCount;

        [Header("Hierarchy MetaData")]
        public bool hasHierarchy;
        public int clusterNodeCount;
        public List<HierarchyInfo> hierarchyInfoList;
    }
}