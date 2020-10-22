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
    /// Dynamically resize NativeLists to Entity NativeArray returned from async EntityQuery. Lists are also used
    /// as Schedule() parameters for proceeding IJobParallelForDefer jobs
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct ResizeRoadSplineContainersJob : IJob
    {
        public NativeArray<Entity> RoadSplineEntities;
        public NativeList<bool> RoadsThatIntersect;
        public NativeList<bool> RoadsThatSelfIntersect;
        public NativeMultiHashMap<int, IntersectionPoint> SplitIndicesMap;

        public void Execute()
        {
            var roadSplinesCount = RoadSplineEntities.Length;
            RoadsThatIntersect.Resize(roadSplinesCount, NativeArrayOptions.ClearMemory);
            RoadsThatSelfIntersect.Resize(roadSplinesCount, NativeArrayOptions.ClearMemory);

            // NOTE: This container capacity is an estimation on how many intersection records will be created when
            // solving for spline intersections. SplitIndicesMap should be replaced with a native container type that
            // can support the following requirements when available:
            //    - Parallel write capabilities
            //    - Dynamic resizing during parallel writes
            //    - Indexing or memcpy-to-NativeArray support for eventual sorting
            SplitIndicesMap.Capacity = math.max(
                roadSplinesCount * roadSplinesCount,
                roadSplinesCount * 10);
        }
    }

    /// <summary>
    /// Index parameters for the parallel instances of FindSelfIntersectionsJob
    /// </summary>
    struct SelfIntersectionPair
    {
        public int BufferIndex;
        public int OffsetIndex;
    }

    /// <summary>
    /// Create a dynamic list of job arguments for the FindSelfIntersectionsJob to use as input
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CreateSelfIntersectionPairsJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> RoadSplineEntities;
        [ReadOnly] public BufferFromEntity<RoadSplineSample> SampleBuffers;
        [WriteOnly] public NativeList<SelfIntersectionPair> SelfIntersectionPairs;

        public void Execute()
        {
            for (var i = 0; i < RoadSplineEntities.Length; i++)
            {
                var numSamples = SampleBuffers[RoadSplineEntities[i]].Length;
                for (var j = 1; j < numSamples; j++)
                {
                    SelfIntersectionPairs.Add(new SelfIntersectionPair
                    {
                        BufferIndex = i,
                        OffsetIndex = j
                    });
                }
            }
        }
    }

    /// <summary>
    /// Used to track spline-segment intersections
    /// </summary>
    struct IntersectionPoint
    {
        public float2 Point;
        public float RoadSplineIndex;
        public bool SelfIntersection;
        public int IntersectionEntityIndex;
    }

    /// <summary>
    /// Identifies all self intersections within each road input spline
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct FindSelfIntersectionsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> RoadSplinesEntities;
        [ReadOnly] public NativeArray<SelfIntersectionPair> SelfIntersectionPairs;
        [ReadOnly] public BufferFromEntity<RoadSplineSample> RoadSplineSampleBuffers;

        [NativeDisableParallelForRestriction] public NativeArray<bool> RoadsThatSelfIntersect;
        [WriteOnly] public NativeMultiHashMap<int, IntersectionPoint>.ParallelWriter SplitIndicesMap;

        public void Execute(int index)
        {
            var pair = SelfIntersectionPairs[index];
            var i = pair.BufferIndex;
            var j = pair.OffsetIndex;
            var buffer = RoadSplineSampleBuffers[RoadSplinesEntities[i]].Reinterpret<RigidTransform>();

            for (var k = j + 2; k < buffer.Length; k++)
            {
                if (!Utilities.GeometryUtility.LineSegmentIntersection2D(
                    buffer[j].pos.xz, buffer[j - 1].pos.xz,
                    buffer[k].pos.xz, buffer[k - 1].pos.xz,
                    out var intersection)) continue;

                RoadsThatSelfIntersect[i] = true;

                var t1 = Utilities.GeometryUtility.Unlerp(buffer[j - 1].pos.xz, buffer[j].pos.xz, intersection);
                var t2 = Utilities.GeometryUtility.Unlerp(buffer[k - 1].pos.xz, buffer[k].pos.xz, intersection);

                SplitIndicesMap.Add(i, new IntersectionPoint
                {
                    Point = intersection,
                    RoadSplineIndex = j - 1 + t1,
                    SelfIntersection = true
                });
                SplitIndicesMap.Add(i, new IntersectionPoint
                {
                    Point = intersection,
                    RoadSplineIndex = k - 1 + t2,
                    SelfIntersection = true
                });
            }
        }
    }

    /// <summary>
    /// Finds all intersections between any two road input splines
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct FindIntersectionsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> RoadSplinesEntities;
        [ReadOnly] public BufferFromEntity<RoadSplineSample> RoadSplineSampleBuffers;

        [NativeDisableParallelForRestriction] public NativeArray<bool> RoadsThatIntersect;
        [WriteOnly] public NativeMultiHashMap<int, IntersectionPoint>.ParallelWriter SplitIndicesMap;

        public void Execute(int index)
        {
            var buffer = RoadSplineSampleBuffers[RoadSplinesEntities[index]].Reinterpret<RigidTransform>();
            for (var j = index + 1; j < RoadSplinesEntities.Length; j++)
            {
                var otherBuffer = RoadSplineSampleBuffers[RoadSplinesEntities[j]].Reinterpret<RigidTransform>();
                for (var p = 1; p < buffer.Length; p++)
                {
                    for (var q = 1; q < otherBuffer.Length; q++)
                    {
                        if (!Utilities.GeometryUtility.LineSegmentIntersection2D(
                            buffer[p].pos.xz, buffer[p - 1].pos.xz,
                            otherBuffer[q].pos.xz, otherBuffer[q - 1].pos.xz,
                            out var intersection)) continue;

                        RoadsThatIntersect[index] = true;
                        RoadsThatIntersect[j] = true;

                        var t1 = Utilities.GeometryUtility.Unlerp(buffer[p - 1].pos.xz, buffer[p].pos.xz, intersection);
                        var t2 = Utilities.GeometryUtility.Unlerp(otherBuffer[q - 1].pos.xz, otherBuffer[q].pos.xz, intersection);

                        SplitIndicesMap.Add(index, new IntersectionPoint
                        {
                            Point = intersection,
                            RoadSplineIndex = p - 1 + t1,
                            SelfIntersection = false
                        });
                        SplitIndicesMap.Add(j, new IntersectionPoint
                        {
                            Point = intersection,
                            RoadSplineIndex = q - 1 + t2,
                            SelfIntersection = false
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Maps a particular road spline to a subarray of its intersection records
    /// </summary>
    struct IntersectionRecordRange
    {
        public int SplineIndex;
        public int StartIndex;
        public int Length;
        public int LastIndex => StartIndex + Length - 1;
    }

    /// <summary>
    /// Finds the subset of unique intersection points among all
    /// of the previously recorded road input spline intersections
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct IdentifyUniqueIntersectionsJob : IJob
    {
        [ReadOnly] public NativeMultiHashMap<int, IntersectionPoint> SplitIndicesMap;

        public NativeList<IntersectionPoint> IntersectionMetaData;
        public NativeList<IntersectionRecordRange> IntersectionRecordRanges;
        public NativeList<float2> UniqueIntersectionPoints;

        /// <summary>
        /// Duplicates form when you have 3 roads intersecting at one point. Multiple intersection records are recorded
        /// for each spline, so we remove those duplicates here.
        /// </summary>
        void RemoveDuplicateIntersectionRecords(
            NativeArray<int> roadIndices)
        {
            if (roadIndices.Length == 0) return;

            int j = 0, lastRoadIdx = 0, count = 1;
            for (var i = 1; i < IntersectionMetaData.Length; i++)
            {
                if (lastRoadIdx != roadIndices[i])
                {
                    IntersectionMetaData[++j] = IntersectionMetaData[i];
                    IntersectionRecordRanges.Add(new IntersectionRecordRange
                    {
                        SplineIndex = lastRoadIdx,
                        StartIndex = j - count,
                        Length = count
                    });
                    lastRoadIdx = roadIndices[i];
                    count = 1;
                    continue;
                }

                // While overwriting duplicates, be sure to preserve whether a self intersection occurs at this point
                var point = IntersectionMetaData[i];
                if (Utilities.GeometryUtility.ApproximatelyEqual(
                    point.RoadSplineIndex,
                    IntersectionMetaData[j].RoadSplineIndex))
                {
                    point.SelfIntersection = IntersectionMetaData[j].SelfIntersection || point.SelfIntersection;
                    IntersectionMetaData[j] = point;
                }
                else
                {
                    IntersectionMetaData[++j] = point;
                    count++;
                }
            }

            IntersectionRecordRanges.Add(new IntersectionRecordRange
            {
                SplineIndex = lastRoadIdx,
                StartIndex = j - count + 1,
                Length = count
            });
            IntersectionMetaData.ResizeUninitialized(j + 1);
        }

        struct IntersectionPointComparer : IComparer<IntersectionPoint>
        {
            public int Compare(IntersectionPoint p1, IntersectionPoint p2)
            {
                var diff = p1.RoadSplineIndex - p2.RoadSplineIndex;
                if (math.abs(diff) < Utilities.GeometryUtility.Tolerance) return 0;
                return diff < 0f ? -1 : 1;
            }
        }

        public void Execute()
        {
            if (SplitIndicesMap.Count() == 0) return;

            var roadIndices = SplitIndicesMap.GetKeyArray(Allocator.Temp);
            var intersectionMetaData = SplitIndicesMap.GetValueArray(Allocator.Temp);

            // Sort intersection metadata by spline interpolation factor
            for (var i = 1; i < roadIndices.Length; i++)
            {
                var j = i - 1;
                var prevIndex = roadIndices[j];
                while (i < roadIndices.Length && roadIndices[i] == prevIndex) i++;
                var subArray = intersectionMetaData.GetSubArray(j, i - j);
                subArray.Sort(new IntersectionPointComparer());
            }

            IntersectionMetaData.AddRange(intersectionMetaData);
            intersectionMetaData.Dispose();

            RemoveDuplicateIntersectionRecords(roadIndices);
            roadIndices.Dispose();

            // Identify all unique intersection points and save them to a list
            for (var i = 0; i < IntersectionMetaData.Length; i++)
            {
                var foundDuplicate = false;
                var metaData = IntersectionMetaData[i];
                var intersectionEntityIndex = 0;

                // Search all known unique intersection points for a duplicate
                for (var j = 0; j < UniqueIntersectionPoints.Length; j++)
                {
                    if (!Utilities.GeometryUtility.ApproximatelyEqual(UniqueIntersectionPoints[j], metaData.Point))
                        continue;
                    foundDuplicate = true;
                    intersectionEntityIndex = j;
                    break;
                }

                if (!foundDuplicate)
                {
                    UniqueIntersectionPoints.Add(metaData.Point);
                    intersectionEntityIndex = UniqueIntersectionPoints.Length - 1;
                }

                metaData.IntersectionEntityIndex = intersectionEntityIndex;
                IntersectionMetaData[i] = metaData;
            }
        }
    }

    /// <summary>
    /// Efficiently creates new intersection entities using a batch entity operation
    ///
    /// NOTE: ExclusiveEntityTransaction cannot be Burst compiled
    /// </summary>
    struct CreateIntersectionEntitiesJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public EntityArchetype IntersectionArchetype;
        public NativeArray<float2> UniqueIntersectionPoints;
        public NativeList<Entity> IntersectionEntities;

        public void Execute()
        {
            IntersectionEntities.ResizeUninitialized(UniqueIntersectionPoints.Length);
            Transaction.CreateEntity(IntersectionArchetype, IntersectionEntities);
        }
    }

    /// <summary>
    /// Creates road entities for road input splines that don't intersect any other splines
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct InitializeComponentsOnIntersectionEntitiesJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> IntersectionEntities;
        [ReadOnly] public NativeArray<float2> UniqueIntersectionPoints;
        public ComponentDataFromEntity<IntersectionData> IntersectionDataComponents;

        public void Execute()
        {
            for (var i = 0; i < IntersectionEntities.Length; i++)
            {
                IntersectionDataComponents[IntersectionEntities[i]] = new IntersectionData
                {
                    Point = UniqueIntersectionPoints[i]
                };
            }
        }
    }

    /// <summary>
    /// Identifies the subset of non-intersecting splines from an array of all spline entities
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CountNonIntersectingRoadsJob : IJob
    {
        [ReadOnly] public NativeArray<bool> RoadsThatIntersect;
        [ReadOnly] public NativeArray<bool> RoadsThatSelfIntersect;
        [ReadOnly] public NativeArray<Entity> SplineEntities;
        public NativeList<Entity> NonIntersectingSplineEntities;

        public void Execute()
        {
            for (var i = 0; i < RoadsThatIntersect.Length; i++)
            {
                if (!RoadsThatIntersect[i] && !RoadsThatSelfIntersect[i])
                    NonIntersectingSplineEntities.Add(SplineEntities[i]);
            }
        }
    }

    /// <summary>
    /// Efficiently creates new non-intersecting road entities using a batch entity operation
    ///
    /// NOTE: ExclusiveEntityTransaction cannot be Burst compiled
    /// </summary>
    struct CreateNonIntersectingRoadsJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public EntityArchetype RoadArchetype;
        [ReadOnly] public NativeArray<Entity> NonIntersectingSplineEntities;

        public NativeList<Entity> NonIntersectingRoadEntities;

        public void Execute()
        {
            NonIntersectingRoadEntities.ResizeUninitialized(NonIntersectingSplineEntities.Length);
            Transaction.CreateEntity(RoadArchetype, NonIntersectingRoadEntities);
        }
    }

    /// <summary>
    /// Initializes component data and dynamic buffers for non intersecting road entities
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct InitializeNonIntersectingRoadsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> NonIntersectingRoadEntities;
        [ReadOnly] public NativeArray<Entity> NonIntersectingSplineEntities;
        [ReadOnly] public BufferFromEntity<RoadSplineSample> SplineSampleBuffers;

        [NativeDisableParallelForRestriction]
        public ComponentDataFromEntity<RoadCenterLineData> RoadCenterLineDataComponents;
        [NativeDisableParallelForRestriction]
        public BufferFromEntity<RoadCenterLineSample> RoadCenterLineSampleBuffers;

        public void Execute(int index)
        {
            var roadEntity = NonIntersectingRoadEntities[index];
            RoadCenterLineDataComponents[roadEntity] = new RoadCenterLineData
            {
                StreetId = index,
                StartIntersection = Entity.Null,
                EndIntersection = Entity.Null
            };

            var splineEntity = NonIntersectingSplineEntities[index];
            var splineBuffer = SplineSampleBuffers[splineEntity].Reinterpret<RigidTransform>();
            var centerLineBuffer = RoadCenterLineSampleBuffers[roadEntity].Reinterpret<RigidTransform>();
            centerLineBuffer.CopyFrom(splineBuffer);
        }
    }

    /// <summary>
    /// Maps a new RoadEntity to a particular section of a road spline
    /// </summary>
    struct RoadSplineRange
    {
        public int StreetId;
        public Entity SplineEntity;
        public float StartIndex;
        public float EndIndex;
        public Entity StartIntersectionEntity;
        public Entity EndIntersectionEntity;
    }

    [BurstCompile(CompileSynchronously = true)]
    struct CountNewRoadEntitiesFromSplineSplitsJob : IJob
    {
        public NativeList<RoadSplineRange> RoadSplineRanges;
        [ReadOnly] public NativeArray<Entity> RoadSplinesEntities;
        [ReadOnly] public NativeArray<Entity> IntersectionEntities;
        [ReadOnly] public NativeArray<Entity> NonIntersectingRoadEntities;
        [ReadOnly] public NativeArray<IntersectionPoint> IntersectionMetaData;
        [ReadOnly] public NativeArray<IntersectionRecordRange> IntersectionRecordRanges;
        [ReadOnly] public BufferFromEntity<RoadSplineSample> RoadSplineSampleBuffers;

        public void Execute()
        {
            var totalNumRoadEntities = 0;
            for (var i = 0; i < IntersectionRecordRanges.Length; i++)
                totalNumRoadEntities += IntersectionRecordRanges[i].Length + 1;

            var numNonIntersectingRoads = NonIntersectingRoadEntities.Length;
            RoadSplineRanges.ResizeUninitialized(totalNumRoadEntities);
            for (int i = 0, j = 0, streetId = numNonIntersectingRoads; i < IntersectionRecordRanges.Length; i++)
            {
                var range = IntersectionRecordRanges[i];
                var splineEntity = RoadSplinesEntities[range.SplineIndex];
                var prevSplineIntersection = IntersectionMetaData[range.StartIndex];
                RoadSplineRanges[j++] = new RoadSplineRange
                {
                    StreetId = streetId,
                    SplineEntity = splineEntity,
                    StartIndex = 0f,
                    EndIndex = prevSplineIntersection.RoadSplineIndex,
                    StartIntersectionEntity = Entity.Null,
                    EndIntersectionEntity = IntersectionEntities[prevSplineIntersection.IntersectionEntityIndex]
                };

                for (var k = range.StartIndex + 1; k <= range.LastIndex; k++)
                {
                    var nextSplineIntersection = IntersectionMetaData[k];
                    streetId = math.select(streetId, streetId + 1, prevSplineIntersection.SelfIntersection);
                    RoadSplineRanges[j++] = new RoadSplineRange
                    {
                        StreetId = streetId,
                        SplineEntity = splineEntity,
                        StartIndex = prevSplineIntersection.RoadSplineIndex,
                        EndIndex = nextSplineIntersection.RoadSplineIndex,
                        StartIntersectionEntity = IntersectionEntities[prevSplineIntersection.IntersectionEntityIndex],
                        EndIntersectionEntity = IntersectionEntities[nextSplineIntersection.IntersectionEntityIndex]
                    };
                    prevSplineIntersection = nextSplineIntersection;
                }

                streetId = math.select(streetId, streetId + 1, prevSplineIntersection.SelfIntersection);
                RoadSplineRanges[j++] = new RoadSplineRange
                {
                    StreetId = streetId++,
                    SplineEntity = splineEntity,
                    StartIndex = prevSplineIntersection.RoadSplineIndex,
                    EndIndex = RoadSplineSampleBuffers[splineEntity].Length - 1,
                    StartIntersectionEntity = IntersectionEntities[prevSplineIntersection.IntersectionEntityIndex],
                    EndIntersectionEntity = Entity.Null
                };
            }
        }
    }

    /// <summary>
    /// Efficiently creates new road entities using a batch entity operation
    ///
    /// NOTE: ExclusiveEntityTransaction cannot be Burst compiled
    /// </summary>
    struct CreateRoadEntities : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public EntityArchetype RoadArchetype;
        [ReadOnly] public NativeArray<RoadSplineRange> RoadSplineRanges;
        public NativeList<Entity> RoadEntities;

        public void Execute()
        {
            RoadEntities.ResizeUninitialized(RoadSplineRanges.Length);
            Transaction.CreateEntity(RoadArchetype, RoadEntities);
        }
    }

    /// <summary>
    /// Divides a road spline into road segments by each intersection encountered along it's traversal
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct SplitSplinesAtIntersectionsIntoUniqueRoadsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        [ReadOnly] public NativeArray<RoadSplineRange> RoadSplineRanges;

        [NativeDisableParallelForRestriction]
        public ComponentDataFromEntity<RoadCenterLineData> RoadCenterLineDataComponents;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<RoadSplineSample> RoadSplineSampleBuffers;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<RoadCenterLineSample> RoadCenterLineSampleBuffers;

        public void Execute(int index)
        {
            var range = RoadSplineRanges[index];
            var roadEntity = RoadEntities[index];

            RoadCenterLineDataComponents[roadEntity] = new RoadCenterLineData
            {
                StreetId = range.StreetId,
                StartIntersection = range.StartIntersectionEntity,
                EndIntersection = range.EndIntersectionEntity
            };

            var splineBuffer = RoadSplineSampleBuffers[range.SplineEntity].Reinterpret<RigidTransform>();
            var roadBuffer = RoadCenterLineSampleBuffers[roadEntity].Reinterpret<RigidTransform>();

            // Get nearest integer spline indices
            var firstIntegerIdx = (int)math.floor(range.StartIndex + 1f);
            var lastIntegerIdx = (int)math.ceil(range.EndIndex - 1f);

            // Add first point if between spline indices
            if (!Utilities.GeometryUtility.ApproximatelyEqual(range.StartIndex, firstIntegerIdx))
            {
                roadBuffer.Add(SplineUtility.LerpTransform(
                    splineBuffer[firstIntegerIdx - 1],
                    splineBuffer[firstIntegerIdx],
                    range.StartIndex - (firstIntegerIdx - 1)));
            }

            // Intermediate points
            for (var i = firstIntegerIdx; i <= lastIntegerIdx; i++)
                roadBuffer.Add(splineBuffer[i]);

            // Add last point if between spline indices
            if (!Utilities.GeometryUtility.ApproximatelyEqual(range.EndIndex, lastIntegerIdx))
            {
                roadBuffer.Add(SplineUtility.LerpTransform(
                    splineBuffer[lastIntegerIdx],
                    splineBuffer[lastIntegerIdx + 1],
                    range.EndIndex - lastIntegerIdx));
            }
        }
    }

    /// <summary>
    /// Adds non-intersecting road entities into the list of intersecting road spline entities
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CombineRoadEntitiesJob : IJob
    {
        public NativeArray<Entity> NonIntersectingRoadEntities;
        public NativeList<Entity> RoadEntities;

        public void Execute()
        {
            RoadEntities.AddRange(NonIntersectingRoadEntities);
        }
    }

    /// <summary>
    /// Maps which intersection a sub section of road entities is a part of from within a larger sorted array of
    /// road entities.
    /// </summary>
    struct RoadConnectionArrayRange
    {
        public Entity IntersectionEntity;
        public int StartIndex;
        public int Length;
    }

    /// <summary>
    /// Dynamically increases the capacity of the IntersectionsToRoadsMap with respect to the size of the NativeArray
    /// of entities returned by an EntityQuery. Also creates a copy of said native array for use in proceeding
    /// IJobParallelForDefer jobs.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct AllocateRoadsToIntersectionsMapJob : IJob
    {
        public NativeArray<Entity> RoadEntities;
        public NativeMultiHashMap<Entity, IntersectionRoadConnection> IntersectionsToRoadsMap;

        public void Execute()
        {
            IntersectionsToRoadsMap.Capacity = RoadEntities.Length * 2;
        }
    }

    /// <summary>
    /// Maps intersection entities to road entities and records which end of the road is connected to the intersection
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct MapIntersectionToRoadsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> RoadEntities;
        [ReadOnly] public ComponentDataFromEntity<RoadCenterLineData> RoadCenterLineDataComponents;
        [WriteOnly] public NativeMultiHashMap<Entity, IntersectionRoadConnection>.ParallelWriter IntersectionsToRoadsMap;

        public void Execute(int index)
        {
            var roadEntity = RoadEntities[index];
            var data = RoadCenterLineDataComponents[roadEntity];

            if (data.StartIntersection != Entity.Null)
            {
                IntersectionsToRoadsMap.Add(data.StartIntersection, new IntersectionRoadConnection
                {
                    Road = roadEntity,
                    Direction = IntersectionRoadDirection.Outgoing
                });
            }

            if (data.EndIntersection != Entity.Null)
            {
                IntersectionsToRoadsMap.Add(data.EndIntersection, new IntersectionRoadConnection
                {
                    Road = roadEntity,
                    Direction = IntersectionRoadDirection.Incoming
                });
            }
        }
    }

    /// <summary>
    /// Create enough corner entities to match the number of intersection road connections in order to later map said
    /// corner entities to their respective intersections.
    ///
    /// NOTE: This function is not burst compiled since ExclusiveEntityTransactions don't support burst compilation
    /// </summary>
    struct CreateCornerEntitiesJob : IJob
    {
        public EntityArchetype CornerArchetype;
        public ExclusiveEntityTransaction Transaction;
        public NativeList<Entity> CornerEntities;
        [ReadOnly] public NativeMultiHashMap<Entity, IntersectionRoadConnection> IntersectionsToRoadsMap;

        public void Execute()
        {
            CornerEntities.ResizeUninitialized(IntersectionsToRoadsMap.Count());
            Transaction.CreateEntity(CornerArchetype, CornerEntities);
        }
    }

    /// <summary>
    /// Identify the key-value ranges of IntersectionsToRoadsMap.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CalculateIntersectionToRoadsMapRangesJob : IJob
    {
        [ReadOnly] public NativeMultiHashMap<Entity, IntersectionRoadConnection> IntersectionsToRoadsMap;
        public NativeList<RoadConnectionArrayRange> RoadConnectionRanges;
        public NativeList<IntersectionRoadConnection> RoadConnections;

        public void Execute()
        {
            var keys = IntersectionsToRoadsMap.GetKeyArray(Allocator.Temp);
            for (int i = 1, startIndex = 0; i <= keys.Length; i++)
            {
                if (i != keys.Length && keys[i] == keys[i - 1])
                    continue;
                RoadConnectionRanges.Add(new RoadConnectionArrayRange
                {
                    IntersectionEntity = keys[i - 1],
                    StartIndex = startIndex,
                    Length = i - startIndex
                });
                startIndex = i;
            }
            keys.Dispose();

            var values = IntersectionsToRoadsMap.GetValueArray(Allocator.Temp);
            RoadConnections.AddRange(values);
            values.Dispose();
        }
    }

    /// <summary>
    /// Records what road entities are apart of each intersection.
    /// Also allocates an appropriate number of new corner entities to each intersection entity based on the number of
    /// road segments connected to that particular intersection.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct WriteIntersectionsToRoadsMapToBufferJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<RoadConnectionArrayRange> RoadConnectionRanges;
        [ReadOnly] public NativeArray<IntersectionRoadConnection> RoadConnections;
        [ReadOnly] public NativeArray<Entity> CornerEntities;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionRoadConnection> RoadConnectionBuffers;

        [NativeDisableParallelForRestriction]
        public BufferFromEntity<IntersectionCornerEntityRef> IntersectionCornerBuffers;

        public void Execute(int index)
        {
            var range = RoadConnectionRanges[index];
            var roadConnectionsBuffer = RoadConnectionBuffers[range.IntersectionEntity];
            var intersectionCornerBuffer = IntersectionCornerBuffers[range.IntersectionEntity];
            roadConnectionsBuffer.AddRange(RoadConnections.GetSubArray(range.StartIndex, range.Length));
            intersectionCornerBuffer.Reinterpret<Entity>().AddRange(
                CornerEntities.GetSubArray(range.StartIndex, range.Length));
        }
    }
}
