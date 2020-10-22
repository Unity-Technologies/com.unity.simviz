#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using SimViz.TestHelpers;
using UnityEngine.SimViz.Content.Controllers;
using UnityEngine.SimViz.Content.Data;

namespace UnityEngine.SimViz.Content.ContentTests
{
    public class MoverTests : SimvizTestBaseSetup
    {
        TestHelpers m_TestHelp = new TestHelpers();
        GameObject m_Cube;
        Vector3 m_CurrentPoint;
        WayPoint m_EndPoint;
        bool m_StillMoving = true;

        [TearDown]
        public void MoverTearDown()
        {
            m_StillMoving = true;
            Object.Destroy(m_Cube);
            Object.Destroy(GameObject.Find("Crossing8Course"));
            Object.Destroy(GameObject.Find("LakeMarcel"));
            Object.Destroy(GameObject.Find("CircleCourse"));
        }

        [UnityTest]
        public IEnumerator MoveAroundCircleCourse()
        {
            m_Cube = SetupMoverObjects(5f, "Assets/CircleCourse.xodr");

            while (m_StillMoving)
            {
                m_CurrentPoint = m_Cube.transform.position;
                if (Utilities.GeometryUtility.ApproximatelyEqual(m_CurrentPoint, m_EndPoint.position))
                {
                    m_StillMoving = false;
                }

                yield return null;
            }

            Assert.AreEqual(m_CurrentPoint, m_EndPoint.position, "Didn't make it to the end of the road");
        }

        [UnityTest]
        public IEnumerator MoveAroundLakeMarcel()
        {
            m_Cube = SetupMoverObjects(80f, "Assets/LakeMarcel.xodr");

            while (m_StillMoving)
            {
                m_CurrentPoint = m_Cube.transform.position;
                if (Utilities.GeometryUtility.ApproximatelyEqual(m_CurrentPoint, m_EndPoint.position))
                {
                    m_StillMoving = false;
                }

                yield return null;
            }

            Assert.AreEqual(m_CurrentPoint, m_EndPoint.position, "Didn't make it to the end of the road");
        }

        [UnityTest]
        public IEnumerator MoveAroundCrossing8Course()
        {
            m_Cube = SetupMoverObjects(200f, "Assets/Crossing8Course.xodr", true);

            while (m_StillMoving)
            {
                m_CurrentPoint = m_Cube.transform.position;
                if (Utilities.GeometryUtility.ApproximatelyEqual(m_CurrentPoint, m_EndPoint.position))
                {
                    m_StillMoving = false;
                }

                yield return null;
            }

            Assert.AreEqual(m_CurrentPoint, m_EndPoint.position, "Didn't make it to the end of the road");
        }

        [UnityTest]
        public IEnumerator MoveAroundCrossing8CoursePathLogging()
        {
            m_Cube = SetupMoverObjects(200f, "Assets/Crossing8Course.xodr", true);

            while (m_StillMoving)
            {
                m_CurrentPoint = m_Cube.transform.position;
                if (Utilities.GeometryUtility.ApproximatelyEqual(m_CurrentPoint, m_EndPoint.position))
                {
                    m_StillMoving = false;
                }

                yield return null;
            }

            Assert.AreEqual(m_CurrentPoint, m_EndPoint.position, "Didn't make it to the end of the road");
        }

        [UnityTest]
        public IEnumerator MoveAroundCrossing8CourseRayCastPosition()
        {
            m_Cube = SetupMoverObjects(200f, "Assets/Crossing8Course.xodr", true);

            while (m_StillMoving)
            {
                m_CurrentPoint = m_Cube.transform.position;
                if (Utilities.GeometryUtility.ApproximatelyEqual(m_CurrentPoint, m_EndPoint.position))
                {
                    m_StillMoving = false;
                }

                yield return null;
            }

            Assert.AreEqual(m_CurrentPoint, m_EndPoint.position, "Didn't make it to the end of the road");
        }

        [UnityTest]
        public IEnumerator MoveAroundCrossing8CourseCheckWheel()
        {
            m_Cube = SetupMoverObjects(200f, "Assets/Crossing8Course.xodr", true);

            while (m_StillMoving)
            {
                m_CurrentPoint = m_Cube.transform.position;
                if (Utilities.GeometryUtility.ApproximatelyEqual(m_CurrentPoint, m_EndPoint.position))
                {
                    m_StillMoving = false;
                }

                yield return null;
            }

            Assert.AreEqual(m_CurrentPoint, m_EndPoint.position, "Didn't make it to the end of the road");
        }

        GameObject SetupMoverObjects(float speed, string xodrFilePath, bool genRandomWayPoint = false)
        {
            if(!genRandomWayPoint)
                m_TestHelp.GenerateWayPointPathFromXodr(xodrFilePath);
            else
                m_TestHelp.GenerateRandomWayPointPathFromXodr(xodrFilePath);

            m_Cube = new GameObject("Mover");
            m_Cube.AddComponent<Camera>();
            m_Cube.AddComponent<Mover>();

            var xodrFile = xodrFilePath.Remove(0, xodrFilePath.LastIndexOf("/") + 1);
            var wayPointName = xodrFile.Substring(0, xodrFile.LastIndexOf("."));
            var wayPointPath = GameObject.Find(wayPointName).GetComponent<WayPointPath>();

            var mover = m_Cube.GetComponent<Mover>();
            mover.wayPointPath = wayPointPath;
            mover.speed = speed;
            m_EndPoint = wayPointPath.wayPoints[wayPointPath.wayPoints.Count - 1];

            return m_Cube;
        }
    }
}
#endif
