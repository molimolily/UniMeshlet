using System.IO;
using UniMeshlet.Common;
using UniMeshlet.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniMeshlet.Editor
{
    public class MeshletGeneratorEditorWindow : EditorWindow
    {
        private Mesh _sourceMesh;
        private string _outputPath = "Assets/"; // デフォルトの出力先ディレクトリを提案
        private MeshletProcessor.Configuration _processorConfig = new MeshletProcessor.Configuration
        {
            MaxVerticesPerMeshlet = 64,
            MaxTrianglesPerMeshlet = 128, // meshoptimizer推奨値
            ConeWeight = 0.25f,            // コーンカリングなし
            PartitionSizeForHierarchy = 4,
            MinRootSizeForHierarchy = 256,
            PreferredBakedMeshIndexFormat = IndexFormat.UInt16, // デフォルトはUInt16、必要ならProcessorがUInt32に昇格
            // TargetLodForIndexTable = 0, // MeshletProcessorで設定
        };

        [MenuItem("Tools/Meshlet Exporter")]
        public static void ShowWindow()
        {
            GetWindow<MeshletGeneratorEditorWindow>("Meshlet Exporter").Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Meshlet Data Exporter", EditorStyles.boldLabel);

            _sourceMesh = (Mesh)EditorGUILayout.ObjectField("Source Mesh", _sourceMesh, typeof(Mesh), false);

            EditorGUILayout.Space();
            GUILayout.Label("Processing Configuration", EditorStyles.boldLabel);
            _processorConfig.MaxVerticesPerMeshlet = EditorGUILayout.IntField("Max Vertices/Meshlet", _processorConfig.MaxVerticesPerMeshlet);
            _processorConfig.MaxTrianglesPerMeshlet = EditorGUILayout.IntField("Max Triangles/Meshlet", _processorConfig.MaxTrianglesPerMeshlet);
            _processorConfig.ConeWeight = EditorGUILayout.FloatField("Cone Weight", _processorConfig.ConeWeight);
            _processorConfig.PartitionSizeForHierarchy = EditorGUILayout.IntField("Hierarchy Partition Size", _processorConfig.PartitionSizeForHierarchy);
            _processorConfig.MinRootSizeForHierarchy = EditorGUILayout.IntField("Hierarchy Min Root Size", _processorConfig.MinRootSizeForHierarchy);
            _processorConfig.PreferredBakedMeshIndexFormat = (IndexFormat)EditorGUILayout.EnumPopup("Baked Mesh Index Format", _processorConfig.PreferredBakedMeshIndexFormat);


            EditorGUILayout.Space();
            GUILayout.Label("Output", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(100)))
            {
                string defaultName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(_sourceMesh));
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save Meshlet Data",
                    defaultName,
                    "meshlet",
                    "Please enter a file name to save the meshlet data to."
                );
                if (!string.IsNullOrEmpty(path))
                {
                    _outputPath = path;
                }
            }
            GUILayout.EndHorizontal();


            if (GUILayout.Button("Generate and Export Meshlet Data"))
            {
                if (!ValidateInput()) return;

                MeshletData processedData = default;
                try
                {
                    System.Action<string, float> progressCallback = (message, progress) =>
                    {
                        EditorUtility.DisplayProgressBar("Generating Meshlet Data", message, progress);
                    };

                    processedData = MeshletProcessor.ProcessMeshletData(_sourceMesh, _processorConfig, progressCallback: progressCallback);

                    if (!processedData.IsValid)
                    {
                        Debug.LogError("Meshlet processing failed. Resulting data is invalid.");
                        return;
                    }

                    if (MeshletExporter.Export(_outputPath, processedData, progressCallback))
                    {
                        AssetDatabase.ImportAsset(_outputPath); // ScriptedImporterをトリガー
                        Debug.Log($"Successfully generated and exported meshlet data to: {_outputPath}");
                    }
                    else
                    {
                        Debug.LogError($"Failed to export meshlet data to: {_outputPath}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error during meshlet generation or export: {e.Message}\n{e.StackTrace}");
                }
                finally
                {
                    processedData.Dispose();
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        private bool ValidateInput()
        {
            if (_sourceMesh == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a Source Mesh.", "OK");
                return false;
            }
            if (string.IsNullOrEmpty(_outputPath))
            {
                EditorUtility.DisplayDialog("Error", "Please specify an Output Directory.", "OK");
                return false;
            }
            if (!_sourceMesh.isReadable)
            {
                EditorUtility.DisplayDialog("Error", $"Mesh '{_sourceMesh.name}' is not readable. Please enable Read/Write in its import settings.", "OK");
                return false;
            }
            return true;
        }
    }
}