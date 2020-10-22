using System;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.MapElements;
using UnityEngine.SimViz.Content.Pipeline.Systems;
using UnityEngine.SimViz.Content.RoadMeshing;

namespace UnityEngine.SimViz.Content.Pipeline.SamplePipelines
{
    [CreateAssetMenu(
        fileName = "SamplePlacementPipeline",
        menuName = "SimViz/Sample Placement Pipeline",
        order = 2)]
    public class SamplePlacementPipeline : ScriptableObject
    {
        public enum RoadNetworkSource
        {
            OpenDrive, SplineBased
        }

        [HideInInspector] public RoadNetworkSource roadNetworkSource;
        [HideInInspector] public RoadNetworkDescription roadNetworkDescription;

        // Used a GameObject Prefab instead of a direct reference to RoadPathNetworkToMesh due an issue with Unity
        // caching of old component references. This issue is discussed in the Unity forum post below:
        // https://forum.unity.com/threads/editing-prefab-causes-references-to-it-to-become-null-until-the-object-referencing-it-is-reloaded.568120/
        [HideInInspector] public GameObject splineBasedRoadNetwork;
        [HideInInspector] public bool createRoadMesh = true;
        [HideInInspector] public bool drawRoadOutline;
        [HideInInspector] public Material roadMaterial;
        [HideInInspector] public float uvMultiplier = 1;
        [HideInInspector] public LaneType BorderLaneType = LaneType.None;

        // bool m_UseEcsRoadNetwork = false;
        // public ProcessingScheme processingScheme;
        [Header("Placement Path Construction Options")]
        [Tooltip("Minimum area requirement for determining whether an area encapsulated by roads is considered by the placement algorithms")]
        [Range(0f, 1000f)]
        public float MinimumPolygonArea = 800f;
        [Tooltip("Configurable expansion of imported OpenDrive road geometry to fill in potential discontinuities between polygons")]
        [Range(0f, 10f)]
        public float ExtensionDistance = 0.1f;

        [Header("Roadside Building Placement")]
        public bool placeBuildings = true;
        public PlacementCategory buildingCategory;
        public float buildingSpacing;
        public float buildingOffset;
        public LayerMask buildingLayerMask;

        [Header("Roadside Tree Placement")]
        public bool placeTrees = true;
        public PlacementCategory treeCategory;
        public float treeSpacing;
        public float treeOffset;
        public LayerMask treeLayerMask;

        [Header("Roadside Street Light Placement")]
        public bool placeStreetLights;
        public PlacementCategory streetLightCategory;
        public float streetLightSpacing;
        public float streetLightOffset;
        public LayerMask streetLightLayerMask;

        [Header("Roadside Sign Placement")]
        public bool placeRoadSigns;
        public PlacementCategory roadSignCategory;
        public float roadSignSpacing;
        public float roadSignOffset;
        public LayerMask signLayerMask;

        [Header("Area Tree Placement")]
        public bool placePoissonTrees;
        public float poissonTreeSpacing;
        public float innerPoissonPolygonOffset;
        public float outerPoissonPolygonOffset;
        public LayerMask areaTreeLayerMask;

        bool RoadNetworkSourceExists()
        {
            if (roadNetworkSource == RoadNetworkSource.OpenDrive && roadNetworkDescription == null)
            {
                Debug.LogError("RoadNetworkSource is null");
                return false;
            }

            if (roadNetworkSource == RoadNetworkSource.SplineBased &&
                (splineBasedRoadNetwork == null ||
                splineBasedRoadNetwork.GetComponent<RoadPathNetworkToMesh>() == null))
            {
                Debug.LogError("SplineBasedRoadNetwork is null");
                return false;
            }

            return true;
        }

