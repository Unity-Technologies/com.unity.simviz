using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.RoadMeshing.Jobs;
#if PERCEPTION_PRESENT
using UnityEngine.Perception.GroundTruth;
#endif

namespace UnityEngine.SimViz.Content.RoadMeshing.Systems
{
    [Serializable]
    public class RoadMaterialParameters
    {
        public RoadSurfaceInfo roadSurface = new RoadSurfaceInfo("RoadSurface", null);
        public RoadSurfaceInfo gutter = new RoadSurfaceInfo("Sidewalk", null);
        public RoadSurfaceInfo curb = new RoadSurfaceInfo("Sidewalk", null);
        public RoadSurfaceInfo sidewalk = new RoadSurfaceInfo("Sidewalk", null);
        public RoadSurfaceInfo expansionJoint = new RoadSurfaceInfo("Sidewalk", null);
        public RoadSurfaceInfo centerLineMarking = new RoadSurfaceInfo("RoadMarking", null);
        public RoadSurfaceInfo laneMarking = new RoadSurfaceInfo("RoadMarking", null);
        public bool addMeshCollider = true;

        [HideInInspector] public GameObject parentObject;
    }

    [Serializable]
    public class RoadSurfaceInfo
    {
        public string label;
        public Material material;

        public RoadSurfaceInfo(string label, Material material)
        {
            this.label = label;
            this.material = material;
        }
    }

    /// <summary>
    /// Combines road SubMesh Vertex and Triangle data into new mesh objects
    /// </summary>
    [DisableAutoCreation]
    class AssembleMeshSystem : ComponentSystem, IGeneratorSystem<RoadMaterialParameters>
    {
        public RoadMaterialParameters Parameters { get; set; }

        protected override void OnUpdate()
        {
            var materialToSubMeshMap = new NativeMultiHashMap<RoadMaterial, SubMeshRecord>(0, Allocator.TempJob);
            var subMeshQuery = GetEntityQuery(typeof(SubMesh));
            var subMeshEntities = subMeshQuery.ToEntityArrayAsync(Allocator.TempJob, out var queryHandle);

            var subMeshEntitiesCopy = new NativeList<Entity>(Allocator.TempJob);
            var subMeshes = new NativeList<SubMesh>(Allocator.TempJob);
            var subMeshRecords = new NativeList<SubMeshRecord>(Allocator.TempJob);
            var subMeshMap = new NativeList<MappedSubMeshIndices>(Allocator.TempJob);
            var triangles = new NativeList<int>(Allocator.TempJob);
            var vertices = new NativeList<CombinedVertex>(Allocator.TempJob);

            var copySubMeshEntities = new CopySubMeshEntities
            {
                MaterialToSubMeshMap = materialToSubMeshMap,
                SubMeshEntitiesCopy = subMeshEntitiesCopy,
                SubMeshEntities = subMeshEntities
            }.Schedule(queryHandle);

            var countSubMeshes = new CountSubMeshesJob
            {
                MaterialToSubMeshMap = materialToSubMeshMap.AsParallelWriter(),
                SubMeshBuffers = GetBufferFromEntity<SubMesh>(),
                SubMeshEntities = subMeshEntitiesCopy.AsDeferredJobArray()
            }.Schedule(subMeshEntitiesCopy, 1, copySubMeshEntities);

            var meshRangesJob = new GetMaterialMeshRangesJob
            {
                UniqueGameObjectPerMaterial = true,
                MaterialToSubMeshMap = materialToSubMeshMap,
                SubMeshes = subMeshes,
                SubMeshRecords = subMeshRecords,
                SubMeshMap = subMeshMap,
                Triangles = triangles,
                Vertices = vertices
            }.Schedule(countSubMeshes);

            var copySubMeshesToLinearArrayJob = new CopySubMeshesToLinearArrayJob
            {
                SubMeshRecords = subMeshRecords.AsDeferredJobArray(),
                SubMeshMap = subMeshMap.AsDeferredJobArray(),
                CombinedVertexBuffers = GetBufferFromEntity<CombinedVertex>(),
                TriangleBuffers = GetBufferFromEntity<Triangle>(),
                Vertices = vertices.AsDeferredJobArray(),
                Triangles = triangles.AsDeferredJobArray()
            }.Schedule(subMeshRecords, 1, meshRangesJob);
            copySubMeshesToLinearArrayJob.Complete();

            Profiler.BeginSample("Create Mesh Objects");
            CreateMeshForEachSubMesh(vertices, triangles, subMeshes);
            Profiler.EndSample();

            materialToSubMeshMap.Dispose(meshRangesJob);
            subMeshEntities.Dispose(meshRangesJob);
            subMeshEntitiesCopy.Dispose(meshRangesJob);
            subMeshes.Dispose(meshRangesJob);
            subMeshRecords.Dispose(meshRangesJob);
            subMeshMap.Dispose(meshRangesJob);
            triangles.Dispose(meshRangesJob);
            vertices.Dispose(meshRangesJob);
        }

        void CreateMeshForEachSubMesh(
            NativeArray<CombinedVertex> vertices,
            NativeArray<int> triangles,
            NativeArray<SubMesh> subMeshes)
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds;
            foreach (var subMesh in subMeshes)
            {
                var vertexSlice = vertices.GetSubArray(subMesh.VertexStartIndex, subMesh.VertexCount);
                var trianglesSlice = triangles.GetSubArray(subMesh.TriangleStartIndex, subMesh.TriangleCount);
                var surfaceInfo = GetRoadSurfaceInfo(subMesh.Material);
                var meshObject = new GameObject(surfaceInfo.label) { isStatic = true };
                meshObject.transform.parent = Parameters.parentObject.transform;

                var filter = meshObject.AddComponent<MeshFilter>();
                var mesh = new Mesh();
                var layout = new[]
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position),
                    new VertexAttributeDescriptor(VertexAttribute.Normal),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                };
                mesh.SetVertexBufferParams(vertexSlice.Length, layout);
                mesh.SetVertexBufferData(vertexSlice, 0, 0, vertexSlice.Length, 0, flags);
                mesh.SetIndexBufferParams(trianglesSlice.Length, IndexFormat.UInt32);
                mesh.SetIndexBufferData(trianglesSlice, 0, 0, trianglesSlice.Length, flags);
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, subMesh.TriangleCount), flags);
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;

                var renderer = meshObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = surfaceInfo.material;

                if (Parameters.addMeshCollider)
                    meshObject.AddComponent<MeshCollider>();

#if PERCEPTION_PRESENT
                var label = meshObject.AddComponent<Labeling>();
                label.labels.Add(surfaceInfo.label);
#endif
            }
        }

        RoadSurfaceInfo GetRoadSurfaceInfo(RoadMaterial roadMaterial)
        {
            RoadSurfaceInfo material = null;
            switch (roadMaterial.MaterialId)
            {
                case 0:
                    material = Parameters.roadSurface;
                    break;
                case 1:
                    material = Parameters.gutter;
                    break;
                case 2:
                    material = Parameters.curb;
                    break;
                case 3:
                    material = Parameters.sidewalk;
                    break;
                case 4:
                    material = Parameters.expansionJoint;
                    break;
                case 5:
                    material = Parameters.centerLineMarking;
                    break;
                case 6:
                    material = Parameters.laneMarking;
                    break;
            }

            return material;
        }
    }
}
