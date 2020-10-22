using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Data;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.RoadMeshing.Jobs;

namespace UnityEngine.SimViz.Content.RoadMeshing.Systems
{
    [Serializable]
    public class CameraPathParameters
    {
        public float offsetFromCurb = 1.5f;
        [HideInInspector] public GameObject parentObject;
    }

    [DisableAutoCreation]
    public class CameraPathSystem : ComponentSystem, IGeneratorSystem<CameraPathParameters>
    {
        EntityArchetype m_PlacementPathArchetype;

        public CameraPathParameters Parameters { get; set; }

        protected override void OnCreate()
        {
            m_PlacementPathArchetype = EntityManager.CreateArchetype(typeof(CameraPathSample));
        }

        protected override void OnUpdate()
        {
            var transaction = EntityManager.BeginExclusiveEntityTransaction();

            var roadEntities = EntityManager.CreateEntityQuery(typeof(RoadCenterLineData))
                .ToEntityArrayAsync(Allocator.TempJob, out var queryHandle);
            var cameraPathEntities = new NativeList<Entity>(Allocator.TempJob);
            var cameraPathSamples = new NativeList<RigidTransform>(Allocator.TempJob);
            var cameraPathSampleRanges = new NativeList<PlacementPathSampleRange>(Allocator.TempJob);
            var longestPath = new NativeArray<Entity>(1, Allocator.TempJob);

            var traverseCurbEdgesJob = new TraverseCurbEdgesJob
            {
                OffsetFromCurb = Parameters.offsetFromCurb,
                RoadEntities = roadEntities,
                CameraPathSamples = cameraPathSamples,
                CameraPathSampleRanges = cameraPathSampleRanges,
                IntersectionCornerComponents = GetComponentDataFromEntity<IntersectionCorner>(true),
                IntersectionRoadConnections = GetBufferFromEntity<IntersectionRoadConnection>(true),
                IntersectionCornerEntityRefs = GetBufferFromEntity<IntersectionCornerEntityRef>(true),
                RoadCenterLineSamples = GetBufferFromEntity<RoadCenterLineSample>(true),
                RoadCenterLineDataComponents = GetComponentDataFromEntity<RoadCenterLineData>(true),
                LateralProfileComponents = GetComponentDataFromEntity<LateralProfile>(true),
                LateralProfileEntityRefs = GetBufferFromEntity<LateralProfileEntityRef>(true),
                LateralProfileSampleBuffers = GetBufferFromEntity<LateralProfileSample>(true)
            }.Schedule(queryHandle);

            var createCameraPathEntitiesJob = new CreateCameraPathEntitiesJob
            {
                Transaction = transaction,
                CameraPathArchetype = m_PlacementPathArchetype,
                CameraPathEntities = cameraPathEntities,
                CameraPathSampleRanges = cameraPathSampleRanges.AsDeferredJobArray()
            }.Schedule(traverseCurbEdgesJob);

            var initializeCameraPathsJob = new InitializeCameraPathsJob
            {
                CameraPathEntities = cameraPathEntities,
                Samples = cameraPathSamples.AsDeferredJobArray(),
                CameraPathSampleBuffers = GetBufferFromEntity<CameraPathSample>(),
                CameraPathSampleRanges = cameraPathSampleRanges.AsDeferredJobArray()
            }.Schedule(cameraPathSampleRanges, 2, createCameraPathEntitiesJob);

            var getLongestCameraPathJob = new GetLongestCameraPathJob
            {
                LongestPath = longestPath,
                CameraPathEntities = cameraPathEntities.AsDeferredJobArray(),
                CameraPathSampleBuffers = GetBufferFromEntity<CameraPathSample>(true)
            }.Schedule(initializeCameraPathsJob);

            roadEntities.Dispose(getLongestCameraPathJob);
            cameraPathSamples.Dispose(getLongestCameraPathJob);
            cameraPathEntities.Dispose(getLongestCameraPathJob);
            cameraPathSampleRanges.Dispose(getLongestCameraPathJob);

            EntityManager.ExclusiveEntityTransactionDependency = getLongestCameraPathJob;
            EntityManager.EndExclusiveEntityTransaction();

            if (longestPath[0] != Entity.Null)
            {
                var sampleBuffer = EntityManager.GetBuffer<CameraPathSample>(longestPath[0])
                    .Reinterpret<WayPoint>().AsNativeArray();
                var cameraPathObj = new GameObject("CameraPath");
                cameraPathObj.transform.parent = Parameters.parentObject.transform;
                var wayPointPath = cameraPathObj.AddComponent<WayPointPath>();
                var wayPoints = wayPointPath.wayPoints;
                wayPoints.AddRange(sampleBuffer);
            }
            longestPath.Dispose();
        }
    }
}
