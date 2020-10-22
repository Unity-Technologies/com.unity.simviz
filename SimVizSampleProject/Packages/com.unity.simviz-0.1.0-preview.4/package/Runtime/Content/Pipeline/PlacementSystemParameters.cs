using Unity.Mathematics;

namespace UnityEngine.SimViz.Content.Pipeline
{
    public class PlacementSystemParameters
    {
        ///<summary>The minimum path distance between consecutively placed objects</summary>
        public float spacing = 5f;

        ///<summary>The normal distance away from a path an object will be set. Can be negative.</summary>
        public float offsetFromPath;

        ///<summary>The rotation applied to each placed object relative to the current path heading</summary>
        public quaternion rotationFromPath = quaternion.RotateY(-math.PI / 2f);

        ///<summary>The parent transform of which each placed object will become a child of</summary>
        public Transform parent;

        ///<summary>The layer mask used for occupancy detection when an object is being placed</summary>
        public int collisionLayerMask;

        ///<summary>
        /// Scale factor applied to the bounding boxes used to check if an object is colliding.
        /// Useful for permitting a small amount of overlap between adjacent objects to prevent gaps from appearing.
        /// </summary>
        public float collisionCheckScale = 1.0f;

        ///<summary>The placement category from which to place objects from</summary>
        public PlacementCategory category;
    }

    public class PoissonPlacementSystemParameters
    {
        ///<summary>The minimum path distance between consecutively placed objects</summary>
        public float spacing = 5f;

        /// <summary>The closest a placed object can be from the path's edge</summary>
        public float innerOffsetFromPath;

        /// <summary>The farthest a placed object can be from the path's edge</summary>
        public float outerOffsetFromPath;

        ///<summary>The rotation applied to each placed object relative to the current path heading</summary>
        public quaternion rotationFromPath = quaternion.RotateY(math.PI / 2f);

        ///<summary>The parent transform of which each placed object will become a child of</summary>
        public Transform parent;

        ///<summary>The layer mask used for occupancy detection when an object is being placed</summary>
        public int collisionLayerMask;

        ///<summary>The placement category from which to place objects from</summary>
        public PlacementCategory category;
    }
}