        GameObject RunOpenDrivePipeline(ContentPipeline pipeline)
        {
            var parentObject = new GameObject("Placed Road Objects");

//            if (useEcsRoadNetwork)
//            {
//                pipeline.RunGenerator<RoadNetworkDescriptionToEcsSystem, RoadNetworkDescriptionToEcsSystemParameters>(new RoadNetworkDescriptionToEcsSystemParameters()
//                {
//                    roadNetworkDescription = roadNetworkDescription
//                });
//                pipeline.RunGenerator<GeneratePolygonsFromEcsSystem, PolygonSystemFromEcsParameters>(new PolygonSystemFromEcsParameters()
//                {
//                    minimumPolygonArea = MinimumPolygonArea,
//                    extensionDistance = ExtensionDistance,
//                    processingScheme = processingScheme
//                });
//            }
//            else
//            {
                pipeline.RunGenerator<PlacementPathsFromPolygonsSystem, PolygonSystemParameters>(new PolygonSystemParameters()
                {
                    roadNetworkDescription = roadNetworkDescription,
                    minimumPolygonArea = MinimumPolygonArea,
                    extensionDistance = ExtensionDistance,
                    outermostLaneType = BorderLaneType
                });
//            }

            pipeline.RunGenerator<MeshFromPolygonsSystem, MeshFromPolygonsParameters>(new MeshFromPolygonsParameters()
            {
                Material = roadMaterial,
                Parent = parentObject.transform,
                UVMultiplier = uvMultiplier
            });

            if (createRoadMesh)
            {
                pipeline.RunGenerator<MeshFromPolygonsSystem, MeshFromPolygonsParameters>(new MeshFromPolygonsParameters()
                {
                    Material = roadMaterial,
                    Parent = parentObject.transform,
                    UVMultiplier = uvMultiplier
                });
            }

            if (drawRoadOutline)
            {
                pipeline.RunGenerator<PolygonDrawingSystem, PolygonDrawingParameters>(new PolygonDrawingParameters
                {
                    parent = parentObject.transform
                });
            }

            ExecutePlacementAlgorithms(pipeline, parentObject);

            return parentObject;
        }

        GameObject RunSplineBasedRoadNetworkPipeline(ContentPipeline pipeline)
        {
            var parentObject = new GameObject("Placed Road Objects");
            var roadObject = new GameObject("Road");
            var networkToMesh = splineBasedRoadNetwork.GetComponent<RoadPathNetworkToMesh>();
            networkToMesh.generateRoadMesh = createRoadMesh;
            networkToMesh.GenerateRoadMesh(pipeline, roadObject);
            ExecutePlacementAlgorithms(pipeline, parentObject);
            roadObject.transform.parent = parentObject.transform;
            return parentObject;
        }

        void ExecutePlacementAlgorithms(ContentPipeline pipeline, GameObject parentObject)
        {
            if (placeBuildings)
            {
                pipeline.RunGenerator<UniformPlacementSystem, PlacementSystemParameters>(new PlacementSystemParameters
                {
                    spacing = buildingSpacing,
                    category = buildingCategory,
                    collisionLayerMask = buildingLayerMask.value,
                    parent = parentObject.transform,
                    offsetFromPath = buildingOffset
                });
            }

            if (placeTrees)
            {
                pipeline.RunGenerator<UniformPlacementSystem, PlacementSystemParameters>(new PlacementSystemParameters
                {
                    spacing = treeSpacing,
                    category = treeCategory,
                    collisionLayerMask = treeLayerMask.value,
                    parent = parentObject.transform,
                    offsetFromPath = treeOffset
                });
            }

            if (placeStreetLights)
            {
                pipeline.RunGenerator<UniformPlacementSystem, PlacementSystemParameters>(new PlacementSystemParameters
                {
                    spacing = streetLightSpacing,
                    category = streetLightCategory,
                    collisionLayerMask = streetLightLayerMask.value,
                    parent = parentObject.transform,
                    offsetFromPath = streetLightOffset
                });
            }

            if (placeRoadSigns)
            {
                pipeline.RunGenerator<UniformPlacementSystem, PlacementSystemParameters>(new PlacementSystemParameters
                {
                    spacing = roadSignSpacing,
                    category = roadSignCategory,
                    collisionLayerMask = signLayerMask.value,
                    parent = parentObject.transform,
                    offsetFromPath = roadSignOffset,
                    rotationFromPath = quaternion.RotateY(math.PI)
                });
            }

            if (placePoissonTrees)
            {
                pipeline.RunGenerator<PoissonDiscSamplingPlacementSystem, PoissonPlacementSystemParameters>(
                    new PoissonPlacementSystemParameters
                {
                    spacing = poissonTreeSpacing,
                    category = treeCategory,
                    collisionLayerMask = areaTreeLayerMask.value,
                    parent = parentObject.transform,
                    innerOffsetFromPath = innerPoissonPolygonOffset,
                    outerOffsetFromPath = outerPoissonPolygonOffset
                });
            }
        }

        public GameObject RunPipeline()
        {
            if (!RoadNetworkSourceExists())
                return null;

            var pipeline = new ContentPipeline();
            GameObject generatedObjects;

            try
            {
                generatedObjects = roadNetworkSource == RoadNetworkSource.OpenDrive
                    ? RunOpenDrivePipeline(pipeline)
                    : RunSplineBasedRoadNetworkPipeline(pipeline);
            }
            catch
            {
                pipeline.Dispose();
                throw;
            }

            pipeline.Dispose();
            return generatedObjects;
        }
    }
}
