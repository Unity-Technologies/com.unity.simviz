using UnityEditor;
using UnityEngine.SimViz.Content.Editor;

namespace UnityEngine.SimViz.Scenarios.Editor
{
    public static class ScenariosMenu
    {
        [MenuItem("SimViz/Content Pipeline/Generate Way Point Path")]
        public static void RoadNetworkToWayPointPath()
        {
            var roadNetworkDescription = ContentMenu.GetSelectedRoadNetwork();
            if (roadNetworkDescription == null)
            {
                Debug.LogError("Select an OpenDrive road network description asset in the project window " +
                    "before selecting this menu item to generate a way point path.");
                return;
            }

            var root = new GameObject($"{roadNetworkDescription.name}");
            ContentMenu.AddRoadNetworkReference(root, roadNetworkDescription);
            root.AddComponent(typeof(RoadNetworkWayPointGenerator));
        }
    }
}
