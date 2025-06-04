#define HIERARCHY_CULLING_DEBUG

using System.Linq;
using UnityEngine;

namespace UniMeshlet
{
    public class MeshletDrawer : MonoBehaviour
    {
        private static readonly int ObjectToWorldID = Shader.PropertyToID("_ObjectToWorld");
        private static readonly int WorldToObjectID = Shader.PropertyToID("_WorldToObject");
        private static readonly int TableStrideID = Shader.PropertyToID("_TableStride");
        private static readonly int MeshletIndexTableID = Shader.PropertyToID("_MeshletIndexTable");
        
        [SerializeField] private MeshletDataInfo meshletDataInfo;
        [SerializeField] private Material material;
        [SerializeField] private ComputeShader cullCompute;
        [SerializeField] private ComputeShader compactionCompute;
        [SerializeField] private Camera cullingCamera;
        [SerializeField] private bool cullingEnabled = true;
        private bool PropertyCheck => meshletDataInfo != null && material != null && cullCompute != null && compactionCompute != null;
        
        private bool _isInitialized = false;
        private MeshletMesh _meshletMesh;
        private MaterialPropertyBlock _matProps;
        private GraphicsBuffer _drawArgsBuffer;
        private CullingPass _cullingPass;
        private CompactionPass _compactionPass;
        
        private GraphicsBuffer.IndirectDrawIndexedArgs DrawArgs(int indexCount)
        {
            return new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = (uint)indexCount,
                instanceCount = 1,
                startIndex = 0,
                baseVertexIndex = 0,
                startInstance = 0
            };
        }

        private void Setup()
        {
            _meshletMesh = new MeshletMesh(meshletDataInfo);
            _matProps = new MaterialPropertyBlock();
            
            _drawArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var args = new[] { DrawArgs(0) };
            _drawArgsBuffer.SetData(args);
            
            _cullingPass = new CullingPass(cullCompute, _meshletMesh);
            _compactionPass = new CompactionPass(compactionCompute);
            
            _isInitialized = true;
        }

        private void Release()
        {
            _compactionPass?.Dispose();
            _cullingPass?.Dispose();
            _drawArgsBuffer?.Dispose();
            _drawArgsBuffer = null;
            _meshletMesh?.Dispose();
            _isInitialized = false;
        }
        
        private void OnEnable()
        {
            if (!PropertyCheck)
            {
                Debug.LogError("Property are not assigned in MeshletDrawer. Please assign them in the inspector.");
                return;
            }

            if (!_isInitialized)
                Setup();
            
            if(cullingCamera == null)
                cullingCamera = Camera.main;
        }
        
        private void OnDisable() => Release();

        private void Draw()
        {
            var meshletData = _meshletMesh.GetMeshletData;
            
            // Initialize DrawArgsBuffer
            _drawArgsBuffer.SetData(cullingEnabled
                ? new[] { DrawArgs(0) }
                : new[] { DrawArgs(_meshletMesh.IndexBuffer.count) });

            if (cullingEnabled)
            {
                _cullingPass.Execute(_meshletMesh, cullingCamera, this.transform);
                _compactionPass.Execute(_meshletMesh, _cullingPass.VisibleMeshletBuffer,
                _cullingPass.IndexCounterBuffer, _cullingPass.DispatchArgsBuffer,
                    _drawArgsBuffer);

                #if UNITY_EDITOR && HIERARCHY_CULLING_DEBUG
                if (meshletData.HasHierarchy)
                {
                    var clusterCounterBuffer = _cullingPass.ClusterCounterBuffer;
                    if (clusterCounterBuffer != null && meshletData.HierarchyInfo is { Count: > 0 })
                    {
                        uint[] clusterCounts = new uint[meshletData.HierarchyInfo.Count];
                        clusterCounterBuffer.GetData(clusterCounts);
                        Debug.Log($"Cluster Counts: ({meshletData.HierarchyInfo[0].Count}, " + string.Join(", ", clusterCounts.Select(x => x.ToString())) + ")");
                    }
                }
                #endif
            }

            //  Set the material properties
            _matProps.Clear();
            _matProps.SetMatrix(ObjectToWorldID, transform.localToWorldMatrix);
            _matProps.SetMatrix(WorldToObjectID, transform.worldToLocalMatrix);
            _matProps.SetInt(TableStrideID, (int)meshletData.TableFormat);
            _matProps.SetBuffer(MeshletIndexTableID, _meshletMesh.MeshletIndexTableBuffer);
            
            // Set bounding volume
            var localBounds = meshletData.BakedMesh.bounds;
            var center = transform.TransformPoint(localBounds.center);
            var extents = Vector3.Scale(transform.lossyScale, localBounds.extents);
            var worldBounds = new Bounds(center, extents * 2f);
            
            // Set RenderParams
            var rp = new RenderParams(material)
            {
                worldBounds = worldBounds,
                matProps = _matProps
            };
            
            // Draw the mesh
            Graphics.RenderMeshIndirect(rp, meshletData.BakedMesh, _drawArgsBuffer);
        }

        private void Update()
        {
            if(!PropertyCheck) return;
            if (!_isInitialized) Setup();
            
            Draw();
        }
    }
}