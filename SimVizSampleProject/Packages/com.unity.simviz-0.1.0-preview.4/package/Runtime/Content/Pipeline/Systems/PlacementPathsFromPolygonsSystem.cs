using System;
using System.Collections.Generic;
using System.Linq;
using ClipperLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.MapElements;
using UnityEngine.SimViz.Content.Sampling;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.Pipeline.Systems
{
    [DisableAutoCreation]
    public class PlacementPathsFromPolygonsSystem : ComponentSystem, IGeneratorSystem<PolygonSystemParameters>
    {
        public PolygonSystemParameters Parameters { get; set; }

        EntityArchetype m_SamplesArchetype;

        protected override void OnCreate()
        {
            m_SamplesArchetype = EntityManager.CreateArchetype(
                typeof(PointSampleGlobal),
                typeof(PolygonOrientationComponent));
        }

        protected override void OnUpdate()
        {
            var polygons = PolygonsFromRoadOutline(Parameters.extensionDistance);

            var clipper = new Clipper();
            foreach (var polygon in polygons)
            {
                if (!Clipper.Orientation(polygon))
                    polygon.Reverse();
                if (Clipper.Area(polygon) > 0)
                    clipper.AddPath(polygon, PolyType.ptSubject, true);
            }

            var solution = new List<List<IntPoint>>();
            clipper.Execute(ClipType.ctUnion, solution, PolyFillType.pftNonZero, PolyFillType.pftNonZero);

            solution.RemoveAll(IsSmallerThanMinimumArea);

            if (solution.Count == 0 && polygons.Count != 0)
            {
                throw new Exception($"Unable to create a polygon from " +
                    $"{Parameters.roadNetworkDescription.name}");
            }

            foreach (var polygon in solution)
            {
                var pathEntity = EntityManager.CreateEntity(m_SamplesArchetype);

                var orientation = Clipper.Orientation(polygon)
                    ? PolygonOrientation.Outside
                    : PolygonOrientation.Inside;

                var pathSamplesBuffer = EntityManager.GetBuffer<PointSampleGlobal>(pathEntity).Reinterpret<RigidTransform>();
                var placementPathSamples = PlacementUtility.IntPointsToRigidTransforms(polygon, true, Allocator.TempJob);
                pathSamplesBuffer.AddRange(placementPathSamples);
                placementPathSamples.Dispose();

                EntityManager.SetComponentData(pathEntity, new PolygonOrientationComponent
                {
                    Orientation = orientation
                });
            }
        }

        List<List<IntPoint>> PolygonsFromRoadOutline(float extensionDistance)
        {
            var polygons = new List<List<IntPoint>>();
            var rnd = Parameters.roadNetworkDescription;
            foreach (var road in rnd.AllRoads)
            {
                var points = GeometrySampling.BuildSamplesFromRoadOutlineWithExtensionDistance(
                    road, 0.5f, extensionDistance, Parameters.outermostLaneType);
                var path = new List<IntPoint>(points.Length);
                foreach (var point in points)
                {
                    path.Add(new IntPoint(
                        point.pose.pos.x * PlacementUtility.UpScaleFactor,
                        point.pose.pos.z * PlacementUtility.UpScaleFactor));
                }

                if (!Clipper.Orientation(path))
                    path.Reverse();

                polygons.Add(path);

                points.Dispose();
            }

            return polygons;
        }

        bool IsSmallerThanMinimumArea(List<IntPoint> polygon)
        {
            var area = Parameters.minimumPolygonArea *
                         (PlacementUtility.UpScaleFactor * PlacementUtility.UpScaleFactor);
            return Math.Abs(Clipper.Area(polygon)) < area;
        }
    }

    public struct PolygonSystemParameters
    {
        public RoadNetworkDescription roadNetworkDescription;

        ///<summary>Filters polygons containing an area smaller than the specified value</summary>
        public float minimumPolygonArea;

        ///<summary>Offset applied to the beginning and end of road segment polygons. Increases the likelihood
        /// of polygon overlap to encourage the creation of one contiguous road network polygon.</summary>
        public float extensionDistance;

        public LaneType outermostLaneType;
    }

    public enum PolygonOrientation
    {
        Outside, Inside
    }

    public struct PolygonOrientationComponent : IComponentData
    {
        public PolygonOrientation Orientation;
    }
}
