using PathCreationEditor;
using UnityEditor;
using UnityEditor.Experimental.TerrainAPI;

namespace UnityEngine.SimViz.Content
{
    [CustomEditor (typeof(RoadPath))]
    public class RoadPathEditor : PathEditor
    {
        // TODO: Want to override the PathEditor OnInspectorGUI to include only a subset of the customization present
        //       Many of the sliders/knobs in the inspector we either want to remove access entirely or move to the
        //       network management inspector to keep them consistent across
    }
}
