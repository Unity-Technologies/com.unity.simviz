using NUnit.Framework;
using UnityEngine.SimViz.Scenarios;
using UnityEngine.SimViz.Content.MapElements;
using SimViz.TestHelpers;
using UnityEngine.SimViz.Content.Data;

namespace UnityEngine.SimViz.Content.ContentTests
{
    public class WayPointPathTests : SimvizTestBaseSetup
    {
        TestHelpers m_TestHelp = new TestHelpers();
        GameObject m_WayPointObj;
        RoadNetworkDescription m_TestRoad;

        [SetUp]
        public void WayPointPathTestsSetUp()
        {
            m_WayPointObj = new GameObject("TestWayPointPath", typeof(WayPointPath));
        }

        [TearDown]
        public void WayPointPathTestsTearDown()
        {
            Object.Destroy(m_WayPointObj);
        }

        //uses editor-only apis in TestHelper
#if UNITY_EDITOR
        [Test, TestCaseSource(typeof(XodrTestData), "TestCases")]
        public void GenerateRandomWayPointPathFromXodr(string file)
        {
            m_TestHelp.GetTestRoadNetwork(file, out m_TestRoad);

            var root = new GameObject($"{m_TestRoad.name}");
            var reference = root.AddComponent<RoadNetworkReference>();
            var roadNetworkPath = root.AddComponent<RoadNetworkWayPointGenerator>();
            root.AddComponent<WayPointPath>();

            reference.RoadNetwork = m_TestRoad;
            roadNetworkPath.RoadNetwork = m_TestRoad;

            Assert.IsNotNull(root.name, "Failed to create the Way point Path using the Xodr File");

            if (!m_TestRoad.name.Contains("Circle"))
                roadNetworkPath.GenerateRandomizedPath();
            else
                Assert.Ignore("{0}Road Network doesn't contain enough points for random path generation", m_TestRoad.name);

            var pointCount = root.GetComponent<WayPointPath>().wayPoints.Count;

            Assert.AreNotEqual(" ", roadNetworkPath.RoadId, "Road Id's are blank");
            Assert.AreNotEqual(0, roadNetworkPath.LaneId, "Lane ID is 0");
            Assert.Greater(pointCount, 0, "Way point path doesn't have any segments");
        }

        [Test, TestCaseSource(typeof(XodrTestData), "TestCases")]
        public void GenerateNewWayPointPathFromXodr(string file)
        {
            m_TestHelp.GetTestRoadNetwork(file, out m_TestRoad);

            var root = new GameObject($"{m_TestRoad.name}");
            var reference = root.AddComponent<RoadNetworkReference>();
            var roadNetworkPath = root.AddComponent<RoadNetworkWayPointGenerator>();

            root.AddComponent<WayPointPath>();

            reference.RoadNetwork = m_TestRoad;
            roadNetworkPath.RoadNetwork = m_TestRoad;

            Assert.IsNotNull(root.name, "Failed to create the Way point Path using the Xodr File");

            roadNetworkPath.GenerateNewWayPointPath();

            var pointCount = root.GetComponent<WayPointPath>().wayPoints.Count;

            Assert.AreNotEqual(" ", roadNetworkPath.RoadId, "Road Id's are blank");
            Assert.AreNotEqual(0, roadNetworkPath.LaneId, "Lane ID is 0");
            Assert.Greater(pointCount, 0, "Way point path doesn't have any segments");
        }
#endif
    }
}
