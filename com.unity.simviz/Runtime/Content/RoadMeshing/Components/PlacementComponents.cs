using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing.Components
{
    [InternalBufferCapacity(0)]
    public struct PlacementPathSample : IBufferElementData
    {
        RigidTransform Sample;
    }

    [InternalBufferCapacity(0)]
    public struct CameraPathSample : IBufferElementData
    {
        RigidTransform Sample;
    }
}
