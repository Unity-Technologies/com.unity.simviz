using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.RoadMeshing.Jobs;
using UnityEngine.SimViz.Content.Sampling;

namespace UnityEngine.SimViz.Content.RoadMeshing.Systems
{
    [DisableAutoCreation]
    class CreateRoadEdgePathsSystem : ComponentSystem, IGeneratorSystem
    {
        EntityArchetype m_PlacementPathArchetype;

        protected override void OnCreate()
        {
            m_PlacementPathArchetype = EntityManager.CreateArchetype(typeof(PointSampleGlobal));
        }

        protected override void OnUpdate()
        {
            var transaction = EntityManager.BeginExclusiveEntityTransaction();

            var roadEntities = EntityManager.CreateEntityQuery(typeof(RoadCenterLineData))
                .ToEntityArrayAsync(Allocator.TempJob, out var queryHandle);
            var placementPathEntities = new NativeList<Entity>(Allocator.TempJob);
            var placementPathSamples = new NativeList<RigidTransform>(Allocator.TempJob);
            var placementPathSampleRanges = new NativeList<PlacementPathSampleRange>(Allocator.TempJob);

            var traverseRoadEdges = new TraverseRoadEdgesJob
            {
                RoadEntities = roadEntities,
                PlacementPathSamples = placementPathSamples,
                PlacementPathSampleRanges = placementPathSampleRanges,
                IntersectionCornerComponents = GetComponentDataFromEntity<IntersectionCorner>(true),
                IntersectionRoadConnections = GetBufferFromEntity<IntersectionRoadConnection>(true),
                IntersectionCornerEntityRefs = GetBufferFromEntity<IntersectionCornerEntityRef>(true),
                RoadCenterLineSamples = GetBufferFromEntity<RoadCenterLineSample>(true),
                RoadCenterLineDataComponents = GetComponentDataFromEntity<RoadCenterLineData>(true),
                LateralProfileEntityRefs = GetBufferFromEntity<LateralProfileEntityRef>(true),
                LateralProfileSampleBuffers = GetBufferFromEntity<LateralProfileSample>(true)
            }.Schedule(queryHandle);

            var createPlacementPathEntitiesJob = new CreatePlacementPathEntitiesJob
            {
                Transaction = transaction,
                PlacementPathArchetype = m_PlacementPathArchetype,
                PlacementPathEntities = placementPathEntities,
                PlacementPathSampleRanges = placementPathSampleRanges.AsDeferredJobArray()
            }.Schedule(traverseRoadEdges);

            var initializePlacementPathsJob = new InitializePlacementPathsJob
            {
                PlacementPathEntities = placementPathEntities,
                Samples = placementPathSamples.AsDeferredJobArray(),
                PlacementPathSampleBuffers = GetBufferFromEntity<PointSampleGlobal>(),
                PlacementPathSampleRanges = placementPathSampleRanges.AsDeferredJobArray()
            }.Schedule(placementPathSampleRanges, 2, createPlacementPathEntitiesJob);

            roadEntities.Dispose(initializePlacementPathsJob);
            placementPathSamples.Dispose(initializePlacementPathsJob);
            placementPathEntities.Dispose(initializePlacementPathsJob);
            placementPathSampleRanges.Dispose(initializePlacementPathsJob);

            EntityManager.ExclusiveEntityTransactionDependency = initializePlacementPathsJob;
            EntityManager.EndExclusiveEntityTransaction();
        }
    }
}
