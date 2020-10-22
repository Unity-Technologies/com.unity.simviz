using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.ContentTests
{
    [TestFixture]
    public class GeometryUtilityTests
    {
        public static IEnumerable<TestCaseData> AngleBetweenVectorsTestCases()
        {
            yield return new TestCaseData(new float2(1f, 0f), new float2(1f, 0f), 0f);
            yield return new TestCaseData(new float2(1f, 0f), new float2(0f, 1f), math.PI / 2f);
            yield return new TestCaseData(new float2(0f, 1f), new float2(1f, 0f), math.PI / 2f);
        }

        [Test]
        [TestCaseSource(nameof(AngleBetweenVectorsTestCases))]
        public static void AngleBetweenVectorsTest(float2 v1, float2 v2, float angle)
        {
            var calculatedAngle = Utilities.GeometryUtility.AngleBetweenVectors(v1, v2);
            Assert.AreEqual(angle, calculatedAngle, Utilities.GeometryUtility.Tolerance);
        }

        public static IEnumerable<TestCaseData> SignedAngleBetweenVectorsTestCases()
        {
            yield return new TestCaseData(new float2(1f, 0f), new float2(1f, 0f), 0f);
            yield return new TestCaseData(new float2(1f, 0f), new float2(0f, 1f), math.PI / 2f);
            yield return new TestCaseData(new float2(0f, 1f), new float2(1f, 0f), math.PI / -2f);
            yield return new TestCaseData(new float2(0f, -1f), new float2(1f, 1f), math.PI * 0.75f);
            yield return new TestCaseData(new float2(1f, 1f), new float2(0f, -1f), math.PI * -0.75f);

            var oneOverRoot2 = 1f / math.sqrt(2);
            yield return new TestCaseData(
                new float2(0f, 1f),
                new float2(-oneOverRoot2, -oneOverRoot2),
                math.PI * 0.75f);
        }

        [Test]
        [TestCaseSource(nameof(SignedAngleBetweenVectorsTestCases))]
        public static void SignedAngleBetweenVectorsTest(float2 v1, float2 v2, float angle)
        {
            var calculatedAngle = Utilities.GeometryUtility.SignedAngleBetweenVectors(v1, v2);
            Assert.AreEqual(angle, calculatedAngle, Utilities.GeometryUtility.Tolerance);
        }

        public static IEnumerable<TestCaseData> LineIntersection2DTestCases()
        {
            // Same lines
            yield return new TestCaseData(
                new float2(1f, 0f),
                new float2(0f, 0f),
                new float2(1f, 0f),
                new float2(0f, 0f),
                new float2(0f, 0f),
                false);

            // Parallel lines
            yield return new TestCaseData(
                new float2(0f, 1f),
                new float2(1f, 2f),
                new float2(0f, 0f),
                new float2(1f, 1f),
                new float2(0f, 0f),
                false);

            // Intersecting lines
            yield return new TestCaseData(
                new float2(1f, 0f),
                new float2(0f, 0f),
                new float2(0f, 1f),
                new float2(0f, 0f),
                new float2(0f, 0f),
                true);
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(10f, 10f),
                new float2(10f, 0f),
                new float2(0f, 10f),
                new float2(5f, 5f),
                true);
            yield return new TestCaseData(
                new float2(-1f, -1f),
                new float2(-10f, -10f),
                new float2(-10f, 0f),
                new float2(0f, -10f),
                new float2(-5f, -5f),
                true);
        }

        [Test]
        [TestCaseSource(nameof(LineIntersection2DTestCases))]
        public static void LineIntersection2DTest(
            float2 a1,
            float2 a2,
            float2 b1,
            float2 b2,
            float2 intersection,
            bool intersectionShouldOccur)
        {
            var didIntersect = Utilities.GeometryUtility.LineIntersection2D(a1, a2, b1, b2, out var calculatedIntersection);
            Assert.AreEqual(intersectionShouldOccur, didIntersect);
            if (!intersectionShouldOccur) return;

            Assert.AreEqual(intersection.x, calculatedIntersection.x, Utilities.GeometryUtility.Tolerance);
            Assert.AreEqual(intersection.y, calculatedIntersection.y, Utilities.GeometryUtility.Tolerance);
        }

        public static IEnumerable<TestCaseData> LineSegmentIntersection2DTestCases()
        {
            // Same segments
            yield return new TestCaseData(
                new float2(1f, 0f),
                new float2(0f, 0f),
                new float2(1f, 0f),
                new float2(0f, 0f),
                new float2(0f, 0f),
                false);

            // Parallel segments
            yield return new TestCaseData(
                new float2(0f, 1f),
                new float2(1f, 2f),
                new float2(0f, 0f),
                new float2(1f, 1f),
                new float2(0f, 0f),
                false);

            // Intersect segments
            yield return new TestCaseData(
                new float2(1f, 0f),
                new float2(0f, 0f),
                new float2(0f, 1f),
                new float2(0f, 0f),
                new float2(0f, 0f),
                true);
            yield return new TestCaseData(
                new float2(-0.03242731f, -7.206635f),
                new float2(37.96757f, -42.20663f),
                new float2(0.03242731f, -7.206635f),
                new float2(-37.96757f, -42.20663f),
                new float2(0f, -7.236502f),
                true);

            // Intersect at end point
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(1f, 0f),
                new float2(0f, 0f),
                new float2(0f, 1f),
                new float2(0f, 0f),
                true);

            // Intersect end point on segment
            yield return new TestCaseData(
                new float2(-2f, 0f),
                new float2(2f, 0f),
                new float2(0f, 0f),
                new float2(0f, 1f),
                new float2(0f, 0f),
                true);
        }

        [Test]
        [TestCaseSource(nameof(LineSegmentIntersection2DTestCases))]
        public static void LineSegmentIntersection2DTest(
            float2 a1,
            float2 a2,
            float2 b1,
            float2 b2,
            float2 intersection,
            bool intersectionShouldOccur)
        {
            var didIntersect = Utilities.GeometryUtility.LineSegmentIntersection2D(
                a1, a2, b1, b2, out var calculatedIntersection);

            Assert.AreEqual(intersectionShouldOccur, didIntersect);
            if (!intersectionShouldOccur) return;

            Assert.AreEqual(intersection.x, calculatedIntersection.x, Utilities.GeometryUtility.Tolerance);
            Assert.AreEqual(intersection.y, calculatedIntersection.y, Utilities.GeometryUtility.Tolerance);
        }


        public static IEnumerable<TestCaseData> PointOnLineSegmentTestCases()
        {
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(2f, 0f),
                new float2(1f, 0f),
                true);
            yield return new TestCaseData(
                new float2(-3f, -3f),
                new float2(4f, 4f),
                new float2(3f, 3f),
                true);
            yield return new TestCaseData(
                new float2(-1f, 0f),
                new float2(2f, 0f),
                new float2(-1f, 0f),
                true);
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(0f, 0f),
                new float2(0f, 0f),
                true);
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(2f, 0f),
                new float2(3f, 0f),
                false);
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(2f, 0f),
                new float2(-0.0001f, 0f),
                false);
        }

        [Test]
        [TestCaseSource(nameof(PointOnLineSegmentTestCases))]
        public static void PointOnLineSegmentTest(float2 p1, float2 p2, float2 p3, bool onLineSegment)
        {
            var calculatedCheck = Utilities.GeometryUtility.PointOnLineSegment(p1, p2, p3);
            Assert.AreEqual(onLineSegment, calculatedCheck);
        }

        public static IEnumerable<TestCaseData> PointDistanceFromLineSegmentTestCases()
        {
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(1f, 0f),
                new float2(0f, 0f),
                0f);
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(1f, 0f),
                new float2(-1f, 0f),
                1f);
            yield return new TestCaseData(
                new float2(0f, 0f),
                new float2(1f, 0f),
                new float2(0f, 1f),
                1f);
            yield return new TestCaseData(
                new float2(-1f, 0f),
                new float2(1f, 0f),
                new float2(0f, 1f),
                1f);
            yield return new TestCaseData(
                new float2(-1f, 0f),
                new float2(1f, 0f),
                new float2(0f, 0.0001f),
                0.0001f);
        }

        [Test]
        [TestCaseSource(nameof(PointDistanceFromLineSegmentTestCases))]
        public static void PointDistanceFromLineSegmentTest(float2 p1, float2 p2, float2 p3, float expectedDist)
        {
            var calculatedDist = Utilities.GeometryUtility.PointDistanceFromLineSegment(p1, p2, p3);
            Assert.AreEqual(expectedDist, calculatedDist, Utilities.GeometryUtility.Tolerance);
        }

        public static IEnumerable<TestCaseData> SignedAngleBetweenAnglesTestCases()
        {
            yield return new TestCaseData(math.PI, 0.0001f, math.PI - 0.0001f);
            yield return new TestCaseData(math.PI, -0.0001f, -(math.PI - 0.0001f));
            yield return new TestCaseData(-math.PI * 0.75f, math.PI * 0.75f, math.PI * 0.5f);
            yield return new TestCaseData(math.PI * 0.75f, -math.PI * 0.75f, -math.PI * 0.5f);
        }

        [Test]
        [TestCaseSource(nameof(SignedAngleBetweenAnglesTestCases))]
        public static void SignedAngleBetweenAnglesTest(float target, float source, float angle)
        {
            var calculatedAngle = Utilities.GeometryUtility.SignedAngleBetweenAngles(target, source);
            Assert.AreEqual(angle, calculatedAngle, Utilities.GeometryUtility.Tolerance);
        }
    }
}
