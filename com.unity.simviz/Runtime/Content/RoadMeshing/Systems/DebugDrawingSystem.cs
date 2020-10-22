using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Pipeline;
using UnityEngine.SimViz.Content.RoadMeshing.Components;
using UnityEngine.SimViz.Content.Sampling;
using UnityEngine.UIElements;

namespace UnityEngine.SimViz.Content.RoadMeshing.Systems
{
    [Serializable]
    public class DebugDrawingParameters
    {
        [HideInInspector]
        public GameObject parentObject;
        public bool drawCornerDebugLines;
        public bool drawPlacementPaths;
        public bool drawCameraPaths;
    }

    [DisableAutoCreation]
    public class DebugDrawingSystem : ComponentSystem, IGeneratorSystem<DebugDrawingParameters>
    {
        public DebugDrawingParameters Parameters { get; set; }

        protected override void OnUpdate()
        {
            if (Parameters.drawCornerDebugLines)
            {
                // Draw intersection lane edges and corner tangents
                Entities.ForEach((
                    ref IntersectionCorner corner,
                    DynamicBuffer<LeftLaneEdgeSample> leftBuffer,
                    DynamicBuffer<RightLaneEdgeSample> rightBuffer) =>
                {
                    if (!Utilities.GeometryUtility.ApproximatelyEqual(corner.Center, new float3()))
                    {
                        CreateCircle(corner.Center, corner.Radius);
                        DrawTangent(corner.Center, corner.TangentLeft.pos);
                        DrawTangent(corner.Center, corner.TangentRight.pos);
                    }
                    CreateLine(leftBuffer.Reinterpret<RigidTransform>().AsNativeArray());
                    CreateLine(rightBuffer.Reinterpret<RigidTransform>().AsNativeArray());
                });
            }

            if (Parameters.drawPlacementPaths)
            {
                Entities.ForEach((
                    DynamicBuffer<PointSampleGlobal> pathSampleBuffer) =>
                {
                    CreateLine(pathSampleBuffer.Reinterpret<RigidTransform>().AsNativeArray());
                });
            }

            if (Parameters.drawCameraPaths)
            {
                Entities.ForEach((
                    DynamicBuffer<CameraPathSample> pathSampleBuffer) =>
                {
                    CreateLine(pathSampleBuffer.Reinterpret<RigidTransform>().AsNativeArray());
                });
            }
        }

        void DrawTangent(Vector3 center, float3 tangent)
        {
            CreateLine(new Vector3[] { center, tangent });
        }

        void CreateCircle(Vector3 center, float radius)
        {
            const int numSamples = 30;
            const float angleDelta = math.PI * 2f / numSamples;
            var lineObj = new GameObject("Center");
            lineObj.transform.parent = Parameters.parentObject.transform;
            var lineRenderer = lineObj.AddComponent<LineRenderer>();
            lineRenderer.loop = true;
            lineRenderer.positionCount = numSamples;
            var samples = new Vector3[numSamples];
            var angle = 0f;
            for (var i = 0; i < numSamples; i++)
            {
                samples[i] = center + new Vector3(math.cos(angle), 0f, math.sin(angle)) * radius;
                angle += angleDelta;
            }

            lineRenderer.SetPositions(samples);
            lineRenderer.widthCurve = new AnimationCurve { keys = new []{ new Keyframe(0f, .2f) }};
        }

        void CreateLine(NativeArray<RigidTransform> samples, bool looped = false)
        {
            CreateLine(RigidTransformToVector3(samples), looped);
        }

        void CreateLine(Vector3[] samples, bool looped = false)
        {
            var lineObj = new GameObject("Samples");
            lineObj.transform.parent = Parameters.parentObject.transform;
            var lineRenderer = lineObj.AddComponent<LineRenderer>();
            var mat = new Material(Shader.Find("Unlit/Color"))
            {
                color = Random.ColorHSV(0f, 1f, 1f, 1f, 1f, 1f)
            };
            lineRenderer.material = mat;
            lineRenderer.loop = looped;
            lineRenderer.positionCount = samples.Length;
            lineRenderer.SetPositions(samples);
            lineRenderer.widthCurve = new AnimationCurve { keys = new []{ new Keyframe(0f, .1f) }};
        }

        Vector3[] RigidTransformToVector3(NativeArray<RigidTransform> poses)
        {
            var vectors = new Vector3[poses.Length];
            for (var i = 0; i < poses.Length; i++)
            {
                vectors[i] = poses[i].pos;
            }

            return vectors;
        }
    }
}
