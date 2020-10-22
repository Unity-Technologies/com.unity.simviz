using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.RoadMeshing.Jobs;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing.Systems
{
    /// <summary>
    /// Generates road geometry from queried road spline entities
    /// </summary>
    [DisableAutoCreation]
    class RoadBuilderSystem : ComponentSystem, IGeneratorSystem<RoadBuilderParameters>
    {
        public RoadBuilderParameters Parameters { get; set; }

        EntityArchetype m_IntersectionArchetype;
        EntityArchetype m_RoadArchetype;
        EntityArchetype m_CornerArchetype;
        EntityArchetype m_ProfileArchetype;
        EntityArchetype m_SurfaceArchetype;
        EntityArchetype m_RoadMarkingArchetype;
        EntityArchetype m_MeshArchetype;

        ExclusiveEntityTransaction m_Transaction;

        NativeList<Entity> m_RoadEntities;
        NativeList<Entity> m_IntersectionEntities;
        NativeList<Entity> m_CornerEntities;
        NativeList<Entity> m_SurfaceEntities;
        NativeList<Entity> m_RoadMarkingEntities;

        NativeHashMap<Entity, float> m_IncomingRoadCropIndicesMap;
        NativeHashMap<Entity, float> m_OutgoingRoadCropIndicesMap;

        protected override void OnCreate()
        {
            m_IntersectionArchetype = EntityManager.CreateArchetype(
                typeof(IntersectionData),
                typeof(IntersectionCornerEntityRef),
                typeof(IntersectionRoadConnection));
            m_RoadArchetype = EntityManager.CreateArchetype(
                typeof(RoadCenterLineData),
                typeof(RoadCenterLineSample),
                typeof(LateralProfileEntityRef));
            m_CornerArchetype = EntityManager.CreateArchetype(
                typeof(IntersectionCorner),
                typeof(CornerRoadCropIndices),
                typeof(RoadCenterLineSample),
                typeof(LeftLaneEdgeSample),
                typeof(RightLaneEdgeSample),
                typeof(RoundedCornerSample),
                typeof(LateralProfileEntityRef),
                typeof(IntersectionGridSample),
                typeof(IntersectionMeshInsideEdgeSample),
                typeof(IntersectionMeshOutsideEdgeSample));
            m_ProfileArchetype = EntityManager.CreateArchetype(
                typeof(LateralProfile),
                typeof(LateralProfileSurfaceEntityRef),
                typeof(LateralProfileSample));
            m_SurfaceArchetype = EntityManager.CreateArchetype(
                typeof(LateralProfileSurface),
                typeof(SubMesh),
                typeof(CombinedVertex),
                typeof(Triangle));
            m_RoadMarkingArchetype = EntityManager.CreateArchetype(
                typeof(RoadMarking),
                typeof(SubMesh),
                typeof(CombinedVertex),
                typeof(Triangle),
                typeof(RoadMarkingSample),
                typeof(DashPoint));
            m_MeshArchetype = EntityManager.CreateArchetype(
                typeof(IntersectionMesh),
                typeof(CombinedVertex),
                typeof(Triangle),
                typeof(SubMesh));
        }

        void ScheduleSplineToRoadJobs(JobHandle inputDeps, out JobHandle outputDeps)
        {
            var splitIndicesMap = new NativeMultiHashMap<int, IntersectionPoint>(0, Allocator.TempJob);
            var selfIntersectionPairs = new NativeList<SelfIntersectionPair>(Allocator.TempJob);
            var roadsThatSelfIntersect = new NativeList<bool>(Allocator.TempJob);
            var roadsThatIntersect = new NativeList<bool>(Allocator.TempJob);
            var intersectionMetadata = new NativeList<IntersectionPoint>(Allocator.TempJob);
            var roadIntersectionIndexRanges = new NativeList<IntersectionRecordRange>(Allocator.TempJob);
            var uniqueIntersectionPoints = new NativeList<float2>(Allocator.TempJob);
            var nonIntersectingSplineEntities = new NativeList<Entity>(Allocator.TempJob);
            var nonIntersectingRoadEntities = new NativeList<Entity>(Allocator.TempJob);
            var roadSplineRanges = new NativeList<RoadSplineRange>(Allocator.TempJob);
            var intersectionToRoadsMap = new NativeMultiHashMap<Entity, IntersectionRoadConnection>(0, Allocator.TempJob);
            var roadConnectionRanges = new NativeList<RoadConnectionArrayRange>(Allocator.TempJob);
            var roadConnections = new NativeList<IntersectionRoadConnection>(Allocator.TempJob);

            m_IntersectionEntities = new NativeList<Entity>(Allocator.TempJob);
            m_RoadEntities = new NativeList<Entity>(Allocator.TempJob);
            m_CornerEntities = new NativeList<Entity>(Allocator.TempJob);

            var query = EntityManager.CreateEntityQuery(typeof(RoadSplineSample));
            query.AddDependency(inputDeps);
            var roadSplineEntities = query.ToEntityArrayAsync(Allocator.TempJob, out var queryHandle);

            var resizeRoadSplineListsJob = new ResizeRoadSplineContainersJob
            {
                RoadSplineEntities = roadSplineEntities,
                RoadsThatIntersect = roadsThatIntersect,
                RoadsThatSelfIntersect = roadsThatSelfIntersect,
                SplitIndicesMap = splitIndicesMap
            }.Schedule(queryHandle);

            var createSelfIntersectionPairsJob = new CreateSelfIntersectionPairsJob
            {
                SampleBuffers = GetBufferFromEntity<RoadSplineSample>(true),
                RoadSplineEntities = roadSplineEntities,
                SelfIntersectionPairs = selfIntersectionPairs
            }.Schedule(resizeRoadSplineListsJob);

            var findSelfIntersectionsJob = new FindSelfIntersectionsJob
            {
                RoadSplinesEntities = roadSplineEntities,
                RoadSplineSampleBuffers = GetBufferFromEntity<RoadSplineSample>(true),
                SelfIntersectionPairs = selfIntersectionPairs.AsDeferredJobArray(),
                RoadsThatSelfIntersect = roadsThatSelfIntersect.AsDeferredJobArray(),
                SplitIndicesMap = splitIndicesMap.AsParallelWriter()
            }.Schedule(selfIntersectionPairs, 1, createSelfIntersectionPairsJob);

            var findIntersectionsJob = new FindIntersectionsJob
            {
                RoadSplinesEntities = roadSplineEntities,
                RoadSplineSampleBuffers = GetBufferFromEntity<RoadSplineSample>(true),
                RoadsThatIntersect = roadsThatIntersect.AsDeferredJobArray(),
                SplitIndicesMap = splitIndicesMap.AsParallelWriter()
            }.Schedule(roadsThatIntersect, 1, findSelfIntersectionsJob);

            var identifyUniqueIntersectionsJob = new IdentifyUniqueIntersectionsJob
            {
                IntersectionRecordRanges = roadIntersectionIndexRanges,
                IntersectionMetaData = intersectionMetadata,
                SplitIndicesMap = splitIndicesMap,
                UniqueIntersectionPoints = uniqueIntersectionPoints
            }.Schedule(findIntersectionsJob);

            var createIntersectionEntitiesJob = new CreateIntersectionEntitiesJob
            {
                Transaction = m_Transaction,
                IntersectionArchetype = m_IntersectionArchetype,
                IntersectionEntities = m_IntersectionEntities,
                UniqueIntersectionPoints = uniqueIntersectionPoints.AsDeferredJobArray()
            }.Schedule(identifyUniqueIntersectionsJob);

            var initializeComponentsOnIntersectionEntitiesJob = new InitializeComponentsOnIntersectionEntitiesJob
            {
                IntersectionEntities = m_IntersectionEntities.AsDeferredJobArray(),
                UniqueIntersectionPoints = uniqueIntersectionPoints.AsDeferredJobArray(),
                IntersectionDataComponents = GetComponentDataFromEntity<IntersectionData>()
            }.Schedule(createIntersectionEntitiesJob);

            var countNonIntersectingRoadsJob = new CountNonIntersectingRoadsJob
            {
                SplineEntities = roadSplineEntities,
                NonIntersectingSplineEntities = nonIntersectingSplineEntities,
                RoadsThatIntersect = roadsThatIntersect.AsDeferredJobArray(),
                RoadsThatSelfIntersect = roadsThatSelfIntersect.AsDeferredJobArray()
            }.Schedule(initializeComponentsOnIntersectionEntitiesJob);

            var createNonIntersectingRoadsJob = new CreateNonIntersectingRoadsJob
            {
                Transaction = m_Transaction,
                RoadArchetype = m_RoadArchetype,
                NonIntersectingSplineEntities = nonIntersectingSplineEntities.AsDeferredJobArray(),
                NonIntersectingRoadEntities = nonIntersectingRoadEntities
            }.Schedule(countNonIntersectingRoadsJob);

            var initializeNonIntersectingRoadsJob = new InitializeNonIntersectingRoadsJob
            {
                NonIntersectingRoadEntities = nonIntersectingRoadEntities.AsDeferredJobArray(),
                NonIntersectingSplineEntities = nonIntersectingSplineEntities.AsDeferredJobArray(),
                RoadCenterLineDataComponents = GetComponentDataFromEntity<RoadCenterLineData>(),
                RoadCenterLineSampleBuffers = GetBufferFromEntity<RoadCenterLineSample>(),
                SplineSampleBuffers = GetBufferFromEntity<RoadSplineSample>(true)
            }.Schedule(nonIntersectingRoadEntities, 1, createNonIntersectingRoadsJob);

            var countNewRoadEntitiesFromSplineSplitsJob = new CountNewRoadEntitiesFromSplineSplitsJob
            {
                RoadSplineRanges = roadSplineRanges,
                NonIntersectingRoadEntities = nonIntersectingRoadEntities.AsDeferredJobArray(),
                IntersectionEntities = m_IntersectionEntities.AsDeferredJobArray(),
                IntersectionMetaData = intersectionMetadata.AsDeferredJobArray(),
                IntersectionRecordRanges = roadIntersectionIndexRanges.AsDeferredJobArray(),
                RoadSplineSampleBuffers = GetBufferFromEntity<RoadSplineSample>(true),
                RoadSplinesEntities = roadSplineEntities
            }.Schedule(initializeNonIntersectingRoadsJob);

            var createRoadEntities = new CreateRoadEntities
            {
                Transaction = m_Transaction,
                RoadArchetype = m_RoadArchetype,
                RoadSplineRanges = roadSplineRanges.AsDeferredJobArray(),
                RoadEntities = m_RoadEntities
            }.Schedule(countNewRoadEntitiesFromSplineSplitsJob);

            var splitSplinesAtIntersectionsIntoUniqueRoadsJob = new SplitSplinesAtIntersectionsIntoUniqueRoadsJob
            {
                RoadEntities = m_RoadEntities.AsDeferredJobArray(),
                RoadSplineRanges = roadSplineRanges.AsDeferredJobArray(),
                RoadCenterLineDataComponents = GetComponentDataFromEntity<RoadCenterLineData>(),
                RoadCenterLineSampleBuffers = GetBufferFromEntity<RoadCenterLineSample>(),
                RoadSplineSampleBuffers = GetBufferFromEntity<RoadSplineSample>(true)
            }.Schedule(m_RoadEntities, 1, createRoadEntities);

            var combineIntersectingAndNonIntersectingRoadEntitiesJob = new CombineRoadEntitiesJob
            {
                NonIntersectingRoadEntities = nonIntersectingRoadEntities.AsDeferredJobArray(),
                RoadEntities = m_RoadEntities
            }.Schedule(splitSplinesAtIntersectionsIntoUniqueRoadsJob);

            var allocateRoadsToIntersectionsMapJob = new AllocateRoadsToIntersectionsMapJob
            {
                RoadEntities = m_RoadEntities.AsDeferredJobArray(),
                IntersectionsToRoadsMap = intersectionToRoadsMap
            }.Schedule(combineIntersectingAndNonIntersectingRoadEntitiesJob);

            var mapIntersectionToRoadsJob = new MapIntersectionToRoadsJob
            {
                RoadEntities = m_RoadEntities.AsDeferredJobArray(),
                IntersectionsToRoadsMap = intersectionToRoadsMap.AsParallelWriter(),
                RoadCenterLineDataComponents = GetComponentDataFromEntity<RoadCenterLineData>()
            }.Schedule(m_RoadEntities, 1, allocateRoadsToIntersectionsMapJob);

            var createCornerEntitiesJob = new CreateCornerEntitiesJob
            {
                Transaction = m_Transaction,
                CornerArchetype = m_CornerArchetype,
                CornerEntities = m_CornerEntities,
                IntersectionsToRoadsMap = intersectionToRoadsMap,
            }.Schedule(mapIntersectionToRoadsJob);

            var calculateIntersectionToRoadsMapRanges = new CalculateIntersectionToRoadsMapRangesJob
            {
                IntersectionsToRoadsMap = intersectionToRoadsMap,
                RoadConnectionRanges = roadConnectionRanges,
                RoadConnections = roadConnections
            }.Schedule(createCornerEntitiesJob);

            var writeMapToBufferJob = new WriteIntersectionsToRoadsMapToBufferJob
            {
                IntersectionCornerBuffers = GetBufferFromEntity<IntersectionCornerEntityRef>(),
                RoadConnectionBuffers = GetBufferFromEntity<IntersectionRoadConnection>(),
                RoadConnectionRanges = roadConnectionRanges.AsDeferredJobArray(),
                RoadConnections = roadConnections.AsDeferredJobArray(),
                CornerEntities = m_CornerEntities.AsDeferredJobArray()
            }.Schedule(roadConnectionRanges, 1, calculateIntersectionToRoadsMapRanges);

            roadSplineEntities.Dispose(writeMapToBufferJob);
            splitIndicesMap.Dispose(writeMapToBufferJob);
            selfIntersectionPairs.Dispose(writeMapToBufferJob);
            roadsThatSelfIntersect.Dispose(writeMapToBufferJob);
            roadsThatIntersect.Dispose(writeMapToBufferJob);
            intersectionMetadata.Dispose(writeMapToBufferJob);
            roadIntersectionIndexRanges.Dispose(writeMapToBufferJob);
            uniqueIntersectionPoints.Dispose(writeMapToBufferJob);
            nonIntersectingSplineEntities.Dispose(writeMapToBufferJob);
            nonIntersectingRoadEntities.Dispose(writeMapToBufferJob);
            roadSplineRanges.Dispose(writeMapToBufferJob);
            intersectionToRoadsMap.Dispose(writeMapToBufferJob);
            roadConnectionRanges.Dispose(writeMapToBufferJob);
            roadConnections.Dispose(writeMapToBufferJob);

            outputDeps = writeMapToBufferJob;
        }

        void ScheduleLateralProfileCreationJobs(JobHandle inputDeps, out JobHandle outputDeps)
        {
            m_SurfaceEntities = new NativeList<Entity>(Allocator.TempJob);
            m_RoadMarkingEntities = new NativeList<Entity>(Allocator.TempJob);

            var profileEntities = new NativeList<Entity>(Allocator.TempJob);
            var numLanesArray = new NativeList<int>(Allocator.TempJob);
            var surfaceStartIndices = new NativeList<int>(Allocator.TempJob);
            var roadMarkingStartIndices = new NativeList<int>(Allocator.TempJob);

            var resizeNumExtraLanesArrayJob = new ResizeNumExtraLanesArrayJob
            {
                RoadEntities = m_RoadEntities.AsDeferredJobArray(),
                NumExtraLanes = numLanesArray
            }.Schedule(inputDeps);

            JobHandle calculateNumOfExtraLanesJobHandle;
            if (Parameters.lateralProfileParameters.randomNumLanes)
            {
                var randomSeed = RandomUtility.CombineSeedWithBaseSeed(
                    Parameters.baseRandomSeed, Parameters.lateralProfileParameters.randomSeed);
                var setNumOfExtraLanesJob = new SetRandomNumOfLanes
                {
                    MaxNumLanes = Parameters.lateralProfileParameters.numLanes,
                    RoadEntities = m_RoadEntities.AsDeferredJobArray(),
                    RandomSeed = randomSeed,
                    NumLanesArray = numLanesArray.AsDeferredJobArray(),
                    RoadCenterLineDataComponents = GetComponentDataFromEntity<RoadCenterLineData>()
                }.Schedule(numLanesArray, 2, resizeNumExtraLanesArrayJob);
                calculateNumOfExtraLanesJobHandle = setNumOfExtraLanesJob;
            }
            else
            {
                var setNumOfExtraLanesJob = new SetNumOfLanes
                {
                    NumLanes = Parameters.lateralProfileParameters.numLanes,
                    NumLanesArray = numLanesArray.AsDeferredJobArray()
                }.Schedule(numLanesArray, 2, resizeNumExtraLanesArrayJob);
                calculateNumOfExtraLanesJobHandle = setNumOfExtraLanesJob;
            }

            var resizeLateralProfileEntityArrays = new ResizeLateralProfileEntityArraysJob
            {
                NumLanes = numLanesArray.AsDeferredJobArray(),
                SurfaceStartIndices = surfaceStartIndices,
                RoadMarkingStartIndices = roadMarkingStartIndices,
                ProfileEntities = profileEntities,
                SurfaceEntities = m_SurfaceEntities,
                RoadMarkingEntities = m_RoadMarkingEntities,
            }.Schedule(calculateNumOfExtraLanesJobHandle);

            var createRoadMarkingEntities = new CreateRoadMarkingEntities
            {
                Transaction = m_Transaction,
                ProfileArchetype = m_ProfileArchetype,
                SurfaceArchetype = m_SurfaceArchetype,
                RoadMarkingArchetype = m_RoadMarkingArchetype,
                ProfileEntities = profileEntities.AsDeferredJobArray(),
                SurfaceEntities = m_SurfaceEntities.AsDeferredJobArray(),
                RoadMarkingEntities = m_RoadMarkingEntities.AsDeferredJobArray(),
            }.Schedule(resizeLateralProfileEntityArrays);

            var addDefaultLateralProfileToRoadsJob = new AddDefaultLateralProfileToRoadsJob
            {
                ProfileParams = Parameters.lateralProfileParameters.GetParams(),
                MarkingParams = Parameters.roadMarkingParameters.GetParams(),
                RoadEntities = m_RoadEntities.AsDeferredJobArray(),
                ProfileEntities = profileEntities.AsDeferredJobArray(),
                SurfaceEntities = m_SurfaceEntities.AsDeferredJobArray(),
                RoadMarkingEntities = m_RoadMarkingEntities.AsDeferredJobArray(),
                NumLanes = numLanesArray.AsDeferredJobArray(),
                SurfaceStartIndices = surfaceStartIndices.AsDeferredJobArray(),
                RoadMarkingStartIndices = roadMarkingStartIndices.AsDeferredJobArray(),
                Profiles = GetComponentDataFromEntity<LateralProfile>(),
                Surfaces = GetComponentDataFromEntity<LateralProfileSurface>(),
                RoadMarkings = GetComponentDataFromEntity<RoadMarking>(),
                ProfileBuffers = GetBufferFromEntity<LateralProfileEntityRef>(),
                SurfaceBuffers = GetBufferFromEntity<LateralProfileSurfaceEntityRef>(),
                SampleBuffers = GetBufferFromEntity<LateralProfileSample>()
            }.Schedule(numLanesArray, 1, createRoadMarkingEntities);

            profileEntities.Dispose(addDefaultLateralProfileToRoadsJob);
            numLanesArray.Dispose(addDefaultLateralProfileToRoadsJob);
            surfaceStartIndices.Dispose(addDefaultLateralProfileToRoadsJob);
            roadMarkingStartIndices.Dispose(addDefaultLateralProfileToRoadsJob);

            // NOTE: We call the ScheduleBatchedJobs API here to force the jobs scheduled before this point to start
            //     executing, otherwise the scheduled jobs will continue to wait until the final job is scheduled in
            //     this system.
            JobHandle.ScheduleBatchedJobs();
            outputDeps = addDefaultLateralProfileToRoadsJob;
        }

        void ScheduleCornerCreationJobs(JobHandle inputDeps, out JobHandle outputDeps)
        {
            m_IncomingRoadCropIndicesMap = new NativeHashMap<Entity, float>(0, Allocator.TempJob);
            m_OutgoingRoadCropIndicesMap = new NativeHashMap<Entity, float>(0, Allocator.TempJob);

            var cornerProfileEntities = new NativeList<Entity>(Allocator.TempJob);
            var cornerSurfaceEntities = new NativeList<Entity>(Allocator.TempJob);
            var copiedLateralProfileSurfaces = new NativeList<LateralProfileSurface>(Allocator.TempJob);
            var copiedLateralProfileSamples = new NativeList<float2>(Allocator.TempJob);
            var numSurfacesAndSamples = new NativeArray<int>(2, Allocator.TempJob);
            var cornerLateralProfileSurfaceCopyRange =
                new NativeList<LateralProfileCopyRange>(Allocator.TempJob);

            var identifyCornersJob = new IdentifyCornersJob
            {
                CornerRadius = Parameters.intersectionParameters.cornerRadius,
                IntersectionEntities = m_IntersectionEntities.AsDeferredJobArray(),
                RoadCenterLineBuffers = GetBufferFromEntity<RoadCenterLineSample>(true),
                RoadBuffers = GetBufferFromEntity<IntersectionRoadConnection>(),
                CornerEntityBuffers = GetBufferFromEntity<IntersectionCornerEntityRef>(true),
                CornerData = GetComponentDataFromEntity<IntersectionCorner>()
            }.Schedule(m_IntersectionEntities, 1, inputDeps);

            var populateCornerRoadEdgeBuffersJob = new PopulateCornerRoadEdgeBuffersJob
            {
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                Profiles = GetComponentDataFromEntity<LateralProfile>(true),
                CornerData = GetComponentDataFromEntity<IntersectionCorner>(true),
                ProfileBuffers = GetBufferFromEntity<LateralProfileEntityRef>(true),
                LeftLaneEdgeBuffers = GetBufferFromEntity<LeftLaneEdgeSample>(),
                RightLaneEdgeBuffers = GetBufferFromEntity<RightLaneEdgeSample>(),
                RoadCenterLineBuffers = GetBufferFromEntity<RoadCenterLineSample>(true)
            }.Schedule(m_CornerEntities, 1, identifyCornersJob);

            var roundedCornerJob = new RoundedCornerJob
            {
                CornerData = GetComponentDataFromEntity<IntersectionCorner>(),
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                CornerSamplesPerMeter = Parameters.intersectionParameters.cornerSamplesPerMeter,
                LeftBufferLookup = GetBufferFromEntity<LeftLaneEdgeSample>(),
                RightBufferLookup = GetBufferFromEntity<RightLaneEdgeSample>(),
                RoundedCornerSampleBuffers = GetBufferFromEntity<RoundedCornerSample>()
            }.Schedule(m_CornerEntities, 1, populateCornerRoadEdgeBuffersJob);

            var resizeCropMapsJob = new ResizeCropMapsJob
            {
                RoadEntities = m_RoadEntities.AsDeferredJobArray(),
                IncomingRoadCropIndices = m_IncomingRoadCropIndicesMap,
                OutgoingRoadCropIndices = m_OutgoingRoadCropIndicesMap
            }.Schedule(roundedCornerJob);

            var stitchIntersectionOutlineJob = new StitchIntersectionOutlineJob
            {
                CornerSamplesPerMeter =  Parameters.intersectionParameters.cornerSamplesPerMeter,
                IntersectionEntities = m_IntersectionEntities.AsDeferredJobArray(),
                CornerData = GetComponentDataFromEntity<IntersectionCorner>(true),
                CornerRoadCropIndexData = GetComponentDataFromEntity<CornerRoadCropIndices>(),
                IncomingRoadCropIndices = m_IncomingRoadCropIndicesMap.AsParallelWriter(),
                OutgoingRoadCropIndices = m_OutgoingRoadCropIndicesMap.AsParallelWriter(),
                CornerEntityBuffers = GetBufferFromEntity<IntersectionCornerEntityRef>(true),
                LeftBufferLookup = GetBufferFromEntity<LeftLaneEdgeSample>(true),
                RightBufferLookup = GetBufferFromEntity<RightLaneEdgeSample>(true),
                RoundedCornerBuffers = GetBufferFromEntity<RoundedCornerSample>(true),
                RoadCenterLineSampleBuffers = GetBufferFromEntity<RoadCenterLineSample>(),
            }.Schedule(m_IntersectionEntities, 1, resizeCropMapsJob);

            var countCornerLateralProfileEntitiesJob = new CountCornerLateralProfileEntitiesJob
            {
                CornerLateralProfileCopyRange = cornerLateralProfileSurfaceCopyRange,
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                NumSurfacesAndSamples = numSurfacesAndSamples,
                CopiedCornerSurfaces = copiedLateralProfileSurfaces,
                CopiedCornerSamples = copiedLateralProfileSamples,
                Profiles = GetComponentDataFromEntity<LateralProfile>(true),
                Surfaces = GetComponentDataFromEntity<LateralProfileSurface>(true),
                IntersectionCorners = GetComponentDataFromEntity<IntersectionCorner>(true),
                ProfileBuffers = GetBufferFromEntity<LateralProfileEntityRef>(true),
                SurfaceBuffers = GetBufferFromEntity<LateralProfileSurfaceEntityRef>(true),
                LateralProfileSampleBuffers = GetBufferFromEntity<LateralProfileSample>(true)
            }.Schedule(stitchIntersectionOutlineJob);

            var createCornerLateralProfileEntitiesJob = new CreateCornerLateralProfileEntitiesJob
            {
                Transaction = m_Transaction,
                SurfaceArchetype = m_SurfaceArchetype,
                ProfileArchetype = m_ProfileArchetype,
                NumProfilesAndSurfaces = numSurfacesAndSamples,
                AllSurfaceEntities = m_SurfaceEntities,
                CornerSurfaceEntities = cornerSurfaceEntities,
                ProfileEntities = cornerProfileEntities,
                CornerEntities = m_CornerEntities.AsDeferredJobArray()
            }.Schedule(countCornerLateralProfileEntitiesJob);

            var copyLateralProfileSurfacesAndSamplesForCorners = new CopyLateralProfileSurfacesAndSamplesForCorners
            {
                CornerProfileEntities = cornerProfileEntities.AsDeferredJobArray(),
                CopiedCornerSurfaces = copiedLateralProfileSurfaces.AsDeferredJobArray(),
                CopiedCornerSamples = copiedLateralProfileSamples.AsDeferredJobArray(),
                CornerLateralProfileSurfaceCopyRange = cornerLateralProfileSurfaceCopyRange.AsDeferredJobArray(),
                LateralProfileSampleBuffers = GetBufferFromEntity<LateralProfileSample>(true),
                SurfaceBuffers = GetBufferFromEntity<LateralProfileSurfaceEntityRef>(true),
                Surfaces = GetComponentDataFromEntity<LateralProfileSurface>(true)
            }.Schedule(cornerLateralProfileSurfaceCopyRange, 1, createCornerLateralProfileEntitiesJob);

            var addLateralProfileToCornersJob = new WriteLateralProfileToCornersJob
            {
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                CornerProfileEntities = cornerProfileEntities.AsDeferredJobArray(),
                CornerSurfaceEntities = cornerSurfaceEntities.AsDeferredJobArray(),
                CornerLateralProfileSurfaceCopyRange = cornerLateralProfileSurfaceCopyRange.AsDeferredJobArray(),
                CopiedCornerSamples = copiedLateralProfileSamples.AsDeferredJobArray(),
                CopiedCornerSurfaces = copiedLateralProfileSurfaces.AsDeferredJobArray(),
                Profiles = GetComponentDataFromEntity<LateralProfile>(),
                Surfaces = GetComponentDataFromEntity<LateralProfileSurface>(),
                ProfileBuffers = GetBufferFromEntity<LateralProfileEntityRef>(),
                SurfaceBuffers = GetBufferFromEntity<LateralProfileSurfaceEntityRef>(),
                LateralProfileSampleBuffers = GetBufferFromEntity<LateralProfileSample>()
            }.Schedule(m_CornerEntities, 1, copyLateralProfileSurfacesAndSamplesForCorners);

            cornerProfileEntities.Dispose(addLateralProfileToCornersJob);
            cornerSurfaceEntities.Dispose(addLateralProfileToCornersJob);
            copiedLateralProfileSurfaces.Dispose(addLateralProfileToCornersJob);
            copiedLateralProfileSamples.Dispose(addLateralProfileToCornersJob);
            numSurfacesAndSamples.Dispose(addLateralProfileToCornersJob);
            cornerLateralProfileSurfaceCopyRange.Dispose(addLateralProfileToCornersJob);

            outputDeps = addLateralProfileToCornersJob;
        }

        void ScheduleIntersectionGeometryJobs(JobHandle inputDeps, out JobHandle outputDeps)
        {
            var smoothCornerSampleInterpolationJob = new SmoothCornerSampleInterpolationJob
            {
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                CenterLineBuffers = GetBufferFromEntity<RoadCenterLineSample>()
            }.Schedule(m_CornerEntities, 1, inputDeps);

            var createIntersectionContoursJob = new CreateIntersectionContoursJob
            {
                NumSamples = Parameters.intersectionParameters.numIntersectionSamples,
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                CenterLineBuffers = GetBufferFromEntity<RoadCenterLineSample>(),
                CornerData = GetComponentDataFromEntity<IntersectionCorner>(),
                CornerRoadCropIndices = GetComponentDataFromEntity<CornerRoadCropIndices>(),
                IntersectionGridSamples = GetBufferFromEntity<IntersectionGridSample>(),
                InsideEdgeBuffers = GetBufferFromEntity<IntersectionMeshInsideEdgeSample>(),
                OutsideEdgeBuffers = GetBufferFromEntity<IntersectionMeshOutsideEdgeSample>()
            }.Schedule(m_CornerEntities, 1, smoothCornerSampleInterpolationJob);

            var raiseContoursJob = new RaiseContoursJob
            {
                NumSamples = Parameters.intersectionParameters.numIntersectionSamples,
                IntersectionEntities = m_IntersectionEntities.AsDeferredJobArray(),
                CornerEntities = GetBufferFromEntity<IntersectionCornerEntityRef>(),
                GridSamples = GetBufferFromEntity<IntersectionGridSample>()
            }.Schedule(m_IntersectionEntities, 1, createIntersectionContoursJob);

            var interpolateContoursJob = new InterpolateContoursJob
            {
                NumSamples = Parameters.intersectionParameters.numIntersectionSamples,
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                IntersectionGridSamples = GetBufferFromEntity<IntersectionGridSample>(),
                InsideEdgeBuffers = GetBufferFromEntity<IntersectionMeshInsideEdgeSample>()
            }.Schedule(m_CornerEntities, 1, raiseContoursJob);

            outputDeps = interpolateContoursJob;
        }

        void ScheduleRoadCropJobs(JobHandle inputDeps, out JobHandle outputDeps)
        {
            var incomingRoadEntities = new NativeList<Entity>(Allocator.TempJob);
            var incomingRoadCropIndices = new NativeList<float>(Allocator.TempJob);
            var outgoingRoadCropEntities = new NativeList<Entity>(Allocator.TempJob);
            var outgoingRoadCropIndices = new NativeList<float>(Allocator.TempJob);
            var storedCropInfo = new NativeList<KeyValuePair<int, RigidTransform>>(Allocator.TempJob);

            var allocateCropArraysJob = new AllocateCropArraysJob
            {
                IncomingRoadCropIndicesMap = m_IncomingRoadCropIndicesMap,
                OutgoingRoadCropIndicesMap = m_OutgoingRoadCropIndicesMap,
                IncomingRoadEntities = incomingRoadEntities,
                IncomingRoadCropIndices = incomingRoadCropIndices,
                OutgoingRoadCropEntities = outgoingRoadCropEntities,
                OutgoingRoadCropIndices = outgoingRoadCropIndices,
                StoredCropInformation = storedCropInfo
            }.Schedule(inputDeps);

            var storeCropsOfOutgoingRoadsJob = new StoreCropsOfOutgoingRoadsJob
            {
                CenterLineBuffers = GetBufferFromEntity<RoadCenterLineSample>(),
                OutgoingRoadEntities = outgoingRoadCropEntities.AsDeferredJobArray(),
                OutgoingRoadCropIndices = outgoingRoadCropIndices.AsDeferredJobArray(),
                StoredCropInfo = storedCropInfo.AsDeferredJobArray()
            }.Schedule(outgoingRoadCropEntities, 1, allocateCropArraysJob);

            var cropIncomingRoadsJob = new CropIncomingRoadsJob
            {
                CenterLineBuffers = GetBufferFromEntity<RoadCenterLineSample>(),
                IncomingRoadEntities = incomingRoadEntities.AsDeferredJobArray(),
                IncomingRoadCropIndices = incomingRoadCropIndices.AsDeferredJobArray()
            }.Schedule(incomingRoadCropIndices, 1, storeCropsOfOutgoingRoadsJob);

            var cropOutgoingRoadsJob = new CropOutgoingRoadsJob
            {
                CenterLineBuffers = GetBufferFromEntity<RoadCenterLineSample>(),
                OutgoingRoadEntities = outgoingRoadCropEntities.AsDeferredJobArray(),
                StoredCropInformation = storedCropInfo.AsDeferredJobArray()
            }.Schedule(storedCropInfo, 1, cropIncomingRoadsJob);

            incomingRoadEntities.Dispose(cropOutgoingRoadsJob);
            incomingRoadCropIndices.Dispose(cropOutgoingRoadsJob);
            outgoingRoadCropEntities.Dispose(cropOutgoingRoadsJob);
            outgoingRoadCropIndices.Dispose(cropOutgoingRoadsJob);
            storedCropInfo.Dispose(cropOutgoingRoadsJob);

            outputDeps = cropOutgoingRoadsJob;
        }

        void ScheduleMeshCreationJobs(JobHandle inputDeps, out JobHandle outputDeps)
        {
            var intersectionMeshEntities = new NativeList<Entity>(Allocator.TempJob);

            var generateLaneLinesJob = new GenerateLaneLinesJob
            {
                RoadMarkingEntities = m_RoadMarkingEntities.AsDeferredJobArray(),
                Profiles = GetComponentDataFromEntity<LateralProfile>(true),
                Surfaces = GetComponentDataFromEntity<LateralProfileSurface>(true),
                RoadMarkings = GetComponentDataFromEntity<RoadMarking>(true),
                ProfileSampleBuffers = GetBufferFromEntity<LateralProfileSample>(true),
                SampleBuffers = GetBufferFromEntity<RoadCenterLineSample>(true),
                SubMeshBuffers = GetBufferFromEntity<SubMesh>(),
                VertexBuffers = GetBufferFromEntity<CombinedVertex>(),
                TriangleBuffers = GetBufferFromEntity<Triangle>(),
                RoadMarkingSampleBuffers = GetBufferFromEntity<RoadMarkingSample>(),
                DashPointBuffers = GetBufferFromEntity<DashPoint>()
            }.Schedule(m_RoadMarkingEntities, 1, inputDeps);

            var loftRoadProfilesJob = new LoftLateralProfileJob
            {
                SurfaceEntities = m_SurfaceEntities.AsDeferredJobArray(),
                RoadMarkings = GetComponentDataFromEntity<RoadMarking>(true),
                Profiles = GetComponentDataFromEntity<LateralProfile>(true),
                Surfaces = GetComponentDataFromEntity<LateralProfileSurface>(true),
                LoftPathBuffers = GetBufferFromEntity<RoadCenterLineSample>(true),
                ProfileSampleBuffers = GetBufferFromEntity<LateralProfileSample>(true),
                RoadMarkingSampleBuffers = GetBufferFromEntity<RoadMarkingSample>(true),
                DashPointBuffers = GetBufferFromEntity<DashPoint>(true),
                CombinedVertexBuffers = GetBufferFromEntity<CombinedVertex>(),
                TriangleBuffers = GetBufferFromEntity<Triangle>(),
                SubMeshBuffers = GetBufferFromEntity<SubMesh>()
            }.Schedule(m_SurfaceEntities, 1, generateLaneLinesJob);

            var createMeshEntitiesJob = new CreateIntersectionMeshEntitiesJob
            {
                Transaction = m_Transaction,
                IntersectionMeshArchetype = m_MeshArchetype,
                IntersectionMeshEntities = intersectionMeshEntities,
                CornerEntities = m_CornerEntities.AsDeferredJobArray()
            }.Schedule(loftRoadProfilesJob);

            var assembleIntersectionMeshesJob = new AssembleIntersectionMeshesJob
            {
                NumSamples = Parameters.intersectionParameters.numIntersectionSamples,
                CornerEntities = m_CornerEntities.AsDeferredJobArray(),
                MeshEntities = intersectionMeshEntities.AsDeferredJobArray(),
                GridSamples = GetBufferFromEntity<IntersectionGridSample>(),
                InsideEdgeBuffers = GetBufferFromEntity<IntersectionMeshInsideEdgeSample>(),
                OutsideEdgeBuffers = GetBufferFromEntity<IntersectionMeshOutsideEdgeSample>(),
                CombinedVertexBuffers = GetBufferFromEntity<CombinedVertex>(),
                TriangleBuffers = GetBufferFromEntity<Triangle>(),
                SubMeshBuffers = GetBufferFromEntity<SubMesh>()
            }.Schedule(intersectionMeshEntities, 1, createMeshEntitiesJob);

            intersectionMeshEntities.Dispose(assembleIntersectionMeshesJob);

            outputDeps = assembleIntersectionMeshesJob;
        }

        void ScheduleSharedContainerDisposal(JobHandle inputDeps)
        {
            m_IntersectionEntities.Dispose(inputDeps);
            m_RoadEntities.Dispose(inputDeps);
            m_CornerEntities.Dispose(inputDeps);
            m_SurfaceEntities.Dispose(inputDeps);
            m_RoadMarkingEntities.Dispose(inputDeps);
            m_IncomingRoadCropIndicesMap.Dispose(inputDeps);
            m_OutgoingRoadCropIndicesMap.Dispose(inputDeps);
        }

        protected override void OnUpdate()
        {
            Profiler.BeginSample("Scheduling Jobs");
            m_Transaction = EntityManager.BeginExclusiveEntityTransaction();

            var inputDeps = new JobHandle();
            ScheduleSplineToRoadJobs(inputDeps, out inputDeps);
            ScheduleLateralProfileCreationJobs(inputDeps, out inputDeps);
            ScheduleCornerCreationJobs(inputDeps, out inputDeps);
            ScheduleIntersectionGeometryJobs(inputDeps, out inputDeps);
            ScheduleRoadCropJobs(inputDeps, out inputDeps);
            ScheduleMeshCreationJobs(inputDeps, out inputDeps);
            ScheduleSharedContainerDisposal(inputDeps);
            Profiler.EndSample();

            EntityManager.ExclusiveEntityTransactionDependency = inputDeps;
            EntityManager.EndExclusiveEntityTransaction();
        }
    }
}
