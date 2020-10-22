using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.SimViz.Content.RoadMeshing.Components;

namespace UnityEngine.SimViz.Content.RoadMeshing.Jobs
{
    /// <summary>
    /// Value struct in a map of road materials to subMesh entities
    /// </summary>
    struct SubMeshRecord
    {
        public Entity SubMeshEntity;
        public int VertexStartIndex;
        public int TriangleStartIndex;
        public int VertexCount;
        public int TriangleCount;
    }

    /// <summary>
    /// Maps subMesh data to a larger combined array of mesh data
    /// </summary>
    struct MappedSubMeshIndices
    {
        public int TriangleIndexOffset;
        public int VertexStartIndex;
        public int TriangleStartIndex;
    }

    /// <summary>
    /// Copies subMeshEntities to a native list.
    /// IJobParallelForDefer does not (as of 3/9/2019) have a native array interface for IJobParallelForDefer,
    /// so we must first convert the EntityQuery's NativeArray of entities to NativeList before we can setup
    /// any proceeding IJobParallelForDefer jobs.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CopySubMeshEntities : IJob
    {
        public NativeMultiHashMap<RoadMaterial, SubMeshRecord> MaterialToSubMeshMap;
        public NativeArray<Entity> SubMeshEntities;
        public NativeList<Entity> SubMeshEntitiesCopy;

        public void Execute()
        {
            MaterialToSubMeshMap.Capacity = SubMeshEntities.Length * 2;
            SubMeshEntitiesCopy.AddRange(SubMeshEntities);
        }
    }

    /// <summary>
    /// Maps all created SubMeshes to their RoadMaterial id's
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CountSubMeshesJob : IJobParallelForDefer
    {
        [WriteOnly] public NativeMultiHashMap<RoadMaterial, SubMeshRecord>.ParallelWriter MaterialToSubMeshMap;
        [ReadOnly] public BufferFromEntity<SubMesh> SubMeshBuffers;
        [ReadOnly] public NativeArray<Entity> SubMeshEntities;

        public void Execute(int index)
        {
            var subMeshEntity = SubMeshEntities[index];
            var subMeshBuffer = SubMeshBuffers[subMeshEntity];
            for (var i = 0; i < subMeshBuffer.Length; i++)
            {
                var subMesh = subMeshBuffer[i];
                MaterialToSubMeshMap.Add(subMesh.Material, new SubMeshRecord
                {
                    SubMeshEntity = subMeshEntity,
                    VertexStartIndex = subMesh.VertexStartIndex,
                    TriangleStartIndex = subMesh.TriangleStartIndex,
                    VertexCount = subMesh.VertexCount,
                    TriangleCount = subMesh.TriangleCount
                });
            }
        }
    }

    /// <summary>
    /// Calculates where inside of the combined vertex and triangle arrays each SubMesh entity will copy their data to.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct GetMaterialMeshRangesJob : IJob
    {
        public bool UniqueGameObjectPerMaterial;
        public NativeMultiHashMap<RoadMaterial, SubMeshRecord> MaterialToSubMeshMap;
        public NativeList<CombinedVertex> Vertices;
        public NativeList<int> Triangles;
        public NativeList<SubMesh> SubMeshes;
        public NativeList<SubMeshRecord> SubMeshRecords;
        public NativeList<MappedSubMeshIndices> SubMeshMap;

        int m_VertexCount, m_TriangleCount, m_PrevVertexCount, m_PrevTriangleCount, m_TriangleIndexOffset;

        void AddSubMesh(RoadMaterial material)
        {
            SubMeshes.Add(new SubMesh
            {
                Material = material,
                VertexStartIndex = m_PrevVertexCount,
                VertexCount = m_VertexCount - m_PrevVertexCount,
                TriangleStartIndex = m_PrevTriangleCount,
                TriangleCount = m_TriangleCount - m_PrevTriangleCount
            });
            m_PrevVertexCount = m_VertexCount;
            m_PrevTriangleCount = m_TriangleCount;
            if (UniqueGameObjectPerMaterial)
                m_TriangleIndexOffset = 0;
        }

        public void Execute()
        {
            if (MaterialToSubMeshMap.Count() == 0) return;

            // Copy the values array of MaterialToSubMeshMap to the SubMeshRecords NativeList
            {
                var subMeshRecords = MaterialToSubMeshMap.GetValueArray(Allocator.Temp);
                SubMeshRecords.AddRange(subMeshRecords);
                SubMeshMap.ResizeUninitialized(SubMeshRecords.Length);
                subMeshRecords.Dispose();
            }

            var keys = MaterialToSubMeshMap.GetKeyArray(Allocator.Temp);
            for (var i = 0; i < SubMeshRecords.Length; i++)
            {
                var subMesh = SubMeshRecords[i];
                SubMeshMap[i] = new MappedSubMeshIndices
                {
                    TriangleIndexOffset = m_TriangleIndexOffset,
                    VertexStartIndex = m_VertexCount,
                    TriangleStartIndex = m_TriangleCount
                };
                m_VertexCount += subMesh.VertexCount;
                m_TriangleCount += subMesh.TriangleCount;
                m_TriangleIndexOffset += subMesh.VertexCount;

                var material = keys[i];
                if (i == SubMeshRecords.Length - 1 || material != keys[i + 1])
                    AddSubMesh(material);
            }

            Vertices.ResizeUninitialized(m_VertexCount);
            Triangles.ResizeUninitialized(m_TriangleCount);
        }
    }

    /// <summary>
    /// Copies SubMesh vertices and triangles to the combined vertex and triangle arrays.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct CopySubMeshesToLinearArrayJob : IJobParallelForDefer
    {
        [NativeDisableContainerSafetyRestriction] public NativeArray<CombinedVertex> Vertices;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> Triangles;
        [ReadOnly] public NativeArray<MappedSubMeshIndices> SubMeshMap;
        [ReadOnly] public NativeArray<SubMeshRecord> SubMeshRecords;
        [ReadOnly] public BufferFromEntity<CombinedVertex> CombinedVertexBuffers;
        [ReadOnly] public BufferFromEntity<Triangle> TriangleBuffers;

        public void Execute(int index)
        {
            var record = SubMeshRecords[index];
            var mappedIndices = SubMeshMap[index];

            var vertexBuffer = CombinedVertexBuffers[record.SubMeshEntity]
                .AsNativeArray().GetSubArray(record.VertexStartIndex, record.VertexCount);

            var triangleBuffer = TriangleBuffers[record.SubMeshEntity].Reinterpret<int>()
                .AsNativeArray().GetSubArray(record.TriangleStartIndex, record.TriangleCount);

            var triangleCopy = new NativeArray<int>(triangleBuffer, Allocator.Temp);
            for (var i = 0; i < triangleCopy.Length; i++)
                triangleCopy[i] += mappedIndices.TriangleIndexOffset;

            Vertices.GetSubArray(mappedIndices.VertexStartIndex, record.VertexCount).CopyFrom(vertexBuffer);
            Triangles.GetSubArray(mappedIndices.TriangleStartIndex, record.TriangleCount).CopyFrom(triangleCopy);
            triangleCopy.Dispose();
        }
    }
}
