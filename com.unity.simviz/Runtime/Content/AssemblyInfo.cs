using System.Runtime.CompilerServices;

#if UNITY_EDITOR
[assembly: InternalsVisibleTo("Unity.SimViz.Editor.Tests")]
[assembly: InternalsVisibleTo("Unity.SimViz.Editor")]
#endif
[assembly: InternalsVisibleTo("Untiy.SimViz.Tests.Scripts")]
[assembly: InternalsVisibleTo("Unity.SimViz.Runtime.Tests")]
[assembly: InternalsVisibleTo("Unity.SimViz.Runtime")]
[assembly: InternalsVisibleTo("Unity.SimViz.TestProject")]
