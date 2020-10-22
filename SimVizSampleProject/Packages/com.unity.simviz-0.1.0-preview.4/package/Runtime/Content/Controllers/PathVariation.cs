using UnityEngine;
using UnityEngine.SimViz.Content.Data;
using UnityEngine.SimViz.Scenarios;

namespace UnityEngine.SimViz.Content.Controllers
{
    [CreateAssetMenu]
    public class PathVariation : ParameterSet
    {
        public GuidReference pathObject = null;
        public GuidReference variation = null;

        public override void ApplyParameters()
        {
            var path = variation.gameObject.GetComponent<WayPointPath>();
            pathObject.gameObject.GetComponent<WayPointPath>().wayPoints = path.wayPoints;
        }
    }
}

