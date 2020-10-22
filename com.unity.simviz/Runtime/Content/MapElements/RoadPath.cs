using PathCreation;

namespace UnityEngine.SimViz.Content
{
    /// <summary>
    /// Defines contiguous line of samples that form the center line of a road
    /// </summary>
    public class RoadPath : PathCreator
    {
        // TODO: Add implicit casting to other data structures we need (PointSamples, etc.)

        // TODO: Bind this to the pathUpdated event
        // Interface for checking if this path has been updated since value was last checked
        public bool IsStale => true;

        bool m_Initialized = false;

        internal void Initialize()
        {
            if (m_Initialized)
            {
                Debug.LogError("Initialize was called twice on this object - can only be initialized once.");
                return;
            }
            bezierPath.ControlPointMode = BezierPath.ControlMode.Automatic;
            EditorData.keepConstantHandleSize = true;

            // TODO: Some issues need to be fixed in order to relax the constraints we're initializing here:
            // https://github.com/SebLague/Path-Creator/issues/48
            // https://jira.unity3d.com/projects/AISV/issues/AISV-281
            // When snapping control points to the XZ plane, there is an issue with the local->world transform
            // that needs to be sorted in Path-Creator.The workaround is to keep the global transform at the origin
            transform.SetPositionAndRotation(new Vector3(), new Quaternion());

            // TODO: Switch to 3D coordinate space
            bezierPath.Space = PathSpace.xz;

            m_Initialized = true;
        }
    }
}
