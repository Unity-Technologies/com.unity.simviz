using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.Pipeline.Components
{
    [InternalBufferCapacity(0)]
    public struct OffsetPlacementPathRange : IBufferElementData
    {
        public int StartIndex;
        public int SampleCount;
    }

    [InternalBufferCapacity(0)]
    public struct OffsetPlacementPathSample : IBufferElementData
    {
        public RigidTransform Sample;
    }

    [InternalBufferCapacity(0)]
    public struct UniformPlacementPoint : IBufferElementData
    {
        public RigidTransform Sample;
    }
}
