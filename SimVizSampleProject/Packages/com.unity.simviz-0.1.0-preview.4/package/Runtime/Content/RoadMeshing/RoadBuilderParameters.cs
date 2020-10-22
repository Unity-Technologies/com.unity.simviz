using System;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing
{
    [Serializable]
    public class RoadBuilderParameters
    {
        public LateralProfileParameters lateralProfileParameters = new LateralProfileParameters();
        public IntersectionParameters intersectionParameters = new IntersectionParameters();
        public RoadMarkingParameters roadMarkingParameters = new RoadMarkingParameters();
        [HideInInspector] public uint baseRandomSeed;
        [HideInInspector] public GameObject parentObject;
    }

    [Serializable]
    public class IntersectionParameters
    {
        public int2 numIntersectionSamples = new int2(7, 21);
        [Range(0.1f, 20f)] public float cornerRadius = 4.5f;
        [Range(1, 5)] public float cornerSamplesPerMeter = 2f;
    }

    [Serializable]
    public class RoadMarkingParameters
    {
        [Range(0.01f, 0.3f)] public float lineWidth = 0.1524f;
        [Range(0.01f, 4f)] public float dashLength = 3f;
        [Range(0.01f, 20f)] public float dashSeparationDistance = 9.144f;
        [Range(0.01f, 4f)] public float dashBeginningOffset;

        public RoadMarkingParams GetParams()
        {
            return new RoadMarkingParams
            {
                LineWidth = lineWidth,
                DashLength = dashLength,
                DashSeparationDistance = dashSeparationDistance,
                DashBeginningOffset = dashBeginningOffset
            };
        }
    }

    /// <summary>
    /// Mapped RoadMarkingParameters to a struct for passing said parameters to burst compatible jobs
    /// </summary>
    public struct RoadMarkingParams
    {
        public float LineWidth;
        public float DashLength;
        public float DashSeparationDistance;
        public float DashBeginningOffset;
    }

    [Serializable]
    public class LateralProfileParameters
    {
        public uint randomSeed;
        public bool randomNumLanes;
        [Range(1, 4)] public int numLanes = 2;
        [Range(2f, 5f)] public float laneWidth = 3.5f;
        [Range(0f, 0.3f)] public float gutterWidth = 0.1f;
        [Range(0f, 0.2f)] public float gutterDepth = 0.05f;
        [Range(0f, 0.5f)] public float curbHeight = 0.3f;
        [Range(0f, 0.3f)] public float curbRadius = 0.15f;
        [Range(0f, 3f)] public float sidewalkWidth = 1f;
        [Range(0f, 0.02f)] public float expansionJointWidth = 0.0075f;
        [Range(0f, 5f)] public float endCapHeight = 1f;

        public LateralProfileParams GetParams()
        {
            return new LateralProfileParams
            {
                LaneWidth = laneWidth,
                GutterWidth = gutterWidth,
                GutterDepth = gutterDepth,
                CurbHeight = curbHeight,
                CurbRadius = curbRadius,
                SidewalkWidth = sidewalkWidth,
                ExpansionJointWidth = expansionJointWidth,
                EndCapHeight = endCapHeight
            };
        }
    }

    /// <summary>
    /// Mapped LateralProfileParameters to a struct for passing said parameters to burst compatible jobs
    /// </summary>
    public struct LateralProfileParams
    {
        public float LaneWidth;
        public float GutterWidth;
        public float GutterDepth;
        public float CurbHeight;
        public float CurbRadius;
        public float SidewalkWidth;
        public float ExpansionJointWidth;
        public float EndCapHeight;
    }
}
