using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing.Jobs
{
    /// <summary>
    /// Since the EntityQuery executed before this job compiles its NativeArray of entities asynchronously, we must
    /// schedule a job to dynamically resize any containers that map one-to-one with the number of entities returned
    /// from said query. These resized NativeLists also serve a role in correctly scheduling IJobParallelForDefer jobs
    /// since such a job requires a dynamically sized NativeList for its Schedule() interface.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct ResizeNumDecalsArrayJob :IJob
    {
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        public NativeList<int> NumDecalsPerRoad;

        public void Execute()
        {
            NumDecalsPerRoad.ResizeUninitialized(RoadEntities.Length);
        }
    }

    /// <summary>
    /// Density: Num of decals per meter of road surface
    /// OffsetRange: Wiggle factor between [0.0, 1.0] of how varied the decal placement is
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CalculateNumDecalsPerRoadJob : IJobParallelForDefer
    {
        public float Density;
        [WriteOnly] public NativeArray<int> NumDecalsPerRoad;
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> RoadSampleBuffers;

        public void Execute(int index)
        {
            var roadEntity = RoadEntities[index];
            var sampleBuffer = RoadSampleBuffers[roadEntity].Reinterpret<RigidTransform>().AsNativeArray();
            var length = SplineUtility.SplineLength(sampleBuffer);
            NumDecalsPerRoad[index] = math.max(0, (int) (length * Density));
        }
    }

    /// <summary>
    /// Dynamically resize the decal positions array according to the number of decals that will be spread across
    /// each road segment.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct InitializeDecalPositionsArrayJob :IJob
    {
        [ReadOnly] public NativeArray<int> NumDecalsPerRoad;
        public NativeList<int> DecalStartIndices;
        public NativeList<RigidTransform> DecalPoses;

        public void Execute()
        {
            DecalStartIndices.ResizeUninitialized(NumDecalsPerRoad.Length);

            var totalNumDecals = 0;
            for (var i = 0; i < NumDecalsPerRoad.Length; i++)
            {
                DecalStartIndices[i] = totalNumDecals;
                totalNumDecals += NumDecalsPerRoad[i];
            }

            DecalPoses.ResizeUninitialized(totalNumDecals);
        }
    }

    /// <summary>
    /// Iterate over each road segment's CenterLine spline samples to generate an evenly distributed set of positions
    /// to place manhole covers across a road surface. Once uniformly generated, perturb the result within the
    /// constraints of the road's lateral profile.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct SpreadDecalsThroughoutRoadsJob : IJobParallelForDefer
    {
        public uint RandomSeed;
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        [ReadOnly] public NativeArray<int> NumDecalsPerRoad;
        [ReadOnly] public NativeArray<int> DecalStartIndices;
        [ReadOnly] public ComponentDataFromEntity<LateralProfile> Profiles;
        [ReadOnly] public BufferFromEntity<LateralProfileEntityRef> ProfileBuffers;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> RoadSampleBuffers;
        [NativeDisableContainerSafetyRestriction] public NativeArray<RigidTransform> DecalPoses;

        public void Execute(int index)
        {
            var numDecals = NumDecalsPerRoad[index];
            if (numDecals == 0) return;

            var random = RandomUtility.ParallelForRandom(RandomSeed, index);
            var roadEntity = RoadEntities[index];
            var sampleBuffer = RoadSampleBuffers[roadEntity].Reinterpret<RigidTransform>().AsNativeArray();

            var startIndex = DecalStartIndices[index];
            var decalPositions = DecalPoses.GetSubArray(startIndex, numDecals);
            var decalTrackDistances = new NativeArray<float>(numDecals, Allocator.Temp);

            var distancePerDecal = SplineUtility.SplineLength(sampleBuffer) / (numDecals);

            // Offset the decals by a third instead of a half of the separation dist to decrease the chances of
            // overlapping decals being generated.
            var thirdDistPerDecal = distancePerDecal / 3f;
            var currDist = distancePerDecal / 2f;
            for (var i = 0; i < numDecals; i++)
            {
                decalTrackDistances[i] = currDist + random.NextFloat(-thirdDistPerDecal, thirdDistPerDecal);
                currDist += distancePerDecal;
            }

            var remappedSpline =
                SplineUtility.SampleSplineAtDistances(sampleBuffer, decalTrackDistances, Allocator.Temp);

            var lateralProfile = Profiles[ProfileBuffers[roadEntity].Reinterpret<Entity>()[0]];
            var minLateralRange = lateralProfile.LeftDrivableOffset.x;
            var maxLateralRange = lateralProfile.RightDrivableOffset.x;
            minLateralRange = math.clamp(minLateralRange, minLateralRange + 1, 0);
            maxLateralRange = math.clamp(maxLateralRange, 0, maxLateralRange - 1);

            for (var i = 0; i < remappedSpline.Length; i++)
            {
                var pose = remappedSpline[i];
                var randOffset = random.NextFloat(minLateralRange, maxLateralRange);
                var randRotation = quaternion.RotateY(random.NextFloat(0, math.PI * 2f));
                var offset = new float3(randOffset, 0f, 0f);
                decalPositions[i] = new RigidTransform
                {
                    pos = math.transform(pose, offset),
                    rot = math.mul(pose.rot, randRotation)
                };
            }

            remappedSpline.Dispose();
        }
    }
}
