using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing.Components
{
    enum DashCap
    {
        Start, End
    }

    [InternalBufferCapacity(0)]
    struct DashPoint : IBufferElementData
    {
        public float Distance;
        public DashCap Cap;
    }

    [InternalBufferCapacity(0)]
    struct RoadMarkingSample : IBufferElementData
    {
        public RigidTransform Sample;
    }
}
