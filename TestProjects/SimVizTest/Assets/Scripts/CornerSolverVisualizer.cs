using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.RoadMeshing
{
    /// <summary>
    /// Visually demonstrates the different edge cases encountered
    /// when operating the Road Meshing Tool's corner solver.
    /// </summary>
    [ExecuteInEditMode]
    public class CornerSolverVisualizer : MonoBehaviour
    {
        [Range(0f, 20f)]
        public float radius = 1.0f;

        [Range(0.01f, 0.2f)]
        public float lineWidth = 0.1f;

        GameObject m_ParentObj;
        public TestCase testCase;

        public enum TestCase
        {
            BetweenBothSegments,
            PivotLeftSegment,
            PivotRightSegment,
            PivotBothSegments,
            InwardInput,
            OutwardInput
        }

        public void Update()
        {
            SolveCorner();
        }

        void SolveCorner()
        {
            if (m_ParentObj != null)
                DestroyImmediate(m_ParentObj);
            m_ParentObj = new GameObject("RoundedCornerVisualizer");

            NativeArray<float2> leftBuffer, rightBuffer;
            switch (testCase)
            {
                case TestCase.BetweenBothSegments:
                    BetweenBothSegments(out leftBuffer, out rightBuffer);
                    break;
                case TestCase.PivotLeftSegment:
                    PivotNextLeftSegment(out leftBuffer, out rightBuffer);
                    break;
                case TestCase.PivotRightSegment:
                    PivotNextRightSegment(out leftBuffer, out rightBuffer);
                    break;
                case TestCase.PivotBothSegments:
                    PivotBothSegments(out leftBuffer, out rightBuffer);
                    break;
                case TestCase.InwardInput:
                    InwardInput(out leftBuffer, out rightBuffer);
                    break;
                case TestCase.OutwardInput:
                    OutwardInput(out leftBuffer, out rightBuffer);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            SplineUtility.ReverseArray(rightBuffer);

            var leftBufferTransform = Float2ToRigidTransform(leftBuffer);
            var rightBufferTransform = Float2ToRigidTransform(rightBuffer);

            var roundedCornerSolver = new RoundedCornerSolver
            {
                LeftBuffer = leftBufferTransform,
                RightBuffer = rightBufferTransform,
                CornerRadius = radius
            };
            
            if (roundedCornerSolver.Solve(out var results))
            {
                var leftTangent = Utilities.GeometryUtility.SwizzleX0Z(results.LeftTangent);
                var rightTangent = Utilities.GeometryUtility.SwizzleX0Z(results.RightTangent);
                CreateLine(leftBufferTransform, Color.green);
                CreateLine(rightBufferTransform, Color.green);
                CreateLine(new Vector3[] { leftTangent, results.Center3 }, Color.red);
                CreateLine(new Vector3[] { rightTangent, results.Center3 }, Color.blue);
                CreateCircle(results.Center3, radius, Color.cyan);
            }
            else
            {
                CreateLine(leftBufferTransform, Color.magenta);
                CreateLine(rightBufferTransform, Color.magenta);
            }
            
            CreateNormals(leftBuffer);
            CreateNormals(rightBuffer);
            
            leftBuffer.Dispose();
            rightBuffer.Dispose();
            leftBufferTransform.Dispose();
            rightBufferTransform.Dispose();
        }

        void BetweenBothSegments(out NativeArray<float2> leftBuffer, out NativeArray<float2> rightBuffer)
        {
            leftBuffer = new NativeArray<float2>(2, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(0f, 15f),
            };

            rightBuffer = new NativeArray<float2>(2, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(15f, 0f)
            };
        }

        void PivotNextLeftSegment(out NativeArray<float2> leftBuffer, out NativeArray<float2> rightBuffer)
        {
            leftBuffer = new NativeArray<float2>(3, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(2f, 2f),
                [2] = new float2(6f, 15f)
            };

            rightBuffer = new NativeArray<float2>(2, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(15f, 0f)
            };
        }

        void PivotNextRightSegment(out NativeArray<float2> leftBuffer, out NativeArray<float2> rightBuffer)
        {
            leftBuffer = new NativeArray<float2>(2, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(0f, 15f)
            };

            rightBuffer = new NativeArray<float2>(3, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(2f, 2f),
                [2] = new float2(15f, 6f)
            };
        }

        void PivotBothSegments(out NativeArray<float2> leftBuffer, out NativeArray<float2> rightBuffer)
        {
            leftBuffer = new NativeArray<float2>(4, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(2f, 4f),
                [2] = new float2(2f, 6f),
                [3] = new float2(0f, 12f)
            };

            rightBuffer = new NativeArray<float2>(4, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(4f, 2f),
                [2] = new float2(6f, 2f),
                [3] = new float2(12f, 0f)
            };
        }

        void InwardInput(out NativeArray<float2> leftBuffer, out NativeArray<float2> rightBuffer)
        {
            leftBuffer = new NativeArray<float2>(4, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(3, 5f),
                [2] = new float2(7f, 7f),
                [3] = new float2(15f, 10f)
            };

            rightBuffer = new NativeArray<float2>(4, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(2f, -2f),
                [2] = new float2(5f, -4f),
                [3] = new float2(15f, -9f)
            };
        }

        void OutwardInput(out NativeArray<float2> leftBuffer, out NativeArray<float2> rightBuffer)
        {
            leftBuffer = new NativeArray<float2>(5, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(2, 3f),
                [2] = new float2(6f, 12f),
                [3] = new float2(7f, 15f),
                [4] = new float2(6f, 20f)
            };

            rightBuffer = new NativeArray<float2>(5, Allocator.TempJob)
            {
                [0] = new float2(0f, 0f),
                [1] = new float2(3f, 2f),
                [2] = new float2(8f, 3f),
                [3] = new float2(15f, 4f),
                [4] = new float2(20f, 3f)
            };
        }

        NativeArray<float2> LineRendererToFloat2(LineRenderer lineRenderer)
        {
            var buffer = new NativeArray<float2>(lineRenderer.positionCount, Allocator.TempJob);
            for (var i = 0; i < lineRenderer.positionCount; i++)
            {
                var pos = lineRenderer.GetPosition(i);
                buffer[i] = new float2(pos.x, pos.z);
            }
            return buffer;
        }

        NativeArray<RigidTransform> Float2ToRigidTransform(NativeArray<float2> buffer)
        {
            var newBuffer = new NativeArray<RigidTransform>(buffer.Length, Allocator.TempJob);
            for (var i = 0; i < buffer.Length; i++)
            {
                newBuffer[i] = new RigidTransform
                {
                    pos = Float2ToFloat3(buffer[i]),
                    rot = new quaternion()
                };
            }
            return newBuffer;
        }

        static float3 Float2ToFloat3(float2 value)
        {
            return new float3(value.x, 0f, value.y);
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

        void CreateLine(NativeArray<RigidTransform> samples, Color color, bool looped = false)
        {
            CreateLine(RigidTransformToVector3(samples), color, looped);
        }

        void CreateLine(Vector3[] samples, Color color, bool looped = false)
        {
            var lineObj = new GameObject("Samples");
            lineObj.transform.parent = m_ParentObj.transform;
            var lineRenderer = lineObj.AddComponent<LineRenderer>();
            var mat = new Material(Shader.Find("Unlit/Color")) {color = color};
            lineRenderer.material = mat;
            lineRenderer.loop = looped;
            lineRenderer.positionCount = samples.Length;
            lineRenderer.SetPositions(samples);
            lineRenderer.widthCurve = new AnimationCurve { keys = new []{ new Keyframe(0f, lineWidth) }};
        }

        void CreateNormals(NativeArray<float2> line)
        {
            for (var i = 1; i < line.Length - 1; i++)
            {
                var prevPoint = line[i - 1];
                var currPoint = line[i];
                var nextPoint = line[i + 1];

                var v1 = math.normalize(currPoint - prevPoint);
                var v2 = math.normalize(nextPoint - currPoint);
                var tangent = math.normalize(v1 + v2);
                var normal = new float2(-tangent.y, tangent.x);
                CreateLine(new Vector3[]{ Float2ToFloat3(currPoint), Float2ToFloat3(normal + currPoint) }, Color.yellow);
            }
        }

        void CreateCircle(Vector3 center, float circleRadius, Color color)
        {
            const int numSamples = 30;
            const float angleDelta = math.PI * 2f / numSamples;
            var lineObj = new GameObject("Center");
            lineObj.transform.parent = m_ParentObj.transform;
            var lineRenderer = lineObj.AddComponent<LineRenderer>();
            var mat = new Material(Shader.Find("Unlit/Color")) {color = color};
            lineRenderer.material = mat;
            lineRenderer.loop = true;
            lineRenderer.positionCount = numSamples;
            var samples = new Vector3[numSamples];
            var angle = 0f;
            for (var i = 0; i < numSamples; i++)
            {
                samples[i] = center + new Vector3(math.cos(angle), 0f, math.sin(angle)) * circleRadius;
                angle += angleDelta;
            }

            lineRenderer.SetPositions(samples);
            lineRenderer.widthCurve = new AnimationCurve { keys = new []{ new Keyframe(0f, lineWidth) }};
        }
    }
}
