using System;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.Utilities
{
    public static class SplineUtility
    {
        /// <summary>
        /// Applies an "Ease-in-ease-out" transformation to a linear interpolation factor
        /// </summary>
        /// <param name="t">Linear interpolation factor between [0, 1]</param>
        public static float BezierBlend(float t)
        {
            return t * t * (3.0f - 2.0f * t);
        }

        /// <summary>
        /// Linearly interpolates between two RigidTransforms
        /// </summary>
        /// <param name="pose1"></param>
        /// <param name="pose2"></param>
        /// <param name="t">Linear interpolation factor between [0, 1]</param>
        public static RigidTransform LerpTransform(RigidTransform pose1, RigidTransform pose2, float t)
        {
            return new RigidTransform
            {
                pos = math.lerp(
                    pose1.pos,
                    pose2.pos,
                    t),
                rot = math.nlerp(pose1.rot, pose2.rot, t)
            };
        }

        /// <summary>
        /// Returns the sum of the distances between each neighboring pair of points in the given spline.
        /// </summary>
        public static float SplineLength(NativeArray<RigidTransform> spline)
        {
            if (spline.Length <= 1) return 0f;

            var length = 0f;
            var prevPose = spline[0];
            for (var i = 1; i < spline.Length; i++)
            {
                var currPose = spline[i];
                length += math.distance(prevPose.pos, currPose.pos);
                prevPose = currPose;
            }

            return length;
        }

        /// <summary>
        /// Returns a native array containing the distance traveled along a reference spline
        /// from the first sample to each indexed sample.
        /// </summary>
        public static NativeArray<float> SplineDistanceArray(NativeArray<RigidTransform> spline, Allocator allocator)
        {
            var distances = new NativeArray<float>(
                spline.Length, allocator, NativeArrayOptions.UninitializedMemory) {[0] = 0f};
            var totalLength = 0f;
            for (var i = 1; i < spline.Length; i++)
            {
                totalLength += math.distance(spline[i - 1].pos.xz, spline[i].pos.xz);
                distances[i] = totalLength;
            }
            return distances;
        }

        // Reverses the rotation of a spline sample while maintaining the sample's up orientation.
        public static quaternion ReverseSplineQuaternion(quaternion rotation)
        {
            return math.mul(rotation, new quaternion(0f, 1f, 0f, 0f));
        }

        // Reverses the rotation of a spline sample while maintaining the sample's up orientation.
        public static RigidTransform ReversePose(RigidTransform pose)
        {
            return new RigidTransform
            {
                pos = pose.pos,
                rot = ReverseSplineQuaternion(pose.rot)
            };
        }

        /// <summary>
        /// Reverses the order and rotation of each sample in the given spline.
        /// </summary>
        public static void ReverseSpline(NativeArray<RigidTransform> spline)
        {
            var start = 0;
            var end = spline.Length - 1;
            while (start < end)
            {
                var temp = ReversePose(spline[start]);
                spline[start] = ReversePose(spline[end]);
                spline[end] = temp;
                start++;
                end--;
            }
            // Don't forget the middle point!
            if (start == end)
                spline[start] = ReversePose(spline[start]);
        }

        /// <summary>
        /// Reverses the quaternion rotation value associated with each sample of the given spline.
        /// </summary>
        public static void ReverseSplineRotations(NativeArray<RigidTransform> spline)
        {
            for (var i = 0; i < spline.Length; i++)
            {
                var pose = spline[i];
                spline[i] = new RigidTransform
                {
                    pos = pose.pos,
                    rot = ReverseSplineQuaternion(pose.rot)
                };
            }
        }

        /// <summary>
        /// Reverses a native array
        /// </summary>
        public static void ReverseArray<T>(NativeArray<T> elems) where T : struct
        {
            var start = 0;
            var end = elems.Length - 1;
            while (start < end)
            {
                var temp = elems[start];
                elems[start] = elems[end];
                elems[end] = temp;
                start++;
                end--;
            }
        }

        /// <summary>
        /// Remaps a spline using the linear interpolation spacing of a reference spline.
        /// The resulting spline will have a sample length equivalent to the reference spline.
        /// </summary>
        public static NativeArray<RigidTransform> RemapSpline(
            NativeArray<RigidTransform> originalSpline,
            NativeArray<RigidTransform> referenceSpline,
            Allocator allocator)
        {
            var distances = SplineDistanceArray(originalSpline, allocator);
            var referenceDistances = SplineDistanceArray(referenceSpline, allocator);

            var totalDist = distances[distances.Length - 1];
            var totalRefDist = referenceDistances[referenceDistances.Length - 1];

            for (var i = 1; i < distances.Length; i++)
                distances[i] /= totalDist;
            for (var i = 1; i < referenceDistances.Length; i++)
                referenceDistances[i] /= totalRefDist;

            var remappedSpline = new NativeArray<RigidTransform>(
                referenceSpline.Length, allocator, NativeArrayOptions.UninitializedMemory)
            {
                [0] = originalSpline[0],
                [referenceSpline.Length - 1] = originalSpline[originalSpline.Length - 1]
            };

            for (int i = 1, j = 1; i < remappedSpline.Length - 1; i++)
            {
                var referenceDist = referenceDistances[i];
                while (distances[j] < referenceDist) j++;

                var lastDist = distances[j - 1];
                var currDist = distances[j];
                remappedSpline[i] = LerpTransform(
                    originalSpline[j - 1],
                    originalSpline[j],
                    (referenceDist - lastDist) / (currDist - lastDist));
            }

            distances.Dispose();
            referenceDistances.Dispose();

            return remappedSpline;
        }

        /// <summary>
        /// Remaps a spline using evenly spaced linear interpolation.
        /// The resulting spline will have a sample length equivalent numSamples parameter.
        /// </summary>
        public static NativeArray<RigidTransform> EvenlyRemapSpline(
            NativeArray<RigidTransform> originalSpline,
            int numSamples,
            bool looped,
            Allocator allocator)
        {
            if (looped)
            {
                var loopedSpline = new NativeArray<RigidTransform>(
                    originalSpline.Length + 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                originalSpline.CopyTo(loopedSpline.GetSubArray(0, originalSpline.Length));
                loopedSpline[loopedSpline.Length - 1] = loopedSpline[0];
                originalSpline = loopedSpline;
            }

            var distances = SplineDistanceArray(originalSpline, allocator);
            var totalDist = distances[distances.Length - 1];
            for (var i = 1; i < distances.Length; i++)
                distances[i] /= totalDist;

            var remappedSpline = new NativeArray<RigidTransform>(
                numSamples, allocator, NativeArrayOptions.UninitializedMemory)
            {
                [0] = originalSpline[0]
            };

            var divisor = looped ? numSamples : numSamples - 1;
            for (int i = 1, j = 1; i < divisor; i++)
            {
                var referenceDist = (float) i / divisor;
                while (distances[j] < referenceDist) j++;

                var lastDist = distances[j - 1];
                var currDist = distances[j];
                remappedSpline[i] = LerpTransform(
                    originalSpline[j - 1],
                    originalSpline[j],
                    (referenceDist - lastDist) / (currDist - lastDist));
            }

            if (!looped)
            {
                // If not looped, ensure that the original and newly remapped splines exactly share the same end point
                remappedSpline[divisor] = originalSpline[originalSpline.Length - 1];
            }
            else
            {
                // Dispose of the copied spline if looped
                originalSpline.Dispose();
            }

            distances.Dispose();
            return remappedSpline;
        }

        public static NativeArray<RigidTransform> EvenlyDistributeSpline(
            NativeArray<RigidTransform> originalSpline, float spacing, bool looped, Allocator allocator)
        {
            var splineLength = SplineLength(originalSpline);
            var quotient = (int)(splineLength / spacing);
            var sampleCount = looped ? quotient - 1 : quotient;
            return EvenlyRemapSpline(originalSpline, sampleCount, looped, allocator);
        }

        /// <summary>
        /// Returns a distance-interpolated spline sample for each distance in the distance array.
        /// NOTES:
        ///     - The distance array is assumed to be pre-sorted and in increasing order.
        ///     - It is also assumed that last distance of the array is less than or equal to
        ///       the calculated length of the reference spline.
        /// </summary>
        /// <param name="referenceSpline">The spline from which to sample new points</param>
        /// <param name="distances">An array of values indicating how far to travel down the spline before sampling a point</param>
        /// <param name="allocator"></param>
        /// <returns></returns>
        public static NativeArray<RigidTransform> SampleSplineAtDistances(
            NativeArray<RigidTransform> referenceSpline,
            NativeArray<float> distances,
            Allocator allocator)
        {
            var newSamples = new NativeArray<RigidTransform>(distances.Length, allocator);
            var originalDistances = SplineDistanceArray(referenceSpline, allocator);
            var totalLength = originalDistances[originalDistances.Length - 1];
            for (var i = 0; i < originalDistances.Length; i++)
                originalDistances[i] /= totalLength;

            for (int i = 0, j = 1; i < distances.Length; i++)
            {
                var interpolationDist = distances[i] / totalLength;
                while (originalDistances[j] < interpolationDist) j++;

                var lastDist = originalDistances[j - 1];
                var currDist = originalDistances[j];
                newSamples[i] = LerpTransform(
                    referenceSpline[j - 1],
                    referenceSpline[j],
                    (interpolationDist - lastDist) / (currDist - lastDist));
            }

            originalDistances.Dispose();
            return newSamples;
        }

        /// <summary>
        /// Offsets each sample of a spline by the given offset vector and stores the result in a pre-allocated array
        /// </summary>
        public static void OffsetSpline(
            NativeArray<RigidTransform> centerLine,
            NativeArray<RigidTransform> offsetLine,
            float3 offset)
        {
            for (var i = 0; i < centerLine.Length; i++)
            {
                var transformedOffset = math.rotate(centerLine[i].rot, offset);
                offsetLine[i] = new RigidTransform
                {
                    pos = centerLine[i].pos + transformedOffset,
                    rot = centerLine[i].rot
                };
            }
        }

        /// <summary>
        /// Offsets each sample of a spline by the given offset vector
        /// </summary>
        public static NativeArray<RigidTransform> OffsetSpline(
            NativeArray<RigidTransform> centerLine, float3 offset, Allocator allocator)
        {
            var offsetLine = new NativeArray<RigidTransform>(centerLine.Length, allocator);
            for (var i = 0; i < centerLine.Length; i++)
            {
                var transformedOffset = math.rotate(centerLine[i].rot, offset);
                offsetLine[i] = new RigidTransform
                {
                    pos = centerLine[i].pos + transformedOffset,
                    rot = centerLine[i].rot
                };
            }
            return offsetLine;
        }

        /// <summary>
        /// Calculates an explicit rotational component per sample given a float3 path.
        /// </summary>
        /// <param name="path">An array of float3s that define a path</param>
        /// <param name="splineSamples">The container that will be overwritten with the new samples</param>
        /// <param name="looped">Whether or not the path is circular</param>
        /// <returns></returns>
        public static void Float3PathToRigidTransformPath(
            NativeArray<float3> path,
            NativeArray<RigidTransform> splineSamples,
            bool looped)
        {
            // Middle Points
            for (var i = 1; i < path.Length - 1; i++)
            {
                var prevRot = quaternion.LookRotation(path[i] - path[i - 1], new float3(0, 1f, 0));
                var nextRot = quaternion.LookRotation(path[i + 1] - path[i], new float3(0, 1f, 0));
                splineSamples[i] = new RigidTransform
                {
                    pos = path[i],
                    rot = math.slerp(prevRot, nextRot, 0.5f)
                };
            }

            var lastIndex = splineSamples.Length - 1;
            if (looped && path.Length > 2)
            {
                var rotation0 = quaternion.LookRotation(path[lastIndex] - path[lastIndex - 1], new float3(0, 1f, 0));
                var rotation1 = quaternion.LookRotation(path[0] - path[lastIndex], new float3(0, 1f, 0));
                var rotation2 = quaternion.LookRotation(path[1] - path[0], new float3(0, 1f, 0));

                // Last Point
                splineSamples[lastIndex] = new RigidTransform
                {
                    pos = path[lastIndex],
                    rot = math.slerp(rotation0, rotation1, 0.5f)
                };

                // First Point
                splineSamples[0] = new RigidTransform
                {
                    pos = path[0],
                    rot = math.slerp(rotation1, rotation2, 0.5f)
                };
            }
            else
            {
                // First Point
                splineSamples[0] = new RigidTransform
                {
                    pos = path[0],
                    rot = quaternion.LookRotation(path[1] - path[0], new float3(0, 1f, 0))
                };

                // Last Point
                splineSamples[lastIndex] = new RigidTransform
                {
                    pos = path[lastIndex],
                    rot = quaternion.LookRotation(path[lastIndex] - path[lastIndex - 1], new float3(0, 1f, 0))
                };
            }
        }

        /// <summary>
        /// Calculates an explicit rotational component per sample given a float3 path.
        /// </summary>
        /// <param name="path">An array of float3s that define a path</param>
        /// <param name="looped">Whether or not the path is circular</param>
        /// <param name="allocator"></param>
        /// <returns></returns>
        public static NativeArray<RigidTransform> Float3PathToRigidTransformPath(
            NativeArray<float3> path,
            bool looped,
            Allocator allocator)
        {
            var splineSamples = new NativeArray<RigidTransform>(path.Length, allocator);
            Float3PathToRigidTransformPath(path, splineSamples, looped);
            return splineSamples;
        }

        public static NativeArray<RigidTransform> RemoveOverlappingPoints(
            NativeList<RigidTransform> spline,
            Allocator allocator,
            float minDist = 0.1f)
        {
            var culledPoints = new NativeList<RigidTransform>(spline.Length, allocator);
            if (spline.Length != 0)
            {
                var minDistSqr = minDist * minDist;
                culledPoints.Add(spline[0]);
                for (int i = 1, j = 0; i < spline.Length; i++)
                {
                    var dist = math.distancesq(culledPoints[j].pos, spline[i].pos);
                    if (dist > minDistSqr)
                    {
                        culledPoints.Add(spline[i]);
                        j++;
                    }
                }
            }

            var culledSpline = new NativeArray<RigidTransform>(
                culledPoints.Length, allocator, NativeArrayOptions.UninitializedMemory);
            culledSpline.CopyFrom(culledPoints);
            culledPoints.Dispose();
            return culledSpline;
        }
    }
}
