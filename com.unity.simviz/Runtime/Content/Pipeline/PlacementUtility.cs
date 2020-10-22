using System.Collections.Generic;
using System.Linq;
using ClipperLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Pipeline.Components;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.Pipeline
{
    static class PlacementUtility
    {
        public const float UpScaleFactor = 1000f;
        public const float DownScaleFactor = 1 / UpScaleFactor;

        /// <summary>
        /// Offsets a polygon by the given distance. Counter-clockwise polygons will expand while clockwise polygons
        /// will contract.
        /// </summary>
        public static List<NativeArray<RigidTransform>> OffsetPolygon(
            NativeArray<RigidTransform> samples, float distance, Allocator allocator)
        {
            var solution = new List<List<IntPoint>>();
            var polygon = FromSamplesToClipper(samples);

            // Clipper uses Even-Odd polygon boolean operations to calculate offsets, so offsetting a clockwise path
            // will result in an expanded offset path rather than a contracted path we're looking for. The offset
            // direction is reversed here to ensure that the orientation of the polygon determines the offset direction.
            var orientation = Clipper.Orientation(polygon);
            if (!orientation)
                distance *= -1;

            var clipperOffset = new ClipperOffset();
            clipperOffset.AddPath(polygon, JoinType.jtRound, EndType.etClosedPolygon);
            clipperOffset.Execute(ref solution, distance * UpScaleFactor);

            if (!orientation)
            {
                for (var i = 0; i < solution.Count; i++)
                {
                    solution[i].Reverse();
                }
            }

            return ConvertToSamples(solution, allocator);
        }

        public static NativeArray<RigidTransform> OffsetPath(
            NativeArray<RigidTransform> samples,
            float distance,
            bool looped,
            Allocator allocator)
        {
            var solution = new List<List<IntPoint>>();
            var polygon = FromSamplesToClipper(samples);

            if (!Clipper.Orientation(polygon))
                distance *= -1;

            var clipperOffset = new ClipperOffset();
            clipperOffset.AddPath(polygon, JoinType.jtRound, EndType.etOpenButt);
            clipperOffset.Execute(ref solution, distance * UpScaleFactor);

            return IntPointsToRigidTransforms(solution[0], looped, allocator);
        }

        public static List<IntPoint> FromSamplesToClipper(NativeArray<RigidTransform> samples)
        {
            var polygon = new List<IntPoint>(samples.Length);
            foreach (var sample in samples)
            {
                polygon.Add(new IntPoint(
                    sample.pos.x * UpScaleFactor,
                    sample.pos.z * UpScaleFactor
                ));
            }

            return polygon;
        }

        static NativeArray<float3> IntPointListToFloat3Array(List<IntPoint> points, Allocator allocator)
        {
            var newPoints = new NativeArray<float3>(points.Count, allocator);
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                newPoints[i] = new float3(point.X * DownScaleFactor, 0, point.Y * DownScaleFactor);
            }

            return newPoints;
        }

        public static NativeArray<RigidTransform> IntPointsToRigidTransforms(List<IntPoint> points, bool looped, Allocator allocator)
        {
            var path = IntPointListToFloat3Array(points, allocator);
            var splinePath = SplineUtility.Float3PathToRigidTransformPath(path, looped, allocator);
            path.Dispose();
            return splinePath;
        }

        static List<NativeArray<RigidTransform>> ConvertToSamples(List<List<IntPoint>> polygons, Allocator allocator)
        {
            var convertedPolygons = new List<NativeArray<RigidTransform>>(polygons.Count);
            foreach (var polygon in polygons)
            {
                var count = polygon.Count;
                var convertedPolygon = new NativeList<RigidTransform>(count, allocator);
                for (var i = 0; i < polygon.Count; i++)
                {
                    var lastPoint = polygon[((i - 1) % count + count) % count];
                    var point = polygon[i];
                    var nextPoint = polygon[(i + 1) % polygon.Count];

                    var v1 = new float3(point.X - lastPoint.X, 0, point.Y - lastPoint.Y);
                    var v2 = new float3(nextPoint.X - point.X, 0, nextPoint.Y - point.Y);
                    var rotation = quaternion.LookRotation((v1 + v2) / 2, new float3(0, 1, 0));

                    convertedPolygon.Add(new RigidTransform
                    {
                        pos = new float3(point.X * DownScaleFactor, 0, point.Y * DownScaleFactor),
                        rot = rotation
                    });
                }
                convertedPolygons.Add(convertedPolygon);
            }

            return convertedPolygons;
        }

        /// <summary>
        /// Creates a set of polygonal regions offset from a given set of Clipper polygons
        /// </summary>
        /// <param name="paths">Hierarchy of Clipper polygons to be offset</param>
        /// <param name="innerOffset">Padding distance from the edge of the given paths region</param>
        /// <param name="outerOffset">The outer boundary distance of the offset region</param>
        /// <returns></returns>
        public static List<List<IntPoint>> GenerateOffsetRegionFromRoadPaths(
            List<List<IntPoint>> paths,
            float innerOffset,
            float outerOffset)
        {
            var solution = new List<List<IntPoint>>();
            if (outerOffset < 0f || outerOffset < innerOffset)
                return solution;

            var clipperOffset = new ClipperOffset();
            clipperOffset.AddPaths(paths, JoinType.jtMiter, EndType.etClosedPolygon);

            var innerOffsetRegions = new List<List<IntPoint>>();
            clipperOffset.Execute(ref innerOffsetRegions, innerOffset * UpScaleFactor);

            var outerOffsetRegions = new List<List<IntPoint>>();
            clipperOffset.Execute(ref outerOffsetRegions, outerOffset * UpScaleFactor);

            var clipper = new Clipper();
            clipper.AddPaths(outerOffsetRegions, PolyType.ptSubject, true);
            clipper.AddPaths(innerOffsetRegions, PolyType.ptClip, true);

            clipper.Execute(ClipType.ctXor, solution, PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd);
            return solution;
        }

        /// <summary>
        /// Creates a set of new polygon entities from a list of polygons produced by clipper.
        /// </summary>
        public static NativeArray<Entity> ClipperPolygonsToPolygonEntities(
            EntityManager manager,
            EntityArchetype polygonEntityArchetype,
            List<List<IntPoint>> clipperPolygons)
        {
            var polygonEntities = new NativeArray<Entity>(clipperPolygons.Count, Allocator.TempJob);
            manager.CreateEntity(polygonEntityArchetype, polygonEntities);

            for (var p = 0; p < clipperPolygons.Count; p++)
            {
                var polygon = clipperPolygons[p];
                var polygonEntity = polygonEntities[p];
                var polygonPointsBuffer = manager.GetBuffer<PolygonPoint>(polygonEntity).Reinterpret<float2>();
                polygonPointsBuffer.ResizeUninitialized(polygon.Count);

                for (var i = 0; i < polygon.Count; i++)
                {
                    var point = polygon[i];
                    polygonPointsBuffer[i] = new float2(point.X * DownScaleFactor, point.Y * DownScaleFactor);
                }
            }

            return polygonEntities;
        }
    }
}
