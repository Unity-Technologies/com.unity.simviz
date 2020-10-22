using System;
using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing.Components
{
    enum IntersectionRoadDirection
    {
        Outgoing, Incoming
    }

    struct IntersectionMesh : IComponentData {}

    struct IntersectionData : IComponentData
    {
        public float2 Point;
    }

    struct IntersectionCorner : IComponentData
    {
        public IntersectionRoadConnection LeftRoad;
        public IntersectionRoadConnection RightRoad;
        public float3 Center;
        public RigidTransform TangentLeft;
        public RigidTransform TangentRight;
        public float TangentLeftIndex;
        public float TangentRightIndex;
        public float Radius;
    }

    struct CornerRoadCropIndices : IComponentData
    {
        public float LeftRoadIndex;
        public float RightRoadIndex;
    }

    [InternalBufferCapacity(0)]
    struct IntersectionRoadConnection : IBufferElementData, IEquatable<IntersectionRoadConnection>
    {
        public Entity Road;
        public IntersectionRoadDirection Direction;
        public IntersectionRoadDirection InverseDirection =>
            Direction == IntersectionRoadDirection.Incoming
                ? IntersectionRoadDirection.Outgoing
                : IntersectionRoadDirection.Incoming;

        public bool Equals(IntersectionRoadConnection other)
        {
            return Road == other.Road && Direction == other.Direction;
        }

        public override int GetHashCode()
        {
            return (Road.GetHashCode() * 397) ^ (int)Direction;
        }
    }

    [InternalBufferCapacity(0)]
    struct IntersectionCornerEntityRef : IBufferElementData
    {
        public Entity CornerEntity;
    }

    [InternalBufferCapacity(0)]
    struct IntersectionGridSample : IBufferElementData
    {
        public float3 Sample;
    }

    [InternalBufferCapacity(0)]
    struct RoundedCornerSample : IBufferElementData
    {
        public RigidTransform Sample;
    }

    [InternalBufferCapacity(0)]
    struct IntersectionMeshInsideEdgeSample : IBufferElementData
    {
        public RigidTransform Sample;
    }

    [InternalBufferCapacity(0)]
    struct IntersectionMeshOutsideEdgeSample : IBufferElementData
    {
        public RigidTransform Sample;
    }

    [InternalBufferCapacity(0)]
    struct LeftLaneEdgeSample : IBufferElementData
    {
        public RigidTransform Sample;
    }

    [InternalBufferCapacity(0)]
    struct RightLaneEdgeSample : IBufferElementData
    {
        public RigidTransform Sample;
    }
}
