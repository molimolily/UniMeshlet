using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet.Editor
{
    [ScriptedImporter(1, "meshlet")]
    public class MeshletDataImporter : ScriptedImporter
    {
        private const uint EXPECTED_MAGIC_NUMBER = 0x4D455348; // "MESH"
    
        public override void OnImportAsset(AssetImportContext ctx)
        {
        
            var runtimeInfo = ScriptableObject.CreateInstance<MeshletDataInfo>();
            runtimeInfo.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + "_Info";
            runtimeInfo.sourceMeshletDataFilename = Path.GetFileName(ctx.assetPath);
            runtimeInfo.sourceAssetGUID = AssetDatabase.AssetPathToGUID(ctx.assetPath);
            string baseFilename = Path.GetFileNameWithoutExtension(ctx.assetPath);
            string extension = Path.GetExtension(ctx.assetPath); // .meshlet
            // ビルドスクリプトが StreamingAssets にコピーする際のファイル名をここで決定
            if (!string.IsNullOrEmpty(runtimeInfo.sourceAssetGUID))
            {
                runtimeInfo.runtimeMeshletDataFilename = $"{baseFilename}_{runtimeInfo.sourceAssetGUID}{extension}";
            }
            else
            {
                runtimeInfo.runtimeMeshletDataFilename = Path.GetFileName(ctx.assetPath); // フォールバック
            }

            var blockInfos = new List<SerializableDataBlockInfo>();

            try
            {
                using (FileStream fs = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    uint magic = reader.ReadUInt32();
                    if (magic != EXPECTED_MAGIC_NUMBER)
                    {
                        Debug.LogError($"Invalid magic number in {ctx.assetPath}. Import failed.", runtimeInfo);
                        Object.DestroyImmediate(runtimeInfo); return;
                    }

                    runtimeInfo.fileVersion = (int)reader.ReadUInt32();
                    // Potentially check fileVersion here if multiple versions are supported by this importer

                    uint headerSize = reader.ReadUInt32();
                    ulong totalFileSize = reader.ReadUInt64(); // For validation if needed

                    uint numDataBlocks = reader.ReadUInt32();

                    for (int i = 0; i < numDataBlocks; i++)
                    {
                        blockInfos.Add(new SerializableDataBlockInfo
                        {
                            BlockId = reader.ReadUInt32(),
                            BlockOffset = reader.ReadUInt64(),
                            BlockSize = reader.ReadUInt64(),
                            ElementCount = reader.ReadUInt32(),
                            ElementStride = reader.ReadUInt32(),
                            MetaData1 = reader.ReadUInt32(),
                            MetaData2 = reader.ReadUInt32()
                        });
                    }

                    // --- Populate runtimeInfo with metadata from blockInfos ---
                    // This assumes ScriptedImporter only reads metadata, not the full data blocks.
                    // The actual data loading will happen at runtime.

                    foreach (var block in blockInfos)
                    {
                        MeshletDataBlockId id = (MeshletDataBlockId)block.BlockId;
                        switch (id)
                        {
                            case MeshletDataBlockId.BakedVertices:
                                runtimeInfo.bakedVertexCount = (int)block.ElementCount;
                                // Assuming normals, tangents, uvs will have same count or be identified by their own blocks
                                break;
                            case MeshletDataBlockId.BakedNormals:
                                runtimeInfo.bakedMeshHasNormals = block.ElementCount > 0;
                                break;
                            case MeshletDataBlockId.BakedTangents:
                                runtimeInfo.bakedMeshHasTangents = block.ElementCount > 0;
                                break;
                            case MeshletDataBlockId.BakedUVs:
                                runtimeInfo.bakedMeshHasUVs = block.ElementCount > 0;
                                break;
                            case MeshletDataBlockId.BakedIndices:
                                runtimeInfo.bakedIndexCount = (int)block.ElementCount;
                                runtimeInfo.bakedMeshIndexFormat = (IndexFormat)block.MetaData1;
                                break;
                            case MeshletDataBlockId.Meshlets:
                                runtimeInfo.meshletCount = (int)block.ElementCount;
                                break;
                            case MeshletDataBlockId.MeshletVertices:
                                runtimeInfo.totalMeshletVerticesCount = (int)block.ElementCount;
                                break;
                            case MeshletDataBlockId.MeshletTriangles:
                                runtimeInfo.totalMeshletTrianglesByteCount = (int)block.ElementCount; // ElementCount is total bytes here
                                break;
                            case MeshletDataBlockId.MeshletIndexTable:
                                runtimeInfo.meshletIndexTableUIntCount = (int)block.ElementCount;
                                runtimeInfo.meshletTableFormat = (MeshletIndexTableFormat)block.MetaData1;
                                break;
                            case MeshletDataBlockId.MeshletBounds:
                                runtimeInfo.meshletBoundsCount = (int)block.ElementCount;
                                break;
                            case MeshletDataBlockId.ClusterNodes:
                                runtimeInfo.hasHierarchy = true; // Presence of this block implies hierarchy
                                runtimeInfo.clusterNodeCount = (int)block.ElementCount;
                                break;
                            case MeshletDataBlockId.HierarchyInfo:
                                // HierarchyInfoリストをバイナリから読み込んで設定
                                runtimeInfo.hierarchyInfoList = new List<HierarchyInfo>();
                                reader.BaseStream.Seek((long)block.BlockOffset, SeekOrigin.Begin); // HierarchyInfoブロックの先頭へ
                                int hierarchyLevelCount = reader.ReadInt32(); // 要素数（レベル数）
                                for (int i = 0; i < hierarchyLevelCount; i++)
                                {
                                    runtimeInfo.hierarchyInfoList.Add(new HierarchyInfo
                                    {
                                        FirstIndex = reader.ReadInt32(),
                                        Count = reader.ReadInt32()
                                    });
                                }
                                break;
                        }
                    }
                 
                    runtimeInfo.hasHierarchy = (runtimeInfo.clusterNodeCount > 0 && runtimeInfo.hierarchyInfoList != null && runtimeInfo.hierarchyInfoList.Count > 0);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error importing meshlet file {ctx.assetPath}: {e.Message}\n{e.StackTrace}", runtimeInfo);
                Object.DestroyImmediate(runtimeInfo);
                return;
            }

            ctx.AddObjectToAsset("main_info", runtimeInfo);
            ctx.SetMainObject(runtimeInfo);
        }
    }
}