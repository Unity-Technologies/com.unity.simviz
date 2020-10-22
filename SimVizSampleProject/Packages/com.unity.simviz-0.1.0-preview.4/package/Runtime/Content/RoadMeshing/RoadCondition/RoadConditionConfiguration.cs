using UnityEngine.SimViz.Content.RoadMeshing.MaterialConfiguration;
using UnityEngine.SimViz.Scenarios;

namespace UnityEngine.SimViz.Content.RoadMeshing.RoadCondition
{
    /// <summary>
    /// Defines a configuration range of road condition values that can be meaningfully applied as parameter values to
    /// the material shaders of a road surface.
    /// </summary>
    [CreateAssetMenu(
        fileName = "RoadConditionConfiguration",
        menuName = "SimViz/Road Condition Configuration",
        order = 3)]
    public class RoadConditionConfiguration : ParameterSet
    {
        [Range(0f, 1f)] public float wetness;
        [Range(0f, 1f)] public float dustAmount;
        [Range(0f, 1f)] public float snowCover;

        public override void ApplyParameters()
        {
            var assigners = FindObjectsOfType<RoadConditionAssigner>();
            foreach (var assigner in assigners)
            {
                assigner.AssignMaterialParameters(this);
            }
        }
    }
}
