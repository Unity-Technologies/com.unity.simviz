using System;
using System.Collections.Generic;

namespace UnityEngine.SimViz.Content.Data
{
    public class WayPointPath : MonoBehaviour
    {
        [SerializeField]
        public List<WayPoint> wayPoints = new List<WayPoint>();

        void OnDrawGizmosSelected()
        {
            if (wayPoints.Count < 2)
                return;
            for (var i = 0; i < wayPoints.Count - 1; i++)
            {
                Debug.DrawLine(wayPoints[i].position, wayPoints[i + 1].position);
            }
        }
    }
}
