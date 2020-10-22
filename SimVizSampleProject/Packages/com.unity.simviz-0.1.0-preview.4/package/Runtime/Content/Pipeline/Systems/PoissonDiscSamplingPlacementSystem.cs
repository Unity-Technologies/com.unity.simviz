using System;
using System.Collections.Generic;
using ClipperLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Pipeline.Components;
using UnityEngine.SimViz.Content.Pipeline.Jobs;
using UnityEngine.SimViz.Content.Sampling;

namespace UnityEngine.SimViz.Content.Pipeline.Systems
{
    [DisableAutoCreation]
    public class PoissonDiscSamplingPlacementSystem :
        ComponentSystem, IGeneratorSystem<PoissonPlacementSystemParameters>
    {
        public PoissonPlacementSystemParameters Parameters { get; set; }
        const float k_MinimumSpacing = 1f;

        EntityArchetype m_PolygonArchetype;
        EntityArchetype m_PoissonPointRegionArchetype;

        protected override void OnCreate()
        {
            m_PolygonArchetype = EntityManager.CreateArchetype(
                typeof(PolygonPoint));
            m_PoissonPointRegionArchetype = EntityManager.CreateArchetype(
                typeof(PoissonPointRegion),
                typeof(PoissonPoint));
        }

        bool ParametersValidationCheck()
        {
            var errorOutput = "";
            if (Parameters.category == null)
            {
                errorOutput += "Category parameter is null\n";
            }

            if (Parameters.parent == null)
            {
                errorOutput += "Parent transform parameter is null\n";
            }

            if (string.IsNullOrEmpty(errorOutput))
                return true;
            Debug.LogError(errorOutput);
            return false;
        }

        List<List<IntPoint>> GeneratePlacementRegions()
        {
            var polygons = new List<List<IntPoint>>();

            Entities.ForEach((DynamicBuffer<PointSampleGlobal> vertices) =>
            {
                var pathSamples = vertices.Reinterpret<RigidTransform>().AsNativeArray();
                polygons.Add(PlacementUtility.FromSamplesToClipper(pathSamples));
            });

            return PlacementUtility.GenerateOffsetRegionFromRoadPaths(
                polygons,
                Parameters.innerOffsetFromPath,
                Parameters.outerOffsetFromPath);
        }

        void PlaceObjectsAtPoissonPoints(NativeArray<float2> points, Transform parent)
        {
            foreach (var point in points)
            {
                var point3d = Utilities.GeometryUtility.SwizzleX0Z(point);
                var placementObject = Parameters.category.NextPlacementObject();
                var rotation = quaternion.RotateY(Random.Range(0, math.PI * 2));
                if (placementObject.CheckForCollidingObjects(
                    point3d,
                    rotation,
                    Parameters.collisionLayerMask))
                    continue;

                var placedObject = Object.Instantiate(placementObject.Prefab, parent.transform);
                placedObject.transform.position = point3d;
                placedObject.transform.rotation = rotation;
                Physics.SyncTransforms();
            }
        }

        protected override void OnUpdate()
        {
            if (!ParametersValidationCheck())
                return;

            var parentObj = new GameObject(Parameters.category.name);
            parentObj.transform.parent = Parameters.parent;

            var spacing = Parameters.spacing;
            if (Parameters.spacing < k_MinimumSpacing)
            {
                Debug.LogWarning(
                    $"Spacing parameter is less than the minimum of { k_MinimumSpacing }. " +
                    "Proceeding with minimum spacing.");
                spacing = k_MinimumSpacing;
            }

            var offsetPolygons = GeneratePlacementRegions();

            var polygonEntities = PlacementUtility.ClipperPolygonsToPolygonEntities(
                EntityManager,
                m_PolygonArchetype,
                offsetPolygons);
            var poissonPointRegionEntities = new NativeList<Entity>(Allocator.TempJob);
            var outerMostPolygonCount = new NativeArray<int>(1, Allocator.TempJob);
            var unfilteredPoissonPoints = new NativeList<float2>(Allocator.TempJob);
            var insidePolygons = new NativeList<bool>(Allocator.TempJob);
            var filteredPoissonPoints = new NativeList<float2>(Allocator.TempJob);

            var transaction = EntityManager.BeginExclusiveEntityTransaction();

            var countOutermostPolygonsJob = new CountOutermostPolygonsJob
            {
                OuterMostPolygonCount = outerMostPolygonCount,
                PolygonEntities = polygonEntities,
                PolygonPointBuffers = GetBufferFromEntity<PolygonPoint>(true)
            }.Schedule();

            var createPoissonPointRegionEntitiesJob = new CreatePoissonPointRegionEntitiesJob
            {
                Transaction = transaction,
                PoissonPointRegionEntities = poissonPointRegionEntities,
                OuterMostPolygonCount = outerMostPolygonCount,
                PoissonPointRegionArchetype = m_PoissonPointRegionArchetype
            }.Schedule(countOutermostPolygonsJob);

            var calculatePathAreasJob = new CalculatePathAreasJob
            {
                PoissonPointRegionEntities = poissonPointRegionEntities.AsDeferredJobArray(),
                PolygonEntities = polygonEntities,
                PolygonPointBuffers = GetBufferFromEntity<PolygonPoint>(true),
                PoissonPointRegionComponents = GetComponentDataFromEntity<PoissonPointRegion>()
            }.Schedule(poissonPointRegionEntities, 1, createPoissonPointRegionEntitiesJob);

            var generatePoissonPointsJob = new GeneratePoissonPointsJob
            {
                MinimumRadius = spacing,
                RandomSeed = 12345u,
                PoissonPointRegionEntities = poissonPointRegionEntities.AsDeferredJobArray(),
                PoissonPointBuffers = GetBufferFromEntity<PoissonPoint>(),
                PoissonPointRegions = GetComponentDataFromEntity<PoissonPointRegion>(true)
            }.Schedule(poissonPointRegionEntities, 1, calculatePathAreasJob);

            var gatherPoissonPointsJob = new GatherPoissonPointsJob
            {
                PoissonPoints = unfilteredPoissonPoints,
                InsidePolygons = insidePolygons,
                PoissonPointRegionEntities = poissonPointRegionEntities,
                PoissonPointBuffers = GetBufferFromEntity<PoissonPoint>(true)
            }.Schedule(generatePoissonPointsJob);

            var checkPointsAreInsidePolygonsJob = new CheckPointsAreInsidePolygonsJob
            {
                PoissonPoints = unfilteredPoissonPoints.AsDeferredJobArray(),
                InsidePolygons = insidePolygons.AsDeferredJobArray(),
                PolygonEntities = polygonEntities,
                PolygonPointBuffers = GetBufferFromEntity<PolygonPoint>(true)
            }.Schedule(unfilteredPoissonPoints, 8, gatherPoissonPointsJob);

            var gatherFilteredPoissonPointsJob = new GatherFilteredPoissonPointsJob
            {
                UnfilteredPoissonPoints = unfilteredPoissonPoints.AsDeferredJobArray(),
                FilteredPoissonPoints = filteredPoissonPoints,
                InsidePolygon = insidePolygons.AsDeferredJobArray()
            }.Schedule(checkPointsAreInsidePolygonsJob);

            polygonEntities.Dispose(gatherFilteredPoissonPointsJob);
            poissonPointRegionEntities.Dispose(gatherFilteredPoissonPointsJob);
            outerMostPolygonCount.Dispose(gatherFilteredPoissonPointsJob);
            insidePolygons.Dispose(gatherFilteredPoissonPointsJob);
            unfilteredPoissonPoints.Dispose(gatherFilteredPoissonPointsJob);

            EntityManager.ExclusiveEntityTransactionDependency = gatherFilteredPoissonPointsJob;
            EntityManager.EndExclusiveEntityTransaction();

            PlaceObjectsAtPoissonPoints(filteredPoissonPoints, parentObj.transform);
            filteredPoissonPoints.Dispose(gatherFilteredPoissonPointsJob);
        }
    }
}
