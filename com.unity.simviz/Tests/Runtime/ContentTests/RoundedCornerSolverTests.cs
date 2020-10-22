using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.RoadMeshing;

namespace UnityEngine.SimViz.Content.ContentTests
{
    [TestFixture]
    public class RoundedCornerSolverTests
    {
        [Test]
        public void RoadSplineIteratorsTest()
        {
            var expectedResults = new NativeArray<KeyValuePair<int, int>>(9, Allocator.TempJob)
            {
                [0] = new KeyValuePair<int, int>(1, 1),
                [1] = new KeyValuePair<int, int>(2, 1),
                [2] = new KeyValuePair<int, int>(1, 2),
                [3] = new KeyValuePair<int, int>(2, 2),
                [4] = new KeyValuePair<int, int>(3, 1),
                [5] = new KeyValuePair<int, int>(3, 2),
                [6] = new KeyValuePair<int, int>(1, 3),
                [7] = new KeyValuePair<int, int>(2, 3),
                [8] = new KeyValuePair<int, int>(3, 3)
            };

            var results = new NativeList<KeyValuePair<int, int>>(9, Allocator.TempJob);

            var leftBuffer =
                new NativeArray<RigidTransform>(4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
                {
                    [0] = new RigidTransform(new quaternion(), new float3()),
                    [1] = new RigidTransform(new quaternion(), new float3(0f, 0f, 1f)),
                    [2] = new RigidTransform(new quaternion(), new float3(0f, 0f, 2f)),
                    [3] = new RigidTransform(new quaternion(), new float3(0f, 0f, 3f))
                };
            var rightBuffer = new NativeArray<RigidTransform>(4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            {
                [0] = new RigidTransform(new quaternion(), new float3()),
                [1] = new RigidTransform(new quaternion(), new float3(1f, 0f, 0f)),
                [2] = new RigidTransform(new quaternion(), new float3(2f, 0f, 0f)),
                [3] = new RigidTransform(new quaternion(), new float3(3f, 0f, 0f))
            };

            var iterators = new RoadSplineIterators(leftBuffer, rightBuffer);
            do
            {
                results.Add(new KeyValuePair<int, int>(iterators.LeftIdx, iterators.RightIdxForward));
            } while (iterators.TryNextIteration());

            Assert.AreEqual(expectedResults.Length, results.Length);
            for (var i = 0; i < results.Length; i++)
            {
                Assert.AreEqual(expectedResults[i], results[i]);
            }

            expectedResults.Dispose();
            results.Dispose();
            leftBuffer.Dispose();
            rightBuffer.Dispose();
        }

        static IEnumerable<TestCaseData> CheckPivotTangentTestCases()
        {
            // Standard case
            yield return new TestCaseData(
                new float2(0f, 3f),
                new float2(0f, 0f),
                new float2(12f, 0f),
                2f,
                new float2(math.cos(math.asin(0.5f)) * 2f, 2f),
                new float2(0f, 3f),
                new float2(math.cos(math.asin(0.5f)) * 2f, 0f),
                true);

            // Inverse side of line points
            yield return new TestCaseData(
                new float2(0f, -3f),
                new float2(0f, 0f),
                new float2(12f, 0f),
                2f,
                new float2(math.cos(math.asin(0.5f)) * 2f, -2f),
                new float2(0f, -3f),
                new float2(math.cos(math.asin(0.5f)) * 2f, 0f),
                true);

            // Rotated
            yield return new TestCaseData(
                new float2(3f, 0f),
                new float2(0f, 0f),
                new float2(0f, 12f),
                2f,
                new float2(2f, math.cos(math.asin(0.5f)) * 2f),
                new float2(3f, 0f),
                new float2(0f, math.cos(math.asin(0.5f)) * 2f),
                true);

            // Pivot behind first point
            yield return new TestCaseData(
                new float2(-1f, 2f),
                new float2(0f, 0f),
                new float2(2f, 0f),
                2f,
                new float2(1f, 2f),
                new float2(-1f, 2f),
                new float2(1f, 0f),
                true);

            // Range behind pivot
            yield return new TestCaseData(
                new float2(0f, 3f),
                new float2(-10f, 0f),
                new float2(-5f, 0f),
                2f,
                new float2(),
                new float2(),
                new float2(),
                false);

            // Pivot behind range
            yield return new TestCaseData(
                new float2(-10f, 3f),
                new float2(0f, 0f),
                new float2(10f, 0f),
                2f,
                new float2(),
                new float2(),
                new float2(),
                false);

            // Example from Editor
            yield return new TestCaseData(
                new float2(-383.562713623047f, -251.108322143555f),
                new float2(-388.208343505859f, -250.992935180664f),
                new float2(-384.646667480469f, -255.323822021484f),
                2.2999999523162842f,
                new float2(-383.260478698986f, -253.388377807641f),
                new float2(-383.562713623047f, -251.108322143555f),
                new float2(-385.036911461382f, -254.849297525264f),
                true);
        }

        [Test]
        [TestCaseSource(nameof(CheckPivotTangentTestCases))]
        public void CheckPivotTangentTest(
            float2 pivotPoint, float2 linePoint1,
            float2 linePoint2, float cornerRadius,
            float2 center, float2 leftTangent, float2 rightTangent,
            bool shouldBeFound)
        {
            var isFound = RoundedCornerSolver.CheckPivotRadiusEdgeCase(pivotPoint, linePoint1, linePoint2, cornerRadius, out var circleResults);

            var expectedResults = new RoundedCorner
            {
                Center = center,
                LeftTangent = leftTangent,
                RightTangent = rightTangent
            };

            Assert.AreEqual(shouldBeFound, isFound);
            Assert.True(circleResults == expectedResults);
        }

        [Test]
        public void BetweenBothSegmentsTest()
        {
            var leftBuffer = new NativeArray<RigidTransform>(2, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(0f, 0f, -1f)),
                [1] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 10f))
            };
            var rightBuffer = new NativeArray<RigidTransform>(2, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(10f, 0f, 0f)),
                [1] = new RigidTransform(quaternion.identity, new float3(-1f, 0f, 0f))
            };
            var solver = new RoundedCornerSolver
            {
                LeftBuffer = leftBuffer,
                RightBuffer = rightBuffer,
                CornerRadius = 2.0f
            };
            var isFound = solver.Solve(out var results);
            leftBuffer.Dispose();
            rightBuffer.Dispose();

