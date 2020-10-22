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
    /// Dynamically resize array after async EntityQuery to use as a parameter for IJobParallelForDefer Jobs.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct ResizeNumExtraLanesArrayJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        public NativeList<int> NumExtraLanes;

        public void Execute()
        {
            NumExtraLanes.ResizeUninitialized(RoadEntities.Length);
        }
    }

    /// <summary>
    /// Sets a random number of lanes per road segment.
    /// Road segments sharing the same streetId will be assigned the same number of lanes.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct SetRandomNumOfLanes : IJobParallelForDefer
    {
        public int MaxNumLanes;
        public uint RandomSeed;
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        [WriteOnly] public NativeArray<int> NumLanesArray;
        [ReadOnly] public ComponentDataFromEntity<RoadCenterLineData> RoadCenterLineDataComponents;

        public void Execute(int index)
        {
            var streetId = RoadCenterLineDataComponents[RoadEntities[index]].StreetId;
            var random = RandomUtility.ParallelForRandom(RandomSeed, streetId);
            NumLanesArray[index] = random.NextInt(1, MaxNumLanes);
        }
    }

    /// <summary>
    /// Sets the number of lanes per road segment uniformly
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct SetNumOfLanes : IJobParallelForDefer
    {
        public int NumLanes;
        [WriteOnly] public NativeArray<int> NumLanesArray;

        public void Execute(int index)
        {
            NumLanesArray[index] = NumLanes;
        }
    }

    /// <summary>
    /// Prepares arrays to be filled with new lateral profile
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct ResizeLateralProfileEntityArraysJob : IJob
    {
        [ReadOnly] public NativeArray<int> NumLanes;
        public NativeList<Entity> ProfileEntities;
        public NativeList<Entity> SurfaceEntities;
        public NativeList<Entity> RoadMarkingEntities;
        public NativeList<int> SurfaceStartIndices;
        public NativeList<int> RoadMarkingStartIndices;

        public void Execute()
        {
            var numRoadEntities = NumLanes.Length;
            var totalNumLanes = 0;
            for (var i = 0; i < numRoadEntities; i++)
                totalNumLanes += NumLanes[i];

            ProfileEntities.ResizeUninitialized(numRoadEntities);
            SurfaceEntities.ResizeUninitialized(2 * totalNumLanes + 12 * numRoadEntities);
            RoadMarkingEntities.ResizeUninitialized(numRoadEntities + 2 * totalNumLanes);

            SurfaceStartIndices.ResizeUninitialized(numRoadEntities);
            RoadMarkingStartIndices.ResizeUninitialized(numRoadEntities);
            for (int i = 0, numSurfaces = 0, numRoadMarkings = 0; i < numRoadEntities; i++)
            {
                SurfaceStartIndices[i] = numSurfaces;
                RoadMarkingStartIndices[i] = numRoadMarkings;
                var numLanes = NumLanes[i];
                numSurfaces += 2 * numLanes + 12;
                numRoadMarkings += 2 * numLanes + 1;
            }
        }
    }

    /// <summary>
    /// Creates a dynamic number of road marking entities based on the number of lanes per road segment
    ///
    /// NOTE: ExclusiveEntityTransactions cannot be Burst compiled
    /// </summary>
    struct CreateRoadMarkingEntities : IJob
    {
        public EntityArchetype ProfileArchetype;
        public EntityArchetype SurfaceArchetype;
        public EntityArchetype RoadMarkingArchetype;
        public ExclusiveEntityTransaction Transaction;

        public NativeArray<Entity> ProfileEntities;
        public NativeArray<Entity> SurfaceEntities;
        public NativeArray<Entity> RoadMarkingEntities;

        public void Execute()
        {
            Transaction.CreateEntity(ProfileArchetype, ProfileEntities);
            Transaction.CreateEntity(SurfaceArchetype, SurfaceEntities);
            Transaction.CreateEntity(RoadMarkingArchetype, RoadMarkingEntities);
        }
    }

    /// <summary>
    /// Maps a standard lateral profile entity and its associates road surface and road marking entities to
    /// each road entity
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct AddDefaultLateralProfileToRoadsJob : IJobParallelForDefer
    {
        public LateralProfileParams ProfileParams;
        public RoadMarkingParams MarkingParams;
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        [ReadOnly] public NativeArray<Entity> ProfileEntities;
        [ReadOnly] public NativeArray<Entity> SurfaceEntities;
        [ReadOnly] public NativeArray<Entity> RoadMarkingEntities;
        [ReadOnly] public NativeArray<int> NumLanes;
        [ReadOnly] public NativeArray<int> SurfaceStartIndices;
        [ReadOnly] public NativeArray<int> RoadMarkingStartIndices;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<LateralProfile> Profiles;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<LateralProfileSurface> Surfaces;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<RoadMarking> RoadMarkings;
        [NativeDisableParallelForRestriction] public BufferFromEntity<LateralProfileEntityRef> ProfileBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<LateralProfileSurfaceEntityRef> SurfaceBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<LateralProfileSample> SampleBuffers;

        LateralProfileBuilder CreateBuilder(int index)
        {
            var numLanes = NumLanes[index];
            var numSurfaces = 2 * numLanes + 12;
            var surfaceStartIndex = SurfaceStartIndices[index];
            var surfaceEntities = SurfaceEntities.GetSubArray(surfaceStartIndex, numSurfaces);

            var numRoadMarkings = 2 * numLanes + 1;
            var roadMarkingStartIndex = RoadMarkingStartIndices[index];
            var roadMarkingEntities = RoadMarkingEntities.GetSubArray(roadMarkingStartIndex, numRoadMarkings);

            var builder = new LateralProfileBuilder(
                Allocator.Temp,
                RoadEntities[index],
                ProfileEntities[index],
                surfaceEntities,
                roadMarkingEntities);

            var curbHeight = math.max(ProfileParams.CurbHeight, ProfileParams.CurbRadius * 1.5f);

            for (var i = 0; i < numLanes; i++)
                builder.AddRoad(ProfileParams.LaneWidth);
            builder.AddRoad(1f);
            builder.AddGutter(ProfileParams.GutterWidth, ProfileParams.GutterDepth);
            builder.AddCurb(curbHeight, ProfileParams.CurbRadius / 2, ProfileParams.CurbRadius, 5);
            builder.AddExpansionJoint(ProfileParams.ExpansionJointWidth);
            builder.AddSidewalk(ProfileParams.SidewalkWidth);
            builder.AddEndCap(ProfileParams.EndCapHeight);

            builder.SwitchSides();

            for (var i = 0; i < numLanes; i++)
                builder.AddRoad(ProfileParams.LaneWidth);
            builder.AddRoad(1f);
            builder.AddGutter(ProfileParams.GutterWidth, ProfileParams.GutterDepth);
            builder.AddCurb(curbHeight, ProfileParams.CurbRadius / 2, ProfileParams.CurbRadius, 5);
            builder.AddExpansionJoint(ProfileParams.ExpansionJointWidth);
            builder.AddSidewalk(ProfileParams.SidewalkWidth);
            builder.AddEndCap(ProfileParams.EndCapHeight);

            builder.Complete();

            var solidWhiteMarking = new RoadMarking
            {
                Material = RoadMaterial.WhiteLaneLine,
                Width = MarkingParams.LineWidth,
                DashLength = 0,
                SeparationDistance = 0,
                BeginningOffset = 0
            };

            var solidYellowMarking = new RoadMarking
            {
                Material = RoadMaterial.YellowLaneLine,
                Width = MarkingParams.LineWidth,
                DashLength = 0,
                SeparationDistance = 0,
                BeginningOffset = 0
            };

            var dashedWhiteMarking = new RoadMarking
            {
                Material = RoadMaterial.WhiteLaneLine,
                Width = MarkingParams.LineWidth,
                DashLength = MarkingParams.DashLength,
                SeparationDistance = MarkingParams.DashSeparationDistance,
                BeginningOffset = MarkingParams.DashBeginningOffset
            };

            builder.AddRoadMarking(-numLanes, solidWhiteMarking);
            builder.AddRoadMarking(0, solidYellowMarking);
            builder.AddRoadMarking(numLanes, solidWhiteMarking);

            for (var i = 1; i < numLanes; i++)
            {
                builder.AddRoadMarking(i, dashedWhiteMarking);
                builder.AddRoadMarking(-i, dashedWhiteMarking);
            }

            return builder;
        }

        public void Execute(int index)
        {
            var numLanes = NumLanes[index];
            var roadEntity = RoadEntities[index];
            var profileEntity = ProfileEntities[index];

            var profileBuffer = ProfileBuffers[roadEntity].Reinterpret<Entity>();
            profileBuffer.Add(profileEntity);

            var builder = CreateBuilder(index);
            Profiles[profileEntity] = builder.Profile;

            var numSurfaces = 2 * numLanes + 12;
            var surfaceStartIndex = SurfaceStartIndices[index];
            var surfaceEntities = SurfaceEntities.GetSubArray(surfaceStartIndex, numSurfaces);
            SurfaceBuffers[profileEntity].Reinterpret<Entity>().AddRange(surfaceEntities);
            for (var i = 0; i < surfaceEntities.Length; i++)
                Surfaces[surfaceEntities[i]] = builder.Surfaces[i];

            var numRoadMarkings = 2 * numLanes + 1;
            var roadMarkingStartIndex = RoadMarkingStartIndices[index];
            var roadMarkingEntities = RoadMarkingEntities.GetSubArray(roadMarkingStartIndex, numRoadMarkings);
            for (var i = 0; i < roadMarkingEntities.Length; i++)
                RoadMarkings[roadMarkingEntities[i]] = builder.RoadMarkings[i];

            SampleBuffers[profileEntity].Reinterpret<float2>().AddRange(builder.Samples.AsArray());
            builder.Dispose();
        }
    }
}
