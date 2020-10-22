using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Sampling;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing.Jobs
{
    /// <summary>
    /// Traverses the graph of roads and intersection corners to identify the street side edge paths of the road network.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct TraverseCurbEdgesJob : IJob
    {
        public float OffsetFromCurb;
        public NativeList<RigidTransform> CameraPathSamples;
        public NativeList<PlacementPathSampleRange> CameraPathSampleRanges;

        [ReadOnly] public NativeArray<Entity> RoadEntities;

        [ReadOnly] public ComponentDataFromEntity<RoadCenterLineData> RoadCenterLineDataComponents;
        [ReadOnly] public ComponentDataFromEntity<IntersectionCorner> IntersectionCornerComponents;
        [ReadOnly] public ComponentDataFromEntity<LateralProfile> LateralProfileComponents;
        [ReadOnly] public BufferFromEntity<IntersectionRoadConnection> IntersectionRoadConnections;
        [ReadOnly] public BufferFromEntity<IntersectionCornerEntityRef> IntersectionCornerEntityRefs;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> RoadCenterLineSamples;

        [ReadOnly] public BufferFromEntity<LateralProfileEntityRef> LateralProfileEntityRefs;
        [ReadOnly] public BufferFromEntity<LateralProfileSample> LateralProfileSampleBuffers;

        static Entity GetIntersection(RoadCenterLineData data, IntersectionRoadDirection direction)
        {
            return direction == IntersectionRoadDirection.Outgoing ? data.EndIntersection : data.StartIntersection;
        }

        Entity GetCorner(IntersectionRoadConnection connection, Entity intersection)
        {
            var intersectionRoads = IntersectionRoadConnections[intersection].AsNativeArray();

            var connectionIndex = 0;
            for (; connectionIndex < intersectionRoads.Length; connectionIndex++)
            {
                var intersectionRoad = intersectionRoads[connectionIndex];
                if (intersectionRoad.Road == connection.Road &&
                    intersectionRoad.InverseDirection == connection.Direction) break;
            }

            return IntersectionCornerEntityRefs[intersection].Reinterpret<Entity>()[connectionIndex];
        }

        void AppendRightTurnCornerSamples(NativeList<RigidTransform> samples, Entity cornerEntity)
        {
            var cornerSamples = RoadCenterLineSamples[cornerEntity]
                .Reinterpret<RigidTransform>().AsNativeArray();

            var offset = new float3(-OffsetFromCurb, 0f, 0f);

            var offsetSamples = SplineUtility.OffsetSpline(cornerSamples, offset, Allocator.Temp);
            samples.AddRange(offsetSamples);
            offsetSamples.Dispose();
        }

        void AppendRoadSamples(NativeList<RigidTransform> samples, IntersectionRoadConnection connection)
        {
            var centerLineSamples = RoadCenterLineSamples[connection.Road]
                .Reinterpret<RigidTransform>().AsNativeArray();

            var lateralProfileEntity = LateralProfileEntityRefs[connection.Road].Reinterpret<Entity>()[0];
            var lateralProfile = LateralProfileComponents[lateralProfileEntity];

            var lateralOffset = connection.Direction == IntersectionRoadDirection.Incoming
                ? lateralProfile.LeftDrivableOffset.x + OffsetFromCurb
                : lateralProfile.RightDrivableOffset.x - OffsetFromCurb;

            var offset = new float3(lateralOffset, 0f, 0f);
            var offsetSamples = SplineUtility.OffsetSpline(centerLineSamples, offset, Allocator.Temp);
            if (connection.Direction == IntersectionRoadDirection.Incoming)
                SplineUtility.ReverseSpline(offsetSamples);

            samples.AddRange(offsetSamples);
            offsetSamples.Dispose();
        }

        void TraverseRoadEdge(
            IntersectionRoadConnection connection,
            NativeHashMap<IntersectionRoadConnection, bool> visited)
        {
            if (visited[connection])
                return;

            var prevSampleIndex = CameraPathSamples.Length;
            var path = new NativeList<RigidTransform>(Allocator.Temp);

            while (!visited[connection])
            {
                visited[connection] = true;

                AppendRoadSamples(path, connection);

                var roadData = RoadCenterLineDataComponents[connection.Road];
                var intersectionEntity = GetIntersection(roadData, connection.Direction);
                if (intersectionEntity == Entity.Null)
                {
                    connection.Direction = connection.InverseDirection;
                }
                else
                {
                    var cornerEntity = GetCorner(connection, intersectionEntity);
                    AppendRightTurnCornerSamples(path, cornerEntity);

                    connection = IntersectionCornerComponents[cornerEntity].LeftRoad;
                }
            }

            // var culledSpline = SplineUtility.RemoveOverlappingPoints(path, Allocator.Temp, 3f);
            // var splineLength = SplineUtility.SplineLength(path);
            // var culledSpline = SplineUtility.EvenlyRemapSpline(path, (int)(splineLength / 2f), Allocator.Temp);
            CameraPathSamples.AddRange(path);
            CameraPathSampleRanges.Add(new PlacementPathSampleRange
            {
                StartIndex = prevSampleIndex,
                Length = path.Length
            });
            // culledSpline.Dispose();
            path.Dispose();
        }

        public void Execute()
        {
            if (RoadEntities.Length == 0)
                return;

            var visited = new NativeHashMap<IntersectionRoadConnection, bool>(RoadEntities.Length * 2, Allocator.Temp);

            // Mark all road sides as unvisited
            for (var i = 0; i < RoadEntities.Length; i++)
            {
                var roadEntity = RoadEntities[i];
                visited.Add(new IntersectionRoadConnection
                {
                    Road = roadEntity,
                    Direction = IntersectionRoadDirection.Incoming
                }, false);
                visited.Add(new IntersectionRoadConnection
                {
                    Road = roadEntity,
                    Direction = IntersectionRoadDirection.Outgoing
                }, false);
            }

            // Calculate dead-end road edges first with a depth-first traversal
            for (var i = 0; i < RoadEntities.Length; i++)
            {
                var roadEntity = RoadEntities[i];
                var connection = new IntersectionRoadConnection
                {
                    Road = roadEntity,
                    Direction = IntersectionRoadDirection.Outgoing
                };
                TraverseRoadEdge(connection, visited);

                connection.Direction = IntersectionRoadDirection.Incoming;
                TraverseRoadEdge(connection, visited);
            }
        }
    }

    /// <summary>
    /// Creates new camera path entities.
    /// </summary>
    public struct CreateCameraPathEntitiesJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public EntityArchetype CameraPathArchetype;

        public NativeList<Entity> CameraPathEntities;
        [ReadOnly] public NativeArray<PlacementPathSampleRange> CameraPathSampleRanges;

        public void Execute()
        {
            CameraPathEntities.ResizeUninitialized(CameraPathSampleRanges.Length);
            Transaction.CreateEntity(CameraPathArchetype, CameraPathEntities);
        }
    }

    /// <summary>
    /// Fills each new path entity with a set of road edge samples
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct InitializeCameraPathsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeList<Entity> CameraPathEntities;
        [ReadOnly] public NativeArray<PlacementPathSampleRange> CameraPathSampleRanges;
        [ReadOnly] public NativeArray<RigidTransform> Samples;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<CameraPathSample> CameraPathSampleBuffers;

        public void Execute(int index)
        {
            var pathEntity = CameraPathEntities[index];
            var range = CameraPathSampleRanges[index];
            var sampleBuffer = CameraPathSampleBuffers[pathEntity].Reinterpret<RigidTransform>();
            sampleBuffer.AddRange(Samples.GetSubArray(range.StartIndex, range.Length));
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct GetLongestCameraPathJob : IJob
    {
        public NativeArray<Entity> LongestPath;
        [ReadOnly] public NativeArray<Entity> CameraPathEntities;
        [ReadOnly] public BufferFromEntity<CameraPathSample> CameraPathSampleBuffers;

        public void Execute()
        {
            LongestPath[0] = Entity.Null;
            if (CameraPathEntities.Length == 0)
                return;

            var longestPathLength = 0f;
            for (var i = 0; i < CameraPathEntities.Length; i++)
            {
                var entity = CameraPathEntities[i];
                var samples = CameraPathSampleBuffers[entity].Reinterpret<RigidTransform>().AsNativeArray();
                var length = SplineUtility.SplineLength(samples);
                if (length > longestPathLength)
                {
                    longestPathLength = length;
                    LongestPath[0] = entity;
                }
            }
        }
    }
}
