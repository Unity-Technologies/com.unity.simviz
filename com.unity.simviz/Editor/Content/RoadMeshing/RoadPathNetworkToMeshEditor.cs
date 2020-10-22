using UnityEditor;
using UnityEngine;

namespace UnityEngine.SimViz.Content.RoadMeshing
{
    [CustomEditor(typeof(RoadPathNetworkToMesh))]
    public class RoadPathNetworkToMeshEditor : UnityEditor.Editor
    {
        [MenuItem("GameObject/SimViz/Road Path Network", false, 12)]
        public static void CreateRoadPathNetwork()
        {
            var roadNetworkObj = new GameObject("RoadPathNetwork");
            roadNetworkObj.AddComponent<RoadPathNetwork>();
            roadNetworkObj.AddComponent<RoadPathNetworkToMesh>();

            Undo.RegisterCreatedObjectUndo(roadNetworkObj, "Create " + roadNetworkObj.name);
            Selection.activeObject = roadNetworkObj;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var roadMeshTool = (RoadPathNetworkToMesh) target;

            if (GUILayout.Button("Build Road Mesh"))
            {
                roadMeshTool.CreateRoadFromInspector();
            }
        }
    }
}
