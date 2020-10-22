using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing.Jobs
{
    /// <summary>
    /// Records the StartIndex and length of one contiguous spline within a combined spline positions array
    /// </summary>
    struct SplineSampleRange
    {
        public int StartIndex;
        public int Length;
    }

    /// <summary>
    /// Calculates the world rotation of each spline point
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct AddSplineSamplesToBufferJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> SplineEntities;
        [ReadOnly] public NativeArray<float3> SplineSamples;
        [ReadOnly] public NativeArray<SplineSampleRange> Ranges;
        [NativeDisableParallelForRestriction] public BufferFromEntity<RoadSplineSample> SplineSampleBuffers;

        public void Execute(int index)
        {
            var splineEntity = SplineEntities[index];
            var sampleBuffer = SplineSampleBuffers[splineEntity].Reinterpret<RigidTransform>();
            var range = Ranges[index];
            var samples = SplineSamples.GetSubArray(range.StartIndex, range.Length);

            sampleBuffer.ResizeUninitialized(samples.Length);

            SplineUtility.Float3PathToRigidTransformPath(
                samples,
                sampleBuffer.Reinterpret<RigidTransform>().AsNativeArray(),
                false);
        }
    }
}
