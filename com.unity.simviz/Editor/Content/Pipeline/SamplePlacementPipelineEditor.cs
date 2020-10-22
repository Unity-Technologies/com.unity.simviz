using UnityEngine;
using UnityEngine.SimViz.Content.MapElements;
using UnityEngine.SimViz.Content.Pipeline.SamplePipelines;

#if UNITY_EDITOR
namespace UnityEditor.SimViz.Content.Pipeline
{
    [CustomEditor(typeof(SamplePlacementPipeline))]
    public class SamplePlacementPipelineEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var pipeline = (SamplePlacementPipeline) target;

            pipeline.roadNetworkSource = (SamplePlacementPipeline.RoadNetworkSource) EditorGUILayout.EnumPopup(
                "Road Network Source",
                pipeline.roadNetworkSource);
            pipeline.createRoadMesh = EditorGUILayout.Toggle(
                "Create Road Mesh",
                pipeline.createRoadMesh);

            if (pipeline.roadNetworkSource == SamplePlacementPipeline.RoadNetworkSource.OpenDrive)
            {
                pipeline.roadNetworkDescription = (RoadNetworkDescription) EditorGUILayout.ObjectField(
                    "Road Network Description",
                    pipeline.roadNetworkDescription,
                    typeof(RoadNetworkDescription),
                    false);
                pipeline.drawRoadOutline = EditorGUILayout.Toggle(
                    "Draw Road Outline",
                    pipeline.drawRoadOutline);
                pipeline.roadMaterial = (Material) EditorGUILayout.ObjectField(
                    "Road Material",
                    pipeline.roadMaterial,
                    typeof(Material),
                    false);
                pipeline.uvMultiplier = EditorGUILayout.FloatField("UV Multiplier", pipeline.uvMultiplier);
                pipeline.BorderLaneType = (LaneType)EditorGUILayout.EnumPopup(
                    new GUIContent(
                        "Border Lane Type",
                        "Specifies what OpenDrive lane attribute defines the outer edge of the road"),
                    pipeline.BorderLaneType);
                EditorGUILayout.Space();
            }
            else
            {
                pipeline.splineBasedRoadNetwork = (GameObject) EditorGUILayout.ObjectField(
                    "Spline Based Road Network",
                    pipeline.splineBasedRoadNetwork,
                    typeof(GameObject),
                    false);
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();

            EditorGUILayout.Space();

            if (GUILayout.Button("Run Pipeline"))
            {
                var createdObjects = pipeline.RunPipeline();
                if (createdObjects != null)
                    Undo.RegisterCreatedObjectUndo(createdObjects, "placement");
            }
        }
    }
}
#endif
