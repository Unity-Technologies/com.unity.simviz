using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityTemplateProjects
{
    [CustomEditor(typeof(SaveMeshAssets))]
    public class SaveMeshAssetsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var roadMeshObj = (SaveMeshAssets)target;

            if (GUILayout.Button("Save Road Geometry"))
            {
                AssetDatabase.CreateFolder("Assets", "Exported Road Geometry");

                const string basePath = "Assets/Exported Road Geometry/";
                var count = 0;
                foreach (Transform child in roadMeshObj.transform)
                {
                    var filter = child.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        AssetDatabase.CreateAsset(filter.sharedMesh, basePath + child.gameObject.name + (count++) + ".mesh");
                }
            }
        }
    }
}
