using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityEngine.SimViz.Content
{
    [CustomEditor(typeof(RoadPathNetwork))]
    public class RoadPathNetworkEditor : UnityEditor.Editor
    {
        private bool _foldoutIsOpen;
        private RoadPathNetwork _roadPathNetwork;

        public void OnEnable()
        {
            _roadPathNetwork = (RoadPathNetwork) target;
        }

        // TODO: Undo recording only "kind of" works here... creation/destruction of game objects isn't recorded
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var numRoadPaths = _roadPathNetwork.roadPaths.Count;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Road Path"))
            {
                Undo.RecordObject(_roadPathNetwork, "add path");
                _roadPathNetwork.AddRoadPath();
            }

            if (GUILayout.Button("Reset All"))
            {
                Undo.RecordObject(_roadPathNetwork, "reset");
                _roadPathNetwork.Reset();
            }

            EditorGUILayout.EndHorizontal();
            _foldoutIsOpen = EditorGUILayout.Foldout(_foldoutIsOpen, $"Road Paths ({numRoadPaths})");
            if (_foldoutIsOpen)
            {
                var shouldValidate = false;
                for (var roadPathIdx = 0; roadPathIdx < _roadPathNetwork.roadPaths.Count; ++roadPathIdx)
                {
                    var roadPath = _roadPathNetwork.roadPaths[roadPathIdx];
                    if (roadPath == null)
                    {
                        shouldValidate = true;
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(roadPath, typeof(GameObject), true);
                    if (GUILayout.Button("Delete"))
                    {
                        Undo.RecordObject(_roadPathNetwork, "delete path");
                        _roadPathNetwork.DeleteRoadPath(roadPathIdx);
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (shouldValidate)
                {
                    _roadPathNetwork.OnValidate();
                }
            }

        }

        public void OnSceneGUI()
        {
            // TODO: Figure out a way to draw simple representation of all managed Road Paths
        }
    }
}
