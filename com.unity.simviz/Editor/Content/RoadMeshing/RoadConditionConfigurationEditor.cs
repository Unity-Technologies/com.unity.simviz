using System;
using UnityEditor;
using UnityEngine.SimViz.Content.RoadMeshing.RoadCondition;

namespace UnityEngine.SimViz.Content.RoadMeshing.MaterialConfiguration
{
    [CustomEditor(typeof(RoadConditionConfiguration))]
    public class RoadConditionConfigurationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var config = (RoadConditionConfiguration)target;
            if (Application.isPlaying) {
                if (GUILayout.Button("Apply Material Parameters"))
                {
                    config.ApplyParameters();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "You can assign road condition parameters manually while in play mode", MessageType.Info);
            }
        }
    }
}
