using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UniMeshlet.Editor
{
    public class MeshletDataPreProcessBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("CopyMeshletDataToStreamingAssets: Preprocessing build, copying referenced meshlet data...");

            string streamingAssetsTargetSubfolder = MeshletDataInfo.StreamingAssetsTargetSubfolder;
            string targetRootInStreamingAssets =
                Path.Combine(Application.streamingAssetsPath, streamingAssetsTargetSubfolder);

            if (Directory.Exists(targetRootInStreamingAssets))
            {
                FileUtil.DeleteFileOrDirectory(targetRootInStreamingAssets);
                FileUtil.DeleteFileOrDirectory(targetRootInStreamingAssets + ".meta");
                Debug.Log($"Deleted existing StreamingAssets subfolder: {targetRootInStreamingAssets}");
            }
            else
            {
                Directory.CreateDirectory(targetRootInStreamingAssets);
                Debug.Log($"Created StreamingAssets subfolder: {targetRootInStreamingAssets}");
            }

            // 1. プロジェクト内の全ての MeshletDataRuntimeInfo アセットを検索
            // string[] guids = AssetDatabase.FindAssets("t:MeshletDataRuntimeInfo");
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(MeshletDataInfo)}");
            if (guids.Length == 0)
            {
                Debug.Log("No MeshletDataInfo assets found in the project. Nothing to copy.");
                return;
            }

            Debug.Log($"Found {guids.Length} MeshletDataInfo assets. Processing them...");
            int copiedFilesCount = 0;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                MeshletDataInfo runtimeInfo = AssetDatabase.LoadAssetAtPath<MeshletDataInfo>(assetPath);

                if (runtimeInfo == null || string.IsNullOrEmpty(runtimeInfo.sourceMeshletDataFilename))
                {
                    Debug.LogWarning(
                        $"MeshletDataRuntimeInfo at '{assetPath}' is invalid or does not specify a source binary filename. Skipping.");
                    continue;
                }

                // 2. MeshletDataRuntimeInfo が参照する元のバイナリファイルパスを特定する
                //    このためには、runtimeInfo.sourceAssetGUID (元の.meshletdataファイルのGUID) や
                //    あるいは、runtimeInfo.sourceMeshletDataFilename が元のファイル名（GUIDなし）を指し、
                //    特定の検索パス（例： "Assets/GeneratedMeshletData/"）と組み合わせる必要がある。
                //
                //    ここでは、runtimeInfo.sourceAssetGUID が、インポートされたオリジナルの
                //    .meshletdata ファイルのGUIDを指していると仮定する。
                //    そして、runtimeInfo.runtimeMeshletDataFilename が StreamingAssets 内での最終的な名前。

                string originalBinaryAssetPath = null;
                if (!string.IsNullOrEmpty(runtimeInfo.sourceAssetGUID)) // MeshletDataRuntimeInfoに元のGUIDが保存されている場合
                {
                    originalBinaryAssetPath = AssetDatabase.GUIDToAssetPath(runtimeInfo.sourceAssetGUID);
                }


                if (string.IsNullOrEmpty(originalBinaryAssetPath) || !File.Exists(originalBinaryAssetPath))
                {
                    Debug.LogWarning(
                        $"Could not find the original binary file referenced by '{assetPath}'. Searched for: '{originalBinaryAssetPath}'. Skipping.");
                    continue;
                }

                // 3. StreamingAssets へのコピー先パスを決定
                //    runtimeInfo.runtimeMeshletDataFilename には、StreamingAssets内での最終的なファイル名
                //    (GUID付き、またはサブフォルダ構造を含む相対パス) が格納されている想定。
                string destFileName = runtimeInfo.runtimeMeshletDataFilename; // これが最終的な名前
                string destFilePath = Path.Combine(targetRootInStreamingAssets, destFileName);
                string destDirPath = Path.GetDirectoryName(destFilePath);

                if (!Directory.Exists(destDirPath))
                {
                    Directory.CreateDirectory(destDirPath);
                }

                try
                {
                    File.Copy(originalBinaryAssetPath, destFilePath, true); // 上書きコピー
                    copiedFilesCount++;
                    Debug.Log(
                        $"Copied '{originalBinaryAssetPath}' to '{destFilePath}' based on info from '{assetPath}'");
                }
                catch (IOException ex)
                {
                    Debug.LogError($"Error copying file '{originalBinaryAssetPath}' to '{destFilePath}': {ex.Message}");
                }
            }

            if (copiedFilesCount > 0)
            {
                AssetDatabase.Refresh(); // StreamingAssetsの変更をエディタに反映
                Debug.Log(
                    $"Finished copying {copiedFilesCount} meshlet binary files to '{targetRootInStreamingAssets}'.");
            }
            else
            {
                Debug.Log(
                    "No meshlet binary files were copied in this build preprocess step (either none found or errors occurred).");
            }
        }
    }
}