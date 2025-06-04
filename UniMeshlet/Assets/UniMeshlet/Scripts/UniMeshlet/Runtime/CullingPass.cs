using System;
using UniMeshlet.Runtime.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet.Runtime
{
    public class CullingPass : IDisposable
    {
        private static readonly int ObjectToWorldID = Shader.PropertyToID("_ObjectToWorld");
        private static readonly int FrustumPlanesBufferID = Shader.PropertyToID("_FrustumPlanes");
        private static readonly int CameraPositionID = Shader.PropertyToID("_CameraPosition");
        private static readonly int ObjectScaleID = Shader.PropertyToID("_ObjectScale");
        private static readonly int MeshletCountID = Shader.PropertyToID("_MeshletCount");
        private static readonly int RootCountID = Shader.PropertyToID("_RootCount");
        private static readonly int MaxDepthID = Shader.PropertyToID("_MaxDepth");
        private static readonly int MeshletBufferID = Shader.PropertyToID("_Meshlets");
        private static readonly int MeshletBoundBufferID = Shader.PropertyToID("_MeshletBounds");
        private static readonly int ClusterNodesBufferID = Shader.PropertyToID("_ClusterNodes");
        private static readonly int VisibleMeshletBufferID = Shader.PropertyToID("_VisibleMeshletWrite");
        private static readonly int IndexCounterBufferID = Shader.PropertyToID("_IndexCounter");
        private static readonly int DispatchArgsBufferID = Shader.PropertyToID("_DispatchArgs");
        private static readonly int DepthCounterBufferID = Shader.PropertyToID("_DepthCounter");
        private static readonly int VisibleClusterWriteBufferID = Shader.PropertyToID("_VisibleClusterWrite");
        private static readonly int VisibleClusterReadBufferID = Shader.PropertyToID("_VisibleClusterRead");
        private static readonly int ClusterCounterBufferID = Shader.PropertyToID("_ClusterCounter");
        
        private readonly ComputeShader _cullShader;
        private readonly int _clusterCullKernel, _meshletCullKernel, _prepareClusterKernel;
        private uint _clusterCullThreadGroupSizeX, _meshletCullThreadGroupSizeX;
        private LocalKeyword _enableHierarchyCulling;
        
        private Vector4[] _frustumPlanes;
        
        private GraphicsBuffer _visibleMeshletBuffer;
        private GraphicsBuffer _indexCounterBuffer;
        private GraphicsBuffer _dispatchArgsBuffer;
        
        public GraphicsBuffer VisibleMeshletBuffer => _visibleMeshletBuffer;
        public GraphicsBuffer IndexCounterBuffer => _indexCounterBuffer;
        public GraphicsBuffer DispatchArgsBuffer => _dispatchArgsBuffer;

        // For hierarchical cluster culling
        private GraphicsBuffer _depthCounterBuffer;
        private GraphicsBuffer _visibleClusterBufferA;
        private GraphicsBuffer _visibleClusterBufferB;
        private GraphicsBuffer _clusterCounterBuffer;
        
        public GraphicsBuffer ClusterCounterBuffer => _clusterCounterBuffer;
        
        public CullingPass(ComputeShader cull, MeshletMesh meshletMesh)
        {
            _cullShader = cull;
            _clusterCullKernel = _cullShader.FindKernel("CSCullCluster");
            _meshletCullKernel = _cullShader.FindKernel("CSCullMeshlet");
            _prepareClusterKernel = _cullShader.FindKernel("CSPrepareCluster");
            
            if(!cull.IsSupported(_clusterCullKernel))
                Debug.LogError("Cluster culling kernel is not supported on this platform.");
            if(!cull.IsSupported(_meshletCullKernel))
                Debug.LogError("Meshlet culling kernel is not supported on this platform.");
            if(!cull.IsSupported(_prepareClusterKernel))
                Debug.LogError("Prepare cluster kernel is not supported on this platform.");
            
            cull.GetKernelThreadGroupSizes(_clusterCullKernel, out _clusterCullThreadGroupSizeX, out _, out _);
            cull.GetKernelThreadGroupSizes(_meshletCullKernel, out _meshletCullThreadGroupSizeX, out _, out _);
            
            _enableHierarchyCulling = new LocalKeyword(_cullShader, "ENABLE_HIERARCHY_CULLING");
            
            _frustumPlanes = new[] { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero };
            
            _visibleMeshletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Append,
                meshletMesh.MeshletBuffer.count,
                sizeof(uint) * 4);
            _visibleMeshletBuffer.SetCounterValue(0);
            
            _indexCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            _indexCounterBuffer.SetData(new uint[] { 0 });
            
            _dispatchArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));
            _dispatchArgsBuffer.SetData(new uint[] { 0, 1, 1 });
            
            var meshletData = meshletMesh.GetMeshletData;
            if (meshletData.HasHierarchy)
            {
                _depthCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
                _depthCounterBuffer.SetData(new uint[] { 0 });
                _visibleClusterBufferA = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Append,
                    meshletMesh.MeshletBuffer.count, sizeof(uint));
                _visibleClusterBufferB = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Append,
                    meshletMesh.MeshletBuffer.count, sizeof(uint));
                _clusterCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshletData.HierarchyInfo.Count, sizeof(uint));
            }
        }
        
        public void Execute(MeshletMesh meshletMesh, Camera camera, Transform meshTransform)
        {
            var objectToWorld = meshTransform.localToWorldMatrix;
            var viewMat = camera.worldToCameraMatrix;
            var projMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            var vpMat = projMat * viewMat;
            FrustumUtil.ExtractPlanes(vpMat, _frustumPlanes, SystemInfo.usesReversedZBuffer);
            var camPosOS = meshTransform.InverseTransformPoint(camera.transform.position);
            var objectScale = Mathf.Max(meshTransform.lossyScale.x, meshTransform.lossyScale.y, meshTransform.lossyScale.z);
            var meshletData = meshletMesh.GetMeshletData;
            var rootCount = meshletData.HasHierarchy ? meshletData.HierarchyInfo[0].Count : 0;
            var maxDepth = meshletData.HasHierarchy ? meshletData.HierarchyInfo.Count - 1: 0;
            
            // Set CBuffer
            _cullShader.SetMatrix(ObjectToWorldID, objectToWorld);
            _cullShader.SetVectorArray(FrustumPlanesBufferID, _frustumPlanes);
            _cullShader.SetVector(CameraPositionID, camPosOS);
            _cullShader.SetFloat(ObjectScaleID, objectScale);
            _cullShader.SetInt(MeshletCountID, meshletMesh.MeshletBuffer.count);
            _cullShader.SetInt(RootCountID, rootCount);
            _cullShader.SetInt(MaxDepthID, maxDepth);
            
            var argX = (uint)Mathf.CeilToInt(meshletMesh.MeshletBuffer.count / (float)_meshletCullThreadGroupSizeX);

            _cullShader.SetKeyword(_enableHierarchyCulling, meshletData.HasHierarchy);
            // Hierarchical cluster culling
            if (meshletData.HasHierarchy)
            {
                _depthCounterBuffer.SetData(new uint[] { 0 });
                // _visibleClusterBufferA.SetCounterValue(0);
                // _visibleClusterBufferB.SetCounterValue(0);
                _clusterCounterBuffer.SetData(new uint[maxDepth + 1]);
                argX = (uint)Mathf.CeilToInt(rootCount / (float)_clusterCullThreadGroupSizeX);
                _dispatchArgsBuffer.SetData(new uint[] { argX, 1, 1 });
                
                _cullShader.SetBuffer(_clusterCullKernel, ClusterNodesBufferID, meshletMesh.ClusterNodeBuffer);
                _cullShader.SetBuffer(_clusterCullKernel, DepthCounterBufferID, _depthCounterBuffer);
                _cullShader.SetBuffer(_clusterCullKernel, ClusterCounterBufferID, _clusterCounterBuffer);
                
                _cullShader.SetBuffer(_prepareClusterKernel, DepthCounterBufferID, _depthCounterBuffer);
                _cullShader.SetBuffer(_prepareClusterKernel, ClusterCounterBufferID, _clusterCounterBuffer);
                _cullShader.SetBuffer(_prepareClusterKernel, DispatchArgsBufferID, _dispatchArgsBuffer);
                
                bool writeToA = true;
                for (int i = 0; i < maxDepth + 1; i++)
                {
                    if (writeToA)
                    {
                        _visibleClusterBufferA.SetCounterValue(0);
                        _cullShader.SetBuffer(_clusterCullKernel, VisibleClusterWriteBufferID, _visibleClusterBufferA);
                        _cullShader.SetBuffer(_clusterCullKernel, VisibleClusterReadBufferID, _visibleClusterBufferB);
                    }
                    else
                    {
                        _visibleClusterBufferB.SetCounterValue(0);
                        _cullShader.SetBuffer(_clusterCullKernel, VisibleClusterWriteBufferID, _visibleClusterBufferB);
                        _cullShader.SetBuffer(_clusterCullKernel, VisibleClusterReadBufferID, _visibleClusterBufferA);
                    }
                    
                    _cullShader.DispatchIndirect(_clusterCullKernel, _dispatchArgsBuffer);
                    _cullShader.Dispatch(_prepareClusterKernel, 1, 1, 1);
                    
                    writeToA = !writeToA;
                }
                
                _cullShader.SetBuffer(_meshletCullKernel, VisibleClusterReadBufferID,
                    writeToA ? _visibleClusterBufferB : _visibleClusterBufferA);
                _cullShader.SetBuffer(_meshletCullKernel, ClusterCounterBufferID, _clusterCounterBuffer);
            }
            else
            {
                _dispatchArgsBuffer.SetData(new uint[] { argX, 1, 1 });
            }
            
            // Meshlet culling
            _visibleMeshletBuffer.SetCounterValue(0);
            _indexCounterBuffer.SetData(new uint[] { 0 });
            
            _cullShader.SetBuffer(_meshletCullKernel, MeshletBufferID, meshletMesh.MeshletBuffer);
            _cullShader.SetBuffer(_meshletCullKernel, MeshletBoundBufferID, meshletMesh.MeshletBoundBuffer);
            _cullShader.SetBuffer(_meshletCullKernel, VisibleMeshletBufferID, _visibleMeshletBuffer);
            _cullShader.SetBuffer(_meshletCullKernel, IndexCounterBufferID, _indexCounterBuffer);
            
            _cullShader.DispatchIndirect(_meshletCullKernel, _dispatchArgsBuffer);
        }
        
        public void Dispose()
        {
            _dispatchArgsBuffer.Dispose();
            _dispatchArgsBuffer = null;
            _indexCounterBuffer.Dispose();
            _indexCounterBuffer = null;
            _visibleMeshletBuffer?.Dispose();
            _visibleMeshletBuffer = null;
            _depthCounterBuffer?.Dispose();
            _depthCounterBuffer = null;
            _visibleClusterBufferA?.Dispose();
            _visibleClusterBufferA = null;
            _visibleClusterBufferB?.Dispose();
            _visibleClusterBufferB = null;
            _clusterCounterBuffer?.Dispose();
            _clusterCounterBuffer = null;
        }
    }
}