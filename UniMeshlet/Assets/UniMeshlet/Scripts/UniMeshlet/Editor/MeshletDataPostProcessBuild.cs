using System.IO;
using UniMeshlet.Common;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UniMeshlet.Editor
{
    public class MeshletDataPostProcessBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        
        public void OnPostprocessBuild(BuildReport report)
        {
            string streamingAssetsTargetSubfolder = MeshletDataInfo.StreamingAssetsTargetSubfolder;
            string targetRootInStreamingAssets = Path.Combine(Application.streamingAssetsPath, streamingAssetsTargetSubfolder);
            if (Directory.Exists(targetRootInStreamingAssets))
            {
                FileUtil.DeleteFileOrDirectory(targetRootInStreamingAssets);
                FileUtil.DeleteFileOrDirectory(targetRootInStreamingAssets + ".meta");
                Debug.Log($"Deleted existing StreamingAssets subfolder: {targetRootInStreamingAssets}");
            }
        }
    }
}