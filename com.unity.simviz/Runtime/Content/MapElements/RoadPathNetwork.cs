using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.SimViz.Content
{
    /// <summary>
    /// Defines a collection of RoadPaths
    /// </summary>
    public class RoadPathNetwork : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        internal List<GameObject> roadPaths = new List<GameObject>(8);

        public void OnValidate()
        {
            // Traverse backwards since we may be removing elements
            for (var idx = roadPaths.Count - 1; idx >= 0; --idx)
            {
                var roadPath = roadPaths[idx];
                if (roadPath == null)
                {
                    roadPaths.RemoveAt(idx);
                }
                else if (!roadPath.TryGetComponent<RoadPath>(out _))
                {
                    Debug.LogWarning(
                        $"{name}'s child path {roadPath.name} is missing its Road Path component. Removing from list.");
                    roadPaths.RemoveAt(idx);
                }
            }

            if (roadPaths.Count == 0)
            {
                Debug.Log($"Road Path Network {name} is empty. Adding a default Road Path");
                AddRoadPath();
            }
        }

        internal void AddRoadPath()
        {
            var newPath = new GameObject("Road Path");
            newPath.transform.parent = transform;
            var roadPathComponent = newPath.AddComponent<RoadPath>();
            roadPathComponent.Initialize();
            roadPaths.Add(newPath);
        }

        internal void DeleteRoadPath(int idx)
        {
            var pathObject = roadPaths[idx];
            roadPaths.RemoveAt(idx);
            DestroyImmediate(pathObject);
        }

        internal void Reset()
        {
            foreach (var roadPath in roadPaths)
            {
                DestroyImmediate(roadPath);
            }
            roadPaths.Clear();
            AddRoadPath();
        }

        public void SetRoadPaths(List<GameObject> paths)
        {
            for (var i = roadPaths.Count - 1; i >= 0; i--)
            {
                DeleteRoadPath(i);
            }
            roadPaths = paths;
            foreach (var path in paths)
            {
                path.transform.parent = transform;
            }
        }
    }
}
