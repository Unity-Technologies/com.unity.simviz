using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Data;

namespace UnityEngine.SimViz.Content.Controllers
{
    public class Mover : MonoBehaviour
    {
        [Header("Movement")]
        public WayPointPath wayPointPath;

        [Header("Parameters")]
        public float speed = 5f;

        public float m_PathIndex;
        public Vector3 m_TargetPosition;
        Quaternion m_TargetRotation;

        List<WayPoint> m_WayPoints;

        void Start()
        {
            if (wayPointPath == null)
            {
                Debug.LogError("Missing WayPointPath reference");
                enabled = false;
                return;
            }
            m_WayPoints = wayPointPath.wayPoints;
        }

        void Update()
        {
            RecalculateTarget();
            transform.position = m_TargetPosition;
            transform.rotation = m_TargetRotation;
        }

        void RecalculateTarget()
        {
            if (m_PathIndex >= m_WayPoints.Count - 1)
            {
                var lastWayPoint = m_WayPoints[m_WayPoints.Count - 1];
                m_TargetPosition = lastWayPoint.position;
                m_TargetRotation = lastWayPoint.rotation;
                return;
            }

            var distTraveled = Time.deltaTime * speed;
            while (m_PathIndex < m_WayPoints.Count - 1 &&
                !Utilities.GeometryUtility.ApproximatelyEqual(distTraveled, 0f))
            {
                var intIndex = (int)m_PathIndex;
                var point1 = m_WayPoints[intIndex];
                var point2 = m_WayPoints[intIndex + 1];

                var t = m_PathIndex - intIndex;
                var dist = math.distance(point1.position, point2.position);
                var remainingDist = dist * (1 - t);

                if (remainingDist < distTraveled)
                {
                    distTraveled -= remainingDist;
                    m_PathIndex = (int)m_PathIndex + 1;
                }
                else
                {
                    m_PathIndex += distTraveled / dist;
                    m_TargetPosition = Vector3.Lerp(point1.position, point2.position, t);
                    m_TargetRotation = Quaternion.Lerp(point1.rotation, point2.rotation, t);
                    break;
                }
            }
        }
    }
}
