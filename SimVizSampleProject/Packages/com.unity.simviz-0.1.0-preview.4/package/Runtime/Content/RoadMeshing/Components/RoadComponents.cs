using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing.Components
{
    [InternalBufferCapacity(0)]
    struct RoadSplineSample : IBufferElementData
    {
        public RigidTransform Sample;
    }

    [InternalBufferCapacity(0)]
    struct RoadCenterLineSample : IBufferElementData
    {
        public RigidTransform Sample;
    }

    struct RoadCenterLineData : IComponentData
    {
        public int StreetId;
        public Entity StartIntersection;
        public Entity EndIntersection;
    }

    struct RoadMarking : IComponentData
    {
        public Entity Profile;
        public Entity LeftSurface;
        public Entity RightSurface;
        public float Width;
        public float DashLength;
        public float SeparationDistance;
        public float BeginningOffset;
        public RoadMaterial Material;
    }
}
