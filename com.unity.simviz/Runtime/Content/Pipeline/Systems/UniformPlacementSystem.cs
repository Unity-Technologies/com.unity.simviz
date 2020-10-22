using System;
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
    public class UniformPlacementSystem : ComponentSystem, IGeneratorSystem<PlacementSystemParameters>
    {
        public PlacementSystemParameters Parameters { get; set; }
        const float k_MinimumSpacing = 0.01f;

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

            if (string.IsNullOrEmpty(errorOutput)) return true;
            Debug.LogError(errorOutput);
            return false;
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
                    "Overriding spacing parameter with minimum.");
                spacing = k_MinimumSpacing;
            }

            var offsetPathArchetype = EntityManager.CreateArchetype(
                typeof(OffsetPlacementPathRange),
                typeof(OffsetPlacementPathSample),
                typeof(UniformPlacementPoint));
            var query = EntityManager.CreateEntityQuery(typeof(PointSampleGlobal));
            var pathEntities = query.ToEntityArray(Allocator.TempJob);

            var offsetPathEntities = EntityManager.CreateEntity(
                offsetPathArchetype, pathEntities.Length, Allocator.TempJob);

            var offsetPlacementPathsJob = new OffsetPlacementPathsJob
            {
                Offset = Parameters.offsetFromPath,
                PathEntities = pathEntities,
                OffsetPathEntities = offsetPathEntities,
                OffsetPathRangeBuffers = GetBufferFromEntity<OffsetPlacementPathRange>(),
                OffsetPathSampleBuffers = GetBufferFromEntity<OffsetPlacementPathSample>(),
                PathBuffers = GetBufferFromEntity<PointSampleGlobal>()
            }.Schedule(pathEntities.Length, 1);

            var createEvenlySpacedPlacementPointsJob = new CreateEvenlySpacedPlacementPointsJob
            {
                Spacing = spacing,
                OffsetPathEntities = offsetPathEntities,
                PlacementPointBuffers = GetBufferFromEntity<UniformPlacementPoint>(),
                OffsetPathSampleBuffers = GetBufferFromEntity<OffsetPlacementPathSample>(),
                OffsetPathRangeBuffers = GetBufferFromEntity<OffsetPlacementPathRange>()
            }.Schedule(offsetPathEntities.Length, 1, offsetPlacementPathsJob);

            createEvenlySpacedPlacementPointsJob.Complete();

            var placementObject = Parameters.category.NextPlacementObject();
            foreach (var entity in offsetPathEntities)
            {
                var points = EntityManager.GetBuffer<UniformPlacementPoint>(entity)
                    .Reinterpret<RigidTransform>().AsNativeArray();

                foreach (var point in points)
                {
                    var rotation = math.mul(Parameters.rotationFromPath,
                        math.mul(placementObject.Prefab.transform.rotation, point.rot));
                    if (placementObject.CheckForCollidingObjects(
                        point.pos,
                        rotation,
                        Parameters.collisionLayerMask,
                        Parameters.collisionCheckScale))
                    {
                        continue;
                    }

                    var placedObject = Object.Instantiate(placementObject.Prefab, parentObj.transform);

                    placedObject.transform.position = point.pos;
                    placedObject.transform.rotation = math.mul(Parameters.rotationFromPath,
                        math.mul(placementObject.Prefab.transform.rotation, point.rot));

                    Physics.SyncTransforms();
                    placementObject = Parameters.category.NextPlacementObject();
                }
            }

            EntityManager.DestroyEntity(offsetPathEntities);
            pathEntities.Dispose();
            offsetPathEntities.Dispose();
        }
    }
}
