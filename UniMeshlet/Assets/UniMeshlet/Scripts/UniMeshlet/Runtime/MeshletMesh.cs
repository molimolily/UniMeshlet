using System;
using System.Runtime.InteropServices;
using UniMeshlet.Common;
using UnityEngine;

namespace UniMeshlet.Runtime
{
    public class MeshletMesh :IDisposable
    {
        private MeshletData _meshletData;
        public MeshletData GetMeshletData => _meshletData;
        
        public GraphicsBuffer IndexBuffer { get; private set; }
        public GraphicsBuffer MeshletBuffer { get; private set; }
        public GraphicsBuffer MeshletVertexBuffer { get; private set; }
        public GraphicsBuffer MeshletTriangleBuffer { get; private set; }
        public GraphicsBuffer MeshletBoundBuffer { get; private set; }
        public GraphicsBuffer ClusterNodeBuffer { get; private set; }
        public GraphicsBuffer MeshletIndexTableBuffer { get; private set; }
        
        public MeshletMesh(MeshletDataInfo meshletDataInfo)
        {
            _meshletData = MeshletLoader.Load(meshletDataInfo);
            if (_meshletData.IsValid)
            {
                CreateBuffer();
            }
            else
            {
                Debug.LogError("Failed to load Meshlet data or data is invalid.");
            }
        }
        
        public void Dispose()
        {
            _meshletData.Dispose();
            IndexBuffer?.Release();
            MeshletBuffer?.Release();
            MeshletVertexBuffer?.Release();
            MeshletTriangleBuffer?.Release();
            MeshletBoundBuffer?.Release();
            ClusterNodeBuffer?.Release();
            MeshletIndexTableBuffer?.Release();
        }

        private void CreateBuffer()
        {
            var mesh = _meshletData.BakedMesh;
            
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Structured;
            IndexBuffer = mesh.GetIndexBuffer();
            
            MeshletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _meshletData.Meshlets.Length, Marshal.SizeOf<MeshoptMeshlet>());
            MeshletBuffer.SetData(_meshletData.Meshlets);
            
            MeshletVertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _meshletData.MeshletVertices.Length, sizeof(uint));
            MeshletVertexBuffer.SetData(_meshletData.MeshletVertices);
            
            MeshletTriangleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw,
                (_meshletData.MeshletTriangles.Length + 3) >> 2, 4);
            MeshletTriangleBuffer.SetData(_meshletData.MeshletTriangles);
            
            MeshletBoundBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _meshletData.MeshletBounds.Length, Marshal.SizeOf<MeshoptBounds>());
            MeshletBoundBuffer.SetData(_meshletData.MeshletBounds);
            
            MeshletIndexTableBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw,
                _meshletData.MeshletIndexTable.Length, 4);
            MeshletIndexTableBuffer.SetData(_meshletData.MeshletIndexTable);
            
            if (_meshletData.HasHierarchy)
            {
                ClusterNodeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    _meshletData.ClusterNodes.Length, Marshal.SizeOf<ClusterNode>());
                ClusterNodeBuffer.SetData(_meshletData.ClusterNodes);
            }
            else
            {
                ClusterNodeBuffer = null;
            }
        }
    }
}