using System;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.Data
{
    [Serializable]
    public struct WayPoint
    {
        public Quaternion rotation;
        public Vector3 position;

        public static explicit operator WayPoint(RigidTransform transform) => new WayPoint
        {
            rotation = transform.rot,
            position = transform.pos
        };
    }
}
