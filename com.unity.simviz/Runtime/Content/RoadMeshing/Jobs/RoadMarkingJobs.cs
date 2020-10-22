using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing.Jobs
{
    /// <summary>
    /// Generates the mesh vertices and triangles for every lane of road markings. This job will also generate
    /// additional information to help triangulate road surfaces around dashed road markings if necessary.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    struct GenerateLaneLinesJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> RoadMarkingEntities;
        [ReadOnly] public ComponentDataFromEntity<RoadMarking> RoadMarkings;
        [ReadOnly] public ComponentDataFromEntity<LateralProfile> Profiles;
        [ReadOnly] public ComponentDataFromEntity<LateralProfileSurface> Surfaces;
        [ReadOnly] public BufferFromEntity<LateralProfileSample> ProfileSampleBuffers;
        [ReadOnly] public BufferFromEntity<RoadCenterLineSample> SampleBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<CombinedVertex> VertexBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<Triangle> TriangleBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<SubMesh> SubMeshBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<RoadMarkingSample> RoadMarkingSampleBuffers;
        [NativeDisableParallelForRestriction] public BufferFromEntity<DashPoint> DashPointBuffers;

        void AddSolidLaneLine(
            NativeArray<RigidTransform> samples,
            float xOffset,
            RoadMarking marking,
            DynamicBuffer<CombinedVertex> vertexBuffer,
            DynamicBuffer<Triangle> triangleBuffer,
            DynamicBuffer<SubMesh> subMeshBuffer)
        {
            var newSubMesh = new SubMesh();
            var markingWidth = marking.Width / 2;
            var leftOffsetSpline = SplineUtility.OffsetSpline(samples, new float3(-markingWidth + xOffset, 0f, 0f), Allocator.Temp);
            var rightOffsetSpline = SplineUtility.OffsetSpline(samples, new float3(markingWidth + xOffset, 0f, 0f), Allocator.Temp);
            newSubMesh.VertexStartIndex = vertexBuffer.Length;
            newSubMesh.VertexCount = samples.Length * 2;
            vertexBuffer.ResizeUninitialized(vertexBuffer.Length + samples.Length * 2);

            var upVector = new float3(0f, 1f, 0f);
            for (int i = 0, j = newSubMesh.VertexStartIndex; i < samples.Length; i++)
            {
                var leftPoint = leftOffsetSpline[i];
                vertexBuffer[j++] = new CombinedVertex
                {
                    Vertex = leftPoint.pos,
                    Normal = math.mul(leftPoint.rot, upVector),
                    Uv = leftPoint.pos.xz
                };
                var rightPoint = rightOffsetSpline[i];
                vertexBuffer[j++] = new CombinedVertex
                {
                    Vertex = rightPoint.pos,
                    Normal = math.mul(rightPoint.rot, upVector),
                    Uv = rightPoint.pos.xz
                };
            }
            leftOffsetSpline.Dispose();
            rightOffsetSpline.Dispose();

            newSubMesh.TriangleStartIndex = triangleBuffer.Length;
            newSubMesh.TriangleCount = (samples.Length - 1) * 6;
            triangleBuffer.ResizeUninitialized(triangleBuffer.Length + newSubMesh.TriangleCount);
            for (int i = 0, j = newSubMesh.TriangleStartIndex; i < newSubMesh.VertexCount - 2; i += 2)
            {
                triangleBuffer[j++] = i;
                triangleBuffer[j++] = i + 3;
                triangleBuffer[j++] = i + 1;

                triangleBuffer[j++] = i;
                triangleBuffer[j++] = i + 2;
                triangleBuffer[j++] = i + 3;
            }

            newSubMesh.Material = marking.Material;
            subMeshBuffer.Add(newSubMesh);
        }

        void DashSamples(
            NativeArray<RigidTransform> samples,
            RoadMarking marking,
            out NativeList<RigidTransform> dashSamples,
            out NativeList<DashPoint> dashPoints)
        {
            var sampleDistances = SplineUtility.SplineDistanceArray(samples, Allocator.Temp);
            var totalDist = sampleDistances[sampleDistances.Length - 1];
            marking.BeginningOffset %= marking.DashLength;

            var patternDist = marking.DashLength + marking.SeparationDistance;
            var numPatterns = (int)(totalDist / patternDist) + 2;
            var patternPoints = new NativeArray<DashPoint>(numPatterns * 2, Allocator.Temp);
            var accumulatedDist = marking.BeginningOffset;
            for (var i = 0; i < patternPoints.Length;)
            {
                patternPoints[i++] = new DashPoint
                {
                    Distance = accumulatedDist,
                    Cap = DashCap.Start
                };
                accumulatedDist += marking.DashLength;
                patternPoints[i++] = new DashPoint
                {
                    Distance = accumulatedDist,
                    Cap = DashCap.End
                };
                accumulatedDist += marking.SeparationDistance;
            }

            dashSamples = new NativeList<RigidTransform>(patternPoints.Length + sampleDistances.Length, Allocator.Temp);
            dashPoints = new NativeList<DashPoint>(patternPoints.Length + sampleDistances.Length, Allocator.Temp);
            dashSamples.Add(samples[0]);
            dashPoints.Add(new DashPoint
            {
                Distance = 0f,
                Cap = DashCap.End
            });

            for (int i = 1, j = 0; i < samples.Length; i++)
            {
                var prevSample = samples[i - 1];
                var nextSample = samples[i];
                var prevDist = sampleDistances[i - 1];
                var nextDist = sampleDistances[i];
                var betweenDist = nextDist - prevDist;

                while (patternPoints[j].Distance < nextDist)
                {
                    var t = (patternPoints[j].Distance - prevDist) / betweenDist;
                    dashSamples.Add(SplineUtility.LerpTransform(prevSample, nextSample, t));
                    dashPoints.Add(new DashPoint
                    {
                        Distance = i - 1 + t,
                        Cap = patternPoints[j].Cap
                    });
                    j++;
                }
                dashSamples.Add(nextSample);
                dashPoints.Add(new DashPoint
                {
                    Distance = i,
                    Cap = dashPoints[dashPoints.Length - 1].Cap
                });
            }

            sampleDistances.Dispose();
            patternPoints.Dispose();
        }

        void AddDashedLaneLine(
            NativeArray<RigidTransform> samples,
            float xOffset,
            RoadMarking marking,
            DynamicBuffer<CombinedVertex> vertexBuffer,
            DynamicBuffer<Triangle> triangleBuffer,
            DynamicBuffer<SubMesh> subMeshBuffer,
            DynamicBuffer<RigidTransform> roadMarkingSamples,
            DynamicBuffer<DashPoint> dashPoints)
        {
            DashSamples(samples, marking, out var dashSamples, out var dashCaps);
            roadMarkingSamples.AddRange(dashSamples);
            dashPoints.AddRange(dashCaps);

            var markingWidth = marking.Width / 2;
            var leftOffsetSpline = SplineUtility.OffsetSpline(dashSamples, new float3(-markingWidth + xOffset, 0f, 0f), Allocator.Temp);
            var rightOffsetSpline = SplineUtility.OffsetSpline(dashSamples, new float3(markingWidth + xOffset, 0f, 0f), Allocator.Temp);
            var dashVertices = new NativeList<CombinedVertex>(leftOffsetSpline.Length / 2, Allocator.Temp);
            var nonDashVertices = new NativeList<CombinedVertex>(leftOffsetSpline.Length / 2, Allocator.Temp);

            var upVector = new float3(0f, 1f, 0f);
            for (var i = 0; i < dashSamples.Length - 1; i++)
            {
                if (dashCaps[i].Cap == DashCap.Start)
                {
                    var leftPoint = leftOffsetSpline[i];
                    dashVertices.Add(new CombinedVertex
                    {
                        Vertex = leftPoint.pos,
                        Normal = math.mul(leftPoint.rot, upVector),
                        Uv = leftPoint.pos.xz
                    });
                    var rightPoint = rightOffsetSpline[i];
                    dashVertices.Add(new CombinedVertex
                    {
                        Vertex = rightPoint.pos,
                        Normal = math.mul(rightPoint.rot, upVector),
                        Uv = rightPoint.pos.xz
                    });
                    leftPoint = leftOffsetSpline[i + 1];
                    dashVertices.Add(new CombinedVertex
                    {
                        Vertex = leftPoint.pos,
                        Normal = math.mul(leftPoint.rot, upVector),
                        Uv = leftPoint.pos.xz
                    });
                    rightPoint = rightOffsetSpline[i + 1];
                    dashVertices.Add(new CombinedVertex
                    {
                        Vertex = rightPoint.pos,
                        Normal = math.mul(rightPoint.rot, upVector),
                        Uv = rightPoint.pos.xz
                    });
                }
                else
                {
                    var leftPoint = leftOffsetSpline[i];
                    nonDashVertices.Add(new CombinedVertex
                    {
                        Vertex = leftPoint.pos,
                        Normal = math.mul(leftPoint.rot, upVector),
                        Uv = leftPoint.pos.xz
                    });
                    var rightPoint = rightOffsetSpline[i];
                    nonDashVertices.Add(new CombinedVertex
                    {
                        Vertex = rightPoint.pos,
                        Normal = math.mul(rightPoint.rot, upVector),
                        Uv = rightPoint.pos.xz
                    });
                    leftPoint = leftOffsetSpline[i + 1];
                    nonDashVertices.Add(new CombinedVertex
                    {
                        Vertex = leftPoint.pos,
                        Normal = math.mul(leftPoint.rot, upVector),
                        Uv = leftPoint.pos.xz
                    });
                    rightPoint = rightOffsetSpline[i + 1];
                    nonDashVertices.Add(new CombinedVertex
                    {
                        Vertex = rightPoint.pos,
                        Normal = math.mul(rightPoint.rot, upVector),
                        Uv = rightPoint.pos.xz
                    });
                }
            }
            leftOffsetSpline.Dispose();
            rightOffsetSpline.Dispose();
            dashSamples.Dispose();
            dashCaps.Dispose();

            var dashSubMesh = new SubMesh
            {
                VertexCount = dashVertices.Length,
                VertexStartIndex = vertexBuffer.Length,
                TriangleCount = (dashVertices.Length * 3) / 2,
                TriangleStartIndex = triangleBuffer.Length,
                Material = marking.Material
            };

            var nonDashSubMesh = new SubMesh
            {
                VertexCount = nonDashVertices.Length,
                VertexStartIndex = dashSubMesh.VertexStartIndex + dashSubMesh.VertexCount,
                TriangleCount = (nonDashVertices.Length * 3) / 2,
                TriangleStartIndex = dashSubMesh.TriangleStartIndex + dashSubMesh.TriangleCount,
                Material = RoadMaterial.RoadSurface
            };

            vertexBuffer.AddRange(dashVertices);
            vertexBuffer.AddRange(nonDashVertices);

            triangleBuffer.ResizeUninitialized(triangleBuffer.Length + dashSubMesh.TriangleCount + nonDashSubMesh.TriangleCount);
            for (int i = 0, j = dashSubMesh.TriangleStartIndex; i < dashSubMesh.VertexCount; i += 4)
            {
                triangleBuffer[j++] = i;
                triangleBuffer[j++] = i + 3;
                triangleBuffer[j++] = i + 1;

                triangleBuffer[j++] = i;
                triangleBuffer[j++] = i + 2;
                triangleBuffer[j++] = i + 3;
            }

            for (int i = 0, j = nonDashSubMesh.TriangleStartIndex; i < nonDashSubMesh.VertexCount; i += 4)
            {
                triangleBuffer[j++] = i;
                triangleBuffer[j++] = i + 3;
                triangleBuffer[j++] = i + 1;

                triangleBuffer[j++] = i;
                triangleBuffer[j++] = i + 2;
                triangleBuffer[j++] = i + 3;
            }

            subMeshBuffer.Add(dashSubMesh);
            subMeshBuffer.Add(nonDashSubMesh);
            dashVertices.Dispose();
            nonDashVertices.Dispose();
        }

        public void Execute(int index)
        {
            var markingEntity = RoadMarkingEntities[index];
            var marking = RoadMarkings[markingEntity];

            var surface = Surfaces[marking.RightSurface];
            var profile = Profiles[marking.Profile];
            var roadEntity = profile.Road;

            var samples = SampleBuffers[roadEntity].Reinterpret<RigidTransform>().AsNativeArray();
            var xOffset = ProfileSampleBuffers[marking.Profile][surface.StartIndex].Sample.x;

            var vertexBuffer = VertexBuffers[markingEntity];
            var triangleBuffer = TriangleBuffers[markingEntity];
            var subMeshBuffer = SubMeshBuffers[markingEntity];
            var roadMarkingSamples = RoadMarkingSampleBuffers[markingEntity].Reinterpret<RigidTransform>();
            var dashPoints = DashPointBuffers[markingEntity];

            if (Utilities.GeometryUtility.ApproximatelyEqual(marking.SeparationDistance, 0))
                AddSolidLaneLine(
                    samples,
                    xOffset,
                    marking,
                    vertexBuffer,
                    triangleBuffer,
                    subMeshBuffer);
            else
                AddDashedLaneLine(
                    samples,
                    xOffset,
                    marking,
                    vertexBuffer,
                    triangleBuffer,
                    subMeshBuffer,
                    roadMarkingSamples,
                    dashPoints);
        }
    }
}
