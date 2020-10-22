using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PathCreation;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.Pipeline.Components;
using UnityEngine.SimViz.Content.Pipeline.Systems;
using UnityEngine.SimViz.Content.RoadMeshing;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Sampling;
using UnityEngine.SimViz.Content.Utilities;
using UnityEngine.TestTools;

namespace UnityEngine.SimViz.Content.ContentTests
{
#if UNITY_EDITOR
    [PrebuildSetup(typeof(ContentPipelineTests))]
    public class ContentPipelineTests
    {
        ContentPipeline m_Pipeline;
        List<Object> m_TearDownObjects;

        static GameObject CreateRoadPath(IEnumerable<Vector3> path)
        {
            var roadPathObj = new GameObject("RoadPath");
            var roadPath = roadPathObj.AddComponent<RoadPath>();
            var bezierPath = new BezierPath(path) { ControlPointMode = BezierPath.ControlMode.Free };
            bezierPath.ControlPointMode = BezierPath.ControlMode.Automatic;
            roadPath.bezierPath = bezierPath;
            return roadPathObj;
        }

        static GameObject CreateTestRoadPathNetwork(List<GameObject> roadPaths)
        {
            var roadPathNetworkObj = new GameObject("Road Network");
            var roadPathNetwork = roadPathNetworkObj.AddComponent<RoadPathNetwork>();
            roadPathNetwork.SetRoadPaths(roadPaths);
            roadPathNetworkObj.AddComponent<RoadPathNetworkToMesh>();
            return roadPathNetworkObj;
        }

        void GenerateRoadEcsDataAndMesh(List<GameObject> roadPaths, GameObject parentObj)
        {
            var roadPathNetwork = CreateTestRoadPathNetwork(roadPaths);
            m_TearDownObjects.Add(roadPathNetwork);
            var networkToMesh = roadPathNetwork.GetComponent<RoadPathNetworkToMesh>();
            networkToMesh.GenerateRoadMesh(m_Pipeline, parentObj);
        }

        void TestPoissonPlacement(GameObject parentObj)
        {
            var placementCategory = ScriptableObject.CreateInstance<PlacementCategory>();
            m_TearDownObjects.Add(placementCategory);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_TearDownObjects.Add(cube);

            placementCategory.prefabs = new []{ cube };

            var poissonParams = new PoissonPlacementSystemParameters()
            {
                spacing = 10f,
                category = placementCategory,
                parent = parentObj.transform,
                innerOffsetFromPath = 10f,
                outerOffsetFromPath = 100f
            };
            m_Pipeline.RunGenerator<PoissonDiscSamplingPlacementSystem, PoissonPlacementSystemParameters>(poissonParams);
        }

        [SetUp]
        public void SetUp()
        {
            m_TearDownObjects = new List<Object>();
            m_Pipeline = new ContentPipeline();
        }

        [TearDown]
        public void TearDown()
        {
            m_Pipeline.Dispose();
            foreach (var obj in m_TearDownObjects)
            {
                Object.DestroyImmediate(obj);
            }
            m_TearDownObjects.Clear();
        }

        [UnityTest]
        public IEnumerator FourWayStopTest()
        {
            var roadPaths = new List<GameObject>
            {
                CreateRoadPath(new List<Vector3>
                {
                    new Vector3(0f, 0f, -40f), new Vector3(0f, 0f, 40f)
                }),
                CreateRoadPath(new List<Vector3>
                {
                    new Vector3(-40f, 0f, 0f), new Vector3(40f, 0f, 0f)
                }),
            };

            var parentObj = new GameObject("Road Mesh");
            GenerateRoadEcsDataAndMesh(roadPaths, parentObj);

            var manager = m_Pipeline.World.EntityManager;
            var roadCount = manager.CreateEntityQuery(typeof(RoadCenterLineData)).CalculateEntityCount();
            var intersectionCount = manager.CreateEntityQuery(typeof(IntersectionData)).CalculateEntityCount();
            var cornerCount = manager.CreateEntityQuery(typeof(IntersectionCorner)).CalculateEntityCount();
            var placementPathCount = manager.CreateEntityQuery(typeof(PointSampleGlobal)).CalculateEntityCount();

            Assert.AreEqual(4, roadCount);
            Assert.AreEqual(1, intersectionCount);
            Assert.AreEqual(4, cornerCount);
            Assert.AreEqual(1, placementPathCount);

            TestPoissonPlacement(parentObj);
            var poissonRegionCount = manager.CreateEntityQuery(typeof(PoissonPointRegion)).CalculateEntityCount();
            Assert.AreEqual(1, poissonRegionCount);

            yield return null;
        }


        [UnityTest]
        public IEnumerator SingleSelfIntersectionTest()
        {
            var roadPaths = new List<GameObject>
            {
                CreateRoadPath(new List<Vector3>
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 400f),
                    new Vector3(200f, 0f, 400f), new Vector3(200f, 0f, 200f),
                    new Vector3(-200f, 0f, 200f)
                })
            };

            var parentObj = new GameObject("Road Mesh");
            GenerateRoadEcsDataAndMesh(roadPaths, parentObj);

            var manager = m_Pipeline.World.EntityManager;
            var roadCount = manager.CreateEntityQuery(typeof(RoadCenterLineData)).CalculateEntityCount();
            var intersectionCount = manager.CreateEntityQuery(typeof(IntersectionData)).CalculateEntityCount();
            var cornerCount = manager.CreateEntityQuery(typeof(IntersectionCorner)).CalculateEntityCount();
            var placementPathCount = manager.CreateEntityQuery(typeof(PointSampleGlobal)).CalculateEntityCount();

            Assert.AreEqual(3, roadCount);
            Assert.AreEqual(1, intersectionCount);
            Assert.AreEqual(4, cornerCount);
            Assert.AreEqual(2, placementPathCount);

            TestPoissonPlacement(parentObj);
            var poissonRegionCount = manager.CreateEntityQuery(typeof(PoissonPointRegion)).CalculateEntityCount();
            Assert.AreEqual(1, poissonRegionCount);

            yield return null;
        }
    }
#endif
}
