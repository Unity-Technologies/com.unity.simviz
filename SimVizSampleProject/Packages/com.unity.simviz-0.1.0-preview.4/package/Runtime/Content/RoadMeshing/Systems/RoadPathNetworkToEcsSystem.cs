using System;
using PathCreation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.RoadMeshing.Jobs;

namespace UnityEngine.SimViz.Content.RoadMeshing.Systems
{
    class RoadPathNetworkToEcsParameters
    {
        public RoadPathNetwork network;
    }

    /// <summary>
    /// Creates new spline entities from arrays of spline points
    /// </summary>
    [DisableAutoCreation]
    class RoadPathNetworkToEcsSystem : ComponentSystem, IGeneratorSystem<RoadPathNetworkToEcsParameters>
    {
        public RoadPathNetworkToEcsParameters Parameters { get; set; }

        EntityArchetype m_RoadSplineArchetype;

        protected override void OnCreate()
        {
            m_RoadSplineArchetype = EntityManager.CreateArchetype(typeof(RoadSplineSample));
        }

        void GetRoadPathSamples(out NativeArray<float3> samples, out NativeArray<SplineSampleRange> ranges)
        {
            var sampleCount = 0;
            var paths = new Vector3[Parameters.network.roadPaths.Count][];
            for (var i = 0; i < paths.Length; i++)
            {
                var roadPath = Parameters.network.roadPaths[i];
                var pathComponent = roadPath.GetComponent<RoadPath>();
                var bezierPath = pathComponent.bezierPath;
                var path = new VertexPath(bezierPath, pathComponent.transform, 1f, 0.01f).localPoints;
                paths[i] = path;
                sampleCount += path.Length;
            }

            samples = new NativeArray<float3>(
                sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            ranges = new NativeArray<SplineSampleRange>(
                Parameters.network.roadPaths.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            for (int i = 0, sampleOffset = 0; i < ranges.Length; i++)
            {
                var path = paths[i];
                ranges[i] = new SplineSampleRange
                {
                    StartIndex = sampleOffset,
                    Length = path.Length
                };

                var slice = samples.Reinterpret<Vector3>().GetSubArray(sampleOffset, path.Length);
                slice.CopyFrom(path);

                sampleOffset += path.Length;
            }
        }

        protected override void OnUpdate()
        {
            var splineEntities = new NativeArray<Entity>(Parameters.network.roadPaths.Count, Allocator.TempJob);
            EntityManager.CreateEntity(m_RoadSplineArchetype, splineEntities);
            GetRoadPathSamples(out var samples, out var ranges);

            var addSplineSamplesToBuffer = new AddSplineSamplesToBufferJob
            {
                SplineEntities = splineEntities,
                Ranges = ranges,
                SplineSamples = samples,
                SplineSampleBuffers = GetBufferFromEntity<RoadSplineSample>()
            }.Schedule(splineEntities.Length, 1);
            addSplineSamplesToBuffer.Complete();

            splineEntities.Dispose();
            samples.Dispose();
            ranges.Dispose();
        }
    }
}
