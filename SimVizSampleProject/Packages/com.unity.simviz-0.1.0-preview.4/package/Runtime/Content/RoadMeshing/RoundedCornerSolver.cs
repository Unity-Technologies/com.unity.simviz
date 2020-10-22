using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.RoadMeshing
{
    /// <summary>
    /// Calculates how to inscribe a circle between two lane edges in order to produce a rounded corner
    /// </summary>
    struct RoundedCornerSolver
    {
        public NativeArray<RigidTransform> LeftBuffer;
        public NativeArray<RigidTransform> RightBuffer;
        public float CornerRadius;
        public RoadSplineIterators Iterators;
        float2 m_EdgeIntersection;
        int m_LeftIntersectIdx, m_RightIntersectIdx;

        [BurstDiscard]
        static void LogNoEdgeIntersectionWarning()
        {
            Debug.Log("The edges of the road at this corner do not intersect. " +
                "Check if the right and left road edge buffers at this corner are swapped");
        }

        [BurstDiscard]
        static void LogCannotInscribeCircleWarning(float cornerRadius)
        {
            Debug.Log($"Cannot inscribe a corner radius of {cornerRadius} at this junction corner");
        }

        /// <summary>
        /// This function checks the edge case where the inscribed circle's radius can pivot on a particular point
        /// without exceeding the bounds established by the line segment spanning linePoint1 and linePoint2.
        ///
        /// Returns true if the edge case conditions are met. In addition, the inscribed circle information that meets
        /// said conditions will also be output.
        /// </summary>
        public static bool CheckPivotRadiusEdgeCase(
            float2 pivotPoint, float2 linePoint1,
            float2 linePoint2, float cornerRadius,
            out RoundedCorner results)
        {
            var p2 = linePoint2 - linePoint1;
            var pivot = pivotPoint - linePoint1;

            var angle = math.atan2(p2.y, p2.x);
            var sin = math.sin(angle);
            var cos = math.cos(angle);
            var rotation = new float2x2(
                cos, sin,
                -sin, cos);

            p2 = math.mul(rotation, p2);
            pivot = math.mul(rotation, pivot);

            var signedCornerRadius = math.sign(pivot.y) * cornerRadius;
            var pivotAngle = math.asin((pivot.y - signedCornerRadius) / signedCornerRadius);
            var tangentX = pivot.x + math.cos(pivotAngle) * cornerRadius;
            var newTangent = new float2(tangentX, 0f);
            var center = new float2(tangentX, signedCornerRadius);

            if (tangentX >= 0 &&
                tangentX <= p2.x)
            {
                sin = math.sin(-angle);
                cos = math.cos(-angle);
                rotation = new float2x2(
                    cos, sin,
                    -sin, cos);

                newTangent = math.mul(rotation, newTangent);
                center = math.mul(rotation, center);

                newTangent += linePoint1;
                center += linePoint1;
                results = new RoundedCorner
                {
                    LeftTangent = pivotPoint,
                    RightTangent = newTangent,
                    Center = center
                };
                return true;
            }

            results = new RoundedCorner();
            return false;
        }

        /// <summary>
        /// Checks if the current subsection of line segments being iterated over satisfies one of 4 conditions
        /// indicating that a circle of a target radius could be inscribed between said segments.
        ///
        /// An interactive simulation of each of these 4 RoundedCornerSolver conditions can be visualized using the
        /// CornerSolverVisualizer script located within the SimViz TestProject.
        /// </summary>
        bool InscribeCircleInCorner(out RoundedCorner results)
        {
            results = new RoundedCorner();

            int i = Iterators.LeftIdx, j = Iterators.RightIdx;
            var leftSample1 = i == m_LeftIntersectIdx ? m_EdgeIntersection : LeftBuffer[i - 1].pos.xz;
            var leftSample2 = LeftBuffer[i].pos.xz;
            var rightSample1 = j == m_RightIntersectIdx ? m_EdgeIntersection : RightBuffer[j + 1].pos.xz;
            var rightSample2 = RightBuffer[j].pos.xz;

            var leftVec = math.normalize(leftSample2 - leftSample1);
            var rightVec = math.normalize(rightSample2 - rightSample1);

            if (math.cross(new float3(leftVec.x, 0f, leftVec.y), new float3(rightVec.x, 0f, rightVec.y)).y <= 0f)
                return false;

            // Check if corner is between line segments ls1->ls2 and rs1->rs2
            Utilities.GeometryUtility.LineIntersection2D(
                leftSample1,
                leftSample2,
                rightSample1,
                rightSample2,
                out var intersection);
            var halfAngle = Utilities.GeometryUtility.AngleBetweenVectors(rightVec, leftVec) / 2f;
            var tan = math.tan(halfAngle);
            var leftMinRadius = math.distance(intersection, leftSample1) * tan;
            var leftMaxRadius = math.distance(intersection, leftSample2) * tan;
            var rightMinRadius = math.distance(intersection, rightSample1) * tan;
            var rightMaxRadius = math.distance(intersection, rightSample2) * tan;
            if (leftMinRadius <= CornerRadius && CornerRadius <= leftMaxRadius &&
                rightMinRadius <= CornerRadius && CornerRadius <= rightMaxRadius)
            {
                var projectDist = CornerRadius / tan;
                results.LeftTangent = intersection + leftVec * projectDist;
                results.RightTangent = intersection + rightVec * projectDist;
                var halfVec = math.normalize(math.normalize(leftVec) + math.normalize(rightVec));
                results.Center = intersection + halfVec * (CornerRadius / math.sin(halfAngle));
                return true;
            }

            float2 leftSample3 = new float2(), rightSample3 = new float2();

            // Check if corner radius is pivoting on leftSample2
            if (i + 1 < LeftBuffer.Length)
            {
                leftSample3 = LeftBuffer[i + 1].pos.xz;
                var leftVec2 = math.normalize(leftSample3 - leftSample2);
                Utilities.GeometryUtility.LineIntersection2D(
                    leftSample2,
                    leftSample3,
                    rightSample1,
                    rightSample2,
                    out var intersectionLeft);
                var halfAngle2 = Utilities.GeometryUtility.AngleBetweenVectors(rightVec, leftVec2) / 2f;
                var tan2 = math.tan(halfAngle2);
                var leftMinRadius2 = math.distance(intersectionLeft, leftSample2) * tan2;
                if (CornerRadius > leftMaxRadius &&
                    CornerRadius < leftMinRadius2 &&
                    CheckPivotRadiusEdgeCase(leftSample2, rightSample1, rightSample2,
                        CornerRadius, out var leftPivotResults))
                {
                    results = leftPivotResults;
                    return true;
                }
            }

            // Check if corner radius is pivoting on rightSample2
            if (j - 1 >= 0)
            {
                rightSample3 = RightBuffer[j - 1].pos.xz;
                var rightVec2 = math.normalize(rightSample3 - rightSample2);
                Utilities.GeometryUtility.LineIntersection2D(
                    leftSample1,
                    leftSample2,
                    rightSample2,
                    rightSample3,
                    out var intersection3);
                var halfAngle3 = Utilities.GeometryUtility.AngleBetweenVectors(rightVec2, leftVec) / 2f;
                var tan3 = math.tan(halfAngle3);
                var rightMinRadius3 = math.distance(intersection3, rightSample2) * tan3;
                if (CornerRadius > rightMaxRadius &&
                    CornerRadius < rightMinRadius3 &&
                    CheckPivotRadiusEdgeCase(rightSample2, leftSample1, leftSample2,
                        CornerRadius, out var rightPivotResults))
                {
                    results = rightPivotResults.Reverse;
                    return true;
                }
            }

            if (i + 1 >= LeftBuffer.Length || j - 1 < 0) return false;

            // Check if corner radius is pivoting simultaneously on leftSample2 and rightSample2
            float leftMinRadius4, rightMinRadius4;
            var midPoint = (leftSample2 + rightSample2) / 2;
            var dist = math.distance(leftSample2, rightSample2) / 2;
            float angle1, angle2;
            {
                var vec = math.normalize(leftSample2 - rightSample2);
                var vec2 = math.normalize(rightSample3 - rightSample2);
                vec2 = new float2(-vec2.y, vec2.x);
                angle1 = Utilities.GeometryUtility.SignedAngleBetweenVectors(vec2, vec);
                var projDist = math.tan(angle1) * dist;
                var normal = new float2(vec.y, -vec.x);
                rightMinRadius4 = math.distance(rightSample2, midPoint + normal * projDist);
            }
            {
                var vec = math.normalize(rightSample2 - leftSample2);
                var vec2 = math.normalize(leftSample3 - leftSample2);
                vec2 = new float2(vec2.y, -vec2.x);
                angle2 = Utilities.GeometryUtility.SignedAngleBetweenVectors(vec, vec2);
                var projDist = math.tan(angle2) * dist;
                var normal = new float2(-vec.y, vec.x);
                leftMinRadius4 = math.distance(leftSample2, midPoint + normal * projDist);
            }

            if (!(angle1 > 0f) || !(angle2 > 0f) || !(leftMaxRadius < CornerRadius) ||
                !(CornerRadius < leftMinRadius4) || !(rightMaxRadius < CornerRadius) ||
                !(CornerRadius < rightMinRadius4)) return false;

            results.LeftTangent = leftSample2;
            results.RightTangent = rightSample2;

            var midpoint = (leftSample2 + rightSample2) / 2;
            var midPointDist = math.distance(midpoint, rightSample2);
            var distToCenterFromMidPoint = math.sqrt((CornerRadius * CornerRadius) - (midPointDist * midPointDist));
            var toPoint = math.normalize(rightSample2 - leftSample2);
            toPoint = new float2(-toPoint.y, toPoint.x);
            results.Center = midpoint + toPoint * distToCenterFromMidPoint;
            return true;
        }

        /// <summary>
        /// Finds the intersection of two lanes edges then proceeds to inscribe a circle of sufficient radii within
        /// the corner formed by this intersection.
        /// </summary>
        public bool Solve(out RoundedCorner results)
        {
            results = new RoundedCorner();
            // Find Intersection between left and right road edges
            {
                var found = false;
                var iterators = new RoadSplineIterators(LeftBuffer, RightBuffer);
                do
                {
                    int i = iterators.LeftIdx, j = iterators.RightIdx;
                    if (!Utilities.GeometryUtility.LineSegmentIntersection2D(
                        LeftBuffer[i - 1].pos.xz, LeftBuffer[i].pos.xz,
                        RightBuffer[j + 1].pos.xz, RightBuffer[j].pos.xz,
                        out m_EdgeIntersection))
                        continue;

                    found = true;
                    m_LeftIntersectIdx = i;
                    m_RightIntersectIdx = j;

                    break;
                } while (iterators.TryNextIteration());

                if (!found)
                {
                    LogNoEdgeIntersectionWarning();
                    return false;
                }
            }

            // Inscribe a circle into the corner of the detected intersection
            {
                var foundCenter = false;
                Iterators = new RoadSplineIterators(
                    LeftBuffer, RightBuffer, m_LeftIntersectIdx, m_RightIntersectIdx, m_EdgeIntersection);
                do
                {
                    if (!InscribeCircleInCorner(out results)) continue;
                    foundCenter = true;
                    break;
                } while (Iterators.TryNextIteration());

                if (!foundCenter)
                {
                    LogCannotInscribeCircleWarning(CornerRadius);
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Struct for encapsulating the results of the RoundedCornerSolver
    /// </summary>
    struct RoundedCorner
    {
        public float2 LeftTangent, RightTangent, Center;
        public float3 Center3 => new float3(Center.x, 0f, Center.y);

        public RoundedCorner Reverse => new RoundedCorner
        {
            Center = Center,
            LeftTangent = RightTangent,
            RightTangent = LeftTangent
        };

        public static bool operator ==(RoundedCorner c1, RoundedCorner c2)
        {
            return Utilities.GeometryUtility.ApproximatelyEqual(c1.Center, c2.Center) &&
                   Utilities.GeometryUtility.ApproximatelyEqual(c1.LeftTangent, c2.LeftTangent) &&
                   Utilities.GeometryUtility.ApproximatelyEqual(c1.RightTangent, c2.RightTangent);
        }

        public static bool operator !=(RoundedCorner c1, RoundedCorner c2)
        {
            return !(c1 == c2);
        }

        public override string ToString()
        {
            return $"center: {Center}, tangent1: {LeftTangent}, tangent2: {RightTangent}";
        }

        public override bool Equals(object obj)
        {
            return obj != null && Equals((RoundedCorner)obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    /// <summary>
    /// Used to iterate through all permutations of line segments between two lane edges. The order of these
    /// permutations begin closest to the intersection of the lane edges and proceeds outward.
    ///
    /// NOTE: The right lane iterator is reversed since it is guaranteed from earlier preprocessing that right lane will
    /// always being directed inward toward the intersection.
    /// </summary>
    struct RoadSplineIterators
    {
        enum Direction
        {
            Left, Right
        }

        int m_LeftIdx, m_RightIdx;
        readonly int m_LeftStartIdx, m_RightStartIdx;
        readonly int m_LastLeftIdx, m_LastRightIdx;
        float m_LeftDist, m_RightDist;
        readonly NativeArray<RigidTransform> m_LeftBuffer;
        readonly NativeArray<RigidTransform> m_RightBuffer;

        public RoadSplineIterators(
            NativeArray<RigidTransform> leftBuffer,
            NativeArray<RigidTransform> rightBuffer)
        {
            m_LeftIdx = m_RightIdx = 1;
            m_LeftStartIdx = m_RightStartIdx = 1;
            LeftIdx = RightIdxForward = 1;
            m_LastLeftIdx = leftBuffer.Length - 1;
            m_LastRightIdx = rightBuffer.Length - 1;
            m_LeftBuffer = leftBuffer;
            m_RightBuffer = rightBuffer;
            m_LeftDist = 0f;
            m_RightDist = 0f;
            AddLeftDist();
            AddRightDist();
        }

        public RoadSplineIterators(
            NativeArray<RigidTransform> leftBuffer,
            NativeArray<RigidTransform> rightBuffer,
            int leftStartIdx, int rightStartIdx,
            float2 intersection)
        {
            m_LastLeftIdx = leftBuffer.Length - 1;
            m_LastRightIdx = rightBuffer.Length - 1;
            m_LeftBuffer = leftBuffer;
            m_RightBuffer = rightBuffer;
            m_LeftDist = 0f;
            m_RightDist = 0f;

            m_LeftStartIdx = leftStartIdx;
            m_RightStartIdx = m_LastRightIdx - rightStartIdx;

            m_LeftIdx = m_LeftStartIdx;
            m_RightIdx = m_RightStartIdx;

            LeftIdx = leftStartIdx;
            RightIdxForward = m_RightStartIdx;

            AddIntersectionDist(intersection);
        }

        void AddLeftDist()
        {
            m_LeftDist += math.distance(
                m_LeftBuffer[m_LeftIdx].pos,
                m_LeftBuffer[m_LeftIdx - 1].pos);
        }

        void AddRightDist()
        {
            m_RightDist += math.distance(
                m_RightBuffer[m_LastRightIdx - m_RightIdx].pos,
                m_RightBuffer[m_LastRightIdx - m_RightIdx + 1].pos);
        }
        void AddIntersectionDist(float2 intersection)
        {
            m_LeftDist += math.distance(
                intersection,
                m_LeftBuffer[m_LeftIdx].pos.xz);

            m_RightDist += math.distance(
                m_RightBuffer[RightIdx].pos.xz,
               intersection);
        }

        public int LeftIdx { get; private set; }

        public int RightIdx => m_LastRightIdx - RightIdxForward;

        public int RightIdxForward { get; private set; }

        public bool TryNextIteration()
        {
            if (m_LastLeftIdx == m_LeftIdx &&
                m_LastRightIdx == m_RightIdx &&
                LeftIdx == m_LeftIdx &&
                RightIdxForward == m_RightIdx)
                return false;

            if (LeftIdx < m_LeftIdx)
            {
                LeftIdx++;
                return true;
            }
            if (RightIdxForward < m_RightIdx)
            {
                RightIdxForward++;
                return true;
            }

            Direction incrementDirection;

            if (m_LeftIdx == m_LastLeftIdx)
            {
                m_RightIdx++;
                incrementDirection = Direction.Right;
            }
            else if (m_RightIdx == m_LastRightIdx)
            {
                m_LeftIdx++;
                incrementDirection = Direction.Left;
            }
            else if (m_RightDist < m_LeftDist)
            {
                m_RightIdx++;
                AddRightDist();
                incrementDirection = Direction.Right;
            }
            else
            {
                m_LeftIdx++;
                AddLeftDist();
                incrementDirection = Direction.Left;
            }

            if (incrementDirection == Direction.Left)
            {
                LeftIdx = m_LeftIdx;
                RightIdxForward = m_RightStartIdx;
            }
            else
            {
                LeftIdx = m_LeftStartIdx;
                RightIdxForward = m_RightIdx;
            }

            return true;
        }
    }
}
