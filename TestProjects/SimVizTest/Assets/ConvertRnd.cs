using UnityEngine;
using UnityEngine.SimViz.Content.MapElements;
using UnityEngine.SimViz.Content.Pipeline.Systems;

public class ConvertRnd : MonoBehaviour
{
    public RoadNetworkDescription rnd;
    // Start is called before the first frame update
    void Start()
    {
        RoadNetworkDescriptionToEcsSystem.staticRnd = rnd;
    }
}
