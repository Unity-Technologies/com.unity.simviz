using System;
using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing.Components
{
    enum TextureCoordinateStrategy
    {
        TrackSpace, WorldSpace
    }

    [InternalBufferCapacity(2)]
    struct SubMesh : IBufferElementData
    {
        public int VertexStartIndex;
        public int VertexCount;
        public int TriangleStartIndex;
        public int TriangleCount;
        public RoadMaterial Material;
    }

    [InternalBufferCapacity(0)]
    struct CombinedVertex : IBufferElementData
    {
        public float3 Vertex;
        public float3 Normal;
        public float2 Uv;
    }

    [InternalBufferCapacity(0)]
    struct Triangle : IBufferElementData
    {
        public int Value;
        public static implicit operator int(Triangle v) { return v.Value; }
        public static implicit operator Triangle(int v) { return new Triangle { Value = v }; }
    }

    /// <summary>
    /// Made the RoadMaterial an int wrapper instead of an enum so
    /// a RoadMaterial can be used as a key in NativeMultiHashMaps
    /// </summary>
    struct RoadMaterial : IEquatable<RoadMaterial>, IEquatable<int>
    {
        public readonly int MaterialId;
        public static readonly RoadMaterial RoadSurface = new RoadMaterial(0);
        public static readonly RoadMaterial Gutter = new RoadMaterial(1);
        public static readonly RoadMaterial Curb = new RoadMaterial(2);
        public static readonly RoadMaterial SideWalk = new RoadMaterial(3);
        public static readonly RoadMaterial ExpansionJoint = new RoadMaterial(4);
        public static readonly RoadMaterial YellowLaneLine = new RoadMaterial(5);
        public static readonly RoadMaterial WhiteLaneLine = new RoadMaterial(6);

        RoadMaterial(int id)
        {
            MaterialId = id;
        }

        public static implicit operator RoadMaterial(int x)
        {
            return new RoadMaterial(x);
        }

        public static bool operator== (RoadMaterial mat1, RoadMaterial mat2)
        {
            return mat1.MaterialId == mat2.MaterialId;
        }

        public static bool operator !=(RoadMaterial mat1, RoadMaterial mat2)
        {
            return mat1.MaterialId != mat2.MaterialId;
        }

        public bool Equals(RoadMaterial other)
        {
            return MaterialId == other.MaterialId;
        }

        public bool Equals(int other)
        {
            return MaterialId == other;
        }

        public override bool Equals(object obj)
        {
            return obj is RoadMaterial other && Equals(other);
        }

        public override int GetHashCode()
        {
            return MaterialId;
        }
    }
}
