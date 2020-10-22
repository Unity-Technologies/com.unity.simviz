using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Sampling;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing.Jobs
{
    public struct PlacementPathSampleRange
    {
        public int StartIndex;
        public int Length;
    }

    /// <summary>
    /// Traverses the graph of roads and intersection corners to identify the street side edge paths of the road network.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct TraverseRoadEdgesJob : IJob
    {
        public NativeList<RigidTransform> PlacementPathSamples;
        public NativeList<PlacementPathSampleRange> PlacementPathSampleRanges;

        [ReadOnly] public NativeArray<Entity> RoadEntities;

        [ReadOnly] public ComponentDataFromEntity<RoadCenterLineData> RoadCenterLineDataComponents;
        [ReadOnly] public ComponentDataFromEntity<IntersectionCorner> IntersectionCornerComponents;
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

            var lateralProfileEntity = LateralProfileEntityRefs[cornerEntity].Reinterpret<Entity>()[0];
            var lateralProfileSamples = LateralProfileSampleBuffers[lateralProfileEntity].Reinterpret<float2>();

            var lateralOffset = lateralProfileSamples[lateralProfileSamples.Length - 1].x;
            var offset = new float3(lateralOffset, 0f, 0f);

            var offsetSamples = SplineUtility.OffsetSpline(cornerSamples, offset, Allocator.Temp);
            samples.AddRange(offsetSamples);
            offsetSamples.Dispose();
        }

        void AppendRoadSamples(NativeList<RigidTransform> samples, IntersectionRoadConnection connection)
        {
            var centerLineSamples = RoadCenterLineSamples[connection.Road]
                .Reinterpret<RigidTransform>().AsNativeArray();

            var lateralProfileEntity = LateralProfileEntityRefs[connection.Road].Reinterpret<Entity>()[0];
            var lateralProfileSamples = LateralProfileSampleBuffers[lateralProfileEntity].Reinterpret<float2>();

            var lateralOffset = connection.Direction == IntersectionRoadDirection.Incoming
                ? lateralProfileSamples[0].x
                : lateralProfileSamples[lateralProfileSamples.Length - 1].x;

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

            var prevSampleIndex = PlacementPathSamples.Length;
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

            PlacementPathSamples.AddRange(path);
            PlacementPathSampleRanges.Add(new PlacementPathSampleRange
            {
                StartIndex = prevSampleIndex,
                Length = path.Length
            });
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
    /// Creates new placement path entities.
    /// </summary>
    public struct CreatePlacementPathEntitiesJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public EntityArchetype PlacementPathArchetype;

        public NativeList<Entity> PlacementPathEntities;
        [ReadOnly] public NativeArray<PlacementPathSampleRange> PlacementPathSampleRanges;

        public void Execute()
        {
            PlacementPathEntities.ResizeUninitialized(PlacementPathSampleRanges.Length);
            Transaction.CreateEntity(PlacementPathArchetype, PlacementPathEntities);
        }
    }

    /// <summary>
    /// Fills each new path entity with a set of road edge samples
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct InitializePlacementPathsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeList<Entity> PlacementPathEntities;
        [ReadOnly] public NativeArray<PlacementPathSampleRange> PlacementPathSampleRanges;
        [ReadOnly] public NativeArray<RigidTransform> Samples;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<PointSampleGlobal> PlacementPathSampleBuffers;

        public void Execute(int index)
        {
            var pathEntity = PlacementPathEntities[index];
            var range = PlacementPathSampleRanges[index];
            var sampleBuffer = PlacementPathSampleBuffers[pathEntity].Reinterpret<RigidTransform>();
            sampleBuffer.AddRange(Samples.GetSubArray(range.StartIndex, range.Length));
        }
    }
}
