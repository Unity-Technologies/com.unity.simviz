using System;
using UnityEngine.SimViz.Content.RoadMeshing.RoadCondition;

namespace UnityEngine.SimViz.Content.RoadMeshing.MaterialConfiguration
{
    /// <summary>
    /// This script is a primarily a demonstration on how to vary the shader parameters of the materials attached to a
    /// road mesh. It will apply the effects of a randomly selected road condition to the materials of the designated
    /// road surface.
    /// </summary>
    public class RoadConditionAssigner : MonoBehaviour
    {
        MeshRenderer[] m_MeshRenderers;
        static readonly int k_Wetness = Shader.PropertyToID("_Wetness");
        static readonly int k_DustAmount = Shader.PropertyToID("_DustAmount");
        static readonly int k_SnowCover = Shader.PropertyToID("_SnowCover");

        public void OnEnable()
        {
            m_MeshRenderers = GetComponentsInChildren<MeshRenderer>();
        }

        public void AssignMaterialParameters(RoadConditionConfiguration config)
        {

            foreach (var meshRenderer in m_MeshRenderers)
            {
                meshRenderer.material.SetFloat(k_Wetness, config.wetness);
                meshRenderer.material.SetFloat(k_DustAmount, config.dustAmount);
                meshRenderer.material.SetFloat(k_SnowCover, config.snowCover);
            }
        }
    }
}
