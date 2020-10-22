using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing
{
    struct LateralProfileBuilder : IDisposable
    {
        int m_NumLeftSurfaces, m_NumLeftSamples;
        public LateralProfile Profile;
        public NativeList<float2> Samples;
        public NativeList<RoadMarking> RoadMarkings;
        public NativeList<LateralProfileSurface> Surfaces;

        readonly Entity m_RoadEntity;
        readonly Entity m_ProfileEntity;
        readonly NativeArray<Entity> m_SurfaceEntities;
        readonly NativeArray<Entity> m_RoadMarkingEntities;

        public LateralProfileBuilder(
            Allocator allocator,
            Entity roadEntity,
            Entity profileEntity,
            NativeArray<Entity> surfaceEntities,
            NativeArray<Entity> roadMarkingEntities)
        {
            m_NumLeftSurfaces = 0;
            m_NumLeftSamples = 0;
            Profile = new LateralProfile();
            Samples = new NativeList<float2>(allocator);
            RoadMarkings = new NativeList<RoadMarking>(allocator);
            Surfaces = new NativeList<LateralProfileSurface>(allocator);

            m_RoadEntity = roadEntity;
            m_ProfileEntity = profileEntity;
            m_SurfaceEntities = surfaceEntities;
            m_RoadMarkingEntities = roadMarkingEntities;
        }

        public void Dispose()
        {
            Surfaces.Dispose();
            Samples.Dispose();
        }

        public void Complete()
        {
            // Propagate profile offsets
            {
                var offset = float2.zero;
                var samplesIdx = m_NumLeftSamples;
                for (var i = m_NumLeftSurfaces - 1; i >= 0; --i)
                {
                    for (var j = Surfaces[i].SampleCount - 1; j >= 0; --j)
                    {
                        Samples[--samplesIdx] += offset;
                    }
                    offset = Samples[samplesIdx];
                }

                offset = float2.zero;
                samplesIdx = m_NumLeftSamples - 1;
                for (var i = m_NumLeftSurfaces; i < Surfaces.Length; i++)
                {
                    for (var j = 0; j < Surfaces[i].SampleCount; j++)
                    {
                        Samples[++samplesIdx] += offset;
                    }
                    offset = Samples[samplesIdx];
                }
            }

            // Record start indices
            {
                var sampleCount = 0;
                for (var i = 0; i < Surfaces.Length; i++)
                {
                    var surface = Surfaces[i];
                    surface.Profile = m_ProfileEntity;
                    surface.StartIndex = sampleCount;
                    Surfaces[i] = surface;
                    sampleCount += surface.SampleCount;
                }
            }

            // Record road center and road surface extents
            {
                var profile = new LateralProfile
                {
                    Road = m_RoadEntity,
                    LeftDrivableIndex = m_NumLeftSurfaces,
                    RightDrivableIndex = m_NumLeftSurfaces,
                    CenterIndex = m_NumLeftSurfaces,
                };
                for (var i = 0; i < m_NumLeftSamples; i++)
                {
                    var surface = Surfaces[i];
                    if (!surface.Drivable) continue;
                    profile.LeftDrivableOffset = Samples[surface.StartIndex];
                    profile.LeftDrivableIndex = i;
                    break;
                }
                for (var i = Surfaces.Length - 1; i >= m_NumLeftSurfaces; --i)
                {
                    var surface = Surfaces[i];
                    if (!surface.Drivable) continue;
                    profile.RightDrivableOffset = Samples[surface.EndIndex];
                    profile.RightDrivableIndex = i;
                    break;
                }
                Profile = profile;
            }
        }

        public void SwitchSides()
        {
            SplineUtility.ReverseArray(Surfaces.AsArray());
            SplineUtility.ReverseArray(Samples.AsArray());
            InvertSamples();
            m_NumLeftSurfaces = Surfaces.Length;
            m_NumLeftSamples = Samples.Length;
        }

        void InvertSamples()
        {
            for (var i = 0; i < Samples.Length; i++)
            {
                var sample = Samples[i];
                sample.x *= -1;
                Samples[i] = sample;
            }
        }

        public void AddSidewalk(float width)
        {
            Samples.Add(float2.zero);
            Samples.Add(new float2(width, 0f));
            Surfaces.Add(new LateralProfileSurface
            {
                TexStrategy = TextureCoordinateStrategy.TrackSpace,
                Material = RoadMaterial.SideWalk,
                Drivable = false,
                SampleCount = 2
            });
        }

        public void AddExpansionJoint(float width)
        {
            Samples.Add(float2.zero);
            Samples.Add(new float2(0f, -width / 2f));
            Samples.Add(new float2(width, -width / 2f));
            Samples.Add(new float2(width, 0f));
            Surfaces.Add(new LateralProfileSurface
            {
                TexStrategy = TextureCoordinateStrategy.TrackSpace,
                Material = RoadMaterial.ExpansionJoint,
                Drivable = false,
                SampleCount = 4
            });
        }

        public void AddRoad(float width, float camber = 0f)
        {
            Samples.Add(float2.zero);
            Samples.Add(new float2(width, camber));
            Surfaces.Add(new LateralProfileSurface
            {
                TexStrategy = TextureCoordinateStrategy.WorldSpace,
                Material = RoadMaterial.RoadSurface,
                Drivable = true,
                SampleCount = 2
            });
        }

        public void AddGutter(float width, float depth)
        {
            Samples.Add(float2.zero);
            Samples.Add(new float2(width, -depth));
            Samples.Add(new float2(width + 0.02f, -depth));
            Surfaces.Add(new LateralProfileSurface
            {
                TexStrategy = TextureCoordinateStrategy.TrackSpace,
                Material = RoadMaterial.Gutter,
                Drivable = false,
                SampleCount = 3
            });
        }

        public void AddEndCap(float height)
        {
            Samples.Add(float2.zero);
            Samples.Add(new float2(0f, -height));
            Surfaces.Add(new LateralProfileSurface
            {
                TexStrategy = TextureCoordinateStrategy.TrackSpace,
                Material = RoadMaterial.SideWalk,
                Drivable = false,
                SampleCount = 2
            });
        }

        public void AddCurb(
            float height,
            float lowerRadius,
            float upperRadius,
            int numCornerSamples)
        {
            var prevNumSamples = Samples.Length;
            Samples.Add(float2.zero);

            lowerRadius = math.min(lowerRadius, height / 2f);
            upperRadius = math.min(upperRadius, height / 2f);

            // Bottom curb
            var angle = (0.5f * math.PI) / numCornerSamples;
            const float angleOffset = 1.5f * math.PI;
            for (var i = 1; i <= numCornerSamples; i++)
            {
                var rotatedAngle = angle * i;
                Samples.Add(new float2(
                                math.cos(angleOffset + rotatedAngle),
                                1 + math.sin(angleOffset + rotatedAngle)) * lowerRadius);
            }

            // Middle curb
            var middleCurbHeight = height - (upperRadius + lowerRadius);
            if (middleCurbHeight > Utilities.GeometryUtility.Tolerance)
            {
                Samples.Add(new float2(lowerRadius, lowerRadius + middleCurbHeight));
            }

            // Top curb
            var offset = new float2(lowerRadius, lowerRadius + middleCurbHeight);
            for (var i = 1; i <= numCornerSamples; i++)
            {
                var rotatedAngle = angle * i;
                Samples.Add(new float2(1f - math.cos(rotatedAngle), math.sin(rotatedAngle)) * upperRadius + offset);
            }

            Surfaces.Add(new LateralProfileSurface
            {
                TexStrategy = TextureCoordinateStrategy.TrackSpace,
                Material = RoadMaterial.Curb,
                Drivable = false,
                SampleCount = Samples.Length - prevNumSamples
            });
        }

        public void AddRoadMarking(int idx, RoadMarking roadMarking)
        {
            var roadMarkingIndex = RoadMarkings.Length;
            var roadMarkingEntity = m_RoadMarkingEntities[roadMarkingIndex];
            var leftSurfaceIndex = m_NumLeftSurfaces + idx - 1;
            roadMarking.LeftSurface = m_SurfaceEntities[leftSurfaceIndex];
            roadMarking.RightSurface = m_SurfaceEntities[leftSurfaceIndex + 1];
            roadMarking.Profile = m_ProfileEntity;
            RoadMarkings.Add(roadMarking);

            var leftSurface = Surfaces[leftSurfaceIndex];
            leftSurface.RightLaneMarking = roadMarkingEntity;
            Surfaces[leftSurfaceIndex] = leftSurface;

            var rightSurface = Surfaces[leftSurfaceIndex + 1];
            rightSurface.LeftLaneMarking = roadMarkingEntity;
            Surfaces[leftSurfaceIndex + 1] = rightSurface;
        }
    }
}
