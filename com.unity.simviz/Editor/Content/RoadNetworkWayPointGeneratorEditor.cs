using UnityEngine;
using UnityEngine.SimViz.Scenarios;

namespace UnityEditor.SimViz.Scenarios
{
    [CustomEditor(typeof(RoadNetworkWayPointGenerator))]
    public class RoadNetworkWayPointGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var rnp = (RoadNetworkWayPointGenerator)target;
            if(GUILayout.Button("Generate WayPoint Path"))
            {
                rnp.GeneratePathFromInspectorValues();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Randomize WayPoint Path"))
            {
                rnp.GenerateRandomizedPath();
                SceneView.RepaintAll();
            }
        }
    }
}
