using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.RoadMeshing.Components;

namespace UnityEngine.SimViz.Content.RoadMeshing.Jobs
{
    /// <summary>
    /// Creates a vertex and triangle buffer for each road surface entity
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct LoftLateralProfileJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> SurfaceEntities;
        [ReadOnly] public ComponentDataFromEntity<RoadMarking> RoadMarkings;
        [ReadOnly] public ComponentDataFromEntity<LateralProfile> Profiles;
        [ReadOnly] public ComponentDataFromEntity<LateralProfileSurface> Surfaces;
        [ReadOnly] public BufferFromEntity<LateralProfileSample> ProfileSampleBuffers;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> LoftPathBuffers;
        [ReadOnly] public BufferFromEntity<RoadMarkingSample> RoadMarkingSampleBuffers;
        [ReadOnly] public BufferFromEntity<DashPoint> DashPointBuffers;

        [NativeDisableParallelForRestriction] public BufferFromEntity<CombinedVertex> CombinedVertexBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<Triangle> TriangleBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<SubMesh> SubMeshBuffers;

        /// <summary>
        /// Generates a simplified triangulation for road surfaces that do not have dashed road markings
        /// </summary>
        /// <param name="index"></param>
        void SimpleLoft(int index)
        {
            var surfaceEntity = SurfaceEntities[index];
            var surface = Surfaces[surfaceEntity];
            var profile = Profiles[surface.Profile];

            var loftPath = LoftPathBuffers[profile.Road].Reinterpret<RigidTransform>();
            var lateralProfileBuffer = ProfileSampleBuffers[surface.Profile]
                .Reinterpret<float2>().AsNativeArray()
                .GetSubArray(surface.StartIndex, surface.SampleCount);
            var lateralProfile = new NativeArray<float2>(lateralProfileBuffer.Length, Allocator.Temp);
            lateralProfile.CopyFrom(lateralProfileBuffer);

            if (surface.LeftLaneMarking != Entity.Null)
            {
                var markingWidth = RoadMarkings[surface.LeftLaneMarking].Width / 2f;
                lateralProfile[0] += new float2(markingWidth, 0f);
            }
            if (surface.RightLaneMarking != Entity.Null)
            {
                var markingWidth = RoadMarkings[surface.RightLaneMarking].Width / 2f;
                lateralProfile[lateralProfile.Length - 1] -= new float2(markingWidth, 0f);
            }

            var numVertices = loftPath.Length * lateralProfile.Length;
            var vertices = new NativeArray<float3>(numVertices, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < loftPath.Length; i++)
            {
                for (var j = 0; j < lateralProfile.Length; j++)
                {
                    vertices[i * lateralProfile.Length + j] = math.transform(
                        loftPath[i],
                        Utilities.GeometryUtility.SwizzleXY0(lateralProfile[j]));
                }
            }

            // Create normals for lateral profile
            var lateralProfileNormals = new NativeArray<float3>(lateralProfile.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            {
                var v1 = Utilities.GeometryUtility.SwizzleXY0(math.normalize(lateralProfile[1] - lateralProfile[0]));
                v1 = new float3(-v1.y, v1.x, 0f);
                lateralProfileNormals[0] = v1;

                var v2 = v1;
                for (var j = 1; j < lateralProfile.Length - 1; j++)
                {
                    v2 = Utilities.GeometryUtility.SwizzleXY0(math.normalize(lateralProfile[j + 1] - lateralProfile[j]));
                    v2 = new float3(-v2.y, v2.x, 0f);

                    lateralProfileNormals[j] = math.normalize(v1 + v2);
                    v1 = v2;
                }

                lateralProfileNormals[lateralProfile.Length - 1] = v2;
            }

            var normals = new NativeArray<float3>(numVertices, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < loftPath.Length; i++)
            {
                for (var j = 0; j < lateralProfile.Length; j++)
                {
                    normals[i * lateralProfile.Length + j] = math.rotate(loftPath[i], lateralProfileNormals[j]);
                }
            }

            var uvs = new NativeArray<float2>(numVertices, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            if (surface.TexStrategy == TextureCoordinateStrategy.WorldSpace)
            {
                for (var i = 0; i < uvs.Length; i++)
                {
                    uvs[i] = vertices[i].xz;
                }
            }
            else
            {
                var us = new NativeArray<float>(lateralProfile.Length, Allocator.Temp) {[0] = 0f};
                for (var i = 1; i < us.Length; i++)
                {
                    us[i] = math.distance(lateralProfile[i], lateralProfile[i-1]) + us[i-1];
                }

                var vs = new NativeArray<float>(loftPath.Length, Allocator.Temp) {[0] = 0f};
                for (var i = 1; i < vs.Length; i++)
                {
                    vs[i] = math.distance(loftPath[i].pos, loftPath[i-1].pos) + vs[i-1];
                }

                for (var i = 0; i < loftPath.Length; i++)
                {
                    for (var j = 0; j < lateralProfile.Length; j++)
                    {
                        uvs[i * lateralProfile.Length + j] = new float2(us[j], vs[i]);
                    }
                }
            }

            var newSubMesh = new SubMesh { Material = surface.Material };
            var combinedVertexBuffer = CombinedVertexBuffers[surfaceEntity];
            newSubMesh.VertexStartIndex = combinedVertexBuffer.Length;
            newSubMesh.VertexCount = vertices.Length;
            combinedVertexBuffer.ResizeUninitialized(newSubMesh.VertexStartIndex + newSubMesh.VertexCount);
            for (int i = 0, j = newSubMesh.VertexStartIndex; i < vertices.Length; i++, j++)
            {
                combinedVertexBuffer[j] = new CombinedVertex
                {
                    Vertex = vertices[i],
                    Normal = normals[i],
                    Uv = uvs[i]
                };
            }

            vertices.Dispose();
            normals.Dispose();
            uvs.Dispose();

            var triangleBuffer = TriangleBuffers[surfaceEntity].Reinterpret<int>();
            newSubMesh.TriangleStartIndex = triangleBuffer.Length;
            var numTriangles = (loftPath.Length - 1) * (lateralProfile.Length - 1) * 6;
            newSubMesh.TriangleCount = numTriangles;
            triangleBuffer.ResizeUninitialized(newSubMesh.TriangleStartIndex + newSubMesh.TriangleCount);
            for (int i = 1, j = newSubMesh.TriangleStartIndex; i < loftPath.Length; i++)
            {
                var prevOffset = (i-1) * lateralProfile.Length;
                var nextOffset = i * lateralProfile.Length;

                for (var k = 1; k < lateralProfile.Length; k++)
                {
                    triangleBuffer[j++] = prevOffset + k;
                    triangleBuffer[j++] = prevOffset + k - 1;
                    triangleBuffer[j++] = nextOffset + k - 1;

                    triangleBuffer[j++] = nextOffset + k - 1;
                    triangleBuffer[j++] = nextOffset + k;
                    triangleBuffer[j++] = prevOffset + k;
                }
            }

            lateralProfile.Dispose();
            SubMeshBuffers[surfaceEntity].Add(newSubMesh);
        }

        /// <summary>
        /// Triangulates a road surface bordered on at least one side by dashed road markings
        /// </summary>
        /// <param name="index"></param>
        void LoftWithDashedRoadMarking(int index)
        {
            var surfaceEntity = SurfaceEntities[index];
            var surface = Surfaces[surfaceEntity];
            var profile = Profiles[surface.Profile];

            var lateralProfileBuffer = ProfileSampleBuffers[surface.Profile]
                .Reinterpret<float2>().AsNativeArray()
                .GetSubArray(surface.StartIndex, surface.SampleCount);
            var leftLateralPoint = new float3(lateralProfileBuffer[0].x, 0f, 0f);
            var rightLateralPoint = new float3(lateralProfileBuffer[lateralProfileBuffer.Length - 1].x, 0f, 0f);

            var loftPath = LoftPathBuffers[profile.Road].Reinterpret<RigidTransform>().AsNativeArray();
            NativeArray<RigidTransform> leftSamples, rightSamples;
            NativeArray<DashPoint> leftDashPoints, rightDashPoints;

            if (surface.LeftLaneMarking != Entity.Null &&
                !Utilities.GeometryUtility.ApproximatelyEqual(RoadMarkings[surface.LeftLaneMarking].SeparationDistance, 0f))
            {
                var leftMarkingWidth = RoadMarkings[surface.LeftLaneMarking].Width;
                leftLateralPoint.x += leftMarkingWidth / 2f;
                var roadMarkingSamples = RoadMarkingSampleBuffers[surface.LeftLaneMarking]
                    .Reinterpret<RigidTransform>().AsNativeArray();
                leftSamples = Utilities.SplineUtility.OffsetSpline(roadMarkingSamples, leftLateralPoint, Allocator.Temp);
                leftDashPoints = new NativeArray<DashPoint>(DashPointBuffers[surface.LeftLaneMarking].AsNativeArray(), Allocator.Temp);
            }
            else
            {
                if (surface.LeftLaneMarking != Entity.Null)
                    leftLateralPoint.x += RoadMarkings[surface.LeftLaneMarking].Width / 2f;
                leftSamples = Utilities.SplineUtility.OffsetSpline(loftPath, leftLateralPoint, Allocator.Temp);
                leftDashPoints = new NativeArray<DashPoint>(leftSamples.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (var i = 0; i < leftDashPoints.Length; i++)
                {
                    leftDashPoints[i] = new DashPoint { Distance = i };
                }
            }

            if (surface.RightLaneMarking != Entity.Null &&
                !Utilities.GeometryUtility.ApproximatelyEqual(RoadMarkings[surface.RightLaneMarking].SeparationDistance, 0f))
            {
                var rightMarkingWidth = RoadMarkings[surface.RightLaneMarking].Width;
                rightLateralPoint.x -= rightMarkingWidth / 2f;
                var roadMarkingSamples = RoadMarkingSampleBuffers[surface.RightLaneMarking]
                    .Reinterpret<RigidTransform>().AsNativeArray();
                rightSamples = Utilities.SplineUtility.OffsetSpline(roadMarkingSamples, rightLateralPoint, Allocator.Temp);
                rightDashPoints = new NativeArray<DashPoint>(DashPointBuffers[surface.RightLaneMarking].AsNativeArray(), Allocator.Temp);
            }
            else
            {
                if (surface.RightLaneMarking != Entity.Null)
                    rightLateralPoint.x -= RoadMarkings[surface.RightLaneMarking].Width / 2f;
                rightSamples = Utilities.SplineUtility.OffsetSpline(loftPath, rightLateralPoint, Allocator.Temp);
                rightDashPoints = new NativeArray<DashPoint>(rightSamples.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (var i = 0; i < rightDashPoints.Length; i++)
                {
                    rightDashPoints[i] = new DashPoint { Distance = i };
                }
            }

            var vertexBuffer = CombinedVertexBuffers[surfaceEntity];
            var numVertices = leftSamples.Length + rightSamples.Length;
            vertexBuffer.ResizeUninitialized(numVertices);
            var vertexArray = vertexBuffer.AsNativeArray();
            for (var i = 0; i < leftSamples.Length; i++)
            {
                vertexArray[i] = new CombinedVertex
                {
                    Vertex = leftSamples[i].pos,
                    Normal = math.mul(leftSamples[i].rot, new float3(0f, 1f, 0f)),
                    Uv = leftSamples[i].pos.xz
                };
            }
            for (int i = 0, j = leftSamples.Length; i < rightSamples.Length; i++, j++)
            {
                vertexArray[j] = new CombinedVertex
                {
                    Vertex = rightSamples[i].pos,
                    Normal = math.mul(rightSamples[i].rot, new float3(0f, 1f, 0f)),
                    Uv = rightSamples[i].pos.xz
                };
            }

            var triangleBuffer = TriangleBuffers[surfaceEntity].Reinterpret<int>();
            triangleBuffer.ResizeUninitialized(3 * (numVertices - 2));
            var triangleArray = triangleBuffer.AsNativeArray();

            var numLeftSamples = leftSamples.Length;
            for (int i = 1, leftStartIdx = 0, rightStartIdx = 0, triangleIdx = 0; i < loftPath.Length; i++)
            {
                var nextLeftIdx = leftStartIdx + 1;
                var nextRightIdx = rightStartIdx + 1;
                while (!Utilities.GeometryUtility.ApproximatelyEqual(leftDashPoints[nextLeftIdx].Distance, i)) nextLeftIdx++;
                while (!Utilities.GeometryUtility.ApproximatelyEqual(rightDashPoints[nextRightIdx].Distance, i)) nextRightIdx++;

                for (var j = leftStartIdx; j < nextLeftIdx; j++)
                {
                    triangleArray[triangleIdx++] = rightStartIdx + numLeftSamples;
                    triangleArray[triangleIdx++] = j;
                    triangleArray[triangleIdx++] = j + 1;
                }

                for (var j = rightStartIdx; j < nextRightIdx; j++)
                {
                    triangleArray[triangleIdx++] = nextLeftIdx;
                    triangleArray[triangleIdx++] = numLeftSamples + j + 1;
                    triangleArray[triangleIdx++] = numLeftSamples + j;
                }

                leftStartIdx = nextLeftIdx;
                rightStartIdx = nextRightIdx;
            }

            var subMeshBuffer = SubMeshBuffers[surfaceEntity];
            subMeshBuffer.Add(new SubMesh
            {
                Material = RoadMaterial.RoadSurface,
                VertexStartIndex = 0,
                VertexCount = vertexArray.Length,
                TriangleStartIndex = 0,
                TriangleCount = triangleArray.Length
            });

            leftSamples.Dispose();
            leftDashPoints.Dispose();
            rightSamples.Dispose();
            rightDashPoints.Dispose();
        }

        bool HasDashedLaneMarking(LateralProfileSurface surface)
        {
            return (surface.LeftLaneMarking != Entity.Null &&
                    !Utilities.GeometryUtility.ApproximatelyEqual(
                        RoadMarkings[surface.LeftLaneMarking].SeparationDistance, 0)) ||
                   (surface.RightLaneMarking != Entity.Null &&
                    !Utilities.GeometryUtility.ApproximatelyEqual(
                        RoadMarkings[surface.RightLaneMarking].SeparationDistance, 0));
        }

        public void Execute(int index)
        {
            var surfaceEntity = SurfaceEntities[index];
            var surface = Surfaces[surfaceEntity];
            if (HasDashedLaneMarking(surface))
            {
                LoftWithDashedRoadMarking(index);
            }
            else
            {
                SimpleLoft(index);
            }
        }
    }
}
