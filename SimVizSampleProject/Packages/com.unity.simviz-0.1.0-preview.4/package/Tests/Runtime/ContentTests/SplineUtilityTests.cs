using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Utilities;

namespace UnityEngine.SimViz.Content.ContentTests
{
    public class SplineUtilityTests
    {
        private static IEnumerable<TestCaseData> ReverseSplineQuaternionTestCases()
        {
            yield return new TestCaseData(new float3(0f, 0f, 1f));
            yield return new TestCaseData(new float3(0f, 0f, -1f));

            yield return new TestCaseData(new float3(1f, 0f, 0f));
            yield return new TestCaseData(new float3(-1f, 0f, 0f));

            yield return new TestCaseData(new float3(1f, 1f, 1f));
            yield return new TestCaseData(new float3(-1f, -1f, -1f));

            yield return new TestCaseData(new float3(0f, 1f, 1f));
            yield return new TestCaseData(new float3(0f, -1f, -1f));

            yield return new TestCaseData(new float3(1f, 1f, 0f));
            yield return new TestCaseData(new float3(-1f, -1f, 0f));
        }

        [Test]
        [TestCaseSource(nameof(ReverseSplineQuaternionTestCases))]
        public static void ReverseSplineQuaternionTest(float3 source)
        {
            source = math.normalize(source);
            var upVector = new float3(0f, 1f, 0f);
            var forward = quaternion.LookRotation(source, upVector);
            var reverseCheck = quaternion.LookRotation(-source, upVector);
            var reverse = SplineUtility.ReverseSplineQuaternion(forward);

            var p1 = math.normalize(new float3(1f, 1f, 1f));
            var p2 = math.normalize(new float3(-1f, -1f, -1f));

            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(
                math.mul(reverseCheck, p1), math.mul(reverse, p1)));
            Assert.True(Utilities.GeometryUtility.ApproximatelyEqual(
                math.mul(reverseCheck, p2), math.mul(reverse, p2)));
        }
    }
}
