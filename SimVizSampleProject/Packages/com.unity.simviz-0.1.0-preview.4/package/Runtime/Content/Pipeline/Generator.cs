namespace UnityEngine.SimViz.Content.Pipeline
{
    public interface IGeneratorSystem
    {
    }
    public interface IGeneratorSystem<TParameters>
    {
        TParameters Parameters{ get; set; }
    }

}
