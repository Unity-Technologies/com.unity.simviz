using System;
using UnityEngine.Profiling;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.MaterialConfiguration;
using UnityEngine.SimViz.Content.RoadMeshing.RoadCondition;
using UnityEngine.SimViz.Content.RoadMeshing.Systems;

namespace UnityEngine.SimViz.Content.RoadMeshing
{
    [RequireComponent(typeof(RoadPathNetwork))]
    public class RoadPathNetworkToMesh : MonoBehaviour
    {
        public RoadMaterialParameters materialParams = new RoadMaterialParameters();
        public RoadBuilderParameters roadBuilderParameters = new RoadBuilderParameters();
        public DebugDrawingParameters debugDrawingParameters = new DebugDrawingParameters();
        public CameraPathParameters cameraPathParameters = new CameraPathParameters();

#if HDRP_PRESENT
        public ManholeCoverPlacementParameters manholeCoverPlacementParameters = new ManholeCoverPlacementParameters();
#endif
        public uint baseRandomSeed;
        [Tooltip("Toggling this option will prevent road mesh geometry from being generated, " +
            "though debug visualizations and way point paths will still be generated")]
        public bool generateRoadMesh = true;

        RoadPathNetwork m_RoadPathNetwork;

        public void CreateRoadFromInspector()
        {
            if (roadBuilderParameters.parentObject != null)
                DestroyImmediate(roadBuilderParameters.parentObject);

            var parentObject = new GameObject("Road Mesh");
            using (var pipeline = new ContentPipeline())
            {
                GenerateRoadMesh(pipeline, parentObject);
            }
        }

        public void GenerateRoadMesh(
            ContentPipeline pipeline,
            GameObject parentObject,
            RoadConditionConfiguration roadConditionConfig = null)
        {
            Profiler.BeginSample("GenerateRoadMesh");
            m_RoadPathNetwork = GetComponent<RoadPathNetwork>();

            materialParams.parentObject = parentObject;
            roadBuilderParameters.parentObject = parentObject;
            debugDrawingParameters.parentObject = parentObject;
            cameraPathParameters.parentObject = parentObject;

            roadBuilderParameters.baseRandomSeed = baseRandomSeed;

            var roadPathParams = new RoadPathNetworkToEcsParameters { network = m_RoadPathNetwork };

            // TODO: Validate roadBuilderParameter inputs
            pipeline.RunGenerator<RoadPathNetworkToEcsSystem, RoadPathNetworkToEcsParameters>(roadPathParams);
            pipeline.RunGenerator<RoadBuilderSystem, RoadBuilderParameters>(roadBuilderParameters);
            if (generateRoadMesh)
                pipeline.RunGenerator<AssembleMeshSystem, RoadMaterialParameters>(materialParams);
            pipeline.RunGenerator<CreateRoadEdgePathsSystem>();
            pipeline.RunGenerator<CameraPathSystem, CameraPathParameters>(cameraPathParameters);
            pipeline.RunGenerator<DebugDrawingSystem, DebugDrawingParameters>(debugDrawingParameters);

#if HDRP_PRESENT
            manholeCoverPlacementParameters.parentObject = parentObject;
            manholeCoverPlacementParameters.baseRandomSeed = roadBuilderParameters.baseRandomSeed;
            pipeline.RunGenerator<ManholeCoverPlacementSystem, ManholeCoverPlacementParameters>(manholeCoverPlacementParameters);
#endif

            parentObject.AddComponent<RoadConditionAssigner>();
            Profiler.EndSample();
        }
    }
}
