using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.SimViz.Content.Sampling;

namespace UnityEngine.SimViz.Content.Utilities
{
    public static class GeometryUtility
    {
        public const float Tolerance = 0.00001f;
        public const float ToleranceSqr = Tolerance * Tolerance;

        /// <summary>
        /// Checks if and where two lines defined by the input points intersect.
        /// </summary>
        public static bool LineIntersection2D(float2 p0, float2 p1, float2 p2, float2 p3, out float2 intersection)
        {
            var s1 = p1 - p0;
            var s2 = p3 - p2;

            var determinant = -s2.x * s1.y + s1.x * s2.y;
            if (Math.Abs(determinant) < Tolerance)
            {
                // Collinear or parallel
                intersection.x = 0f;
                intersection.y = 0f;
                return false;
            }

            var t = (s2.x * (p0.y - p2.y) - s2.y * (p0.x - p2.x)) / determinant;
            intersection.x = p0.x + (t * s1.x);
            intersection.y = p0.y + (t * s1.y);
            return true;
        }

        /// <summary>
        /// Checks if and where two line segments intersect.
        /// </summary>
        /// <source>https://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect</source>
        public static bool LineSegmentIntersection2D(float2 p0, float2 p1, float2 p2, float2 p3, out float2 intersection)
        {
            var s1 = p1 - p0;
            var s2 = p3 - p2;

            var determinant = -s2.x * s1.y + s1.x * s2.y;
            if (Math.Abs(determinant) < Tolerance)
            {
                // Collinear or parallel
                intersection.x = 0f;
                intersection.y = 0f;
                return false;
            }

            var s = (-s1.y * (p0.x - p2.x) + s1.x * (p0.y - p2.y)) / determinant;
            var t = (s2.x * (p0.y - p2.y) - s2.y * (p0.x - p2.x)) / determinant;

            // detects line segment intersection
            if (s >= 0f && s <= 1f && t >= 0f && t <= 1f)
            {
                // Intersection detected
                intersection.x = p0.x + (t * s1.x);
                intersection.y = p0.y + (t * s1.y);
                return true;
            }

            // No collision
            intersection.x = 0f;
            intersection.y = 0f;
            return false;
        }

        /// <summary>
        /// Returns a positive value when performing a positive modulus operation on a negative dividend.
        /// </summary>
        public static float DivisorSignModulus(float dividend, float divisor)
        {
            return ((dividend % divisor) + divisor) % divisor;
        }

        public static float SignedAngleBetweenAngles(float target, float source)
        {
            return DivisorSignModulus((target - source) + math.PI, math.PI * 2) - math.PI;
        }

        /// <source>https://www.mathworks.com/matlabcentral/answers/180131-how-can-i-find-the-angle-between-two-vectors-including-directional-information</source>
        public static float SignedAngleBetweenVectors(float2 v1, float2 v2)
        {
            return math.atan2(v1.x*v2.y - v1.y*v2.x, v1.x*v2.x + v1.y*v2.y);
        }

        public static float AngleBetweenVectors(float2 v1, float2 v2)
        {
            return math.abs(SignedAngleBetweenVectors(v1, v2));
        }

        public static float PointDistanceFromLineSegment(float2 p1, float2 p2, float2 testPoint)
        {
            p2 -= p1;
            testPoint -= p1;

            var angle = math.atan2(p2.y, p2.x);
            var sin = math.sin(angle);
            var cos = math.cos(angle);
            var rotation = new float2x2(
                cos, sin,
                -sin, cos);
            p2 = math.mul(rotation, p2);
            testPoint = math.mul(rotation, testPoint);

            // Check if testPoint is not between p1 and p2
            if (testPoint.x < 0 || testPoint.x > p2.x)
            {
                return math.min(math.distance(testPoint, p2), math.length(testPoint));
            }

            return math.abs(testPoint.y);
        }

        public static bool PointOnLineSegment(float2 p1, float2 p2, float2 testPoint)
        {
            p2 -= p1;
            testPoint -= p1;

            var angle = math.atan2(p2.y, p2.x);
            var sin = math.sin(angle);
            var cos = math.cos(angle);
            var rotation = new float2x2(
                cos, sin,
                -sin, cos);
            p2 = math.mul(rotation, p2);
            testPoint = math.mul(rotation, testPoint);

            // Check if testPoint is not between p1 and p2
            return testPoint.x >= 0 && testPoint.x <= p2.x && math.abs(testPoint.y) < Tolerance;
        }

        /// <summary>
        /// Returns the linear interpolation factor of point x with respect to points a and b.
        /// Requires point x to be positioned somewhere along the line segment formed between the points a and b.
        /// </summary>
        public static float Unlerp(float2 a, float2 b, float2 x)
        {
            return math.distance(a, x) / math.distance(a, b);
        }

        /// <summary>
        /// Returns the linear interpolation factor of point x with respect to points a and b.
        /// Requires point x to be positioned somewhere along the line segment formed between the points a and b.
        /// </summary>
        public static float Unlerp(float3 a, float3 b, float3 x)
        {
            return math.distance(a, x) / math.distance(a, b);
        }

        // ReSharper disable once InconsistentNaming
        public static float3 SwizzleXY0(float2 point)
        {
            return new float3(point.x, point.y, 0f);
        }

        public static float3 SwizzleX0Z(float2 point)
        {
            return new float3(point.x, 0f, point.y);
        }

        public static bool ApproximatelyEqual(float f1, float f2)
        {
            return math.abs(f1 - f2) < Tolerance;
        }

        public static bool ApproximatelyEqual(float2 p1, float2 p2)
        {
            return math.abs(p1.x - p2.x) < Tolerance &&
                   math.abs(p1.y - p2.y) < Tolerance;
        }

        public static bool ApproximatelyEqual(float3 p1, float3 p2)
        {
            return math.abs(p1.x - p2.x) < Tolerance &&
                   math.abs(p1.y - p2.y) < Tolerance &&
                   math.abs(p1.z - p2.z) < Tolerance;
        }

        public static bool ApproximatelyEqual(float4 p1, float4 p2)
        {
            return math.abs(p1.x - p2.x) < Tolerance &&
                   math.abs(p1.y - p2.y) < Tolerance &&
                   math.abs(p1.z - p2.z) < Tolerance &&
                   math.abs(p1.w - p2.w) < Tolerance;
        }

        public static bool ApproximatelyLessThan(float f1, float f2)
        {
            return f1 - f2 < Tolerance;
        }

        public static bool ApproximatelyGreaterThan(float f1, float f2)
        {
            return f1 - f2 > -Tolerance;
        }
    }
}
