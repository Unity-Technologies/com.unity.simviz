

using UnityEngine.SimViz.Content.Utilities;
#if HDRP_PRESENT
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.RoadMeshing.Jobs;

namespace UnityEngine.SimViz.Content.RoadMeshing.Systems
{
    [Serializable]
    public class ManholeCoverPlacementParameters
    {
        public Material material;
        [Range(0.0f, 0.25f)]
        public float decalDensity;
        public uint randomSeed;

        [HideInInspector]
        public GameObject parentObject;

        [HideInInspector]
        public uint baseRandomSeed;
    }

    /// <summary>
    /// Scatters ManholeCover GameObjects around a road mesh
    /// </summary>
    [DisableAutoCreation]
    class ManholeCoverPlacementSystem : JobComponentSystem, IGeneratorSystem<ManholeCoverPlacementParameters>
    {
        public ManholeCoverPlacementParameters Parameters { get; set; }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (Utilities.GeometryUtility.ApproximatelyEqual(Parameters.decalDensity, 0f))
                return inputDeps;

            var roadEntities = GetEntityQuery(typeof(RoadCenterLineData))
                .ToEntityArrayAsync(Allocator.TempJob, out var queryHandle);
            var roadSampleBuffers = GetBufferFromEntity<RoadCenterLineSample>();
            var numDecalsPerRoad = new NativeList<int>(Allocator.TempJob);
            var decalStartIndices = new NativeList<int>(Allocator.TempJob);
            var decalPoses = new NativeList<RigidTransform>(Allocator.TempJob);

            var resizeNumDecalsArrayJob = new ResizeNumDecalsArrayJob
            {
                RoadEntities = roadEntities,
                NumDecalsPerRoad = numDecalsPerRoad
            }.Schedule(queryHandle);

            var calculateNumDecalsPerRoadJob = new CalculateNumDecalsPerRoadJob
            {
                Density = Parameters.decalDensity,
                NumDecalsPerRoad = numDecalsPerRoad.AsDeferredJobArray(),
                RoadEntities = roadEntities,
                RoadSampleBuffers = roadSampleBuffers
            }.Schedule(numDecalsPerRoad, 1, resizeNumDecalsArrayJob);

            var initializeDecalPositionsArrayJob = new InitializeDecalPositionsArrayJob
            {
                NumDecalsPerRoad = numDecalsPerRoad.AsDeferredJobArray(),
                DecalStartIndices = decalStartIndices,
                DecalPoses = decalPoses
            }.Schedule(calculateNumDecalsPerRoadJob);
            initializeDecalPositionsArrayJob.Complete();

            var spreadDecalsThroughoutRoadsJob = new SpreadDecalsThroughoutRoadsJob
            {
                RandomSeed = RandomUtility.CombineSeedWithBaseSeed(Parameters.baseRandomSeed, Parameters.randomSeed),
                DecalPoses = decalPoses.AsDeferredJobArray(),
                DecalStartIndices = decalStartIndices.AsDeferredJobArray(),
                RoadEntities = roadEntities,
                RoadSampleBuffers = roadSampleBuffers,
                Profiles = GetComponentDataFromEntity<LateralProfile>(),
                ProfileBuffers = GetBufferFromEntity<LateralProfileEntityRef>(),
                NumDecalsPerRoad = numDecalsPerRoad.AsDeferredJobArray(),
            }.Schedule(decalStartIndices, 1, initializeDecalPositionsArrayJob);
            spreadDecalsThroughoutRoadsJob.Complete();

            foreach (var pose in decalPoses)
            {
                var decalObj = new GameObject("ManholeCoverDecal");
                decalObj.transform.parent = Parameters.parentObject.transform;
                decalObj.transform.position = pose.pos + new float3(0, 0.25f, 0);
                decalObj.transform.rotation *= pose.rot;
                var projector = decalObj.AddComponent<DecalProjector>();
                projector.material = Parameters.material;
            }

            roadEntities.Dispose(spreadDecalsThroughoutRoadsJob);
            numDecalsPerRoad.Dispose(spreadDecalsThroughoutRoadsJob);
            decalStartIndices.Dispose(spreadDecalsThroughoutRoadsJob);
            decalPoses.Dispose(spreadDecalsThroughoutRoadsJob);

            return spreadDecalsThroughoutRoadsJob;
        }
    }
}

#endif
