using UnityEditor;
using UnityEngine;

namespace UniMeshlet.Editor
{
    [CustomEditor(typeof(MeshletDataInfo))]
    public class MeshletDataInfoEditor : UnityEditor.Editor
    {
        private MeshletDataInfo _info;

        // styles を static にキャッシュするが、生成は OnInspectorGUI 内で行う
        private static GUIStyle _catStyle;   // 見出し (bold)
        private static GUIStyle _titleStyle; // タイトル (large bold)

        public override void OnInspectorGUI()
        {
            // ---- style が未生成ならここで作る（EditorStyles が確実に初期化済み） ----
            if (_catStyle == null)
            {
                _catStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            }
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.largeLabel)
                {
                    fontStyle  = FontStyle.Bold,
                    alignment  = TextAnchor.MiddleLeft
                };
            }

            // ---- 描画処理 ----
            _info = (MeshletDataInfo)target;

            GUI.enabled = false;  // 完全 Read-only

            DrawTitle("Meshlet Data Info");
            DrawSource();
            DrawRuntime();
            DrawBaked();
            DrawMeshlet();
            DrawHierarchy();

            GUI.enabled = true;
        }

        // --------------- セクション描画 ---------------

        private void DrawTitle(string t)
        {
            GUILayout.Space(2);
            EditorGUILayout.LabelField(t, _titleStyle);
            GUILayout.Space(4);
        }

        private void DrawCategory(string name)
        {
            GUILayout.Space(2);
            EditorGUILayout.LabelField(name, _catStyle);
        }

        private void DrawSource()
        {
            DrawCategory("Source File Information");
            EditorGUILayout.LabelField("Source Asset GUID",      _info.sourceAssetGUID);
            EditorGUILayout.LabelField(".meshlet Filename",      _info.sourceMeshletDataFilename);
            EditorGUILayout.IntField ("File Version",            _info.fileVersion);
        }

        private void DrawRuntime()
        {
            DrawCategory("Runtime File Information");
            EditorGUILayout.LabelField("StreamingAssets Folder", MeshletDataInfo.StreamingAssetsTargetSubfolder);
            EditorGUILayout.LabelField("Runtime Filename",       _info.runtimeMeshletDataFilename);
        }

        private void DrawBaked()
        {
            DrawCategory("Baked Mesh MetaData");
            EditorGUILayout.IntField ("Vertex Count", _info.bakedVertexCount);
            EditorGUILayout.EnumPopup("Index Format", _info.bakedMeshIndexFormat);
            EditorGUILayout.IntField ("Index Count",  _info.bakedIndexCount);
            EditorGUILayout.IntField ("Triangle Count",  _info.bakedIndexCount / 3);
            EditorGUILayout.Toggle  ("Has Normals",   _info.bakedMeshHasNormals);
            EditorGUILayout.Toggle  ("Has Tangents",  _info.bakedMeshHasTangents);
            EditorGUILayout.Toggle  ("Has UVs",       _info.bakedMeshHasUVs);
        }

        private void DrawMeshlet()
        {
            DrawCategory("Meshlet MetaData");
            EditorGUILayout.IntField ("Meshlet Count",            _info.meshletCount);
            EditorGUILayout.IntField ("Meshlet Vertices (total)", _info.totalMeshletVerticesCount);
            EditorGUILayout.IntField ("Triangles Bytes (total)",  _info.totalMeshletTrianglesByteCount);
            EditorGUILayout.IntField ("IndexTable uint Count",    _info.meshletIndexTableUIntCount);
            EditorGUILayout.EnumPopup("IndexTable Format",        _info.meshletTableFormat);
            EditorGUILayout.IntField ("Bounds Count",             _info.meshletBoundsCount);
        }

        private void DrawHierarchy()
        {
            if (!_info.hasHierarchy || _info.hierarchyInfoList == null) return;
            DrawCategory("Hierarchy MetaData");

            EditorGUILayout.IntField("Node Count (total)", _info.clusterNodeCount);

            int depth = _info.hierarchyInfoList.Count;
            EditorGUILayout.IntField("Depth (levels)", depth);

            // ---------- 各レベルのノード数 ----------
            EditorGUILayout.LabelField("Per-depth Node Counts", EditorStyles.miniBoldLabel);

            // 同じインデントでそろえる
            using (new EditorGUI.IndentLevelScope())
            {
                // ラベル幅を固定するとさらに整う
                float prevWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 70f;   // お好みで調整

                for (int i = 0; i < depth; i++)
                {
                    var h = _info.hierarchyInfoList[i];
                    // 1 行につき「Depth i」(ラベル) と ノード数(値) の 2 列
                    EditorGUILayout.LabelField($"Depth {i}", h.Count.ToString());
                }

                EditorGUIUtility.labelWidth = prevWidth;
            }
        }
    }

}