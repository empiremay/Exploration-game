﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

public class CubeSphere : MonoBehaviour
{
    private int gridSize;
    private int seed; //2048
    public TerrainType[] regions;
    public Material material;
    public GameObject camera;
    private Transform cameraTransform;

    private Noise.NormalizeMode normalizeMode=Noise.NormalizeMode.Global;    //Global
    private float radius;
    private static int sqrtChunksPerFace;// = 25;     //25 - 5
    public static float heightMultiplier = DataBetweenScenes.getHeightMultiplier();//10;     //5
    private static int id = 0;

    private static Dictionary<string, float[,]> noiseMaps=new Dictionary<string, float[,]>();  //Face, noiseMap
    private static Dictionary<string, string> faceMap = new Dictionary<string, string>();
    private static Color[] colourMap;
    private static float scale = DataBetweenScenes.getScale();//30f;
    private static int octaves = DataBetweenScenes.getOctaves();//4;
    private static float persistance = DataBetweenScenes.getPersistance();//0.5f;   //0.36f; //0.5f
    private static float lacunarity = DataBetweenScenes.getLacunarity();//1f;       //1.7f;
    private static Vector2 offset = new Vector2(0, 0);

    private static List<VerticesData> verticesData;

    private int chunkSize;
    private static List<Chunk> chunks;

    private Rigidbody rigidbodyAttracted;

    private Vector3 viewerPositionOld;
    private Vector3 viewerPosition;
    private float zoomPercentageOld;

    private float secondsCounter = 0;
    private float secondsToCount = 0.25f;

    const float viewerMoveThreshholdForChunkUpdate = 0.5f;   //10f;
    const float sqrViewerMoveThresholdForChunkUpdate = viewerMoveThreshholdForChunkUpdate * viewerMoveThreshholdForChunkUpdate;
    private int closestChunkNumber = 0;

    private List<Vector3> centers;

    private Vector3[] vertices;     //Only for OnDrawGizmos
    private Vector3[] normals;      //Only for OnDrawGizmos

    //private Thread[] myThreads=new Thread[4];   //Testing
    // Timer tests
    public static Stopwatch timer = new Stopwatch();

    public void Awake()
    {
        gridSize = DataBetweenScenes.getSize();
        seed = DataBetweenScenes.getSeed();
        switch(gridSize)    //3 sizes: 100, 200 & 400
        {
            case 100: sqrtChunksPerFace = 25; break;
            case 200: sqrtChunksPerFace = 25; break;
            case 400: sqrtChunksPerFace = 25; break;
            default: gridSize = 400; sqrtChunksPerFace = 25; break;
        }

        cameraTransform = camera.transform;

        viewerPositionOld = viewerPosition = cameraTransform.position;

        radius = gridSize / 2;
        chunkSize = gridSize / sqrtChunksPerFace;

        InitiateFaceMap();
        GenerateChunks();
    }

    private void InitiateFaceMap()
    {
        faceMap["xy,up"] = "xzy";
        faceMap["xy,left"] = "zy";
        faceMap["xy,right"] = "zyx";
        faceMap["xy,down"] = "xz";

        faceMap["xzy,up"] = "xyz";
        faceMap["xzy,left"] = "zy";
        faceMap["xzy,right"] = "zyx";
        faceMap["xzy,down"] = "xy";
    }

    private void Start()
    {
        ClosestChunkHasChanged();   //Set variables for UpdateChunks()
        StartCoroutine(UpdateChunks());
    }

    private void Update()
    {
        /*secondsCounter += Time.deltaTime;
        if (secondsCounter > secondsToCount)
        {
            secondsCounter = 0;
            //DO THINGS EVERY secondsToCount SECONDS
            if (ClosestChunkHasChanged())
            {
                UpdateChunks();
            }
        }*/

        viewerPosition = cameraTransform.position;
        float zoomPercentage = camera.GetComponent<CameraController>().zoomPercentage;

        if(((viewerPositionOld-viewerPosition).sqrMagnitude > sqrViewerMoveThresholdForChunkUpdate) || (zoomPercentage != zoomPercentageOld))
        {
            viewerPositionOld = viewerPosition;
            zoomPercentageOld = zoomPercentage;
            //DO THINGS EACH TIME VIEWER MOVE viewerMoveThreshHoldForChunkUpdate UNITS
            if (ClosestChunkHasChanged())
            {
                //timer.Start();
                StartCoroutine(UpdateChunks());       //DECOMMENT
                //timer.Stop();
                //print("Tiempo " + timer.ElapsedMilliseconds);
                //timer.Reset();
            }
        }
    }

    private void GenerateChunks()
    {
        chunks = new List<Chunk>();

        verticesData = new List<VerticesData>();
        centers = new List<Vector3>();

        GenerateNoiseMapOfFace("xy");
        GenerateNoiseMapOfFace("xyz");
        GenerateNoiseMapOfFace("zy");
        GenerateNoiseMapOfFace("zyx");
        GenerateNoiseMapOfFace("xz");
        GenerateNoiseMapOfFace("xzy");

        //Ahora mezclar los noiseMap con una funcion que coja los maps del diccionario
        MixNoiseMaps(1, 5);     //Cose las brechas ocasionadas por la generación de Chunks entre diferentes caras
        MixNoiseMaps(1, 10);
        MixNoiseMaps(1, 15);
        /*MixNoiseMaps(1, 25, true);
        MixNoiseMaps(1, 75, true);*/

        GenerateChunksOfFace(chunks, "xy");
        GenerateChunksOfFace(chunks, "xyz");
        GenerateChunksOfFace(chunks, "zy");
        GenerateChunksOfFace(chunks, "zyx");
        GenerateChunksOfFace(chunks, "xz");
        GenerateChunksOfFace(chunks, "xzy");

        GenerateNormals();

        /*VerticesData finalData = CollectData(verticesData);
        vertices = finalData.verticesP();
        normals = finalData.normalsP();*/
    }

    private VerticesData CollectData(List<VerticesData> verticesData)
    {
        int numVertices = 0;
        int numNormals = 0;

        foreach (VerticesData data in verticesData)
        {
            numVertices += data.verticesP().Length;
            numNormals += data.normalsP().Length;
        }

        Vector3[] finalVertices = new Vector3[numVertices];
        Vector3[] finalNormals = new Vector3[numNormals];
        int contVertices = 0;
        int contNormals = 0;

        foreach (VerticesData data in verticesData)
        {
            Vector3[] vertices_data = data.verticesP();
            Vector3[] normals_data = data.normalsP();
            for (int i = 0; i < data.verticesP().Length; i++)
            {
                finalVertices[contVertices++] = vertices_data[i];
            }
            for (int i = 0; i < data.normalsP().Length; i++)
            {
                finalNormals[contNormals++] = normals_data[i];
            }
        }

        return new VerticesData(finalVertices, finalNormals);
    }

    private float[,] FLipMatrix(float[,] noiseMap, int width, int height, string face)
    {
        float[,] matrix = new float[width, height];
        for(int i=0; i<width; i++)
        {
            for(int j=0; j<height; j++)
            {
                switch(face)
                {
                    case "zy": matrix[i, j] = noiseMap[width - 1 - i, j]; break;
                    case "xz": matrix[i, j] = noiseMap[i, height - 1 - j]; break;
                    case "xyz": matrix[i, j] = noiseMap[width - 1 - i, j]; break;
                }
            }
        }
        return matrix;
    }

