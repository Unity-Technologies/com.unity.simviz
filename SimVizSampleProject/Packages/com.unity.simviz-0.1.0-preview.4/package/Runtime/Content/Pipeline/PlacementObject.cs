using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.Pipeline
{
    public class PlacementObject
    {
        public GameObject Prefab { get; }
        Bounds[] m_BoundingBoxes;
        Bounds m_BoundingVolume;

        public PlacementObject(GameObject prefab)
        {
            Prefab = prefab;
            m_BoundingBoxes = GetBoundingBoxes(prefab);
            m_BoundingVolume = GetBoundingVolume(m_BoundingBoxes);
        }

        public bool CheckForCollidingObjects(
            float3 position,
            quaternion rotation,
            int layerMask,
            float scaleFactor = 1.0f)
        {
            var colliding = m_BoundingBoxes.Any(boundingBox => Physics.CheckBox(
                position + math.mul(rotation, boundingBox.center) * scaleFactor,
                boundingBox.extents * scaleFactor,
                rotation,
                layerMask));

            return colliding;
        }

        public void VisualizeBoundingBoxes(
            float3 position,
            quaternion rotation,
            float scaleFactor = 1.0f)
        {
            foreach (var bound in m_BoundingBoxes)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = position + math.mul(rotation, bound.center) * scaleFactor;
                cube.transform.rotation = rotation;
                cube.transform.localScale = bound.size * scaleFactor;
            }
        }

        static Bounds[] GetBoundingBoxes(GameObject prefab)
        {
            var renderComponents = prefab.GetComponentsInChildren<Renderer>();
            if (renderComponents.Length == 0) return new []{ new Bounds()};

            var boundingBoxes = new Bounds[renderComponents.Length];
            for (var i = 0; i < renderComponents.Length; i++)
            {
                boundingBoxes[i] = renderComponents[i].bounds;
            }
            return boundingBoxes;
        }

        static Bounds GetBoundingVolume(IEnumerable<Bounds> boundingBoxes)
        {
            var bounds = new Bounds();
            foreach (var boundingBox in boundingBoxes)
            {
                bounds.Encapsulate(boundingBox);
            }
            return bounds;
        }
    }
}
