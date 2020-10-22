using System.Collections.Generic;
using PathCreation;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SimViz.Content;
using UnityEngine.SimViz.Content.RoadMeshing;

public class RoadPathNetworkGeneratorWindow : EditorWindow
{
    int m_RowCount = 10;
    int m_ColumnCount = 5;
    float m_RowHeight = 100f;
    float m_ColumnWidth = 220f;
    float m_Max2dNoiseOffset = 25f;
    float m_MaxYNoiseOffset = 40f;

    const float k_VerticalNoiseScale = 0.002f;
    const float k_PlanarNoiseScale = 0.003f;
    const int k_PathSubSampleFactor = 2;

    const string k_WindowTitle = "Road Path Network Grid Generator";

    [MenuItem("Window/SimViz/" + k_WindowTitle)]
    static void ShowWindow()
    {
        GetWindow(typeof(RoadPathNetworkGeneratorWindow), false, k_WindowTitle, true);
    }
    
    void OnGUI()
    {
        EditorGUILayout.LabelField("Grid Dimensions", EditorStyles.boldLabel);
        m_RowCount = EditorGUILayout.IntSlider("Rows", m_RowCount, 1, 20);
        m_ColumnCount = EditorGUILayout.IntSlider("Columns", m_ColumnCount, 1, 20);
        
        m_RowHeight = EditorGUILayout.Slider("Row Height", m_RowHeight, 80f, 300f);
        m_ColumnWidth = EditorGUILayout.Slider("Column Width", m_ColumnWidth, 80f, 300f);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Noise Configuration", EditorStyles.boldLabel);
        var minGridDimension = Mathf.Min(m_RowHeight, m_ColumnWidth);
        m_Max2dNoiseOffset = EditorGUILayout.Slider(
            "Max 2D Noise Offset", m_Max2dNoiseOffset, 0f, minGridDimension * 0.25f);
        m_MaxYNoiseOffset = EditorGUILayout.Slider(
            "Max Vertical Noise Offset", m_MaxYNoiseOffset, 0f, minGridDimension * 0.5f);
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Road Network"))
        {
            GenerateGridWithNoise();
        }
    }
    
    static GameObject CreateRoadPath(IEnumerable<Vector3> path)
    {
        var roadPathObj = new GameObject("RoadPath");
        var roadPath = roadPathObj.AddComponent<RoadPath>();
        var bezierPath = new BezierPath(path) { ControlPointMode = BezierPath.ControlMode.Free };
        bezierPath.ControlPointMode = BezierPath.ControlMode.Automatic;
        roadPath.bezierPath = bezierPath;
        return roadPathObj;
    }

    static void CreateTestRoadPathNetwork(List<GameObject> roadPaths)
    {
        var roadPathNetworkObj = new GameObject("Road Network");
        var roadPathNetwork = roadPathNetworkObj.AddComponent<RoadPathNetwork>();
        roadPathNetwork.SetRoadPaths(roadPaths);
        roadPathNetworkObj.AddComponent<RoadPathNetworkToMesh>();
    }
    
    float3 AddNoiseToGridSample(float3 sample)
    {
        var verticalNoiseInput = sample.xz * k_VerticalNoiseScale;
        var horizontalNoiseInput = sample.xz * k_PlanarNoiseScale;
        var noiseOffset = new float3(
            noise.cnoise(horizontalNoiseInput) * m_Max2dNoiseOffset,
            noise.cnoise(verticalNoiseInput) * m_MaxYNoiseOffset,
            noise.cnoise(horizontalNoiseInput.yx) * m_Max2dNoiseOffset);
        return sample + noiseOffset;
    }

    void GenerateGridWithNoise()
    {
        var roadPaths = new List<GameObject>();

        // Make row paths
        var numRowPathSegments = (m_ColumnCount + 1) * k_PathSubSampleFactor;
        var rowPathSegmentLength = m_ColumnWidth / k_PathSubSampleFactor;
        for (var i = 0; i < m_RowCount; i++)
        {
            var columnOffset = i * m_RowHeight;
            var pathPoints = new List<Vector3>();

            for (var j = 0; j <= numRowPathSegments; j++)
            {
                var pathDist = -m_ColumnWidth + rowPathSegmentLength * j;
                var nextPoint = new float3(pathDist, 0f, columnOffset);
                pathPoints.Add(AddNoiseToGridSample(nextPoint));
            }

            roadPaths.Add(CreateRoadPath(pathPoints));
        }
        
        // Make column paths
        var numColumnPathSegments = (m_RowCount + 1) * k_PathSubSampleFactor;
        var columnPathSegmentLength = m_RowHeight / k_PathSubSampleFactor;
        for (var i = 0; i < m_ColumnCount; i++)
        {
            var offset = i * m_ColumnWidth;
            var pathPoints = new List<Vector3>();

            for (var j = 0; j <= numColumnPathSegments; j++)
            {
                var pathDist = -m_RowHeight + columnPathSegmentLength * j;
                var nextPoint = new float3(offset, 0f, pathDist);
                pathPoints.Add(AddNoiseToGridSample(nextPoint));
            }

            roadPaths.Add(CreateRoadPath(pathPoints));
        }

        CreateTestRoadPathNetwork(roadPaths);
    }
}