    private Texture2D CreateTexture(string face)
    {
        int width = gridSize + 1;
        int height = gridSize + 1;

        colourMap = new Color[width * height];

        float[,] noiseMap = noiseMaps[face];

        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                float currentHeight = noiseMap[j, i];
                for (int k = 0; k < regions.Length; k++)
                {
                    if (currentHeight >= regions[k].height)
                    {
                        colourMap[i * width + j] = regions[k].colour;
                    }
                    else
                    {
                        colourMap[i * width + j] = regions[k].colour;
                        break;
                    }
                }
            }
        }

        Texture2D texture = TextureGenerator.TextureFromHeightMap(noiseMap, colourMap);
        return texture;
    }

    private void GenerateNoiseMapOfFace(string face)
    {
        switch (face)
        {
            case "xy": offset = new Vector2(0, 0); break;
            case "xyz": offset = new Vector2(-2 * gridSize, 0); break;
            case "zy": offset = new Vector2(-gridSize, 0); break;
            case "zyx": offset = new Vector2(gridSize, 0); break;
            case "xz": offset = new Vector2(0, gridSize); break;
            case "xzy": offset = new Vector2(0, -gridSize); break;
        }

        //Generate Noise Map
        int width = gridSize + 1;
        int height = gridSize + 1;

        float[,] noiseMap = Noise.GenerateNoiseMap(width, height, seed, scale, octaves, persistance, lacunarity, offset, normalizeMode);

        if (face == "zy" || face == "xz" || face == "xyz") { noiseMap = FLipMatrix(noiseMap, width, height, face); }

        noiseMaps.Add(face, noiseMap);
    }

    private void MixNoiseMaps(int from, int tope, bool recalculateMedium = true)
    {
        int width = gridSize + 1;
        int height = gridSize + 1;

        float[,] noiseMapXZY = noiseMaps["xzy"];
        float[,] noiseMapZY = noiseMaps["zy"];
        float[,] noiseMapXY = noiseMaps["xy"];
        float[,] noiseMapXZ = noiseMaps["xz"];
        float[,] noiseMapZYX = noiseMaps["zyx"];
        float[,] noiseMapXYZ = noiseMaps["xyz"];

        //Lateral ZY - XZY
        for(int j=0; j<width; j++)
        {
            float medium = (noiseMapXZY[0, j] + noiseMapZY[j, height - 1]) / 2;
            if(recalculateMedium)
            {
                noiseMapXZY[0, j] = medium;
                noiseMapZY[j, height - 1] = medium;
            }
            for (int prof=from; prof<tope; prof++)     //prof=1
            {   
                float mediumXZY = (noiseMapXZY[prof, j] + noiseMapXZY[prof - 1, j]) / 2;
                float mediumZY = (noiseMapZY[j, height - 1 - prof] + noiseMapZY[j, height - prof]) / 2;
                noiseMapXZY[prof, j] = mediumXZY;
                noiseMapZY[j, height - 1 - prof] = mediumZY;
            }
        }
        //Corner ZY - XZY - XY
        noiseMapXY[0, height - 1]= noiseMapXZY[0, 0];
        for (int i=from; i<tope; i++)
        {
            float medium;
            noiseMapXY[i, height - 1] = noiseMapXZY[i, 0];
            noiseMapXY[0, height - 1 - i] = noiseMapZY[0, height - 1 - i];
            for(int prof=from; prof<tope; prof++)
            {
                if(i+prof<tope)
                {
                    medium = (noiseMapXY[i + prof, height - 1 - prof] + noiseMapXY[i + prof, height - prof]) / 2;
                    noiseMapXY[i + prof, height - 1 - prof] = medium;
                    medium = (noiseMapXY[prof, height - 1 - i - prof] + noiseMapXY[prof - 1, height - 1 - i - prof]) / 2;
                    noiseMapXY[prof, height - 1 - i - prof] = medium;
                }
            }
            //Diagonal
            noiseMapXY[i, height - 1 - i] = (noiseMapXY[i - 1, height - 1 - i] + noiseMapXY[i, height - i]) / 2;
        }
        //Lateral ZY - XZ
        for(int j=0; j<width; j++)
        {
            float medium = (noiseMapXZ[0, j] + noiseMapZY[j, 0]) / 2;
            if(recalculateMedium)
            {
                noiseMapXZ[0, j] = medium;
                noiseMapZY[j, 0] = medium;
            }
            for(int prof=from; prof<tope; prof++)
            {
                float mediumXZ = (noiseMapXZ[prof, j] + noiseMapXZ[prof - 1, j]) / 2;
                float mediumZY = (noiseMapZY[j, prof] + noiseMapZY[j, prof - 1]) / 2;
                noiseMapXZ[prof, j] = mediumXZ;
                noiseMapZY[j, prof] = mediumZY;
            }
        }
        //Corner ZY - XZ - XY
        noiseMapXY[0, 0] = noiseMapZY[0, 0];
        for (int i=from; i<tope; i++)
        {
            float medium;
            noiseMapXY[0, i] = noiseMapZY[0, i];
            noiseMapXY[i, 0] = noiseMapXZ[i, 0];
            for(int prof=from; prof<tope; prof++)
            {
                if (i+prof<tope)
                {
                    medium = (noiseMapXY[prof, i + prof] + noiseMapXY[prof - 1, i + prof]) / 2;
                    noiseMapXY[prof, i + prof] = medium;
                    medium = (noiseMapXY[i + prof, prof] + noiseMapXY[i + prof, prof - 1]) / 2;
                    noiseMapXY[i + prof, prof] = medium;
                }
            }
            //Diagonal
            noiseMapXY[i, i] = (noiseMapXY[i, i - 1] + noiseMapXY[i - 1, i]) / 2;
        }
        //Lateral ZYX - XZY
        for(int j=0; j<width; j++)
        {
            float medium = (noiseMapZYX[j, height - 1] + noiseMapXZY[height - 1, j]) / 2;
            if(recalculateMedium)
            {
                noiseMapZYX[j, height - 1] = medium;
                noiseMapXZY[height - 1, j] = medium;
            }
            for(int prof=from; prof<tope; prof++)
            {
                float mediumZYX = (noiseMapZYX[j, height - 1 - prof] + noiseMapZYX[j, height - prof]) / 2;
                float mediumXZY = (noiseMapXZY[height - 1 - prof, j] + noiseMapXZY[height - prof, j]) / 2;
                noiseMapZYX[j, height - 1 - prof] = mediumZYX;
                noiseMapXZY[height - 1 - prof, j] = mediumXZY;
            }
        }
        //Corner ZYX - XZY - XY
        noiseMapXY[height - 1, height - 1] = noiseMapXZY[height - 1, 0];
        for (int i=from; i<tope; i++)
        {
            float medium;
            noiseMapXY[height - 1 - i, height - 1] = noiseMapXZY[height - 1 - i, 0];
            noiseMapXY[height - 1, height - 1 - i] = noiseMapZYX[0, height - 1 - i];
            for(int prof=from; prof<tope; prof++)
            {
                if(i+prof<tope)
                {
                    medium = (noiseMapXY[height - 1 - prof, height - i - 1 - prof] + noiseMapXY[height - prof, height - i - 1 - prof]) / 2;
                    noiseMapXY[height - 1 - prof, height - i - 1 - prof] = medium;
                    medium = (noiseMapXY[height - i - 1 - prof, height - 1 - prof] + noiseMapXY[height - i - 1 - prof, height - prof]) / 2;
                    noiseMapXY[height - i - 1 - prof, height - 1 - prof] = medium;
                }
            }
            //Diagonal
            noiseMapXY[height - 1 - i, height - 1 - i] = (noiseMapXY[height - i, height - 1 - i] + noiseMapXY[height - 1 - i, height - i]) / 2;
        }
        //Lateral XZ - ZYX
        for(int j=0; j<width; j++)
        {
            float medium = (noiseMapXZ[height - 1, j] + noiseMapZYX[j, 0]) / 2;
            if(recalculateMedium)
            {
                noiseMapXZ[height - 1, j] = medium;
                noiseMapZYX[j, 0] = medium;
            }
            for(int prof=from; prof<tope; prof++)
            {
                float mediumXZ = (noiseMapXZ[height - 1 - prof, j] + noiseMapXZ[height - prof, j]) / 2;
                float mediumZYX = (noiseMapZYX[j, prof] + noiseMapZYX[j, prof - 1]) / 2;
                noiseMapXZ[height - 1 - prof, j] = mediumXZ;
                noiseMapZYX[j, prof] = mediumZYX;
            }
        }
        //Corner XZ - ZYX - XY
        noiseMapXY[height - 1, 0] = noiseMapZYX[0, 0];
        for (int i=from; i<tope; i++)
        {
            float medium;
            noiseMapXY[height - 1, i] = noiseMapZYX[0, i];
            noiseMapXY[height - 1 - i, 0] = noiseMapXZ[height - 1 - i, 0];
            for(int prof=from; prof<tope; prof++)
            {
                if(i+prof<tope)
                {
                    medium = (noiseMapXY[height - 1 - i - prof, prof] + noiseMapXY[height - 1 - i - prof, prof - 1]) / 2;
                    noiseMapXY[height - 1 - i - prof, prof] = medium;
                    medium = (noiseMapXY[height - 1 - prof, i + prof] + noiseMapXY[height - prof, i + prof]) / 2;
                    noiseMapXY[height - 1 - prof, i + prof] = medium;
                }
            }
            //Diagonal
            noiseMapXY[height - 1 - i, i] = (noiseMapXY[height - i, i] + noiseMapXY[height - 1 - i, i - 1]) / 2;
        }
        //Lateral XYZ - XZY
        for(int j=0; j<width; j++)
        {
            float medium = (noiseMapXYZ[j, height - 1] + noiseMapXZY[j, height - 1]) / 2;
            if(recalculateMedium)
            {
                noiseMapXYZ[j, height - 1] = medium;
                noiseMapXZY[j, height - 1] = medium;
            }
            for(int prof=from; prof<tope; prof++)
            {
                float mediumXYZ = (noiseMapXYZ[j, height - 1 - prof] + noiseMapXYZ[j, height - prof]) / 2;
                float mediumXZY = (noiseMapXZY[j, height - 1 - prof] + noiseMapXZY[j, height - prof]) / 2;
                noiseMapXYZ[j, height - 1 - prof] = mediumXYZ;
                noiseMapXZY[j, height - 1 - prof] = mediumXZY;
            }
        }
        //Lateral XYZ - XZ
        for(int j=0; j<width; j++)
        {
            float medium = (noiseMapXYZ[j, 0] + noiseMapXZ[j, height - 1]) / 2;
            if(recalculateMedium)
            {
                noiseMapXYZ[j, 0] = medium;
                noiseMapXZ[j, height - 1] = medium;
            }
            for(int prof=from; prof<tope; prof++)
            {
                float mediumXYZ = (noiseMapXYZ[j, prof] + noiseMapXYZ[j, prof - 1]) / 2;
                float mediumXZ = (noiseMapXZ[j, height - 1 - prof] + noiseMapXZ[j, height - prof]) / 2;
                noiseMapXYZ[j, prof] = mediumXYZ;
                noiseMapXZ[j, height - 1 - prof] = mediumXZ;
            }
        }
        //Lateral XYZ - ZYX
        for(int j=0; j<width; j++)
        {
            float medium = (noiseMapXYZ[height - 1, height - 1 - j] + noiseMapZYX[height - 1, height - 1 - j]) / 2;
            if(recalculateMedium)
            {
                noiseMapXYZ[height - 1, height - 1 - j] = medium;
                noiseMapZYX[height - 1, height - 1 - j] = medium;
            }
            for(int prof=from; prof<tope; prof++)
            {
                float mediumXYZ = (noiseMapXYZ[height - 1 - prof, height - 1 - j] + noiseMapXYZ[height - prof, height - 1 - j]) / 2;
                float mediumZYX = (noiseMapZYX[height - 1 - prof, height - 1 - j] + noiseMapZYX[height - prof, height - 1 - j]) / 2;
                noiseMapXYZ[height - 1 - prof, height - 1 - j] = mediumXYZ;
                noiseMapZYX[height - 1 - prof, height - 1 - j] = mediumZYX;
            }
        }
        //Corner XZY - XYZ - ZY
        noiseMapZY[height - 1, height - 1] = noiseMapXZY[0, height - 1];
        for (int i = from; i < tope; i++)
        {
            float medium;
            noiseMapZY[height - 1 - i, height - 1] = noiseMapXZY[0, height - 1 - i];
            noiseMapZY[height - 1, height - 1 - i] = noiseMapXYZ[0, height - 1 - i];
            for(int prof=from; prof<tope; prof++)
            {
                if(i+prof<tope)
                {
                    medium = (noiseMapZY[height - 1 - i - prof, height - 1 - prof] + noiseMapZY[height - 1 - i - prof, height - prof]) / 2;
                    noiseMapZY[height - 1 - i - prof, height - 1 - prof] = medium;
                    medium = (noiseMapZY[height - 1 - prof, height - 1 - i - prof] + noiseMapZY[height - prof, height - 1 - i - prof]) / 2;
                    noiseMapZY[height - 1 - prof, height - 1 - i - prof] = medium;
                }
            }
            //Diagonal
            noiseMapZY[height - 1 - i, height - 1 - i] = (noiseMapZY[height - i, height - 1 - i] + noiseMapZY[height - 1 - i, height - i]) / 2;
        }
        //Corner XYZ - XZ - ZY
        noiseMapZY[height - 1, 0] = noiseMapXZ[0, height - 1];
        for (int i=from; i<tope; i++)      //i=1
        {
            float medium;
            noiseMapZY[height - 1 - i, 0] = noiseMapXZ[0, height - 1 - i];
            noiseMapZY[height - 1, i] = noiseMapXYZ[0, i];
            for(int prof=from; prof<tope; prof++)
            {
                if(i+prof<tope)
                {
                    medium = (noiseMapZY[height - 1 - prof, i + prof] + noiseMapZY[height - prof, i + prof]) / 2;
                    noiseMapZY[height - 1 - prof, i + prof] = medium;
                    medium = (noiseMapZY[height - 1 - i - prof, prof] + noiseMapZY[height - 1 - i - prof, prof - 1]) / 2;
                    noiseMapZY[height - 1 - i - prof, prof] = medium;
                }
            }
            //Diagonal
            noiseMapZY[height - 1 - i, i] = (noiseMapZY[height - i, i] + noiseMapZY[height - 1 - i, i - 1]) / 2;
        }
        //Corner ZYX - XYZ - XZY
        noiseMapXZY[height - 1, height - 1] = noiseMapXYZ[height - 1, height - 1];
        for (int i=from; i<tope; i++)
        {
            float medium;
            noiseMapXZY[height - 1 - i, height - 1] = noiseMapXYZ[height - 1 - i, height - 1];
            noiseMapXZY[height - 1, height - 1 - i] = noiseMapZYX[height - 1 - i, height - 1];
            for(int prof=from; prof<tope; prof++)
            {
                if(i+prof<tope)
                {
                    medium = (noiseMapXZY[height - 1 - i - prof, height - 1 - prof] + noiseMapXZY[height - 1 - i - prof, height - prof]) / 2;
                    noiseMapXZY[height - 1 - i - prof, height - 1 - prof] = medium;
                    medium = (noiseMapXZY[height - 1 - prof, height - 1 - i - prof] + noiseMapXZY[height - prof, height - 1 - i - prof]) / 2;
                    noiseMapXZY[height - 1 - prof, height - 1 - i - prof] = medium;
                }
            }
            //Diagonal
            noiseMapXZY[height - 1 - i, height - 1 - i] = (noiseMapXZY[height - i, height - 1 - i] + noiseMapXZY[height - 1 - i, height - i]) / 2;
        }
        //Corner XYZ - ZYX - XZ
        noiseMapXZ[height - 1, height - 1] = noiseMapXYZ[height - 1, 0];
        for (int i=from; i<tope; i++)
        {
            float medium;
            noiseMapXZ[height - 1 - i, height - 1] = noiseMapXYZ[height - 1 - i, 0];
            noiseMapXZ[height - 1, height - 1 - i] = noiseMapZYX[height - 1 - i, 0];
            for(int prof=from; prof<tope; prof++)
            {
                if (i + prof < tope)
                {
                    medium = (noiseMapXZ[height - 1 - i - prof, height - 1 - prof] + noiseMapXZ[height - 1 - i - prof, height - prof]) / 2;
                    noiseMapXZ[height - 1 - i - prof, height - 1 - prof] = medium;
                    medium = (noiseMapXZ[height - 1 - prof, height - 1 - i - prof] + noiseMapXZ[height - prof, height - 1 - i - prof]) / 2;
                    noiseMapXZ[height - 1 - prof, height - 1 - i - prof] = medium;
                }
            }
            //Diagonal
            noiseMapXZ[height - 1 - i, height - 1 - i] = (noiseMapXZ[height - i, height - 1 - i] + noiseMapXZ[height - 1 - i, height - i]) / 2;
        }

        noiseMaps["xzy"] = noiseMapXZY;
        noiseMaps["zy"] = noiseMapZY;
        noiseMaps["xy"] = noiseMapXY;
        noiseMaps["xz"] = noiseMapXZ;
        noiseMaps["zyx"] = noiseMapZYX;
        noiseMaps["xyz"] = noiseMapXYZ;
    }

    private void GenerateChunksOfFace(List<Chunk> chunks, string face)
    {
        switch (face)
        {
            case "xy": Texture2D faceTextureXY = CreateTexture("xy");
                       for (int y = 0; y < sqrtChunksPerFace; y++)
                       {
                           for (int x = 0; x < sqrtChunksPerFace; x++)
                           {
                                Chunk chunk = new Chunk(transform, material, chunkSize, radius, face, x * chunkSize, y * chunkSize, 0, gridSize, regions, faceTextureXY, x, y, id++);
                                chunk.Generate();
                                chunks.Add(chunk);
                                //verticesData.Add(chunk.GetVerticesData());
                                //centers.Add(chunk.GetCenter());
                           }
                       } break;
            case "xyz": Texture2D faceTextureXYZ = CreateTexture("xyz");
                        for (int y = 0; y < sqrtChunksPerFace; y++)
                        {
                            for (int x = 0; x < sqrtChunksPerFace; x++)
                            {
                                Chunk chunk = new Chunk(transform, material, chunkSize, radius, face, x * chunkSize, y * chunkSize, 0, gridSize, regions, faceTextureXYZ, x, y, id++);
                                chunk.Generate();
                                chunks.Add(chunk);
                                //verticesData.Add(chunk.GetVerticesData());
                                //centers.Add(chunk.GetCenter());
                            }
                        }
                        break;
            case "zy":  Texture2D faceTextureZY = CreateTexture("zy");
                        for (int y = 0; y < sqrtChunksPerFace; y++)
                        {
                            for (int z = 0; z < sqrtChunksPerFace; z++)
                            {
                                Chunk chunk = new Chunk(transform, material, chunkSize, radius, face, 0, y * chunkSize, z * chunkSize, gridSize, regions, faceTextureZY, z, y, id++);
                                chunk.Generate();
                                chunks.Add(chunk);
                                //verticesData.Add(chunk.GetVerticesData());
                                //centers.Add(chunk.GetCenter());
                            }
                        }
                        break;
            case "zyx": Texture2D faceTextureZYX = CreateTexture("zyx");
                        for (int y = 0; y < sqrtChunksPerFace; y++)
                        {
                            for (int z = 0; z < sqrtChunksPerFace; z++)
                            {
                                Chunk chunk = new Chunk(transform, material, chunkSize, radius, face, 0, y * chunkSize, z * chunkSize, gridSize, regions, faceTextureZYX, z, y, id++);
                                chunk.Generate();
                                chunks.Add(chunk);
                                //verticesData.Add(chunk.GetVerticesData());
                                //centers.Add(chunk.GetCenter());
                            }
                        }
                        break;
            case "xz":  Texture2D faceTextureXZ = CreateTexture("xz");
                        for (int z = 0; z < sqrtChunksPerFace; z++)
                        {
                            for (int x = 0; x < sqrtChunksPerFace; x++)
                            {
                                Chunk chunk = new Chunk(transform, material, chunkSize, radius, face, x * chunkSize, 0, z * chunkSize, gridSize, regions, faceTextureXZ, x, z, id++);
                                chunk.Generate();
                                chunks.Add(chunk);
                                //verticesData.Add(chunk.GetVerticesData());
                                //centers.Add(chunk.GetCenter());
                            }
                        }
                        break;
            case "xzy": Texture2D faceTextureXZY = CreateTexture("xzy");
                        for (int z = 0; z < sqrtChunksPerFace; z++)
                        {
                            for (int x = 0; x < sqrtChunksPerFace; x++)
                            {
                                Chunk chunk = new Chunk(transform, material, chunkSize, radius, face, x * chunkSize, 0, z * chunkSize, gridSize, regions, faceTextureXZY, x, z, id++);
                                chunk.Generate();
                                chunks.Add(chunk);
                                //verticesData.Add(chunk.GetVerticesData());
                                //centers.Add(chunk.GetCenter());
                            }
                        } break;
        }
    }

    private void GenerateNormals()
    {
        foreach(Chunk chunk in chunks)
        {
            chunk.GenerateNormals();
        }
        /*foreach (Chunk chunk in chunks)
        {
            if(chunk.face=="xy")
            {
                chunk.AdjustBorderNormals2();
            }
        }*/
    }

    private bool ClosestChunkHasChanged()
    {
        //Select closest Chunk
        float min_distance = float.MaxValue;
        Chunk selectedChunk = chunks[0];
        int closestChunkCount = -1;
        int chunkCount = 0;

        foreach(Chunk chunk in chunks)
        {
            float distance = Vector3.Distance(cameraTransform.position, chunk.GetCenter());
            if (distance < min_distance)
            {
                min_distance = distance;
                selectedChunk = chunk;
                closestChunkCount=chunkCount;
            }
            ++chunkCount;
        }

        foreach (Chunk chunk in chunks)
        {
            chunk.SetClosestChunk(false);
        }
        selectedChunk.SetClosestChunk(true);

        if(closestChunkCount != closestChunkNumber)
        {
            closestChunkNumber = closestChunkCount;
            //Update adjacent chunks of the selected chunk
            min_distance = float.MaxValue;
            List<Chunk> adjacentChunks = new List<Chunk>();

            foreach (Chunk chunk in chunks)
            {
                if (chunk != selectedChunk)
                {
                    float distance = Vector3.Distance(selectedChunk.GetCenter(), chunk.GetCenter());
                    chunk.SetDistanceToClosestChunk(distance);
                    adjacentChunks.Add(chunk);
                }
            }
            adjacentChunks.Sort(delegate (Chunk a, Chunk b)
            {
                return a.GetDistanceToClosestChunk().CompareTo(b.GetDistanceToClosestChunk());
            });
            selectedChunk.SetAdjacentChunks(adjacentChunks);
            return true;
        }
        else
        {
            return true;    //false
        }
    }

    private IEnumerator UpdateChunks()
    {
        float zoomPercentage = camera.GetComponent<CameraController>().zoomPercentage;

        int maxChunksToRender = 1300;   //1000
        int minChunksToRender = 50;     //20
        switch(gridSize)
        {
            case 100: maxChunksToRender = 1500; minChunksToRender = 200; break;
            case 200: maxChunksToRender = 1300; minChunksToRender = 110; break;
            case 400: maxChunksToRender = 1000; minChunksToRender = 50; break;
        }
        int chunksToRender = (int)(minChunksToRender + (zoomPercentage / 100) * (maxChunksToRender - minChunksToRender));

        int maxResToRender;
        if(zoomPercentage <= 20f)
        {
            maxResToRender = 1;
        }
        else
        {
            if(zoomPercentage <= 40f)
            {
                maxResToRender = 2;
            }
            else
            {
                if(zoomPercentage <= 60f)
                {
                    maxResToRender = 4;
                }
                else
                {
                    maxResToRender = 8;
                    if(gridSize == 100)
                    {
                        maxResToRender = 4;
                    }
                }
            }
        }

        Chunk closestChunk = chunks[0];

        foreach (Chunk chunk in chunks)
        {
            if (chunk.IsClosestChunk())
            {
                closestChunk = chunk;
                closestChunk.UpdateLOD(maxResToRender);  //1
            }
        }

        int adjacentCount = 1;
        foreach (Chunk adjacent in closestChunk.GetAdjacentChunks())
        {
            if(adjacentCount < (chunksToRender * 1 / 4))
            {
                adjacent.UpdateLOD(maxResToRender);
                ++adjacentCount;
            }
            else
            {
                if(adjacentCount < (chunksToRender * 2 / 4))
                {
                    adjacent.UpdateLOD(maxResToRender);
                    ++adjacentCount;
                }
                else
                {
                    if(adjacentCount < (chunksToRender * 3 / 4))
                    {
                        adjacent.UpdateLOD(maxResToRender);
                        ++adjacentCount;
                    }
                    else
                    {
                        if (adjacentCount < chunksToRender)
                        {
                            adjacent.UpdateLOD(maxResToRender);
                            ++adjacentCount;
                        }
                        else
                        {
                            adjacent.Render(false);
                        }
                    }
                }
            }
        }

        

        /*int adjacentCount = 1;
        foreach (Chunk adjacent in closestChunk.GetAdjacentChunks())
        {
            if (adjacentCount < 10)    //150 - 149
            {
                adjacent.UpdateLOD(1);      //1
                ++adjacentCount;
            }
            else
            {
                if (adjacentCount < 20)    //300 - 250
                {
                    adjacent.UpdateLOD(2);      //2
                    ++adjacentCount;
                }
                else
                {
                    if (adjacentCount < 40)    //600 - 500
                    {
                        adjacent.UpdateLOD(4);      //4
                        ++adjacentCount;
                    }
                    else
                    {
                        if(adjacentCount < 80)     //900 - 650
                        {
                            adjacent.UpdateLOD(8);      //8
                            ++adjacentCount;
                        }
                        else
                        {
                            if(adjacentCount < 1)    //1200 - 900
                            {
                                adjacent.UpdateLOD(1);      //8
                                ++adjacentCount;
                            }
                            else
                            {
                                adjacent.SetActive(false);
                            }
                        }
                    }
                }
            }
        }*/

        yield return null;
    }

    public static int GetRelativeChunkId(int relativeID, string face)
    {
        int orden = 0;
        if (face == "xy") orden = 1;
        if (face == "xyz") orden = 2;
        if (face == "zy") orden = 3;
        if (face == "zyx") orden = 4;
        if (face == "xz") orden = 5;
        if (face == "xzy") orden = 6;
        
        int startingChunk = (sqrtChunksPerFace * sqrtChunksPerFace * orden) - (sqrtChunksPerFace * sqrtChunksPerFace);
        return relativeID - startingChunk;
    }

    public static int GetChunkId(int id, string direction, string face)
    {
        string objectiveFace = faceMap[face + "," + direction];

        int orden = 0;
        if (objectiveFace == "xy") orden = 1;
        if (objectiveFace == "xyz") orden = 2;
        if (objectiveFace == "zy") orden = 3;
        if (objectiveFace == "zyx") orden = 4;
        if (objectiveFace == "xz") orden = 5;
        if (objectiveFace == "xzy") orden = 6;

        int thisOrden = 0;
        if (face == "xy") thisOrden = 1;
        if (face == "xyz") thisOrden = 2;
        if (face == "zy") thisOrden = 3;
        if (face == "zyx") thisOrden = 4;
        if (face == "xz") thisOrden = 5;
        if (face == "xzy") thisOrden = 6;

        int startingChunk = (sqrtChunksPerFace * sqrtChunksPerFace * orden) - (sqrtChunksPerFace * sqrtChunksPerFace);
        int thisStartingChunk = (sqrtChunksPerFace * sqrtChunksPerFace * thisOrden) - (sqrtChunksPerFace * sqrtChunksPerFace);

        switch (direction)
        {
            case "up":
                switch(face)
                {
                    case "xy": return startingChunk + (id - (sqrtChunksPerFace * (sqrtChunksPerFace - 1)));
                    case "xzy": return (startingChunk + (sqrtChunksPerFace * (sqrtChunksPerFace - 1))) + (id - (thisStartingChunk + (sqrtChunksPerFace * (sqrtChunksPerFace - 1))));
                }
                break;
            case "left":
                switch(face)
                {
                    case "xy": return startingChunk + id;
                    case "xzy": return (startingChunk + (sqrtChunksPerFace * (sqrtChunksPerFace - 1))) + (id - thisStartingChunk) / sqrtChunksPerFace;
                }
                break;
            case "right":
                switch(face)
                {
                    case "xy": return startingChunk + (id - (sqrtChunksPerFace - 1));
                    case "xzy": return (startingChunk + (sqrtChunksPerFace * (sqrtChunksPerFace - 1))) + (id - (sqrtChunksPerFace - 1) - thisStartingChunk) / sqrtChunksPerFace;
                }
                break;
            case "down":
                switch(face)
                {
                    case "xy": return startingChunk + id;
                    case "xzy": return (startingChunk + (sqrtChunksPerFace * (sqrtChunksPerFace - 1))) + (id - thisStartingChunk);
                }
                break;
            default: return 0;
        }
        return 0;
    }

    private void OnDrawGizmos()
    {
        /*VerticesData finalData = CollectData(verticesData);
        vertices = finalData.verticesP();
        normals = finalData.normalsP();*/

        /*Gizmos.color = Color.blue;
        Gizmos.DrawSphere(viewerPositionOld, 1f);*/

        /*if (centers != null)
        {
            Gizmos.color = Color.yellow;
            foreach (Vector3 center in centers)
            {
                Gizmos.DrawSphere(center, 1f);
            }
        }

        /*if(chunks != null)
        {
            Gizmos.color = Color.red;
            foreach (Chunk chunk in chunks)
            {
                if (chunk.IsClosestChunk())
                {
                    Gizmos.DrawSphere(chunk.GetCenter(), 1f);
                }
            }
        }*/
        /*if (vertices == null)
        {
            return;
        }
        Gizmos.color = Color.black;
        for (int i = 0; i < vertices.Length; i++)
        {
            //Gizmos.color = Color.black;
            //Gizmos.DrawSphere(vertices[i], 10f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(vertices[i], normals[i]);
            //Vector3 realVertice = vertices[i] + vertices[i] * (20 / radius);
            //Gizmos.DrawRay(new Vector3(0, 0, 0), realVertice);
        }*/
    }

    public class VerticesData
    {
        private Vector3[] verticesParcial;
        private Vector3[] normalsParcial;

        public VerticesData(Vector3[] verticesParcial, Vector3[] normalsParcial)
        {
            this.verticesParcial = verticesParcial;
            this.normalsParcial = normalsParcial;
        }

        public Vector3[] verticesP() { return verticesParcial; }
        public Vector3[] normalsP() { return normalsParcial; }
    }

    public class Chunk
    {
        public GameObject chunkObject;
        MeshRenderer meshRenderer;
        public MeshFilter meshFilter;
        public Mesh mesh;

        TerrainType[] regions;

        public int chunkSize;
        private int gridSize;
        private float radius;
        private Vector3 center;
        Texture2D faceTexture;   //Texture of the face
        private int[] triangles;
        private Vector3[] vertices;

        public Vector3[] borderVertices;

        private bool closestChunk;
        public bool isActive;
        private List<Chunk> adjacentChunks;
        private float distanceToClosestChunk;
        public string face;
        private int fromX;
        private int fromY;
        private int fromZ;
        public int reason;

        public int posX;
        public int posY;
        public int id;

        private VerticesData data;

        public Chunk(Transform parent, Material material, int chunkSize, float radius, string face, int fromX, int fromY, int fromZ, int gridSize, TerrainType[] regions, Texture2D faceTexture, int posX, int posY, int id)
        {
            chunkObject = new GameObject("Chunk");
            chunkObject.transform.parent = parent;
            meshRenderer = chunkObject.AddComponent<MeshRenderer>();
            meshFilter = chunkObject.AddComponent<MeshFilter>();
            meshRenderer.material = material;

            this.chunkSize = chunkSize;
            this.gridSize = gridSize;
            this.radius = radius;
            this.face = face;
            this.fromX = fromX;
            this.fromY = fromY;
            this.fromZ = fromZ;
            this.regions = regions;
            this.faceTexture = faceTexture;
            this.posX = posX;
            this.posY = posY;
            this.id = id;
            isActive = true;
            reason = 1;
            distanceToClosestChunk = 0;

            closestChunk = false;
            adjacentChunks = new List<Chunk>();

            meshFilter.mesh = mesh = new Mesh();

            //Asign SubTexture
            meshRenderer.material.mainTexture = faceTexture;
            //Create Collider
            chunkObject.AddComponent<MeshCollider>();
        }

        public void Generate()
        {
            CreateCenters();
            //Asign Colliders
            CreateVertices();
            CreateTriangles();
            AssignCollider();
        }

        public void Render(bool condition)
        {
            isActive = condition;
            //chunkObject.SetActive(condition);
            chunkObject.GetComponent<MeshRenderer>().enabled = condition;
        }

        public void SetClosestChunk(bool isClosest)
        {
            closestChunk = isClosest;
        }

        public void SetAdjacentChunks(List<Chunk> chunks)
        {
            adjacentChunks = chunks;
        }

        public void SetDistanceToClosestChunk(float distance)
        {
            distanceToClosestChunk = distance;
        }

        public bool IsClosestChunk()
        {
            return closestChunk;
        }

        public void UpdateLOD(int reason)
        {
            if (!isActive) { Render(true); GenerateNormals2();/*CalculateNormals();*/ } //COMMENT/DECOMMENT     
            if (this.reason == reason) { return; }     //Update only if reason is different
            this.reason = reason;

            mesh = meshFilter.mesh;
            mesh.Clear();

            CreateVertices();
            CreateTriangles();
            //CalculateNormals();     //COMMENT
            GenerateNormals2();   //DECOMMNENT

            //verticesData.Add(new VerticesData(mesh.vertices, mesh.normals));
            //GenerateNormals();
        }

        public Vector3 GetCenter()
        {
            return center;
        }

        public VerticesData GetVerticesData() { /*return data;*/ return new VerticesData(mesh.vertices, mesh.normals); }

        public List<Chunk> GetAdjacentChunks() { return adjacentChunks; }

        public float GetDistanceToClosestChunk() { return distanceToClosestChunk; }

        private Vector3 SquareToSpherePosition(Vector3 position)
        {
            Vector3 w = position * 2f / gridSize - Vector3.one;
            float x2 = w.x * w.x;
            float y2 = w.y * w.y;
            float z2 = w.z * w.z;
            Vector3 s;
            s.x = w.x * Mathf.Sqrt(1f - y2 / 2f - z2 / 2f + y2 * z2 / 3f);
            s.y = w.y * Mathf.Sqrt(1f - x2 / 2f - z2 / 2f + x2 * z2 / 3f);
            s.z = w.z * Mathf.Sqrt(1f - x2 / 2f - y2 / 2f + x2 * y2 / 3f);
            s *= radius;
            return s;
        }

        private void CreateCenters()
        {
            switch (face)
            {
                case "xy":
                    center = SquareToSpherePosition(new Vector3(fromX + chunkSize / 2, fromY + chunkSize / 2, 0));
                    break;
                case "xyz":
                    center = SquareToSpherePosition(new Vector3(fromX + chunkSize / 2, fromY + chunkSize / 2, gridSize));
                    break;
                case "zy":
                    center = SquareToSpherePosition(new Vector3(0, fromY + chunkSize / 2, fromZ + chunkSize / 2));
                    break;
                case "zyx":
                    center = SquareToSpherePosition(new Vector3(gridSize, fromY + chunkSize / 2, fromZ + chunkSize / 2));
                    break;
                case "xz":
                    center = SquareToSpherePosition(new Vector3(fromX + chunkSize / 2, 0, fromZ + chunkSize / 2));
                    break;
                case "xzy":
                    center = SquareToSpherePosition(new Vector3(fromX + chunkSize / 2, gridSize, fromZ + chunkSize / 2));
                    break;
            }
        }

        private void CreateVertices()
        {
            int numBorderVertices = (chunkSize / reason + 1) * (chunkSize / reason + 1);
            
            borderVertices = new Vector3[numBorderVertices];
            int numVertices = (chunkSize/reason + 1) * (chunkSize/reason + 1);
            Vector3[] verticesParcial = new Vector3[numVertices];
            Vector3[] normalsParcial = new Vector3[verticesParcial.Length];
            Vector2[] uvs = new Vector2[(chunkSize/reason+1)*(chunkSize/reason+1)];

            int v;
            int cont = 0;
            float[,] noiseMap;
            switch (face)
            {
                case "xy":
                    noiseMap = noiseMaps["xy"];
                    v = 0;
                    cont = 0;
                    for (int y = fromY; y <= (fromY + chunkSize); y+=reason)
                    {
                        for (int x = fromX; x <= (fromX + chunkSize); x+=reason)
                        {
                            float height = noiseMap[x, y] * heightMultiplier;
                            SetVertex(verticesParcial, normalsParcial, height, v, x, y, 0);
                            v++;
                            float coorX = (float)x / (float)gridSize;
                            float coorY = (float)y / (float)gridSize;
                            uvs[cont++] = new Vector2(coorX, coorY);
                        }
                    }
                    break;
                case "xyz":
                    noiseMap = noiseMaps["xyz"];
                    v = 0;
                    cont = 0;
                    for (int y = fromY; y <= (fromY + chunkSize); y+=reason)
                    {
                        for (int x = (fromX + chunkSize); x >= fromX; x-=reason)
                        {
                            float height = noiseMap[x, y] * heightMultiplier;
                            SetVertex(verticesParcial, normalsParcial, height, v, x, y, gridSize);
                            v++;
                            float coorX = (float)x / (float)gridSize;
                            float coorY = (float)y / (float)gridSize;
                            uvs[cont++] = new Vector2(coorX, coorY);
                        }
                    }
                    break;
                case "zy":
                    noiseMap = noiseMaps["zy"];
                    v = 0;
                    cont = 0;
                    for (int y = fromY; y <= (fromY + chunkSize); y+=reason)
                    {
                        for (int z = (fromZ + chunkSize); z >= fromZ; z-=reason)
                        {
                            float height = noiseMap[z, y] * heightMultiplier;
                            SetVertex(verticesParcial, normalsParcial, height, v, 0, y, z);
                            v++;
                            float coorX = (float)z / (float)gridSize;
                            float coorY = (float)y / (float)gridSize;
                            uvs[cont++] = new Vector2(coorX, coorY);
                        }
                    }
                    break;
                case "zyx":
                    noiseMap = noiseMaps["zyx"];
                    v = 0;
                    cont = 0;
                    for (int y = fromY; y <= (fromY + chunkSize); y+=reason)
                    {
                        for (int z = fromZ; z <= (fromZ + chunkSize); z+=reason)
                        {
                            float height = noiseMap[z, y] * heightMultiplier;
                            SetVertex(verticesParcial, normalsParcial, height, v, gridSize, y, z);
                            v++;
                            float coorX = (float)z / (float)gridSize;
                            float coorY = (float)y / (float)gridSize;
                            uvs[cont++] = new Vector2(coorX, coorY);
                        }
                    }
                    break;
                case "xz":
                    noiseMap = noiseMaps["xz"];
                    v = 0;
                    cont = 0;
                    for (int z = fromZ; z <= (fromZ + chunkSize); z+=reason)
                    {
                        for (int x = (fromX + chunkSize); x >= fromX; x-=reason)
                        {
                            float height = noiseMap[x, z] * heightMultiplier;
                            SetVertex(verticesParcial, normalsParcial, height, v, x, 0, z);
                            v++;
                            float coorX = (float)x / (float)gridSize;
                            float coorY = (float)z / (float)gridSize;
                            uvs[cont++] = new Vector2(coorX, coorY);
                        }
                    }
                    break;
                case "xzy":
                    noiseMap = noiseMaps["xzy"];
                    v = 0;
                    cont = 0;
                    for (int z = fromZ; z <= (fromZ+chunkSize); z+=reason)
                    {
                        for (int x = fromX; x <= (fromX+chunkSize); x+=reason)
                        {
                            float height = noiseMap[x, z] * heightMultiplier;
                            SetVertex(verticesParcial, normalsParcial, height, v, x, gridSize, z);
                            v++;
                            float coorX = (float)x / (float)gridSize;
                            float coorY = (float)z / (float)gridSize;
                            uvs[cont++] = new Vector2(coorX, coorY);
                        }
                    }
                    break;
            }

            vertices = verticesParcial;
            mesh.vertices = verticesParcial;
            mesh.uv = uvs;

            //data = new VerticesData(verticesParcial, normalsParcial);
        }

        private void SetVertex(Vector3[] verticesParcial, Vector3[] normalsParcial, float height, int i, float x, float y, float z)      //Generates the vertex 'i' in the coordinates x, y, z
        {
            Vector3 v = new Vector3(x, y, z) * 2f / gridSize - Vector3.one;
            float x2 = v.x * v.x;
            float y2 = v.y * v.y;
            float z2 = v.z * v.z;
            Vector3 s;
            s.x = v.x * Mathf.Sqrt(1f - y2 / 2f - z2 / 2f + y2 * z2 / 3f);
            s.y = v.y * Mathf.Sqrt(1f - x2 / 2f - z2 / 2f + x2 * z2 / 3f);
            s.z = v.z * Mathf.Sqrt(1f - x2 / 2f - y2 / 2f + x2 * y2 / 3f);
            normalsParcial[i] = s;    //s
            verticesParcial[i] = normalsParcial[i] * radius;

            Vector3 realVertice = verticesParcial[i] + verticesParcial[i] * (height / radius);
            verticesParcial[i] = realVertice;     //COMMENT for sphere instead of mountains

            //Assign border vertice if it is
            if (i <= (chunkSize / reason) || i % (chunkSize / reason + 1) == 0 || (i+1) % (chunkSize / reason + 1) == 0 || i >= (chunkSize / reason + 1) * (chunkSize / reason))
            {
                borderVertices[i] = verticesParcial[i];
            }
        }

        private void CreateTriangles()
        {
            int numBorderTriangles = (chunkSize / reason -1) * 24;
            int[] trianglesParcial = new int[(chunkSize/reason) * (chunkSize/reason) * 6];
            int t = 0, v = 0;

            for (int y = 0; y < chunkSize/reason; y++, v++)
            {
                for (int x = 0; x < chunkSize/reason; x++, v++)
                {
                    t = SetQuad(trianglesParcial, t, v, v + 1, v + chunkSize/reason + 1, v + chunkSize/reason + 2);
                }
            }

            triangles = trianglesParcial;
            mesh.triangles = trianglesParcial;
        }

        private int SetQuad(int[] triangles, int i, int v00, int v10, int v01, int v11)
        {
            triangles[i] = v00;
            triangles[i + 1] = triangles[i + 4] = v01;
            triangles[i + 2] = triangles[i + 3] = v10;
            triangles[i + 5] = v11;
            return i + 6;
        }

        private void AssignCollider()
        {
            chunkObject.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        public int GetNumVertices() { return (chunkSize / reason + 1) * (chunkSize / reason + 1); }

        public void CalculateNormals()   //Esta funcion recalcula las normales y luego reasigna las
                                         //normales de los bordes del chunk en base a sus chunks adyacentes
        {
            mesh.RecalculateNormals();
        }

        /*public void AdjustBorderNormals2()  //Modificar todas las normales de los bordes
        {
            Vector3[] normals = mesh.normals;

            int numVertices = GetNumVertices();
            Vector3[] borderNormals = new Vector3[numVertices];

            for(int i=0; i<numVertices; i++)
            {
                borderNormals[i] = normals[i];

                if(borderVertices[i] != new Vector3(0,0,0))
                {
                    if(posX > 0 && posY > 0 && posX < sqrtChunksPerFace - 1 && posY < sqrtChunksPerFace - 1)    //Chunks internos
                    {
                        if (i < (chunkSize / reason + 1))    //Normales de abajo
                        {
                            if (chunks[id - sqrtChunksPerFace].isActive && reason >= chunks[id - sqrtChunksPerFace].reason) borderNormals[i] = chunks[id - sqrtChunksPerFace].mesh.normals[GetStartingI(id - sqrtChunksPerFace, i) + chunks[id - sqrtChunksPerFace].GetNumVertices() - 1 - (chunkSize / chunks[id - sqrtChunksPerFace].reason)];
                        }
                        else
                        {
                            if (i >= ((chunkSize / reason + 1) * (chunkSize / reason)))    //Normales de arriba
                            {
                                if (chunks[id + sqrtChunksPerFace].isActive && reason >= chunks[id + sqrtChunksPerFace].reason) borderNormals[i] = chunks[id + sqrtChunksPerFace].mesh.normals[GetStartingI(id + sqrtChunksPerFace, i - (GetNumVertices() - 1 - chunkSize / reason))];
                            }
                            else
                            {
                                if (i % (chunkSize / reason + 1) == 0)    //Normales de la izquierda
                                {
                                    if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) borderNormals[i] = chunks[id - 1].mesh.normals[GetStartingI(id - 1, i) + (chunkSize / chunks[id - 1].reason)];
                                }
                                else    //Normales de la derecha
                                {
                                    if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) borderNormals[i] = chunks[id + 1].mesh.normals[GetStartingI(id + 1, i - (chunkSize / reason))];
                                }
                            }
                        }
                        //Esquinas
                        if (i == 0)
                        {
                            if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals(i + (chunkSize / chunks[id - 1].reason), borderNormals[i]);
                            if (chunks[id - sqrtChunksPerFace - 1].isActive && reason >= chunks[id - sqrtChunksPerFace - 1].reason) chunks[id - sqrtChunksPerFace - 1].RebuildNormals(chunks[id - sqrtChunksPerFace - 1].GetNumVertices() - 1, borderNormals[i]);
                        }
                        if (i == (chunkSize / reason))
                        {
                            if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals(0, borderNormals[i]);
                            if (chunks[id - sqrtChunksPerFace + 1].isActive && reason >= chunks[id - sqrtChunksPerFace + 1].reason) chunks[id - sqrtChunksPerFace + 1].RebuildNormals(chunks[id - sqrtChunksPerFace + 1].GetNumVertices() - 1 - (chunkSize / chunks[id - sqrtChunksPerFace + 1].reason), borderNormals[i]);
                        }
                        if (i == ((chunkSize / reason + 1) * (chunkSize / reason)))
                        {
                            if (chunks[id + sqrtChunksPerFace - 1].isActive && reason >= chunks[id + sqrtChunksPerFace - 1].reason - 1) chunks[id + sqrtChunksPerFace - 1].RebuildNormals(chunkSize / chunks[id + sqrtChunksPerFace - 1].reason, borderNormals[i]);
                            if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals((chunkSize / chunks[id - 1].reason + 1) * (chunkSize / chunks[id - 1].reason + 1) - 1, borderNormals[i]);
                        }
                        if (i == ((chunkSize / reason + 1) * (chunkSize / reason + 1) - 1))
                        {
                            if (chunks[id + sqrtChunksPerFace + 1].isActive && reason >= chunks[id + sqrtChunksPerFace + 1].reason) chunks[id + sqrtChunksPerFace + 1].RebuildNormals(0, borderNormals[i]);
                            if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals((chunkSize / chunks[id + 1].reason + 1) * (chunkSize / chunks[id + 1].reason), borderNormals[i]);
                        }
                    }
                    else    //Chunks externos
                    {
                        //Normales de arriba
                        if (GetRelativeChunkId(id, face) >= (sqrtChunksPerFace * (sqrtChunksPerFace - 1)))    //Chunk de arriba
                        {
                            if (i >= ((chunkSize / reason + 1) * (chunkSize / reason)))    //Normales de arriba deben de ser de otra cara
                            {
                                if (chunks[GetChunkId(id, "up", face)].isActive && reason >= chunks[GetChunkId(id, "up", face)].reason) borderNormals[i] = chunks[GetChunkId(id, "up", face)].mesh.normals[GetStartingI(GetChunkId(id, "up", face), i - (GetNumVertices() - 1 - chunkSize / reason))];
                            }
                            //Esquinas de arriba
                            if(GetRelativeChunkId(id, face) == (sqrtChunksPerFace * (sqrtChunksPerFace - 1)))   //Chunk de arriba a la izquierda
                            {
                                if (i == ((chunkSize / reason + 1) * (chunkSize / reason)))  //Esquina arriba izquierda
                                {
                                    if (chunks[GetChunkId(id, "left", face)].isActive && reason >= chunks[GetChunkId(id, "left", face)].reason) chunks[GetChunkId(id, "left", face)].RebuildNormals((chunkSize / chunks[GetChunkId(id, "left", face)].reason + 1) * (chunkSize / chunks[GetChunkId(id, "left", face)].reason + 1) - 1, borderNormals[i]);
                                }
                                if (i == ((chunkSize / reason + 1) * (chunkSize / reason + 1) - 1))  //Esquina arriba derecha
                                {
                                    if (chunks[GetChunkId(id, "up", face) + 1].isActive && reason >= chunks[GetChunkId(id, "up", face) + 1].reason) chunks[GetChunkId(id, "up", face) + 1].RebuildNormals(0, borderNormals[i]);
                                    if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals((chunkSize / chunks[id + 1].reason + 1) * (chunkSize / chunks[id + 1].reason), borderNormals[i]);
                                }
                            }
                            else
                            {
                                if(GetRelativeChunkId(id, face) == (sqrtChunksPerFace * sqrtChunksPerFace - 1))    //Chunk de arriba a la derecha
                                {
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason + 1) - 1))  //Esquina arriba derecha
                                    {
                                        if (chunks[GetChunkId(id, "right", face)].isActive && reason >= chunks[GetChunkId(id, "right", face)].reason) chunks[GetChunkId(id, "right", face)].RebuildNormals((chunkSize / chunks[GetChunkId(id, "right", face)].reason + 1) * (chunkSize / chunks[GetChunkId(id, "right", face)].reason), borderNormals[i]);
                                    }
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason)))         //Esquina arriba izquierda
                                    {
                                        if (chunks[GetChunkId(id, "up", face) - 1].isActive && reason >= chunks[GetChunkId(id, "up", face) - 1].reason) chunks[GetChunkId(id, "up", face) - 1].RebuildNormals((chunkSize / chunks[GetChunkId(id, "up", face) - 1].reason), borderNormals[i]);
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals((chunkSize / chunks[id - 1].reason + 1) * (chunkSize / chunks[id - 1].reason + 1) - 1, borderNormals[i]);
                                    }
                                }
                                else                                                                           //Chunk de arriba sin hacer esquina con nada
                                {
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason + 1) - 1))  //Esquina arriba derecha
                                    {
                                        if (chunks[GetChunkId(id, "up", face) + 1].isActive && reason >= chunks[GetChunkId(id, "up", face) + 1].reason) chunks[GetChunkId(id, "up", face) + 1].RebuildNormals(0, borderNormals[i]);
                                        if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals((chunkSize / chunks[id + 1].reason + 1) * (chunkSize / chunks[id + 1].reason), borderNormals[i]);
                                    }
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason)))         //Esquina arriba izquierda
                                    {
                                        if (chunks[GetChunkId(id, "up", face) - 1].isActive && reason >= chunks[GetChunkId(id, "up", face) - 1].reason) chunks[GetChunkId(id, "up", face) - 1].RebuildNormals((chunkSize / chunks[GetChunkId(id, "up", face) - 1].reason), borderNormals[i]);
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals((chunkSize / chunks[id - 1].reason + 1) * (chunkSize / chunks[id - 1].reason + 1) - 1, borderNormals[i]);
                                    }
                                }
                            }
                        }
                        else            //No Chunk de arriba (abajo o en medio)
                        {
                            if (i >= ((chunkSize / reason + 1) * (chunkSize / reason)))    //Normales de arriba
                            {
                                if (chunks[id + sqrtChunksPerFace].isActive && reason >= chunks[id + sqrtChunksPerFace].reason) borderNormals[i] = chunks[id + sqrtChunksPerFace].mesh.normals[GetStartingI(id + sqrtChunksPerFace, i - (GetNumVertices() - 1 - chunkSize / reason))];
                            }
                            //Esquinas de arriba
                            if (GetRelativeChunkId(id, face) % sqrtChunksPerFace == 0)   //Chunk de la izquierda
                            {
                                if (i == ((chunkSize / reason + 1) * (chunkSize / reason)))  //Esquina arriba izquierda
                                {
                                    if (chunks[GetChunkId(id, "left", face)].isActive && reason >= chunks[GetChunkId(id, "left", face)].reason) chunks[GetChunkId(id, "left", face)].RebuildNormals((chunkSize / chunks[GetChunkId(id, "left", face)].reason + 1) * (chunkSize / chunks[GetChunkId(id, "left", face)].reason + 1) - 1, borderNormals[i]);
                                    if (chunks[GetChunkId(id, "left", face) + sqrtChunksPerFace].isActive && reason >= chunks[GetChunkId(id, "left", face) + sqrtChunksPerFace].reason) chunks[GetChunkId(id, "left", face) + sqrtChunksPerFace].RebuildNormals(chunkSize / chunks[GetChunkId(id, "left", face) + sqrtChunksPerFace].reason, borderNormals[i]);
                                }
                                if (i == ((chunkSize / reason + 1) * (chunkSize / reason + 1) - 1))  //Esquina arriba derecha
                                {
                                    if (chunks[id + sqrtChunksPerFace + 1].isActive && reason >= chunks[id + sqrtChunksPerFace + 1].reason) chunks[id + sqrtChunksPerFace + 1].RebuildNormals(0, borderNormals[i]);
                                    if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals((chunkSize / chunks[id + 1].reason + 1) * (chunkSize / chunks[id + 1].reason), borderNormals[i]);
                                }
                            }
                            else
                            {
                                if ((GetRelativeChunkId(id, face) + 1) % sqrtChunksPerFace == 0)    //Chunk de la derecha
                                {
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason + 1) - 1))  //Esquina arriba derecha
                                    {
                                        if (chunks[GetChunkId(id, "right", face)].isActive && reason >= chunks[GetChunkId(id, "right", face)].reason) chunks[GetChunkId(id, "right", face)].RebuildNormals((chunkSize / chunks[GetChunkId(id, "right", face)].reason + 1) * (chunkSize / chunks[GetChunkId(id, "right", face)].reason), borderNormals[i]);
                                        if (chunks[GetChunkId(id, "right", face) + sqrtChunksPerFace].isActive && reason >= chunks[GetChunkId(id, "right", face) + sqrtChunksPerFace].reason) chunks[GetChunkId(id, "right", face) + sqrtChunksPerFace].RebuildNormals(0, borderNormals[i]);
                                    }
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason)))         //Esquina arriba izquierda
                                    {
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals((chunkSize / chunks[id - 1].reason + 1) * (chunkSize / chunks[id - 1].reason + 1) - 1, borderNormals[i]);
                                        if (chunks[id + sqrtChunksPerFace - 1].isActive && reason >= chunks[id + sqrtChunksPerFace - 1].reason) chunks[id + sqrtChunksPerFace - 1].RebuildNormals(chunkSize / chunks[id + sqrtChunksPerFace - 1].reason, borderNormals[i]);
                                    }
                                }
                                else                                                                //Chunk de abajo sin hacer esquina con nada
                                {
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason + 1) - 1))  //Esquina arriba derecha
                                    {
                                        if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals((chunkSize / chunks[id + 1].reason + 1) * (chunkSize / chunks[id + 1].reason), borderNormals[i]);
                                        if (chunks[id + sqrtChunksPerFace + 1].isActive && reason >= chunks[id + sqrtChunksPerFace + 1].reason) chunks[id + sqrtChunksPerFace + 1].RebuildNormals(0, borderNormals[i]);
                                    }
                                    if (i == ((chunkSize / reason + 1) * (chunkSize / reason)))         //Esquina arriba izquierda
                                    {
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals((chunkSize / chunks[id - 1].reason + 1) * (chunkSize / chunks[id - 1].reason + 1) - 1, borderNormals[i]);
                                        if (chunks[id + sqrtChunksPerFace - 1].isActive && reason >= chunks[id + sqrtChunksPerFace - 1].reason) chunks[id + sqrtChunksPerFace - 1].RebuildNormals(chunkSize / chunks[id + sqrtChunksPerFace - 1].reason, borderNormals[i]);
                                    }
                                }
                            }
                        }
                        //Normales de abajo
                        if(GetRelativeChunkId(id, face) < sqrtChunksPerFace)      //Chunk de abajo
                        {
                            if (i < (chunkSize / reason + 1))    //Normales de abajo deben de ser de otra cara
                            {
                                if (chunks[GetChunkId(id, "down", face)].isActive && reason >= chunks[GetChunkId(id, "down", face)].reason) borderNormals[i] = chunks[GetChunkId(id, "down", face)].mesh.normals[chunkSize / chunks[GetChunkId(id, "down", face)].reason - GetStartingI(GetChunkId(id, "down", face), i)];
                            }
                            //Esquinas de abajo
                            if (GetRelativeChunkId(id, face) == 0)                        //Chunk de abajo a la izquierda
                            {
                                if(i==0)                                    //Esquina abajo izquierda
                                {
                                    if (chunks[GetChunkId(id, "left", face)].isActive && reason >= chunks[GetChunkId(id, "left", face)].reason) chunks[GetChunkId(id, "left", face)].RebuildNormals(chunkSize / chunks[GetChunkId(id, "left", face)].reason, borderNormals[i]);
                                }
                                if(i==chunkSize/reason)                     //Esquina abajo derecha
                                {
                                    if (chunks[GetChunkId(id, "down", face) + 1].isActive && reason >= chunks[GetChunkId(id, "down", face) + 1].reason) chunks[GetChunkId(id, "down", face) + 1].RebuildNormals(chunkSize / chunks[GetChunkId(id, "down", face)].reason, borderNormals[i]);
                                    if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals(0, borderNormals[i]);
                                }
                            }
                            else
                            {
                                if(GetRelativeChunkId(id, face) == (sqrtChunksPerFace - 1))       //Chunk de abajo a la derecha
                                {
                                    if (i == 0)                                    //Esquina abajo izquierda
                                    {
                                        if (chunks[GetChunkId(id, "down", face) - 1].isActive && reason >= chunks[GetChunkId(id, "down", face) - 1].reason) chunks[GetChunkId(id, "down", face) - 1].RebuildNormals(0, borderNormals[i]);
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals(chunkSize / chunks[id - 1].reason, borderNormals[i]);
                                    }
                                    if (i == chunkSize / reason)                     //Esquina abajo derecha
                                    {
                                        if (chunks[GetChunkId(id, "right", face)].isActive && reason >= chunks[GetChunkId(id, "right", face)].reason) chunks[GetChunkId(id, "right", face)].RebuildNormals(0, borderNormals[i]);
                                    }
                                }
                                else                                                    //Chunk de abajo sin hacer esquina con nada
                                {
                                    if (i == 0)                                    //Esquina abajo izquierda
                                    {
                                        if (chunks[GetChunkId(id, "down", face) - 1].isActive && reason >= chunks[GetChunkId(id, "down", face) - 1].reason) chunks[GetChunkId(id, "down", face) - 1].RebuildNormals(0, borderNormals[i]);
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals(chunkSize / chunks[id - 1].reason, borderNormals[i]);
                                    }
                                    if (i == chunkSize / reason)                     //Esquina abajo derecha
                                    {
                                        if (chunks[GetChunkId(id, "down", face) + 1].isActive && reason >= chunks[GetChunkId(id, "down", face) + 1].reason) chunks[GetChunkId(id, "down", face) + 1].RebuildNormals(chunkSize / chunks[GetChunkId(id, "down", face)].reason, borderNormals[i]);
                                        if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals(0, borderNormals[i]);
                                    }
                                }
                            }
                        }
                        else                                                //No Chunk de abajo (arriba o en medio)
                        {
                            if (i < (chunkSize / reason + 1))    //Normales de abajo
                            {
                                if (chunks[id - sqrtChunksPerFace].isActive && reason >= chunks[id - sqrtChunksPerFace].reason) borderNormals[i] = chunks[id - sqrtChunksPerFace].mesh.normals[GetStartingI(id - sqrtChunksPerFace, i) + chunks[id - sqrtChunksPerFace].GetNumVertices() - 1 - (chunkSize / chunks[id - sqrtChunksPerFace].reason)];
                            }
                            //Esquinas de abajo
                            if (GetRelativeChunkId(id, face) % sqrtChunksPerFace == 0)   //Chunk de la izquierda
                            {
                                if (i == 0)  //Esquina abajo izquierda
                                {
                                    if (chunks[GetChunkId(id, "left", face)].isActive && reason >= chunks[GetChunkId(id, "left", face)].reason) chunks[GetChunkId(id, "left", face)].RebuildNormals(chunkSize / chunks[GetChunkId(id, "left", face)].reason, borderNormals[i]);
                                    if (chunks[GetChunkId(id, "left", face) - sqrtChunksPerFace].isActive && reason >= chunks[GetChunkId(id, "left", face) - sqrtChunksPerFace].reason) chunks[GetChunkId(id, "left", face) - sqrtChunksPerFace].RebuildNormals((chunkSize / chunks[GetChunkId(id, "left", face) - sqrtChunksPerFace].reason + 1) * (chunkSize / chunks[GetChunkId(id, "left", face) - sqrtChunksPerFace].reason + 1) - 1, borderNormals[i]);
                                }
                                if (i == (chunkSize / reason))  //Esquina abajo derecha
                                {
                                    if (chunks[id - sqrtChunksPerFace + 1].isActive && reason >= chunks[id - sqrtChunksPerFace + 1].reason) chunks[id - sqrtChunksPerFace + 1].RebuildNormals((chunkSize / chunks[id - sqrtChunksPerFace + 1].reason + 1) * (chunkSize / chunks[id - sqrtChunksPerFace + 1].reason), borderNormals[i]);
                                    if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals(0, borderNormals[i]);
                                }
                            }
                            else
                            {
                                if ((GetRelativeChunkId(id, face) + 1) % sqrtChunksPerFace == 0)    //Chunk de la derecha
                                {
                                    if (i == (chunkSize / reason))  //Esquina abajo derecha
                                    {
                                        if (chunks[GetChunkId(id, "right", face)].isActive && reason >= chunks[GetChunkId(id, "right", face)].reason) chunks[GetChunkId(id, "right", face)].RebuildNormals(0, borderNormals[i]);
                                        if (chunks[GetChunkId(id, "right", face) - sqrtChunksPerFace].isActive && reason >= chunks[GetChunkId(id, "right", face) - sqrtChunksPerFace].reason) chunks[GetChunkId(id, "right", face) - sqrtChunksPerFace].RebuildNormals((chunkSize / chunks[GetChunkId(id, "right", face) - sqrtChunksPerFace].reason + 1) * (chunkSize / chunks[GetChunkId(id, "right", face) - sqrtChunksPerFace].reason), borderNormals[i]);
                                    }
                                    if (i == 0)         //Esquina abajo izquierda
                                    {
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals(chunkSize / chunks[id - 1].reason, borderNormals[i]);
                                        if (chunks[id - sqrtChunksPerFace - 1].isActive && reason >= chunks[id - sqrtChunksPerFace - 1].reason) chunks[id - sqrtChunksPerFace - 1].RebuildNormals((chunkSize / chunks[id - sqrtChunksPerFace - 1].reason + 1) * (chunkSize / chunks[id - sqrtChunksPerFace - 1].reason + 1) - 1, borderNormals[i]);
                                    }
                                }
                                else                                                                //Chunk de arriba sin hacer esquina con nada
                                {
                                    if (i == (chunkSize / reason))  //Esquina abajo derecha
                                    {
                                        if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) chunks[id + 1].RebuildNormals(0, borderNormals[i]);
                                        if (chunks[id - sqrtChunksPerFace + 1].isActive && reason >= chunks[id - sqrtChunksPerFace + 1].reason) chunks[id - sqrtChunksPerFace + 1].RebuildNormals((chunkSize / chunks[id - sqrtChunksPerFace + 1].reason + 1) * (chunkSize / chunks[id - sqrtChunksPerFace + 1].reason), borderNormals[i]);
                                    }
                                    if (i == 0)         //Esquina abajo izquierda
                                    {
                                        if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) chunks[id - 1].RebuildNormals(chunkSize / chunks[id - 1].reason, borderNormals[i]);
                                        if (chunks[id - sqrtChunksPerFace - 1].isActive && reason >= chunks[id - sqrtChunksPerFace - 1].reason) chunks[id - sqrtChunksPerFace - 1].RebuildNormals((chunkSize / chunks[id - sqrtChunksPerFace - 1].reason + 1) * (chunkSize / chunks[id - sqrtChunksPerFace - 1].reason + 1) - 1, borderNormals[i]);
                                    }
                                }
                            }
                        }
                        //Normales de la izquierda
                        if(GetRelativeChunkId(id, face) % sqrtChunksPerFace == 0)
                        {
                            if (i % (chunkSize / reason + 1) == 0)    //Normales de la izquierda deben de ser de otra cara
                            {
                                if (chunks[GetChunkId(id, "left", face)].isActive && reason >= chunks[GetChunkId(id, "left", face)].reason) borderNormals[i] = chunks[GetChunkId(id, "left", face)].mesh.normals[GetStartingI(GetChunkId(id, "left", face), i) + (chunkSize / chunks[GetChunkId(id, "left", face)].reason)];
                            }
                        }
                        else
                        {
                            if (i % (chunkSize / reason + 1) == 0)    //Normales de la izquierda
                            {
                                if (chunks[id - 1].isActive && reason >= chunks[id - 1].reason) borderNormals[i] = chunks[id - 1].mesh.normals[GetStartingI(id - 1, i) + (chunkSize / chunks[id - 1].reason)];
                            }
                        }
                        //Normales de la derecha
                        if((GetRelativeChunkId(id, face) + 1) % sqrtChunksPerFace == 0)
                        {
                            if ((i + 1) % (chunkSize / reason + 1) == 0)    //Normales de la derecha deben de ser de otra cara
                            {
                                if (chunks[GetChunkId(id, "right", face)].isActive && reason >= chunks[GetChunkId(id, "right", face)].reason) borderNormals[i] = chunks[GetChunkId(id, "right", face)].mesh.normals[GetStartingI(GetChunkId(id, "right", face), i) - (chunkSize / chunks[GetChunkId(id, "right", face)].reason)];
                            }
                        }
                        else
                        {
                            if ((i + 1) % (chunkSize / reason + 1) == 0)    //Normales de la derecha
                            {
                                if (chunks[id + 1].isActive && reason >= chunks[id + 1].reason) borderNormals[i] = chunks[id + 1].mesh.normals[GetStartingI(id + 1, i - (chunkSize / reason))];
                            }
                        }
                    }
                }
            }
            mesh.normals = borderNormals;
        }*/

        public int GetStartingI(int chunkId, int i, bool case_left=false)
        {
            if((chunkId == id - 1) || (chunkId == id + 1))
            {
                case_left = true;
            }
            int starting_i;
            int relation;       //Relationship between reasons
            if (chunks[chunkId].reason < reason)
            {
                relation = reason / chunks[chunkId].reason;
                if (case_left)
                {
                    int fila = i / (chunkSize / reason + 1);
                    if (fila == 0) { return 0; }
                    int max_fila = chunkSize / reason;
                    int max_fila_objetivo = chunkSize / chunks[chunkId].reason;
                    double relacion_filas = max_fila / fila;
                    int fila_objetivo = (int)Math.Round((double)max_fila_objetivo / relacion_filas);

                    starting_i = (chunkSize / chunks[chunkId].reason + 1) * fila_objetivo;
                }
                else
                {
                    starting_i = i * relation;
                }
            }
            else
            {
                relation = chunks[chunkId].reason / reason;
                if (chunks[chunkId].reason == reason)
                {
                    starting_i = i;
                }
                else
                {
                    if(case_left)
                    {
                        int fila = i / (chunkSize / reason + 1);
                        if (fila == 0) { return 0; }
                        int max_fila = chunkSize / reason;
                        int max_fila_objetivo = chunkSize / chunks[chunkId].reason;
                        double relacion_filas = max_fila / fila;
                        int fila_objetivo = (int)Math.Round((double)max_fila_objetivo / relacion_filas);

                        starting_i = (chunkSize / chunks[chunkId].reason + 1) * fila_objetivo;
                    }
                    else
                    {
                        starting_i = (int)Math.Round((double)(i / relation));
                    }
                }
            }
            return starting_i;
        }

        public void RebuildNormals(int index, Vector3 content)
        {
            Vector3[] normals = mesh.normals;
            normals[index] = content;
            mesh.normals = normals;
        }

        public void GenerateNormals()
        {
            CalculateNormals();
            //AdjustBorderNormals();
        }

        private void GenerateNormals2()
        {
            CalculateNormals();
            switch(face)
            {
                case "xy": StaticTerrainFunctions.AdjustBorderNormalsXY(this, sqrtChunksPerFace, chunks); break;
                //case "xyz": StaticTerrainFunctions.AdjustBorderNormalsXYZ(this, sqrtChunksPerFace, chunks); break;
                //case "zy": StaticTerrainFunctions.AdjustBorderNormalsZY(this, sqrtChunksPerFace, chunks); break;
                //case "zyx": StaticTerrainFunctions.AdjustBorderNormalsZYX(this, sqrtChunksPerFace, chunks); break;
                //case "xz": StaticTerrainFunctions.AdjustBorderNormalsXZ(this, sqrtChunksPerFace, chunks); break;
                case "xzy": StaticTerrainFunctions.AdjustBorderNormalsXZY(this, sqrtChunksPerFace, chunks); break;
            }
            /*if(face=="xyz")
            {
                if(id==27)
                {
                    int numVertices = GetNumVertices();
                    Vector3[] borderNormals = new Vector3[numVertices];
                    borderNormals = mesh.normals;
                    borderNormals[0] = new Vector3(0, 0, 0);
                    borderNormals[1] = new Vector3(0, 0, 0);
                    borderNormals[2] = new Vector3(0, 0, 0);
                    mesh.normals = borderNormals;
                }
            }*/
            //AdjustBorderNormals2();
        }
    }
}

[System.Serializable]
public struct TerrainType
{
    public string name;
    public float height;
    public Color colour;
}
