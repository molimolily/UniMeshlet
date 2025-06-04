using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UniMeshlet.Common;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet.Runtime
{
    public static class MeshletLoader
    {
        private const uint EXPECTED_MAGIC_NUMBER = 0x4D455348; // "MESH"
        private const uint EXPECTED_FILE_VERSION = 1;
        
        public static MeshletData Load(MeshletDataInfo meshletDataInfo)
        {
            string path = null;
            #if UNITY_EDITOR
            path = AssetDatabase.GUIDToAssetPath(meshletDataInfo.sourceAssetGUID);
            #else
            path = Path.Combine(Application.streamingAssetsPath, MeshletDataInfo.StreamingAssetsTargetSubfolder, meshletDataInfo.runtimeMeshletDataFilename);
            #endif
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"Meshlet binary file not found at: {path}");
                return default;
            }
            
            return LoadFromFile(path);
        }

        private static MeshletData LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"Meshlet binary file not found at: {filePath}");
                return default; // 無効なデータを返す
            }
            
            var loadedData = new MeshletData();
            var success = false;
            
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath); // 同期ロード

                using (MemoryStream ms = new MemoryStream(fileData))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    // --- 1. ヘッダー読み込み ---
                    uint magic = reader.ReadUInt32();
                    if (magic != EXPECTED_MAGIC_NUMBER)
                        throw new System.Exception($"Invalid magic number. Expected {EXPECTED_MAGIC_NUMBER:X}, got {magic:X}.");

                    uint version = reader.ReadUInt32();
                    if (version != EXPECTED_FILE_VERSION)
                        throw new System.Exception($"Mismatched file version. Expected {EXPECTED_FILE_VERSION}, got {version}.");

                    uint headerSize = reader.ReadUInt32();
                    ulong totalFileSize = reader.ReadUInt64();
                    uint numDataBlocks = reader.ReadUInt32();

                    var blockInfos = new List<SerializableDataBlockInfo>(); // ScriptedImporterと同じ構造体
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

                    // --- 2. ProcessedMeshletData の各フィールドをバイナリから読み込んで設定 ---
                    loadedData.BakedMesh = new Mesh();
                    loadedData.BakedMesh.name = Path.GetFileNameWithoutExtension(filePath) + "_LoadedInstance";

                    var verticesBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.BakedVertices);
                    if(verticesBlock.HasValue) loadedData.BakedMesh.vertices = ReadDataBlockArray<Vector3>(reader, verticesBlock.Value);

                    var normalsBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.BakedNormals);
                    if(normalsBlock.HasValue && normalsBlock.Value.ElementCount > 0) loadedData.BakedMesh.normals = ReadDataBlockArray<Vector3>(reader, normalsBlock.Value);
                    
                    var tangentsBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.BakedTangents);
                    if(tangentsBlock.HasValue && tangentsBlock.Value.ElementCount > 0) loadedData.BakedMesh.tangents = ReadDataBlockArray<Vector4>(reader, tangentsBlock.Value);

                    var uvsBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.BakedUVs);
                    if(uvsBlock.HasValue && uvsBlock.Value.ElementCount > 0) loadedData.BakedMesh.uv = ReadDataBlockArray<Vector2>(reader, uvsBlock.Value);
                    
                    var indicesBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.BakedIndices);
                    if (indicesBlock.HasValue) {
                        loadedData.BakedMesh.indexFormat = (IndexFormat)indicesBlock.Value.MetaData1;
                        if (loadedData.BakedMesh.indexFormat == IndexFormat.UInt16)
                            loadedData.BakedMesh.SetIndices(ReadDataBlockArray<ushort>(reader, indicesBlock.Value), MeshTopology.Triangles, 0);
                        else
                            loadedData.BakedMesh.SetIndices(ReadDataBlockArray<int>(reader, indicesBlock.Value), MeshTopology.Triangles, 0);
                    }
                    loadedData.BakedMesh.RecalculateBounds();


                    loadedData.Meshlets = CreateNativeArrayFromBlock<MeshoptMeshlet>(reader, blockInfos, MeshletDataBlockId.Meshlets, Allocator.Persistent);
                    loadedData.MeshletVertices = CreateNativeArrayFromBlock<uint>(reader, blockInfos, MeshletDataBlockId.MeshletVertices, Allocator.Persistent);
                    
                    var trianglesFileBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.MeshletTriangles);
                    if (trianglesFileBlock.HasValue) {
                        byte[] triangleBytes = ReadRawBytesFromBlock(reader, trianglesFileBlock.Value);
                        if (triangleBytes.Length > 0) {
                           loadedData.MeshletTriangles = new NativeArray<byte>(triangleBytes, Allocator.Persistent);
                        } else {
                           loadedData.MeshletTriangles = new NativeArray<byte>(0, Allocator.Persistent);
                        }
                    } else {
                        loadedData.MeshletTriangles = new NativeArray<byte>(0, Allocator.Persistent);
                    }


                    var indexTableBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.MeshletIndexTable);
                    if(indexTableBlock.HasValue) {
                        loadedData.MeshletIndexTable = CreateNativeArrayFromBlock<uint>(reader, indexTableBlock.Value, Allocator.Persistent);
                        loadedData.TableFormat = (MeshletIndexTableFormat)indexTableBlock.Value.MetaData1;
                    } else {
                        loadedData.MeshletIndexTable = new NativeArray<uint>(0, Allocator.Persistent);
                        // デフォルトのTableFormatを設定するか、エラーにする
                        loadedData.TableFormat = MeshletIndexTableFormat.U8; // 仮
                    }


                    loadedData.MeshletBounds = CreateNativeArrayFromBlock<MeshoptBounds>(reader, blockInfos, MeshletDataBlockId.MeshletBounds, Allocator.Persistent);

                    var clusterNodesBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.ClusterNodes);
                    var hierarchyInfoBlock = FindBlockInfo(blockInfos, MeshletDataBlockId.HierarchyInfo);

                    if (clusterNodesBlock.HasValue && clusterNodesBlock.Value.ElementCount > 0 && hierarchyInfoBlock.HasValue)
                    {
                        loadedData.ClusterNodes = CreateNativeArrayFromBlock<ClusterNode>(reader, clusterNodesBlock.Value, Allocator.Persistent);
                        loadedData.HierarchyInfo = new List<HierarchyInfo>();
                        reader.BaseStream.Seek((long)hierarchyInfoBlock.Value.BlockOffset, SeekOrigin.Begin);
                        int hierarchyLevelCount = reader.ReadInt32();
                        for (int i = 0; i < hierarchyLevelCount; i++)
                        {
                            loadedData.HierarchyInfo.Add(new HierarchyInfo
                            {
                                FirstIndex = reader.ReadInt32(),
                                Count = reader.ReadInt32()
                            });
                        }
                    }
                    else
                    {
                        loadedData.ClusterNodes = new NativeArray<ClusterNode>(0, Allocator.Persistent);
                        loadedData.HierarchyInfo = new List<HierarchyInfo>();
                    }
                    success = true;
                } // using BinaryReader
            } // using MemoryStream
            catch (System.Exception e)
            {
                Debug.LogError($"Error loading and parsing meshlet file '{filePath}': {e.Message}\n{e.StackTrace}");
                loadedData.Dispose(); // 失敗した場合は確保したリソースを破棄
                return default;
            }
            
            if (!loadedData.IsValid && success) { // パースは成功したがデータが無効と判定された場合
                Debug.LogWarning($"Loaded data for '{filePath}' but it was marked as invalid by its own IsValid check.");
                loadedData.Dispose();
                return default;
            }
            if (!success) { // successフラグが立たなかった（通常はcatchで処理されるが念のため）
                loadedData.Dispose();
                return default;
            }
            
            return loadedData;
        }
        
        // --- BinaryReader Helper Methods for loading specific data blocks ---
        private static SerializableDataBlockInfo? FindBlockInfo(List<SerializableDataBlockInfo> blockInfos, MeshletDataBlockId id)
        {
            foreach (var block in blockInfos)
            {
                if (block.BlockId == (uint)id) return block;
            }
            // Debug.LogWarning($"Data block with ID {id} not found in binary file."); // 呼び出し側で警告を出すか判断
            return null;
        }
        
        private static T[] ReadDataBlockArray<T>(BinaryReader reader, SerializableDataBlockInfo blockInfo) where T : struct
        {
            if (blockInfo.ElementCount == 0) return System.Array.Empty<T>();
            reader.BaseStream.Seek((long)blockInfo.BlockOffset, SeekOrigin.Begin);
            // ReadBlittableArrayは、elementStrideではなくMarshal.SizeOf<T>を使うので、
            // blockInfo.ElementStrideは検証用として使える
            if (blockInfo.ElementStride != Marshal.SizeOf<T>() && blockInfo.ElementStride != 0) { // Stride 0は単一要素やバイト配列の場合など
                Debug.LogWarning($"Stride mismatch for block { (MeshletDataBlockId)blockInfo.BlockId}. Expected {Marshal.SizeOf<T>()}, got {blockInfo.ElementStride}");
            }
            return ReadBlittableArrayInternal<T>(reader, (int)blockInfo.ElementCount);
        }
        
        private static NativeArray<T> CreateNativeArrayFromBlock<T>(BinaryReader reader, List<SerializableDataBlockInfo> blockInfos, MeshletDataBlockId id, Allocator allocator) where T : struct
        {
            var blockInfoOpt = FindBlockInfo(blockInfos, id);
            if (!blockInfoOpt.HasValue || blockInfoOpt.Value.ElementCount == 0)
            {
                return new NativeArray<T>(0, allocator);
            }
            return CreateNativeArrayFromBlock<T>(reader, blockInfoOpt.Value, allocator);
        }
        
        private static NativeArray<T> CreateNativeArrayFromBlock<T>(BinaryReader reader, SerializableDataBlockInfo blockInfo, Allocator allocator) where T : struct
        {
            if (blockInfo.ElementCount == 0) return new NativeArray<T>(0, allocator);
            reader.BaseStream.Seek((long)blockInfo.BlockOffset, SeekOrigin.Begin);
            T[] managedArray = ReadBlittableArrayInternal<T>(reader, (int)blockInfo.ElementCount);
            if (managedArray.Length > 0) {
                return new NativeArray<T>(managedArray, allocator);
            } else {
                return new NativeArray<T>(0, allocator);
            }
        }
        
        private static byte[] ReadRawBytesFromBlock(BinaryReader reader, SerializableDataBlockInfo blockInfo)
        {
            if (blockInfo.BlockSize == 0) return System.Array.Empty<byte>();
            reader.BaseStream.Seek((long)blockInfo.BlockOffset, SeekOrigin.Begin);
            return reader.ReadBytes((int)blockInfo.BlockSize);
        }
        
        private static T[] ReadBlittableArrayInternal<T>(BinaryReader reader, int elementCount) where T : struct
        {
            if (elementCount == 0) return System.Array.Empty<T>();
            int totalBytesToRead = elementCount * Marshal.SizeOf<T>();
            byte[] byteArray = reader.ReadBytes(totalBytesToRead);
            T[] resultArray = new T[elementCount];

            GCHandle handle = GCHandle.Alloc(resultArray, GCHandleType.Pinned);
            try
            {
                System.IntPtr pointer = handle.AddrOfPinnedObject();
                Marshal.Copy(byteArray, 0, pointer, totalBytesToRead);
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
            return resultArray;
        }
    }
}