            Assert.AreEqual(true, isFound);
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(2f, 2f), results.Center));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(0f, 2f), results.LeftTangent));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(2f, 0f), results.RightTangent));
        }

        [Test]
        public void PivotEndOfLeftSegmentTest()
        {
            var leftBuffer = new NativeArray<RigidTransform>(3, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 0f)),
                [1] = new RigidTransform(quaternion.identity, new float3(2f, 0f, 2f)),
                [2] = new RigidTransform(quaternion.identity, new float3(5f, 0f, 10f))
            };
            var rightBuffer = new NativeArray<RigidTransform>(2, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(10f, 0f, 0f)),
                [1] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 0f))
            };
            var solver = new RoundedCornerSolver
            {
                LeftBuffer = leftBuffer,
                RightBuffer = rightBuffer,
                CornerRadius = 1.45f
            };
            var isFound = solver.Solve(out var results);
            leftBuffer.Dispose();
            rightBuffer.Dispose();

            Assert.AreEqual(true, isFound);
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(3.341641f, 1.45f), results.Center));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(2f, 2f), results.LeftTangent));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(3.341641f, 0f), results.RightTangent));
        }

        [Test]
        public void PivotEndOfRightSegmentTest()
        {
            var leftBuffer = new NativeArray<RigidTransform>(2, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 0f)),
                [1] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 10f)),
            };
            var rightBuffer = new NativeArray<RigidTransform>(3, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(10f, 0f, 5f)),
                [1] = new RigidTransform(quaternion.identity, new float3(2f, 0f, 2f)),
                [2] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 0f))
            };
            var solver = new RoundedCornerSolver
            {
                LeftBuffer = leftBuffer,
                RightBuffer = rightBuffer,
                CornerRadius = 1.45f
            };
            var isFound = solver.Solve(out var results);
            leftBuffer.Dispose();
            rightBuffer.Dispose();

            Assert.AreEqual(true, isFound);
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(1.45f, 3.341641f), results.Center));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(0f, 3.341641f), results.LeftTangent));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(2f, 2f), results.RightTangent));
        }

        [Test]
        public void PivotBothSegmentsTest()
        {
            var leftBuffer = new NativeArray<RigidTransform>(3, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 0f)),
                [1] = new RigidTransform(quaternion.identity, new float3(2f, 0f, 4f)),
                [2] = new RigidTransform(quaternion.identity, new float3(2f, 0f, 10f))
            };
            var rightBuffer = new NativeArray<RigidTransform>(3, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(10f, 0f, 2f)),
                [1] = new RigidTransform(quaternion.identity, new float3(4f, 0f, 2f)),
                [2] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 0f))
            };

            // Expecting any radius between minRadius and maxRadius to pivot on points (2, 4) and (4, 2) simultaneously
            var minRadius = math.sin(math.PI / 8) * math.sqrt(4 * 4 + 2 * 2);
            var maxRadius = 2f;
            var betweenRadius = (minRadius + maxRadius) / 2f;

            var solver = new RoundedCornerSolver
            {
                LeftBuffer = leftBuffer,
                RightBuffer = rightBuffer,
                CornerRadius = betweenRadius
            };
            var isFound = solver.Solve(out var results);
            leftBuffer.Dispose();
            rightBuffer.Dispose();

            Assert.AreEqual(true, isFound);
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(3.849602f, 3.849602f), results.Center));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(2f, 4f), results.LeftTangent));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(new float2(4f, 2f), results.RightTangent));
        }

        [Test]
        public void ZeroSizeCornerRadiusTest()
        {
            var leftBuffer = new NativeArray<RigidTransform>(2, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(0f, 0f, -1f)),
                [1] = new RigidTransform(quaternion.identity, new float3(0f, 0f, 10f)),
            };
            var rightBuffer = new NativeArray<RigidTransform>(2, Allocator.TempJob)
            {
                [0] = new RigidTransform(quaternion.identity, new float3(10f, 0f, 0f)),
                [1] = new RigidTransform(quaternion.identity, new float3(-1f, 0f, 0f)),
            };
            var solver = new RoundedCornerSolver
            {
                LeftBuffer = leftBuffer,
                RightBuffer = rightBuffer,
                CornerRadius = 0f
            };
            var isFound = solver.Solve(out var results);
            leftBuffer.Dispose();
            rightBuffer.Dispose();

            Assert.AreEqual(true, isFound);
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(float2.zero, results.Center));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(float2.zero, results.LeftTangent));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(float2.zero, results.RightTangent));
        }
    }
}
