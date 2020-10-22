using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Pipeline.Components;
using UnityEngine.SimViz.Content.Sampling;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.Pipeline.Jobs
{
    struct OffsetPlacementPathsJob : IJobParallelFor
    {
        public float Offset;
        [ReadOnly] public NativeArray<Entity> PathEntities;
        [ReadOnly] public NativeArray<Entity> OffsetPathEntities;

        [ReadOnly] public BufferFromEntity<PointSampleGlobal> PathBuffers;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<OffsetPlacementPathRange> OffsetPathRangeBuffers;
        [NativeDisableParallelForRestriction]
        public BufferFromEntity<OffsetPlacementPathSample> OffsetPathSampleBuffers;

        public void Execute(int index)
        {
            var placementPathEntity = PathEntities[index];
            var offsetPathEntity = OffsetPathEntities[index];

            var pathBuffer = PathBuffers[placementPathEntity].Reinterpret<RigidTransform>().AsNativeArray();
            var offsetPathRangesBuffer = OffsetPathRangeBuffers[offsetPathEntity];
            var offsetPathBuffer = OffsetPathSampleBuffers[offsetPathEntity].Reinterpret<RigidTransform>();

            var offsetPolygons = PlacementUtility.OffsetPolygon(pathBuffer, Offset, Allocator.Temp);
            foreach (var offsetPolygon in offsetPolygons)
            {
                offsetPathRangesBuffer.Add(new OffsetPlacementPathRange
                {
                    StartIndex = offsetPathRangesBuffer.Length,
                    SampleCount = offsetPolygon.Length
                });
                offsetPathBuffer.AddRange(offsetPolygon);
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct CreateEvenlySpacedPlacementPointsJob : IJobParallelFor
    {
        public float Spacing;
        [ReadOnly] public NativeArray<Entity> OffsetPathEntities;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<UniformPlacementPoint> PlacementPointBuffers;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<OffsetPlacementPathRange> OffsetPathRangeBuffers;
        [NativeDisableParallelForRestriction]
        public BufferFromEntity<OffsetPlacementPathSample> OffsetPathSampleBuffers;

        public void Execute(int index)
        {
            var offsetPathEntity = OffsetPathEntities[index];
            var rangeBuffer = OffsetPathRangeBuffers[offsetPathEntity];
            var offsetPathBuffer = OffsetPathSampleBuffers[offsetPathEntity]
                .Reinterpret<RigidTransform>().AsNativeArray();
            var placementPointBuffer = PlacementPointBuffers[offsetPathEntity].Reinterpret<RigidTransform>();

            for (var i = 0; i < rangeBuffer.Length; i++)
            {
                var range = rangeBuffer[i];
                var offsetPath = offsetPathBuffer.GetSubArray(range.StartIndex, range.SampleCount);
                var uniformSamples = SplineUtility.EvenlyDistributeSpline(offsetPath, Spacing, true, Allocator.Temp);
                placementPointBuffer.AddRange(uniformSamples);
                uniformSamples.Dispose();
            }
        }
    }
}
