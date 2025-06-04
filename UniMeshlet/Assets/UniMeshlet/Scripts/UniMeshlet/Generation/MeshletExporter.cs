using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UniMeshlet.Common;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet.Generation
{
    public static class MeshletExporter
    {
        const uint MAGIC_NUMBER = 0x4D455348; // "MESH"
        const int BINARY_FILE_VERSION = 1; // または共有定数

        public static bool Export(string path, MeshletData data, System.Action<string, float> progressCallback = null)
        {
            if (!data.IsValid)
            {
                Debug.LogError("MeshletExporter: Provided MeshletData is invalid. Export aborted.");
                return false;
            }

            progressCallback?.Invoke("Preparing to write binary file...", 0.90f);
            List<SerializableDataBlockInfo> blockInfos = new List<SerializableDataBlockInfo>();

            try
            {
                using (MemoryStream dataStream = new MemoryStream())
                using (BinaryWriter dataWriter = new BinaryWriter(dataStream))
                {
                    // --- 各データブロックを dataStream に書き込み、BlockInfo を作成 ---
                    if (data.BakedMesh.vertexCount > 0)
                    {
                        blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.BakedVertices,
                            data.BakedMesh.vertices));
                        if (data.BakedMesh.normals is { Length: > 0 })
                            blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.BakedNormals,
                                data.BakedMesh.normals));
                        if (data.BakedMesh.tangents is { Length: > 0 })
                            blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.BakedTangents,
                                data.BakedMesh.tangents));
                        if (data.BakedMesh.uv is { Length: > 0 })
                            blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.BakedUVs,
                                data.BakedMesh.uv));

                        if (data.BakedMesh.indexFormat == IndexFormat.UInt16)
                        {
                            var indices = new List<ushort>();
                            data.BakedMesh.GetTriangles(indices, 0);
                            blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.BakedIndices,
                                indices.ToArray(), (uint)IndexFormat.UInt16));
                        }
                        else
                        {
                            blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.BakedIndices,
                                data.BakedMesh.GetIndices(0), (uint)data.BakedMesh.indexFormat));
                        }
                    }

                    // Meshlet Core Data
                    blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.Meshlets, data.Meshlets));
                    blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.MeshletVertices,
                        data.MeshletVertices));
                    blockInfos.Add(CreateBlockInfoAndWriteRawBytes(dataWriter, MeshletDataBlockId.MeshletTriangles,
                        data.MeshletTriangles)); // byte[]

                    // Meshlet Index Table
                    blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.MeshletIndexTable,
                        data.MeshletIndexTable, (uint)data.TableFormat));

                    // Meshlet Bounds Data
                    blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.MeshletBounds,
                        data.MeshletBounds));

                    // Hierarchy Data
                    if (data.HasHierarchy)
                    {
                        blockInfos.Add(CreateBlockInfoAndWrite(dataWriter, MeshletDataBlockId.ClusterNodes,
                            data.ClusterNodes));
                        // HierarchyInfo は List<struct> なのでカスタムシリアライズ
                        blockInfos.Add(WriteHierarchyInfoList(dataWriter, MeshletDataBlockId.HierarchyInfo,
                            data.HierarchyInfo));
                    }

                    progressCallback?.Invoke("Writing binary file header...", 0.95f);
                    dataWriter.Flush();
                    long totalDataBlockSize = dataStream.Length;

                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    using (BinaryWriter finalWriter = new BinaryWriter(fs))
                    {
                        finalWriter.Write(MAGIC_NUMBER);
                        finalWriter.Write(BINARY_FILE_VERSION);

                        uint headerSize = (uint)(
                            sizeof(uint) + // Magic
                            sizeof(int) + // Version
                            sizeof(uint) + // HeaderSize field
                            sizeof(ulong) + // TotalFileSize field
                            sizeof(uint) + // NumDataBlocks field
                            blockInfos.Count * Marshal.SizeOf<SerializableDataBlockInfo>()
                        );
                        finalWriter.Write(headerSize);
                        long totalFileSizePlaceholderPos = finalWriter.BaseStream.Position;
                        finalWriter.Write((ulong)0);
                        finalWriter.Write((uint)blockInfos.Count);

                        foreach (var t in blockInfos)
                        {
                            var info = t;
                            info.BlockOffset += headerSize;
                            WriteSerializableBlockInfo(finalWriter, info);
                        }

                        dataStream.Position = 0;
                        dataStream.CopyTo(fs);
                        finalWriter.Flush();

                        ulong actualTotalFileSize = (ulong)finalWriter.BaseStream.Position;
                        finalWriter.BaseStream.Seek(totalFileSizePlaceholderPos, SeekOrigin.Begin);
                        finalWriter.Write(actualTotalFileSize);
                    }
                }

                progressCallback?.Invoke("Binary file written successfully.", 0.99f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError(
                    $"MeshletBinaryExporter: Error writing binary file to {path}: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        // --- BlockInfo作成 & データ書き込みヘルパー (MemoryStreamへ) ---
        private static SerializableDataBlockInfo CreateBlockInfoAndWrite<T>(BinaryWriter dataWriter,
            MeshletDataBlockId id, T[] array, uint metaData1 = 0, uint metaData2 = 0) where T : struct
        {
            var info = new SerializableDataBlockInfo
                { BlockId = (uint)id, MetaData1 = metaData1, MetaData2 = metaData2 };
            if (array == null || array.Length == 0)
            {
                info.ElementCount = 0;
                info.ElementStride = 0;
                info.BlockSize = 0;
                info.BlockOffset = (ulong)dataWriter.BaseStream.Position; // 現在位置（データは書き込まない）
                return info;
            }

            info.BlockOffset = (ulong)dataWriter.BaseStream.Position;
            info.ElementCount = (uint)array.Length;
            info.ElementStride = (uint)Marshal.SizeOf<T>();

            if (typeof(T) == typeof(Vector3)) WriteVector3ArrayInternal(dataWriter, array as Vector3[]);
            else if (typeof(T) == typeof(Vector4)) WriteVector4ArrayInternal(dataWriter, array as Vector4[]);
            else if (typeof(T) == typeof(Vector2)) WriteVector2ArrayInternal(dataWriter, array as Vector2[]);
            else if (typeof(T) == typeof(int) && id == MeshletDataBlockId.BakedIndices)
                WriteIntArrayInternal(dataWriter, array as int[]);
            else if (typeof(T) == typeof(ushort) && id == MeshletDataBlockId.BakedIndices)
                WriteUShortArrayInternal(dataWriter, array as ushort[]);
            else throw new System.ArgumentException($"Unsupported array type for CreateBlockInfoAndWrite: {typeof(T)}");

            info.BlockSize = (ulong)dataWriter.BaseStream.Position - info.BlockOffset;
            return PadStreamToAlignment(dataWriter, ref info, 4); // 4バイトアライメント
        }

        private static SerializableDataBlockInfo CreateBlockInfoAndWrite<T>(BinaryWriter dataWriter,
            MeshletDataBlockId id, NativeArray<T> array, uint metaData1 = 0, uint metaData2 = 0) where T : unmanaged
        {
            var info = new SerializableDataBlockInfo
                { BlockId = (uint)id, MetaData1 = metaData1, MetaData2 = metaData2 };
            if (!array.IsCreated || array.Length == 0)
            {
                info.ElementCount = 0;
                info.ElementStride = 0;
                info.BlockSize = 0;
                info.BlockOffset = (ulong)dataWriter.BaseStream.Position;
                return info;
            }

            info.BlockOffset = (ulong)dataWriter.BaseStream.Position;
            info.ElementCount = (uint)array.Length;
            info.ElementStride = (uint)Marshal.SizeOf<T>();
            WriteNativeArrayBlittableInternal(dataWriter, array);
            info.BlockSize = (ulong)dataWriter.BaseStream.Position - info.BlockOffset;
            return PadStreamToAlignment(dataWriter, ref info, 4);
        }

        private static SerializableDataBlockInfo CreateBlockInfoAndWriteRawBytes<T>(BinaryWriter dataWriter,
            MeshletDataBlockId id, NativeArray<T> array, uint metaData1 = 0, uint metaData2 = 0) where T : unmanaged
        {
            // This is specifically for byte[] like data (e.g. MeshletTriangles) that might not be T[]
            var info = new SerializableDataBlockInfo
                { BlockId = (uint)id, MetaData1 = metaData1, MetaData2 = metaData2 };
            if (!array.IsCreated || array.Length == 0)
            {
                info.ElementCount = 0; // For byte array, ElementCount is total bytes, Stride is 1
                info.ElementStride = 1;
                info.BlockSize = 0;
                info.BlockOffset = (ulong)dataWriter.BaseStream.Position;
                return info;
            }

            info.BlockOffset = (ulong)dataWriter.BaseStream.Position;

            int totalBytes = array.Length * Marshal.SizeOf<T>(); // Marshal.SizeOf<T> should be 1 if T is byte
            info.ElementCount = (uint)totalBytes;
            info.ElementStride = 1; // Treating as raw bytes

            WriteNativeArrayBlittableInternal(dataWriter, array); // Writes raw bytes
            info.BlockSize = (ulong)totalBytes;
            return PadStreamToAlignment(dataWriter, ref info, 4);
        }


        private static SerializableDataBlockInfo WriteHierarchyInfoList(BinaryWriter dataWriter, MeshletDataBlockId id,
            List<HierarchyInfo> list)
        {
            var info = new SerializableDataBlockInfo { BlockId = (uint)id };
            info.BlockOffset = (ulong)dataWriter.BaseStream.Position;
            if (list == null || list.Count == 0)
            {
                info.ElementCount = 0;
                info.ElementStride = (uint)Marshal.SizeOf<HierarchyInfo>(); // Even if empty, stride is known
                info.BlockSize = 0;
                dataWriter.Write(0); // Write count 0
                return info;
            }

            dataWriter.Write(list.Count); // Number of HierarchyInfo structs
            info.ElementCount = (uint)list.Count;
            info.ElementStride = sizeof(int) * 2; // FirstIndex (int) + Count (int)

            foreach (var hi in list)
            {
                dataWriter.Write(hi.FirstIndex);
                dataWriter.Write(hi.Count);
            }

            info.BlockSize = (ulong)dataWriter.BaseStream.Position - info.BlockOffset;
            return PadStreamToAlignment(dataWriter, ref info, 4);
        }

        private static SerializableDataBlockInfo PadStreamToAlignment(BinaryWriter writer,
            ref SerializableDataBlockInfo info, int alignment)
        {
            long currentLength = (long)info.BlockSize;
            long remainder = currentLength % alignment;
            if (remainder != 0)
            {
                long padding = alignment - remainder;
                for (int i = 0; i < padding; i++)
                {
                    writer.Write((byte)0x00); // Pad with zeros
                }

                info.BlockSize += (ulong)padding; // Update block size to include padding
            }

            return info;
        }


        // --- Internal BinaryWriter Helper Methods (for dataStream) ---
        private static void WriteVector3ArrayInternal(BinaryWriter writer, Vector3[] array)
        {
            // Length is written by caller in the block info or explicitly for lists
            foreach (var v in array)
            {
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
            }
        }

        private static void WriteVector4ArrayInternal(BinaryWriter writer, Vector4[] array)
        {
            foreach (var v in array)
            {
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
                writer.Write(v.w);
            }
        }

        private static void WriteVector2ArrayInternal(BinaryWriter writer, Vector2[] array)
        {
            foreach (var v in array)
            {
                writer.Write(v.x);
                writer.Write(v.y);
            }
        }

        private static void WriteIntArrayInternal(BinaryWriter writer, int[] array)
        {
            foreach (var t in array) writer.Write(t);
        }

        private static void WriteUShortArrayInternal(BinaryWriter writer, ushort[] array)
        {
            foreach (var t in array) writer.Write(t);
        }

        private static unsafe void WriteNativeArrayBlittableInternal<T>(BinaryWriter writer, NativeArray<T> array)
            where T : unmanaged
        {
            int structSize = Marshal.SizeOf<T>();
            int totalBytes = array.Length * structSize;
            byte[] byteArray = new byte[totalBytes];
            void* ptr = array.GetUnsafeReadOnlyPtr();
            Marshal.Copy((System.IntPtr)ptr, byteArray, 0, totalBytes);
            writer.Write(byteArray);
        }

        private static void WriteSerializableBlockInfo(BinaryWriter writer, SerializableDataBlockInfo info)
        {
            writer.Write(info.BlockId);
            writer.Write(info.BlockOffset);
            writer.Write(info.BlockSize);
            writer.Write(info.ElementCount);
            writer.Write(info.ElementStride);
            writer.Write(info.MetaData1);
            writer.Write(info.MetaData2);
        }
    }
}