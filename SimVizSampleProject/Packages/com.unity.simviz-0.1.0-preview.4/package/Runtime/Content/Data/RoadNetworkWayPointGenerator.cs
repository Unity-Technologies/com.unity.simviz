using System;
using System.Linq;
using Unity.Collections;
using UnityEngine.SimViz.Content;
using UnityEngine.SimViz.Content.Data;
using UnityEngine.SimViz.Content.MapElements;
using UnityEngine.SimViz.Content.Sampling;

namespace UnityEngine.SimViz.Scenarios
{
    public enum RoadForwardSide
    {
        Right,
        Left
    }

    [RequireComponent(typeof(WayPointPath))]
    public class RoadNetworkWayPointGenerator : MonoBehaviour
    {
        // XXX: May want to modify the serialization behavior for this reference
        public RoadNetworkDescription RoadNetwork;
        // Invalid values will get randomized on path generation
        public string RoadId = "";
        public int LaneId;
        // NOTE: There is guaranteed to be at least one waypoint per lane section currently
        [Range(10f, 100f)]
        public float DistanceBetweenPoints = 25.0f;
        // If a randomized path is generated below this value - will attempt to find a new one
        [Range(1, 100)]
        public int MinimumNumberWaypoints = 10;
        public uint Seed = 1;
        // The side of the road that a vehicle should be on when driving forward
        public RoadForwardSide forwardSide = RoadForwardSide.Right;

        // Put an upper limit on how many times we attempt to generate random paths
        const int k_MaxPathGenerationAttempts = 5000;
        // Limit the amount of times traversal from one road to the next is allowed (to prevent unforseen looping)
        const int k_MaxTraversalSteps = 1000;

        [SerializeField, HideInInspector]
        NativeString64 m_RoadId;
        [SerializeField, HideInInspector]
        WayPointPath m_WayPointPath;
        [SerializeField, HideInInspector]
        uint m_RandSeedCurrent;
        [SerializeField, HideInInspector]
        string m_RoadNetworkCurrent;
        [SerializeField, HideInInspector]
        Unity.Mathematics.Random m_Rand;

        public void Awake()
        {
            InitializeInternals();
        }

        public void Reset()
        {
            InitializeInternals();
        }

        public void OnValidate()
        {
            UpdateInternals();
        }

        internal void GenerateNewWayPointPath()
        {
            var roadIds = new RoadSet(RoadNetwork);
            if (!roadIds.Contains(m_RoadId))
            {
                m_RoadId = roadIds.GetNotTraversed(Math.Abs(m_Rand.NextInt()));
                if (m_RoadId.Equals(default))
                {
                    if (roadIds.Count == 0)
                    {
                        Debug.LogError($"There were no RoadIds in {nameof(roadIds)}.");
                        return;
                    }

                    // It would be very mysterious if this happened...
                    System.Diagnostics.Debug.Fail($"Failed to get a m_RoadId from {nameof(roadIds)} even though there are {roadIds.Count}");
                    return;
                }
            }
            // Mark this initial road as traversed in case it's not a valid starting point and we need to get a new one
            roadIds.Traverse(m_RoadId);

            var road = RoadNetwork.GetRoadById(m_RoadId);
            while (!RoadHasAnyLanes(road))
            {
                m_RoadId = roadIds.GetNotTraversed(m_Rand.NextInt());
                if (m_RoadId.Equals(default))
                {
                    Debug.LogError($"Failed to find any roads with lanes in {RoadNetwork.name}");
                    return;
                }

                road = RoadNetwork.GetRoadById(m_RoadId);
                roadIds.Traverse(m_RoadId);
            }

            if (LaneId == 0 || !RoadHasLane(road, LaneId))
            {
                LaneId = GetRandomLaneId(road);
            }

            // Reset record so only our starting road has been touched

            // Ensure what's displayed in the inspector is consistent with this value
            RoadId = m_RoadId.ToString();
            var pathPoints = new NativeList<PointSampleGlobal>(Allocator.Temp);
            // NOTE: In road coordinates, "forward" is an arbitrary direction, so the path generated may not follow
            //       regional driving laws for side of road
            var startIdx = GetRoadStartSectionIdx(road);
            var startDirection = GetRoadStartDirection();
            var traversalState = new TraversalState(RoadNetwork, m_RoadId, startIdx, LaneId, startDirection);
            var samplesPerMeter = 1f / DistanceBetweenPoints;
            var roadIdCurrent = m_RoadId.ToString();
            var samplingState = new SamplingStateRoad(road, samplesPerMeter);
            var numRoadsTraversed = 1;
            var numTotalSteps = 0;
            do
            {
                numTotalSteps++;
                if (roadIdCurrent != traversalState.RoadId.ToString())
                {
                    numRoadsTraversed++;
                    roadIdCurrent = traversalState.RoadId.ToString();
                    //Debug.Log($"Moving to road {roadIdCurrent}, lane {traversalState.LaneId}");
                    road = RoadNetwork.GetRoadById(roadIdCurrent);
                    samplingState = new SamplingStateRoad(road, samplesPerMeter);
                }

                var numSamplesSection = GeometrySampling.ComputeNumSamplesForLaneSection(road, traversalState.LaneSectionIdx,
                    samplesPerMeter);
                var newSamples = new NativeArray<PointSampleGlobal>(numSamplesSection, Allocator.Temp);
                GeometrySampling.BuildSamplesInsideLaneSection(
                    road, traversalState.LaneId, numSamplesSection, 0, traversalState.Direction,
                    ref samplingState, ref newSamples);
                pathPoints.AddRange(newSamples);
                newSamples.Dispose();
            } while (RoadNetworkTraversal.TryAdvanceOneLaneSection(RoadNetwork, ref traversalState) &&
                numTotalSteps < k_MaxTraversalSteps);

            if (k_MaxTraversalSteps == numTotalSteps)
            {
                Debug.LogWarning($"Traversal reached maximum allowed steps ({k_MaxTraversalSteps}. " +
                    $"May have encountered a loop...");
            }

            var wayPoints = m_WayPointPath.wayPoints;
            wayPoints.Clear();
            wayPoints.AddRange(pathPoints.AsArray().Reinterpret<WayPoint>());

            Debug.Log($"Path generated: {pathPoints.Length} points over {numRoadsTraversed} road segments.");
            pathPoints.Dispose();
        }

