using System;
using System.Collections.Generic;
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
    /// For each intersection, sort the intersecting road segments by their 2D angular direction out of the intersection
    /// and assign a corner entity to each pair of neighboring road segments.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct IdentifyCornersJob : IJobParallelForDefer
    {
        public float CornerRadius;
        [ReadOnly] public NativeArray<Entity> IntersectionEntities;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> RoadCenterLineBuffers;
        [ReadOnly] public BufferFromEntity<IntersectionCornerEntityRef> CornerEntityBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<IntersectionRoadConnection> RoadBuffers;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<IntersectionCorner> CornerData;

        struct HeadingAndConnectionPair
        {
            public float Heading;
            public IntersectionRoadConnection Connection;
        }

        struct HeadingAndConnectionPairComparer : IComparer<HeadingAndConnectionPair>
        {
            public int Compare(HeadingAndConnectionPair x, HeadingAndConnectionPair y)
            {
                return x.Heading - y.Heading > float.Epsilon ? 1 : -1;
            }
        }

        public void Execute(int index)
        {
            var intersectionEntity = IntersectionEntities[index];
            var roadEntities = RoadBuffers[intersectionEntity];

            // Sort road headings by radian value to identify neighboring roads
            var roadHeadings = new NativeArray<HeadingAndConnectionPair>(roadEntities.Length, Allocator.Temp);
            for (var i = 0; i < roadEntities.Length; i++)
            {
                var road = roadEntities[i];
                var buffer = RoadCenterLineBuffers[road.Road].Reinterpret<RigidTransform>();
                var vec = road.Direction == IntersectionRoadDirection.Outgoing ?
                    math.rotate(buffer[0].rot, new float3(0f, 0f, 1f)) :
                    math.rotate(buffer[buffer.Length - 1].rot, new float3(0f, 0f, -1f));
                var angle = math.atan2(vec.z, vec.x);

                roadHeadings[i] = new HeadingAndConnectionPair
                {
                    Heading = angle,
                    Connection = road
                };
            }
            roadHeadings.Sort(new HeadingAndConnectionPairComparer());

            // Write back sorted road connections for this intersection
            for (var i = 0; i < roadHeadings.Length; i++)
            {
                roadEntities[i] = roadHeadings[i].Connection;
            }

            // Assign new corner entities to each neighboring pair of road segments
            var cornerBuffer = CornerEntityBuffers[intersectionEntity].Reinterpret<Entity>();
            for (var i = 0; i < roadHeadings.Length; i++)
            {
                var rightRoad = roadHeadings[i];
                var leftRoad = roadHeadings[(i + 1) % roadHeadings.Length];
                var cornerEntity = cornerBuffer[i];

                CornerData[cornerEntity] = new IntersectionCorner
                {
                    LeftRoad = leftRoad.Connection,
                    RightRoad = rightRoad.Connection,
                    Radius = CornerRadius
                };
            }

            roadHeadings.Dispose();
        }
    }

    /// <summary>
    /// For each corner entity, generate left and right offset road edge samples that will be used to form a corner
    /// between a pair of intersecting roads.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct PopulateCornerRoadEdgeBuffersJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> CornerEntities;
        [ReadOnly] public ComponentDataFromEntity<IntersectionCorner> CornerData;
        [ReadOnly] public BufferFromEntity<LateralProfileEntityRef> ProfileBuffers;
        [ReadOnly] public ComponentDataFromEntity<LateralProfile> Profiles;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> RoadCenterLineBuffers;

        [NativeDisableParallelForRestriction] public BufferFromEntity<LeftLaneEdgeSample> LeftLaneEdgeBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<RightLaneEdgeSample> RightLaneEdgeBuffers;

        static float3 OffsetDirection(bool offsetRight, LateralProfile profile)
        {
            var width = offsetRight ? profile.LeftDrivableOffset.x : profile.RightDrivableOffset.x;
            return new float3(width, 0f, 0f);
        }

        public void Execute(int index)
        {
            var cornerEntity = CornerEntities[index];
            var cornerData = CornerData[cornerEntity];
            var leftRoad = cornerData.LeftRoad;
            var rightRoad = cornerData.RightRoad;
            var leftProfile = Profiles[ProfileBuffers[leftRoad.Road].Reinterpret<Entity>()[0]];
            var rightProfile = Profiles[ProfileBuffers[rightRoad.Road].Reinterpret<Entity>()[0]];

            // Create left road edge samples
            var leftReferenceSamples = RoadCenterLineBuffers[leftRoad.Road].Reinterpret<RigidTransform>();
            var leftBuffer = LeftLaneEdgeBuffers[cornerEntity].Reinterpret<RigidTransform>();
            leftBuffer.ResizeUninitialized(leftReferenceSamples.Length);
            var leftIncoming = leftRoad.Direction == IntersectionRoadDirection.Incoming;
            SplineUtility.OffsetSpline(
                leftReferenceSamples.AsNativeArray(),
                leftBuffer.AsNativeArray(),
                OffsetDirection(leftIncoming, leftProfile));
            if (leftIncoming)
                SplineUtility.ReverseSpline(leftBuffer.AsNativeArray());

            // Create right road edge samples
            var rightReferenceSamples = RoadCenterLineBuffers[rightRoad.Road].Reinterpret<RigidTransform>();
            var rightBuffer = RightLaneEdgeBuffers[cornerEntity].Reinterpret<RigidTransform>();
            rightBuffer.ResizeUninitialized(rightReferenceSamples.Length);
            var rightOutgoing = rightRoad.Direction == IntersectionRoadDirection.Outgoing;
            SplineUtility.OffsetSpline(
                rightReferenceSamples.AsNativeArray(),
                rightBuffer.AsNativeArray(),
                OffsetDirection(rightOutgoing, rightProfile));
            if (rightOutgoing)
                SplineUtility.ReverseSpline(rightBuffer.AsNativeArray());
        }
    }

    /// <summary>
    /// Generate 2D rounded corner samples from a circle inscribed between a pair of intersecting road segments
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct RoundedCornerJob : IJobParallelForDefer
    {
        public float CornerSamplesPerMeter;

        [ReadOnly] public NativeArray<Entity> CornerEntities;
        [ReadOnly] public BufferFromEntity<LeftLaneEdgeSample> LeftBufferLookup;
        [ReadOnly] public BufferFromEntity<RightLaneEdgeSample> RightBufferLookup;

        [NativeDisableParallelForRestriction]
        public ComponentDataFromEntity<IntersectionCorner> CornerData;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<RoundedCornerSample> RoundedCornerSampleBuffers;

        public void Execute(int index)
        {
            var cornerEntity = CornerEntities[index];
            var corner = CornerData[cornerEntity];
            var leftBuffer = LeftBufferLookup[cornerEntity].Reinterpret<RigidTransform>().AsNativeArray();
            var rightBuffer = RightBufferLookup[cornerEntity].Reinterpret<RigidTransform>().AsNativeArray();

            // Inscribe a circle of the designated radius
            var solver = new RoundedCornerSolver
            {
                LeftBuffer = leftBuffer,
                RightBuffer = rightBuffer,
                CornerRadius = corner.Radius
            };
            if (!solver.Solve(out var roundedCorner))
            {
                throw new Exception(
                    $"Cannot inscribe a corner radius of {corner.Radius} at this junction corner");
            }

            // Set corner information
            {
                var iterators = solver.Iterators;
                var i = iterators.LeftIdx;
                var j = iterators.RightIdx;

                var leftSample1 = leftBuffer[i - 1].pos.xz;
                var leftSample2 = leftBuffer[i].pos.xz;

                var rightSample1 = rightBuffer[j + 1].pos.xz;
                var rightSample2 = rightBuffer[j].pos.xz;

                // Save these parameters for debugging
                corner.Center = roundedCorner.Center3;

                // Save the tangent indices for the cropping of the
                // road-intersection overlap in a following job
                corner.TangentLeftIndex =
                    (i - 1) +
                    (math.distance(roundedCorner.LeftTangent, leftSample1) / math.distance(leftSample2, leftSample1));
                corner.TangentRightIndex =
                    (iterators.RightIdxForward - 1) +
                    math.distance(roundedCorner.RightTangent, rightSample1) / math.distance(rightSample2, rightSample1);

                // Make sure the tangents never exceed the integer index they're on because we will be using
                // the float to integer cast operation to recover the interpolation factor.
                corner.TangentLeftIndex = math.clamp(
                    corner.TangentLeftIndex,
                    i - 1 + Utilities.GeometryUtility.Tolerance,
                    i - Utilities.GeometryUtility.Tolerance);
                corner.TangentRightIndex = math.clamp(
                    corner.TangentRightIndex,
                    iterators.RightIdxForward - 1 + Utilities.GeometryUtility.Tolerance,
                    iterators.RightIdxForward - Utilities.GeometryUtility.Tolerance);

                // Save tangents for debugging
                corner.TangentLeft = SplineUtility.LerpTransform(
                    leftBuffer[i - 1], leftBuffer[i], corner.TangentLeftIndex - (int)corner.TangentLeftIndex);
                corner.TangentRight = SplineUtility.LerpTransform(
                    rightBuffer[j + 1], rightBuffer[j],
                    corner.TangentRightIndex - (int)corner.TangentRightIndex);
            }


            // Create Corner Samples
            {
                var v1 = corner.TangentLeft.pos - corner.Center;
                var v2 = corner.TangentRight.pos - corner.Center;
                var angleBetweenTangents = Utilities.GeometryUtility.AngleBetweenVectors(v1.xz, v2.xz);
                var numCornerSamples = (int) (CornerSamplesPerMeter * angleBetweenTangents * corner.Radius);

                var angleStep = angleBetweenTangents / (numCornerSamples + 1);
                var startAngle = math.atan2(v2.z, v2.x);

                var cornerBuffer = RoundedCornerSampleBuffers[cornerEntity].Reinterpret<RigidTransform>();
                cornerBuffer.ResizeUninitialized(numCornerSamples + 2);

                cornerBuffer[0] = corner.TangentRight;
                cornerBuffer[cornerBuffer.Length - 1] = corner.TangentLeft;

                var sampleAngle = startAngle;
                for (var i = 0; i < numCornerSamples; i++)
                {
                    sampleAngle -= angleStep;
                    var radiusVec = new float3(math.cos(sampleAngle), 0f, math.sin(sampleAngle)) * corner.Radius;
                    cornerBuffer[i + 1] = new RigidTransform
                    {
                        pos = radiusVec + corner.Center,
                        rot = math.slerp(
                            corner.TangentRight.rot,
                            corner.TangentLeft.rot,
                            (float)(i + 1) / (numCornerSamples + 1))
                    };
                }
            }

            CornerData[cornerEntity] = corner;
        }
    }

    /// <summary>
    /// Dynamically resizes intersection-overlap-crop-maps based on the number of generated road entities
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct ResizeCropMapsJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        public NativeHashMap<Entity, float> IncomingRoadCropIndices;
        public NativeHashMap<Entity, float> OutgoingRoadCropIndices;

        public void Execute()
        {
            IncomingRoadCropIndices.Capacity = RoadEntities.Length;
            OutgoingRoadCropIndices.Capacity = RoadEntities.Length;
        }
    }

    /// <summary>
    /// Combine the generated rounded corner samples with the remaining excess length of road consumed by the
    /// intersection's road surface extents. Additionally, record how much of the road surface is consumed by the
    /// intersection surface to later crop the excess spline samples from participating road segments.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct StitchIntersectionOutlineJob : IJobParallelForDefer
    {
        public float CornerSamplesPerMeter;
        [ReadOnly] public NativeArray<Entity> IntersectionEntities;
        [ReadOnly] public ComponentDataFromEntity<IntersectionCorner> CornerData;

        [ReadOnly] public BufferFromEntity<IntersectionCornerEntityRef> CornerEntityBuffers;
        [ReadOnly] public BufferFromEntity<LeftLaneEdgeSample> LeftBufferLookup;
        [ReadOnly] public BufferFromEntity<RightLaneEdgeSample> RightBufferLookup;
        [ReadOnly] public BufferFromEntity<RoundedCornerSample> RoundedCornerBuffers;

        [WriteOnly] public NativeHashMap<Entity, float>.ParallelWriter IncomingRoadCropIndices;
        [WriteOnly] public NativeHashMap<Entity, float>.ParallelWriter OutgoingRoadCropIndices;

        [NativeDisableParallelForRestriction]
        public ComponentDataFromEntity<CornerRoadCropIndices> CornerRoadCropIndexData;
        [NativeDisableParallelForRestriction]
        public BufferFromEntity<RoadCenterLineSample> RoadCenterLineSampleBuffers;

        struct CornerSampleInterpolator
        {
            public NativeList<RigidTransform> Samples;
            readonly float m_SampleDistance;

            public CornerSampleInterpolator(float sampleDistance)
            {
                m_SampleDistance = sampleDistance;
                Samples = new NativeList<RigidTransform>(Allocator.Temp);
            }

            public void Initialize()
            {
                Samples.Clear();
            }

            public void Add(RigidTransform sample)
            {
                if (Samples.Length == 0)
                {
                    Samples.Add(sample);
                    return;
                }

                var prevSample = Samples[Samples.Length - 1];
                var dist = math.distance(prevSample.pos.xz, sample.pos.xz);

                if (dist > m_SampleDistance)
                {
                    var numExtraSamples = (int)(dist / m_SampleDistance);
                    for (var i = 1; i <= numExtraSamples; i++)
                    {
                        var t = (float)i / (numExtraSamples + 1);
                        Samples.Add(SplineUtility.LerpTransform(prevSample, sample, t));
                    }
                }

                Samples.Add(sample);
            }

            public void Dispose()
            {
                Samples.Dispose();
            }

            public RigidTransform LastSample => Samples[Samples.Length - 1];
        }

        public void Execute(int index)
        {
            var cornerEntities = CornerEntityBuffers[IntersectionEntities[index]].Reinterpret<Entity>();
            var prevCorner = cornerEntities[cornerEntities.Length - 1];
            var currCorner = cornerEntities[0];

            // Initialize corner sample interpolator
            var interpolator = new CornerSampleInterpolator(2f / CornerSamplesPerMeter);

            for (var i = 0; i < cornerEntities.Length; i++)
            {
                var nextCorner = cornerEntities[(i + 1) % cornerEntities.Length];

                var prevCornerData = CornerData[prevCorner];
                var currCornerData = CornerData[currCorner];
                var nextCornerData = CornerData[nextCorner];

                var cornerRoadCropIndices = new CornerRoadCropIndices
                {
                    LeftRoadIndex = currCornerData.TangentLeftIndex,
                    RightRoadIndex = currCornerData.TangentRightIndex
                };

                interpolator.Initialize();

                // Add corner's right edge samples
                {
                    var buffer = RightBufferLookup[currCorner].Reinterpret<RigidTransform>();
                    var maxIdx = math.max(currCornerData.TangentRightIndex, prevCornerData.TangentLeftIndex);
                    cornerRoadCropIndices.RightRoadIndex = maxIdx;

                    if (currCornerData.RightRoad.Direction == IntersectionRoadDirection.Incoming)
                        IncomingRoadCropIndices.TryAdd(currCornerData.RightRoad.Road, maxIdx);
                    else
                        OutgoingRoadCropIndices.TryAdd(currCornerData.RightRoad.Road, maxIdx);

                    if (currCornerData.TangentRightIndex < maxIdx &&
                        !Utilities.GeometryUtility.ApproximatelyEqual(currCornerData.TangentRightIndex, maxIdx))
                    {
                        var firstInterpolatedPointIdx = (buffer.Length - 1) - maxIdx;
                        var invertedTangentIdx = (buffer.Length - 1) - currCornerData.TangentRightIndex;
                        var startIdx = (int)math.ceil(firstInterpolatedPointIdx);
                        var endIdx = (int)invertedTangentIdx;

                        var t = startIdx - firstInterpolatedPointIdx;

                        if (!Utilities.GeometryUtility.ApproximatelyEqual(firstInterpolatedPointIdx, startIdx))
                        {
                            interpolator.Add(SplineUtility.LerpTransform(
                                buffer[startIdx],
                                buffer[startIdx - 1],
                                t));
                        }

                        for (var j = startIdx; j < endIdx; ++j)
                        {
                            interpolator.Add(buffer[j]);
                        }
                    }
                }

                // Add corner's rounded edge samples
                var roundedCornerBuffer =
                    RoundedCornerBuffers[currCorner].Reinterpret<RigidTransform>().AsNativeArray();
                interpolator.Samples.AddRange(roundedCornerBuffer);

                // Add corner's left edge samples
                {
                    var maxIdx = math.max(currCornerData.TangentLeftIndex, nextCornerData.TangentRightIndex);
                    cornerRoadCropIndices.LeftRoadIndex = maxIdx;

                    if (currCornerData.TangentLeftIndex < maxIdx &&
                        !Utilities.GeometryUtility.ApproximatelyEqual(currCornerData.TangentLeftIndex, maxIdx))
                    {
                        var buffer = LeftBufferLookup[currCorner].Reinterpret<RigidTransform>();

                        var intMaxIdx = (int)maxIdx;
                        var minIdx = (int) math.ceil(currCornerData.TangentLeftIndex) + 1;

                        for (var j = minIdx; j < intMaxIdx; ++j)
                        {
                            interpolator.Add(buffer[j]);
                        }

                        var lastSample = SplineUtility.LerpTransform(
                            buffer[intMaxIdx],
                            buffer[intMaxIdx + 1],
                            maxIdx - intMaxIdx);

                        if (!Utilities.GeometryUtility.ApproximatelyEqual(interpolator.LastSample.pos, lastSample.pos))
                        {
                            interpolator.Add(lastSample);
                        }
                    }
                }

                // Record corner crop indices for use in the contour job
                CornerRoadCropIndexData[currCorner] = cornerRoadCropIndices;

                // Add the new outline samples to a new buffer on the corner entity
                // and to the intersection's complete outline buffer
                var cornerBuffer = RoadCenterLineSampleBuffers[currCorner].Reinterpret<RigidTransform>();
                cornerBuffer.CopyFrom(interpolator.Samples);

                // Iterate to next corner
                prevCorner = currCorner;
                currCorner = nextCorner;
            }

            interpolator.Dispose();
        }
    }

    struct LateralProfileCopyRange
    {
        public Entity OriginalProfileEntity;
        public int OriginalSurfaceStartIndex;
        public int OriginalSampleStartIndex;
        public int SurfacesStartIndex;
        public int NumSurfaces;
        public int SamplesStartIndex;
        public int NumSamples;
    }

    /// <summary>
    /// Counts the total number of new LateralProfile and LateralProfileSurface entities needed for all corner entities
    /// to facilitate an efficient batch creation of entities.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CountCornerLateralProfileEntitiesJob : IJob
    {
        [WriteOnly] public NativeArray<int> NumSurfacesAndSamples;
        public NativeList<LateralProfileCopyRange> CornerLateralProfileCopyRange;
        public NativeList<LateralProfileSurface> CopiedCornerSurfaces;
        public NativeList<float2> CopiedCornerSamples;

        [ReadOnly] public NativeArray<Entity> CornerEntities;
        [ReadOnly] public ComponentDataFromEntity<IntersectionCorner> IntersectionCorners;
        [ReadOnly] public ComponentDataFromEntity<LateralProfile> Profiles;
        [ReadOnly] public ComponentDataFromEntity<LateralProfileSurface> Surfaces;
        [ReadOnly] public BufferFromEntity<LateralProfileEntityRef> ProfileBuffers;
        [ReadOnly] public BufferFromEntity<LateralProfileSurfaceEntityRef> SurfaceBuffers;
        [ReadOnly] public BufferFromEntity<LateralProfileSample> LateralProfileSampleBuffers;

        public void Execute()
        {
            CornerLateralProfileCopyRange.ResizeUninitialized(CornerEntities.Length);

            var numSurfaces = 0;
            var numSamples = 0;
            for (var c = 0; c < CornerEntities.Length; c++)
            {
                var cornerEntity = CornerEntities[c];
                var corner = IntersectionCorners[cornerEntity];
                var rightRoadEntity = corner.RightRoad.Road;

                var profileBuffer = ProfileBuffers[rightRoadEntity].Reinterpret<Entity>();

                var profileEntity = profileBuffer[0];
                var profile = Profiles[profileEntity];

                var roadSurfaceEntities = SurfaceBuffers[profileEntity].Reinterpret<Entity>();

                var surfaceStartIndex = profile.RightDrivableIndex + 1;
                var numNewSurfaces = roadSurfaceEntities.Length - surfaceStartIndex;

                var rightSurface = Surfaces[roadSurfaceEntities[surfaceStartIndex]];
                var sampleStartIndex = rightSurface.StartIndex;
                var sampleBuffer = LateralProfileSampleBuffers[profileEntity].Reinterpret<float2>();
                var numNewSamples = sampleBuffer.Length - rightSurface.StartIndex;

                CornerLateralProfileCopyRange[c] = new LateralProfileCopyRange
                {
                    OriginalProfileEntity = profileEntity,
                    OriginalSurfaceStartIndex = surfaceStartIndex,
                    OriginalSampleStartIndex = sampleStartIndex,
                    SurfacesStartIndex = numSurfaces,
                    NumSurfaces = numNewSurfaces,
                    SamplesStartIndex = numSamples,
                    NumSamples = numNewSamples
                };
                numSurfaces += numNewSurfaces;
                numSamples += numNewSamples;
            }

            NumSurfacesAndSamples[0] = numSurfaces;
            NumSurfacesAndSamples[1] = numSamples;

            CopiedCornerSurfaces.ResizeUninitialized(numSurfaces);
            CopiedCornerSamples.ResizeUninitialized(numSamples);
        }
    }

    /// <summary>
    /// Creates the new LateralProfile and LateralProfileSurface entities for each corner entity in a few efficient
    /// batch ExclusiveEntityTransaction operations
    /// </summary>
    struct CreateCornerLateralProfileEntitiesJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public EntityArchetype ProfileArchetype;
        public EntityArchetype SurfaceArchetype;

        [ReadOnly] public NativeArray<int> NumProfilesAndSurfaces;

        [ReadOnly] public NativeArray<Entity> CornerEntities;
        public NativeList<Entity> ProfileEntities;
        public NativeList<Entity> CornerSurfaceEntities;
        public NativeList<Entity> AllSurfaceEntities;

        public void Execute()
        {
            ProfileEntities.ResizeUninitialized(CornerEntities.Length);
            CornerSurfaceEntities.ResizeUninitialized(NumProfilesAndSurfaces[0]);

            Transaction.CreateEntity(ProfileArchetype, ProfileEntities);
            Transaction.CreateEntity(SurfaceArchetype, CornerSurfaceEntities);

            AllSurfaceEntities.AddRange(CornerSurfaceEntities);
        }
    }

    /// <summary>
    /// Copy LateralProfileSurfaces and LateralProfileSamples from a corner's incident roads to later write back to the
    /// road's own lateral profile entities.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CopyLateralProfileSurfacesAndSamplesForCorners : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> CornerProfileEntities;
        [ReadOnly] public NativeArray<LateralProfileCopyRange> CornerLateralProfileSurfaceCopyRange;
        [ReadOnly] public ComponentDataFromEntity<LateralProfileSurface> Surfaces;
        [ReadOnly] public BufferFromEntity<LateralProfileSurfaceEntityRef> SurfaceBuffers;
        [ReadOnly] public BufferFromEntity<LateralProfileSample> LateralProfileSampleBuffers;

        [WriteOnly] public NativeArray<float2> CopiedCornerSamples;
        [WriteOnly] public NativeArray<LateralProfileSurface> CopiedCornerSurfaces;

        public void Execute(int index)
        {
            var cornerProfileEntity = CornerProfileEntities[index];
            var range = CornerLateralProfileSurfaceCopyRange[index];

            var roadSurfaceEntities = SurfaceBuffers[range.OriginalProfileEntity].Reinterpret<Entity>();
            var sampleBuffer = LateralProfileSampleBuffers[range.OriginalProfileEntity].Reinterpret<float2>();

            // Copy LateralProfileSurfaces
            var surfaces = roadSurfaceEntities.AsNativeArray()
                .GetSubArray(range.OriginalSurfaceStartIndex, range.NumSurfaces);
            var copiedCornerSurfaces =
                CopiedCornerSurfaces.GetSubArray(range.SurfacesStartIndex, range.NumSurfaces);
            for (int i = 0, startIdx = 0; i < surfaces.Length; i++)
            {
                var tempSurface = Surfaces[surfaces[i]];
                tempSurface.StartIndex = startIdx;
                tempSurface.Profile = cornerProfileEntity;
                tempSurface.LeftLaneMarking = Entity.Null;
                tempSurface.RightLaneMarking = Entity.Null;
                copiedCornerSurfaces[i] = tempSurface;
                startIdx += tempSurface.SampleCount;
            }

            // Copy LateralProfileSamples
            var samples = sampleBuffer.AsNativeArray().GetSubArray(range.OriginalSampleStartIndex, range.NumSamples);
            var copiedCornerSamples = CopiedCornerSamples.GetSubArray(range.SamplesStartIndex, range.NumSamples);
            var offset = new float2(samples[0].x, 0f);
            for (var i = 0; i < copiedCornerSamples.Length; i++)
                copiedCornerSamples[i] = samples[i] - offset;
        }
    }

    /// <summary>
    /// Writes copied non-drivable lateral profile samples and surfaces from each corner's incident road segment's
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct WriteLateralProfileToCornersJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> CornerEntities;
        [ReadOnly] public NativeArray<Entity> CornerProfileEntities;
        [ReadOnly] public NativeArray<Entity> CornerSurfaceEntities;
        [ReadOnly] public NativeArray<LateralProfileCopyRange> CornerLateralProfileSurfaceCopyRange;

        [ReadOnly] public NativeArray<float2> CopiedCornerSamples;
        [ReadOnly] public NativeArray<LateralProfileSurface> CopiedCornerSurfaces;

        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<LateralProfile> Profiles;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<LateralProfileSurface> Surfaces;
        [NativeDisableParallelForRestriction] public BufferFromEntity<LateralProfileEntityRef> ProfileBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<LateralProfileSurfaceEntityRef> SurfaceBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<LateralProfileSample> LateralProfileSampleBuffers;

        public void Execute(int index)
        {
            var cornerEntity = CornerEntities[index];
            var cornerProfileEntity = CornerProfileEntities[index];
            var range = CornerLateralProfileSurfaceCopyRange[index];

            // Add new LateralProfile Entity
            var cornerProfileBuffer = ProfileBuffers[cornerEntity].Reinterpret<Entity>();
            cornerProfileBuffer.Add(cornerProfileEntity);
            Profiles[cornerProfileEntity] = new LateralProfile
            {
                Road = cornerEntity,
                CenterIndex = 0,
                LeftDrivableIndex = 0,
                LeftDrivableOffset = float2.zero,
                RightDrivableIndex = 0,
                RightDrivableOffset = float2.zero
            };

            // Add new LateralProfileSurfaceEntities
            var cornerSurfaceEntities =
                CornerSurfaceEntities.GetSubArray(range.SurfacesStartIndex, range.NumSurfaces);
            var cornerSurfaceBuffer = SurfaceBuffers[cornerProfileEntity].Reinterpret<Entity>();
            cornerSurfaceBuffer.AddRange(cornerSurfaceEntities);

            // Write copied LateralProfileSurfaces
            var copiedSurfaces = CopiedCornerSurfaces.GetSubArray(range.SurfacesStartIndex, range.NumSurfaces);
            for (var i = 0; i < copiedSurfaces.Length; i++)
                Surfaces[cornerSurfaceBuffer[i]] = copiedSurfaces[i];

            // Add copied LateralProfileSamples
            var copiedSamples = CopiedCornerSamples.GetSubArray(range.SamplesStartIndex, range.NumSamples);
            var cornerSampleBuffer = LateralProfileSampleBuffers[cornerProfileEntity].Reinterpret<float2>();
            cornerSampleBuffer.AddRange(copiedSamples);
        }
    }

    /// <summary>
    /// Smooth the y-component values (height) of each corner sample to prevent creases from forming between a corner's
    /// incident road segments
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct SmoothCornerSampleInterpolationJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> CornerEntities;
        [NativeDisableParallelForRestriction] public BufferFromEntity<RoadCenterLineSample> CenterLineBuffers;

        public void Execute(int index)
        {
            var cornerEntity = CornerEntities[index];
            var samples = CenterLineBuffers[cornerEntity].Reinterpret<RigidTransform>().AsNativeArray();
            if (samples.Length <= 2) return;

            var firstSample = samples[0];
            var lastSample = samples[samples.Length - 1];

            // Calculate distances for interpolation
            var distances = SplineUtility.SplineDistanceArray(samples, Allocator.Temp);
            var totalLength = distances[distances.Length - 1];

            // Interpolate Y component of sample positions
            var firstHeight = firstSample.pos.y;
            var lastHeight = lastSample.pos.y;
            for (var i = 1; i < samples.Length - 1; i++)
            {
                var currLength = distances[i];
                var sample = samples[i];
                var interpolatedHeight = math.lerp(firstHeight, lastHeight, SplineUtility.BezierBlend(currLength / totalLength));
                samples[i] = new RigidTransform
                {
                    pos = new float3(sample.pos.x, interpolatedHeight, sample.pos.z),
                    rot = sample.rot
                };
            }

            // Conform road quaternions to new 3D path
            var currPos = samples[1].pos;
            var v1 = math.normalize(currPos - firstSample.pos);
            var upVector = new float3(0f, 1f, 0f);
            for (var i = 1; i < samples.Length - 1; i++)
            {
                var nextPos = samples[i+1].pos;
                var v2 = math.normalize(nextPos - currPos);
                var direction = math.normalize(v1 + v2);

                samples[i] = new RigidTransform
                {
                    pos = currPos,
                    rot = quaternion.LookRotation(direction, upVector)
                };

                currPos = nextPos;
                v1 = v2;
            }
        }
    }

    /// <summary>
    /// Dynamically allocate road segment cropping arrays off the main thread
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct AllocateCropArraysJob : IJob
    {
        [ReadOnly] public NativeHashMap<Entity, float> IncomingRoadCropIndicesMap;
        [ReadOnly] public NativeHashMap<Entity, float> OutgoingRoadCropIndicesMap;

        [WriteOnly] public NativeList<float> IncomingRoadCropIndices;
        [WriteOnly] public NativeList<Entity> IncomingRoadEntities;
        [WriteOnly] public NativeList<float> OutgoingRoadCropIndices;
        [WriteOnly] public NativeList<Entity> OutgoingRoadCropEntities;
        public NativeList<KeyValuePair<int, RigidTransform>> StoredCropInformation;

        static void AddArrayToList<T>(NativeList<T> list, NativeArray<T> array) where T : struct
        {
            list.AddRange(array);
            array.Dispose();
        }

        public void Execute()
        {
            AddArrayToList(IncomingRoadEntities, IncomingRoadCropIndicesMap.GetKeyArray(Allocator.Temp));
            AddArrayToList(IncomingRoadCropIndices, IncomingRoadCropIndicesMap.GetValueArray(Allocator.Temp));

            AddArrayToList(OutgoingRoadCropEntities, OutgoingRoadCropIndicesMap.GetKeyArray(Allocator.Temp));
            AddArrayToList(OutgoingRoadCropIndices, OutgoingRoadCropIndicesMap.GetValueArray(Allocator.Temp));

            StoredCropInformation.ResizeUninitialized(OutgoingRoadCropIndicesMap.Count());
        }
    }

    /// <summary>
    /// Store outgoing road crop info.
    /// We store the crop info first instead of immediately cropping because
    /// doing so would render a road segment's incoming crop indices invalid.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct StoreCropsOfOutgoingRoadsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> OutgoingRoadEntities;
        [ReadOnly] public NativeArray<float> OutgoingRoadCropIndices;

        [WriteOnly] public NativeArray<KeyValuePair<int, RigidTransform>> StoredCropInfo;

        [NativeDisableParallelForRestriction] public BufferFromEntity<RoadCenterLineSample> CenterLineBuffers;

        public void Execute(int index)
        {
            var buffer = CenterLineBuffers[OutgoingRoadEntities[index]].Reinterpret<RigidTransform>();
            var cropIndex = OutgoingRoadCropIndices[index];
            var cropIndexInt = (int) cropIndex;
            if (Utilities.GeometryUtility.ApproximatelyEqual(cropIndex, cropIndexInt))
            {
                StoredCropInfo[index] =
                    new KeyValuePair<int, RigidTransform>(cropIndexInt + 1, buffer[cropIndexInt]);
            }
            else
            {
                var interpolatedPose = SplineUtility.LerpTransform(
                    buffer[cropIndexInt],
                    buffer[cropIndexInt + 1],
                    cropIndex - cropIndexInt);
                StoredCropInfo[index] = new KeyValuePair<int, RigidTransform>(cropIndexInt, interpolatedPose);
            }
        }
    }

    /// <summary>
    /// Crop the front end of a road segment so that it doesn't intersect the surface of an intersection.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CropIncomingRoadsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> IncomingRoadEntities;
        [ReadOnly] public NativeArray<float> IncomingRoadCropIndices;
        [NativeDisableParallelForRestriction] public BufferFromEntity<RoadCenterLineSample> CenterLineBuffers;

        public void Execute(int index)
        {
            var buffer = CenterLineBuffers[IncomingRoadEntities[index]].Reinterpret<RigidTransform>();
            var cropIndex = buffer.Length - IncomingRoadCropIndices[index] - 1;
            var cropIndexInt = (int) cropIndex;
            if (Utilities.GeometryUtility.ApproximatelyEqual(cropIndex, cropIndexInt))
            {
                buffer.ResizeUninitialized(cropIndexInt + 1);
            }
            else
            {
                buffer[cropIndexInt + 1] = SplineUtility.LerpTransform(
                    buffer[cropIndexInt],
                    buffer[cropIndexInt + 1],
                    cropIndex - cropIndexInt);
                buffer.ResizeUninitialized(cropIndexInt + 2);
            }
        }
    }

    /// <summary>
    /// Crop the back end of a road segment so that it doesn't intersect the surface of an intersection.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CropOutgoingRoadsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> OutgoingRoadEntities;
        [ReadOnly] public NativeArray<KeyValuePair<int, RigidTransform>> StoredCropInformation;
        [NativeDisableParallelForRestriction] public BufferFromEntity<RoadCenterLineSample> CenterLineBuffers;

        public void Execute(int index)
        {
            var buffer = CenterLineBuffers[OutgoingRoadEntities[index]].Reinterpret<RigidTransform>();
            var cropIndex = StoredCropInformation[index].Key;
            buffer[cropIndex] = StoredCropInformation[index].Value;
            if (cropIndex != 0)
                buffer.RemoveRange(0, cropIndex);
        }
    }

    /// <summary>
    /// Create a non-cartesian grid of interpolated lateral profile surface across a portion of an intersection surface
    /// bounded by a set of corner samples and two intersecting road center line splines.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CreateIntersectionContoursJob : IJobParallelForDefer
    {
        public int2 NumSamples;
        [ReadOnly] public NativeArray<Entity> CornerEntities;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> CenterLineBuffers;
        [ReadOnly] public ComponentDataFromEntity<IntersectionCorner> CornerData;
        [ReadOnly] public ComponentDataFromEntity<CornerRoadCropIndices> CornerRoadCropIndices;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionGridSample> IntersectionGridSamples;
        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionMeshInsideEdgeSample> InsideEdgeBuffers;
        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionMeshOutsideEdgeSample> OutsideEdgeBuffers;

        NativeArray<RigidTransform> CropSamples(
            NativeArray<RigidTransform> samples,
            float cropIdx,
            IntersectionRoadDirection direction)
        {
            NativeArray<RigidTransform> croppedSamples;

            if (direction == IntersectionRoadDirection.Outgoing)
            {
                var cropIdxInt = (int) cropIdx;
                croppedSamples = new NativeArray<RigidTransform>((int)math.ceil(cropIdx) + 1, Allocator.Temp);
                for (var i = 0; i <= cropIdxInt; i++)
                {
                    croppedSamples[i] = samples[i];
                }

                croppedSamples[croppedSamples.Length - 1] = SplineUtility.LerpTransform(
                    samples[cropIdxInt],
                    samples[cropIdxInt + 1],
                    cropIdx - cropIdxInt);
            }
            else
            {
                var cropIdxInt = (int) cropIdx;
                var lastCopyIdx = (samples.Length - 1) - cropIdxInt;
                croppedSamples = new NativeArray<RigidTransform>((int) math.ceil(cropIdx) + 1, Allocator.Temp)
                {
                    [0] = SplineUtility.LerpTransform(
                        samples[lastCopyIdx],
                        samples[lastCopyIdx - 1],
                        cropIdx - cropIdxInt)
                };

                for (int i = lastCopyIdx, j = 1; i <= samples.Length - 1; i++, j++)
                {
                    croppedSamples[j] = samples[i];
                }
            }

            return croppedSamples;
        }

        public void Execute(int index)
        {
            var cornerEntity = CornerEntities[index];
            var cornerData = CornerData[cornerEntity];

            var leftRoadSamples = CenterLineBuffers[cornerData.LeftRoad.Road].Reinterpret<RigidTransform>();
            var rightRoadSamples = CenterLineBuffers[cornerData.RightRoad.Road].Reinterpret<RigidTransform>();
            var cornerSamples = CenterLineBuffers[cornerEntity].Reinterpret<RigidTransform>()
                .Reinterpret<RigidTransform>().AsNativeArray();

            // Crop left and right road samples
            var cornerRoadCropIndices = CornerRoadCropIndices[cornerEntity];
            var croppedLeftRoadSamples = CropSamples(
                leftRoadSamples.AsNativeArray(),
                cornerRoadCropIndices.LeftRoadIndex,
                cornerData.LeftRoad.Direction);
            var croppedRightRoadSamples = CropSamples(
                rightRoadSamples.AsNativeArray(),
                cornerRoadCropIndices.RightRoadIndex,
                cornerData.RightRoad.Direction);

            // Reverse splines as needed
            if (cornerData.LeftRoad.Direction == IntersectionRoadDirection.Incoming)
                SplineUtility.ReverseSpline(croppedLeftRoadSamples);
            if (cornerData.RightRoad.Direction == IntersectionRoadDirection.Outgoing)
                SplineUtility.ReverseSpline(croppedRightRoadSamples);

            // Remap the road line splines
            var numRoadSamples = NumSamples.y / 2 + 1;
            var remappedLeftRoadSamples = SplineUtility.EvenlyRemapSpline(croppedLeftRoadSamples, numRoadSamples, false, Allocator.Temp);
            var remappedRightRoadSamples = SplineUtility.EvenlyRemapSpline(croppedRightRoadSamples, numRoadSamples, false, Allocator.Temp);
            croppedLeftRoadSamples.Dispose();
            croppedRightRoadSamples.Dispose();

            // Combine road samples into one contiguous line without duplicating the shared intersection point
            var insideEdgeBuffer = InsideEdgeBuffers[cornerEntity].Reinterpret<RigidTransform>();
            insideEdgeBuffer.AddRange(remappedRightRoadSamples);
            insideEdgeBuffer.AddRange(remappedLeftRoadSamples.GetSubArray(1, numRoadSamples - 1));
            remappedLeftRoadSamples.Dispose();
            remappedRightRoadSamples.Dispose();

            // Re-interpolate road samples using corner samples
            var outsideEdgeBuffer = OutsideEdgeBuffers[cornerEntity].Reinterpret<RigidTransform>();
            var remappedCornerSpline = SplineUtility.EvenlyRemapSpline(cornerSamples, NumSamples.y, false, Allocator.Temp);
            outsideEdgeBuffer.AddRange(remappedCornerSpline);
            remappedCornerSpline.Dispose();

            // Create and fill contour buffer
            var remappedRoadSplineBuffer = IntersectionGridSamples[cornerEntity].Reinterpret<float3>();
            remappedRoadSplineBuffer.Capacity = NumSamples.x * NumSamples.y;
            for (var i = 0; i < NumSamples.y; i++)
            {
                remappedRoadSplineBuffer.Add(insideEdgeBuffer[i].pos);
            }

            for (var k = 1; k < NumSamples.x - 1; k++)
            {
                for (var i = 0; i < NumSamples.y; i++)
                {
                    remappedRoadSplineBuffer.Add(math.lerp(
                        insideEdgeBuffer[i].pos,
                        outsideEdgeBuffer[i].pos,
                        (float)k / (NumSamples.x - 1)));
                }
            }

            for (var i = 0; i < NumSamples.y; i++)
            {
                remappedRoadSplineBuffer.Add(outsideEdgeBuffer[i].pos);
            }
        }
    }

    /// <summary>
    /// Calculate the average central height value for each intersecting road segment and smoothly interpolate all road
    /// segment center lines toward this height value.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct RaiseContoursJob : IJobParallelForDefer
    {
        public int2 NumSamples;
        [ReadOnly] public NativeArray<Entity> IntersectionEntities;
        [ReadOnly] public BufferFromEntity<IntersectionCornerEntityRef> CornerEntities;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionGridSample> GridSamples;

        public void Execute(int index)
        {
            var intersectionEntity = IntersectionEntities[index];
            var cornerEntities = CornerEntities[intersectionEntity].Reinterpret<Entity>();
            var numCorners = cornerEntities.Length;

            // Generate weights
            // Example: Num samples = 9, weights = [1, .75, .5, .25, 0, .25, .5, .75, 1]
            var weights = new NativeArray<float>(NumSamples.y, Allocator.Temp);
            var halfSamples = NumSamples.y / 2;
            for (int i = halfSamples, j = 0; i >= 0; --i, j++)
                weights[j] = SplineUtility.BezierBlend((float)i / halfSamples);
            for (int i = NumSamples.y - 1, j = 0; i > halfSamples; --i, j++)
                weights[i] = weights[j];

            // Calculate the elevation at the center of the intersection
            var centerY = 0f;
            for (var i = 0; i < numCorners; i++)
                centerY += GridSamples[cornerEntities[i]][NumSamples.y / 2 + 1].Sample.y;
            centerY /= numCorners;

            // Interpolate roads
            var prevCorner = numCorners - 1;
            for (var i = 0; i < cornerEntities.Length; i++)
            {
                var samples = GridSamples[cornerEntities[i]].Reinterpret<float3>();
                for (var j = 0; j <= halfSamples; j++)
                {
                    var sample = samples[j];
                    sample.y = math.lerp(centerY, samples[j].y, weights[j]);
                    samples[j] = sample;
                }

                var neighborSamples = GridSamples[cornerEntities[prevCorner]].Reinterpret<float3>();
                for (var j = 0; j <= halfSamples; j++)
                {
                    neighborSamples[NumSamples.y - j - 1] = samples[j];
                }

                prevCorner = i;
            }
        }
    }

    /// <summary>
    /// Interpolate smooth height values for each vertex in the non-cartesian grid constructed
    /// for each intersection corner
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct InterpolateContoursJob : IJobParallelForDefer
    {
        public int2 NumSamples;
        [ReadOnly] public NativeArray<Entity> CornerEntities;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionGridSample> IntersectionGridSamples;
        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionMeshInsideEdgeSample> InsideEdgeBuffers;

        public void Execute(int index)
        {
            var cornerEntity = CornerEntities[index];
            var gridSamples = IntersectionGridSamples[cornerEntity].Reinterpret<float3>();
            var endOffset = (NumSamples.x - 1) * NumSamples.y;

            // Calculate contour interpolation weights
            var weights = new NativeArray<float>(NumSamples.x, Allocator.Temp);
            for (var i = 0; i < NumSamples.x; i++)
            {
                weights[i] = SplineUtility.BezierBlend((float) i / (NumSamples.x - 1));
            }

            for (var i = 1; i < NumSamples.x - 1; i++)
            {
                var offset = NumSamples.y * i;
                for (var j = 0; j < NumSamples.y; j++)
                {
                    var roadValue = gridSamples[j].y;
                    var cornerValue = gridSamples[endOffset + j].y;
                    var t = weights[i];
                    var sample = gridSamples[offset + j];
                    sample.y = math.lerp(roadValue, cornerValue, t);
                    gridSamples[offset + j] = sample;
                }
            }
            weights.Dispose();

            // Adjust inside edge heights
            var upVector = new float3(0f, 1f, 0f);
            var insideEdgeBuffer = InsideEdgeBuffers[cornerEntity].Reinterpret<RigidTransform>();
            for (var i = 1; i < NumSamples.y - 1; i++)
            {
                var prevSample = gridSamples[i - 1];
                var sample = gridSamples[i];
                var nextSample = gridSamples[i + 1];

                var vec1 = math.normalize(sample - prevSample);
                var vec2 = math.normalize(nextSample - sample);
                insideEdgeBuffer[i] = new RigidTransform
                {
                    pos = sample,
                    rot = quaternion.LookRotation(math.normalize(vec1 + vec2), upVector)
                };
            }
        }
    }

    /// <summary>
    /// Creates one intersection mesh entity for every intersection corner entity
    ///
    /// NOTE: ExclusiveEntityTransaction cannot be Burst compiled
    /// </summary>
    struct CreateIntersectionMeshEntitiesJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public EntityArchetype IntersectionMeshArchetype;
        public NativeArray<Entity> CornerEntities;
        public NativeList<Entity> IntersectionMeshEntities;

        public void Execute()
        {
            IntersectionMeshEntities.ResizeUninitialized(CornerEntities.Length);
            Transaction.CreateEntity(IntersectionMeshArchetype, IntersectionMeshEntities);
        }
    }

    /// <summary>
    /// Creates the vertex and triangle index arrays for each intersection entity
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct AssembleIntersectionMeshesJob : IJobParallelForDefer
    {
        public int2 NumSamples;
        [ReadOnly] public NativeArray<Entity> CornerEntities;
        [ReadOnly] public NativeArray<Entity> MeshEntities;
        [ReadOnly] public BufferFromEntity<IntersectionGridSample> GridSamples;
        [ReadOnly] public BufferFromEntity<IntersectionMeshInsideEdgeSample> InsideEdgeBuffers;
        [ReadOnly] public BufferFromEntity<IntersectionMeshOutsideEdgeSample> OutsideEdgeBuffers;

        [NativeDisableParallelForRestriction] public BufferFromEntity<CombinedVertex> CombinedVertexBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<Triangle> TriangleBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<SubMesh> SubMeshBuffers;

        public void Execute(int index)
        {
            var cornerEntity = CornerEntities[index];
            var meshEntity = MeshEntities[index];

            var gridSamples = GridSamples[cornerEntity].Reinterpret<float3>();
            var vertices = new NativeArray<float3>(gridSamples.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            vertices.CopyFrom(gridSamples.AsNativeArray());

            var normals = new NativeArray<float3>(gridSamples.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var insideEdgeBuffer = InsideEdgeBuffers[cornerEntity].Reinterpret<RigidTransform>();
            var outsideEdgeBuffer = OutsideEdgeBuffers[cornerEntity].Reinterpret<RigidTransform>();

            // Set inside edge normals
            var upVector = new float3(0f, 1f, 0f);
            for (var i = 0; i < NumSamples.y; i++)
            {
                normals[i] = math.mul(insideEdgeBuffer[i].rot, upVector);
            }

            // Set outside edge normals
            for (int i = (NumSamples.x - 1) * NumSamples.y, j = 0; j < NumSamples.y; i++, j++)
            {
                normals[i] = math.mul(outsideEdgeBuffer[j].rot, upVector);
            }

            // Set middle normals
            for (var i = 1; i < NumSamples.x - 1; i++)
            {
                var offset = i * NumSamples.y;
                for (var j = 0; j < NumSamples.y; j++)
                {
                    var insideRotation = insideEdgeBuffer[j].rot;
                    var outsideRotation = outsideEdgeBuffer[j].rot;
                    var rotation = math.slerp(insideRotation, outsideRotation, (float)i / (NumSamples.x - 1));
                    normals[offset + j] = math.mul(rotation, upVector);
                }
            }

            // Set intersection center normal
            normals[NumSamples.y / 2] = upVector;

            var uvs = new NativeArray<float2>(gridSamples.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < uvs.Length; i++)
            {
                uvs[i] = vertices[i].xz;
            }

            var combinedVertexBuffer = CombinedVertexBuffers[meshEntity];
            combinedVertexBuffer.ResizeUninitialized(gridSamples.Length);
            for (var i = 0; i < gridSamples.Length; i++)
            {
                combinedVertexBuffer[i] = new CombinedVertex
                {
                    Vertex = vertices[i],
                    Normal = normals[i],
                    Uv = uvs[i]
                };
            }

            var triangles = TriangleBuffers[meshEntity].Reinterpret<int>();
            triangles.ResizeUninitialized((NumSamples.x - 1) * (NumSamples.y - 1) * 6);
            for (int i = 1, triIdx = 0; i < NumSamples.x; i++)
            {
                var offset1 = (i-1) * NumSamples.y;
                var offset2 = offset1 + NumSamples.y;

                for (var k = 1; k < NumSamples.y; k++)
                {
                    var j = k - 1;
                    triangles[triIdx++] = offset1 + j;
                    triangles[triIdx++] = offset1 + k;
                    triangles[triIdx++] = offset2 + j;

                    triangles[triIdx++] = offset1 + k;
                    triangles[triIdx++] = offset2 + k;
                    triangles[triIdx++] = offset2 + j;
                }
            }

            var subMeshBuffer = SubMeshBuffers[meshEntity];
            subMeshBuffer.Add(new SubMesh
            {
                Material = RoadMaterial.RoadSurface,
                VertexStartIndex = 0,
                VertexCount = combinedVertexBuffer.Length,
                TriangleStartIndex = 0,
                TriangleCount = triangles.Length
            });

            vertices.Dispose();
            normals.Dispose();
            uvs.Dispose();
        }
    }
}
