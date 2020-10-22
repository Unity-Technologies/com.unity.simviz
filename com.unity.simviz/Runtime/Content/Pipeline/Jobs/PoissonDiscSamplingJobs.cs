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
    /// <summary>
    /// Counts the outermost polygons defined within the given set of polygons originally offset using clipper
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct CountOutermostPolygonsJob : IJob
    {
        [WriteOnly] public NativeArray<int> OuterMostPolygonCount;
        [ReadOnly] public NativeArray<Entity> PolygonEntities;
        [ReadOnly] public BufferFromEntity<PolygonPoint> PolygonPointBuffers;

        /// <summary>
        /// Calculates the signed area of the polygon created by the given looped path.
        /// Sourced from Clipper's Area() function.
        /// </summary>
        float SignedArea(NativeArray<float2> path)
        {
            if (path.Length < 3)
                return 0;

            var area = 0f;
            for (int i = 0, j = path.Length - 1; i < path.Length; ++i)
            {
                area += (path[j].x + path[i].x) * (path[j].y - path[i].y);
                j = i;
            }
            return -area * 0.5f;
        }

        /// <summary>
        /// Mimics the behavior of Clipper's Orientation() function.
        /// True == counter-clockwise, False == clockwise
        /// </summary>
        bool PathOrientation(int polygonEntityIndex)
        {
            var polygonEntity = PolygonEntities[polygonEntityIndex];
            var path = PolygonPointBuffers[polygonEntity].Reinterpret<float2>().AsNativeArray();
            return SignedArea(path) >= 0;
        }

        public void Execute()
        {
            if (PolygonEntities.Length == 0)
            {
                OuterMostPolygonCount[0] = 0;
                return;
            }

            var count = 1;
            var initialOrientation = PathOrientation(0);
            for (; count < PolygonEntities.Length; count++)
            {
                if (PathOrientation(count) != initialOrientation) break;
            }
            OuterMostPolygonCount[0] = count;
        }
    }

    /// <summary>
    /// Creates a new poisson point region entity for each of the outermost polygons defining the offset region.
    /// </summary>
    public struct CreatePoissonPointRegionEntitiesJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public NativeList<Entity> PoissonPointRegionEntities;
        public EntityArchetype PoissonPointRegionArchetype;
        public NativeArray<int> OuterMostPolygonCount;

        public void Execute()
        {
            PoissonPointRegionEntities.ResizeUninitialized(OuterMostPolygonCount[0]);
            Transaction.CreateEntity(PoissonPointRegionArchetype, PoissonPointRegionEntities);
        }
    }

    /// <summary>
    /// Calculates the bounding box of the outermost polygons contained within the offset region.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct CalculatePathAreasJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> PoissonPointRegionEntities;
        [ReadOnly] public NativeArray<Entity> PolygonEntities;
        [ReadOnly] public BufferFromEntity<PolygonPoint> PolygonPointBuffers;

        [NativeDisableParallelForRestriction]
        public ComponentDataFromEntity<PoissonPointRegion> PoissonPointRegionComponents;

        public void Execute(int index)
        {
            var regionEntity = PoissonPointRegionEntities[index];
            var polygonEntity = PolygonEntities[index];
            var points = PolygonPointBuffers[polygonEntity].Reinterpret<float2>().AsNativeArray();

            if (points.Length == 0)
                return;

            var right = points[0].x;
            var left = points[0].x;
            var top = points[0].y;
            var bottom = points[0].y;

            for (var i = 1; i < points.Length; i++)
            {
                right = math.max(right, points[i].x);
                left = math.min(left, points[i].x);
                top = math.max(top, points[i].y);
                bottom = math.min(bottom, points[i].y);
            }

            PoissonPointRegionComponents[regionEntity] = new PoissonPointRegion
            {
                Size = new float2(right - left, top - bottom),
                Center = new float2((right + left) / 2f, (top + bottom) / 2f)
            };
        }
    }

    /// <summary>
    /// Creates a rectangular region of poisson points for each bounding box encapsulating the
    /// outermost offset polygon regions.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct GeneratePoissonPointsJob : IJobParallelForDefer
    {
        public float MinimumRadius;
        public uint RandomSeed;

        [ReadOnly] public NativeArray<Entity> PoissonPointRegionEntities;
        [ReadOnly] public ComponentDataFromEntity<PoissonPointRegion> PoissonPointRegions;

        [NativeDisableParallelForRestriction] public BufferFromEntity<PoissonPoint> PoissonPointBuffers;

        public void Execute(int index)
        {
            var poissonPointRegionEntity = PoissonPointRegionEntities[index];
            var pointRegion = PoissonPointRegions[poissonPointRegionEntity];
            var poissonPointBuffer = PoissonPointBuffers[poissonPointRegionEntity].Reinterpret<float2>();

            var poissonPoints = PoissonDiscSampling.Sample(
                pointRegion.Size.x,
                pointRegion.Size.y,
                MinimumRadius,
                RandomUtility.ParallelForRandomSeed(RandomSeed, index),
                PoissonDiscSampling.defaultSamplingResolution,
                Allocator.Temp);

            var offset = pointRegion.Center - pointRegion.Size / 2f;
            for (var i = 0; i < poissonPoints.Length; i++)
                poissonPoints[i] += offset;

            poissonPointBuffer.AddRange(poissonPoints);
        }
    }

    /// <summary>
    /// Combines all generated poisson points into a single array. This array is to be later be filtered based on
    /// whether each point is contained within the offset region.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct GatherPoissonPointsJob : IJob
    {
        public NativeList<float2> PoissonPoints;
        public NativeList<bool> InsidePolygons;
        [ReadOnly] public NativeList<Entity> PoissonPointRegionEntities;
        [ReadOnly] public BufferFromEntity<PoissonPoint> PoissonPointBuffers;

        public void Execute()
        {
            for (var i = 0; i < PoissonPointRegionEntities.Length; i++)
            {
                var entity = PoissonPointRegionEntities[i];
                var buffer = PoissonPointBuffers[entity].Reinterpret<float2>().AsNativeArray();
                PoissonPoints.AddRange(buffer);
            }

            InsidePolygons.ResizeUninitialized(PoissonPoints.Length);
        }
    }

    /// <summary>
    /// Checks whether each point is located within the given set of polygons.
    /// Source: https://observablehq.com/@tmcw/understanding-point-in-polygon
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct CheckPointsAreInsidePolygonsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<float2> PoissonPoints;
        [ReadOnly] public NativeArray<Entity> PolygonEntities;
        [ReadOnly] public BufferFromEntity<PolygonPoint> PolygonPointBuffers;
        [WriteOnly] public NativeArray<bool> InsidePolygons;

        public void Execute(int index)
        {
            var point = PoissonPoints[index];

            var inside = false;
            for (var p = 0; p < PolygonEntities.Length; p++)
            {
                var polygonEntity = PolygonEntities[p];
                var polygon = PolygonPointBuffers[polygonEntity].Reinterpret<float2>().AsNativeArray();

                if (polygon.Length < 3)
                    continue;

                for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                {
                    var xi = polygon[i].x;
                    var yi = polygon[i].y;
                    var xj = polygon[j].x;
                    var yj = polygon[j].y;

                    var intersect = ((yi > point.y) != (yj > point.y))
                        && (point.x < (xj - xi) * (point.y - yi) / (yj - yi) + xi);

                    inside = intersect ? !inside : inside;
                }
            }

            InsidePolygons[index] = inside;
        }
    }

    /// <summary>
    /// Returns a list of poisson points filtered by whether the points are contained within the offset region.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct GatherFilteredPoissonPointsJob : IJob
    {
        [ReadOnly] public NativeArray<float2> UnfilteredPoissonPoints;
        [ReadOnly] public NativeArray<bool> InsidePolygon;
        public NativeList<float2> FilteredPoissonPoints;

        public void Execute()
        {
            FilteredPoissonPoints.Capacity = UnfilteredPoissonPoints.Length;
            for (var i = 0; i < UnfilteredPoissonPoints.Length; i++)
            {
                if (InsidePolygon[i])
                    FilteredPoissonPoints.Add(UnfilteredPoissonPoints[i]);
            }
            FilteredPoissonPoints.Capacity = FilteredPoissonPoints.Length;
        }
    }
}
