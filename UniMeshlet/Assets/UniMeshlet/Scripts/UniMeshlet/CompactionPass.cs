using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet
{
    public class CompactionPass : IDisposable
    {
        private static readonly int IndexBufferID = Shader.PropertyToID("_Indices");
        private static readonly int MeshletBufferID = Shader.PropertyToID("_Meshlets");
        private static readonly int MeshletVertexBufferID = Shader.PropertyToID("_MeshletVertices");
        private static readonly int MeshletTriangleBufferID = Shader.PropertyToID("_MeshletTriangles");
        private static readonly int MeshletIndexTableBufferID = Shader.PropertyToID("_MeshletIndexTable");
        private static readonly int VisibleInfoReadBufferID = Shader.PropertyToID("_VisibleMeshlets");
        private static readonly int IndexCounterID = Shader.PropertyToID("_IndexCounter");
        private static readonly int DrawArgsBufferID = Shader.PropertyToID("_DrawArgs");
        private static readonly int TableStrideID = Shader.PropertyToID("_TableStride");
        
        private readonly ComputeShader _compactionShader;
        private readonly int _kernel;
        private readonly LocalKeyword _use16BitIndices;
        
        public CompactionPass(ComputeShader compactionShader)
        {
            _compactionShader = compactionShader;
            _kernel = _compactionShader.FindKernel("CSCompaction");
            
            if(!compactionShader.IsSupported(_kernel))
                Debug.LogError("Compaction shader is not supported on this platform. Please check the shader compatibility.");
            
            _use16BitIndices = new LocalKeyword(_compactionShader, "USE_16_BIT_INDICES");
        }

        public void Execute(MeshletMesh meshletMesh, GraphicsBuffer visibleMeshletBuffer, GraphicsBuffer indexCounterBuffer,
            GraphicsBuffer dispatchArgsBuffer, GraphicsBuffer drawArgsBuffer)
        {
            var meshletData = meshletMesh.GetMeshletData;
            _compactionShader.SetBuffer(_kernel, IndexBufferID, meshletMesh.IndexBuffer);
            _compactionShader.SetBuffer(_kernel, MeshletBufferID, meshletMesh.MeshletBuffer);
            _compactionShader.SetBuffer(_kernel, MeshletVertexBufferID, meshletMesh.MeshletVertexBuffer);
            _compactionShader.SetBuffer(_kernel, MeshletTriangleBufferID, meshletMesh.MeshletTriangleBuffer);
            _compactionShader.SetBuffer(_kernel, MeshletIndexTableBufferID, meshletMesh.MeshletIndexTableBuffer);
            _compactionShader.SetBuffer(_kernel, VisibleInfoReadBufferID, visibleMeshletBuffer);
            _compactionShader.SetBuffer(_kernel, IndexCounterID, indexCounterBuffer);
            _compactionShader.SetBuffer(_kernel, DrawArgsBufferID, drawArgsBuffer);
            _compactionShader.SetInt(TableStrideID, (int)meshletData.TableFormat);
            _compactionShader.SetKeyword(_use16BitIndices, meshletData.BakedMesh.indexFormat == IndexFormat.UInt16);
            GraphicsBuffer.CopyCount(visibleMeshletBuffer, dispatchArgsBuffer, 0); // 要修正
            _compactionShader.DispatchIndirect(_kernel, dispatchArgsBuffer);
            
        }
        
        public void Dispose()
        {
        }
    }
}