using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.Pipeline.Components
{
    [InternalBufferCapacity(0)]
    public struct PolygonPoint : IBufferElementData
    {
        public float2 Point;
    }

    [InternalBufferCapacity(0)]
    public struct PoissonPoint : IBufferElementData
    {
        public float2 Point;
    }

    /// <summary>
    /// An axis aligned bounding box encapsulating a region of generated poisson points
    /// </summary>
    public struct PoissonPointRegion : IComponentData
    {
        public float2 Size;
        public float2 Center;
    }
}
