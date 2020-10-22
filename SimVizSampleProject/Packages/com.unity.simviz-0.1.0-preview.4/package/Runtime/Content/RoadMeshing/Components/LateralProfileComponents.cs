using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing.Components
{
    struct LateralProfileSurface : IComponentData
    {
        public Entity Profile;
        public Entity LeftLaneMarking;
        public Entity RightLaneMarking;
        public RoadMaterial Material;
        public TextureCoordinateStrategy TexStrategy;
        public bool Drivable;
        public int SampleCount;
        public int StartIndex;
        public int EndIndex => StartIndex + SampleCount - 1;
    }

    struct LateralProfile : IComponentData
    {
        public Entity Road;
        public float2 LeftDrivableOffset;
        public float2 RightDrivableOffset;
        public int CenterIndex;
        public int LeftDrivableIndex;
        public int RightDrivableIndex;
    }


    [InternalBufferCapacity(0)]
    struct LateralProfileSample : IBufferElementData
    {
        public float2 Sample;
    }

    [InternalBufferCapacity(0)]
    struct LateralProfileSurfaceEntityRef : IBufferElementData
    {
        public Entity Surface;
    }

    [InternalBufferCapacity(0)]
    struct LateralProfileEntityRef : IBufferElementData
    {
        public Entity Profile;
    }
}