        public void GeneratePathFromInspectorValues()
        {
            if (RoadNetwork == null)
            {
                Debug.LogError("Road network reference is null");
                return;
            }

            GenerateNewWayPointPath();
            var numPoints = m_WayPointPath.wayPoints.Count;
            if (numPoints < MinimumNumberWaypoints)
            {
                Debug.LogWarning(
                    $"Generated path has only {numPoints} - you may want to randomize it to get a larger one.");
            }
        }

        public void GenerateRandomizedPath()
        {
            if (RoadNetwork == null)
            {
                Debug.LogError("Road network reference is null");
                return;
            }

            int numPointsPath;
            var numAttempts = 0;
            do
            {
                RoadId = "";
                LaneId = 0;
                UpdateInternals();
                GenerateNewWayPointPath();
                numAttempts++;
                numPointsPath = m_WayPointPath.wayPoints.Count;
            } while (numPointsPath < MinimumNumberWaypoints && numAttempts < k_MaxPathGenerationAttempts);

            if (numAttempts == k_MaxPathGenerationAttempts)
            {
                Debug.LogError("Failed to generate a random path with an adequate number of points.");
            }
        }

        int GetRandomLaneId(Road road)
        {
            var startIdx = GetRoadStartSectionIdx(road);
            var start = road.laneSections[startIdx];
            var lanes = start.edgeLanes.Select(r => r.id);
            return lanes.ElementAt(Random.Range(0, lanes.Count()));
        }

        void InitializeInternals()
        {
            m_WayPointPath = GetComponent<WayPointPath>();
            if (RoadNetwork == null)
            {
                RoadNetwork = GetComponent<RoadNetworkReference>()?.RoadNetwork;
            }

            m_RandSeedCurrent = Seed;
            m_RoadNetworkCurrent = RoadNetwork != null ? RoadNetwork.name : "";
            InitializeRand(Seed);
        }

        void InitializeRand(uint seed)
        {
            m_RandSeedCurrent = seed;
            m_Rand = new Unity.Mathematics.Random(seed);
        }

        int GetRoadStartSectionIdx(Road road)
        {
            return forwardSide == RoadForwardSide.Right ? 0 : road.laneSections.Count - 1;
        }

        TraversalDirection GetRoadStartDirection()
        {
            return forwardSide == RoadForwardSide.Right ? TraversalDirection.Forward : TraversalDirection.Backward;
        }

        bool RoadHasAnyLanes(Road road)
        {
            return road.laneSections != null && road.laneSections.Any(section => section.edgeLanes.Any());
        }

        bool RoadHasLane(Road road, int laneId)
        {
            foreach (var section in road.laneSections)
            {
                if (section.HasLane(laneId))
                {
                    return true;
                }
            }

            return false;
        }

        void UpdateInternals()
        {
            if (m_RandSeedCurrent != Seed)
            {
                InitializeRand(Seed);
            }

            if (RoadNetwork == null)
            {
                m_RoadNetworkCurrent = "";
            }
            else if (m_RoadNetworkCurrent != RoadNetwork.name)
            {
                m_RoadNetworkCurrent = RoadNetwork.name;
                m_RoadId = default;
                LaneId = 0;
            }

            if (m_WayPointPath == null)
            {
                m_WayPointPath = GetComponent<WayPointPath>();
                if (m_WayPointPath == null)
                {
                    Debug.LogError("No WaypointPath attached - won't be able to generate a path.");
                }
            }

            if (RoadNetwork?.HasRoad(RoadId) ?? false)
            {
                m_RoadId = new NativeString64(RoadId);
            }
            else
            {
                m_RoadId = default;
            }
        }
    }
}
