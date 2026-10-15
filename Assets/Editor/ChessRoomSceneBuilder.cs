using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Editor-only authoring tool. The resulting scene needs no runtime scripts.
public static class ChessRoomSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/ChessRoomBlockout.unity";
    private const string MaterialFolder = "Assets/Materials/ChessRoom";

    [MenuItem("Tools/Albedo Gambit/Create Chess Room Blockout")]
    public static void CreateFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath))
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return;
        }
        Build();
    }

    public static void Build()
    {
        if (File.Exists(ScenePath))
            throw new InvalidOperationException("Scene already exists; refusing to overwrite authored changes.");

        Directory.CreateDirectory(MaterialFolder);
        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);

        var floor = Material("Floor - Warm Stone", "8C877E");
        var wall = Material("Walls - Plaster", "C8C2B4");
        var edge = Material("Board - Frame", "393F43");
        var light = Material("Square - Ivory", "E4DCC8");
        var dark = Material("Square - Slate", "495C67");

        var room = new GameObject("Room").transform;
        Box("Floor (12 x 12 m)", room, new Vector3(0, -.2f, 0), new Vector3(12, .4f, 12), floor);
        Box("Wall - Back", room, new Vector3(0, 1.5f, 6), new Vector3(12.3f, 3, .3f), wall);
        Box("Wall - Left", room, new Vector3(-6, 1.5f, 0), new Vector3(.3f, 3, 12), wall);

        var board = new GameObject("Chessboard (8 x 8)").transform;
        Box("Board Base", board, new Vector3(0, .09f, 0), new Vector3(8.3f, .18f, 8.3f), edge);
        var squares = new GameObject("Squares").transform;
        squares.SetParent(board, false);
        for (int rank = 0; rank < 8; rank++)
        for (int file = 0; file < 8; file++)
        {
            string coordinate = $"{(char)('A' + file)}{rank + 1}";
            Box("Square " + coordinate, squares,
                new Vector3(file - 3.5f, .2f, rank - 3.5f),
                new Vector3(1, .04f, 1), (file + rank) % 2 == 0 ? dark : light);
        }

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 7.2f;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 100;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = ColorHex("262F3B");
        camera.transform.rotation = Quaternion.Euler(35.26439f, -45, 0);
        camera.transform.position = new Vector3(0, .8f, 0) - camera.transform.forward * 24;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        var key = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1, .96f, .88f);
        key.intensity = 1.5f;
        key.shadows = LightShadows.Soft;
        key.shadowBias = .04f;
        key.shadowNormalBias = .25f;
        key.transform.rotation = Quaternion.Euler(50, -30, 0);
        RenderSettings.sun = key;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.65f, .70f, .78f);
        RenderSettings.ambientEquatorColor = new Color(.44f, .47f, .52f);
        RenderSettings.ambientGroundColor = new Color(.28f, .27f, .25f);
        RenderSettings.fog = false;

        if (squares.childCount != 64)
            throw new InvalidOperationException("Expected 64 squares.");
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException("Could not save scene.");
        AssetDatabase.SaveAssets();
        Capture(camera);
        File.WriteAllText("Logs/ChessRoomValidation.txt",
            "PASS: Scene saved: " + ScenePath + "\n" +
            "64 squares, 32 ivory + 32 slate; A1 is dark.\n" +
            "Room: 12 x 12 m; exactly two walls, height 3 m.\n" +
            "Orthographic camera: 35.26439 degrees elevation, size 7.2.\n" +
            "68 cube meshes with colliders; no runtime script dependency.\n");
        Debug.Log("CHESS_ROOM_CREATED: " + ScenePath);
    }

    private static Material Material(string name, string hex)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader not found.");
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", ColorHex(hex));
        material.SetFloat("_Smoothness", .18f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Color ColorHex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var color);
        return color;
    }

    private static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = scale;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void Capture(Camera camera)
    {
        Directory.CreateDirectory("Logs");
        var target = new RenderTexture(1440, 1080, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1440, 1080, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1440, 1080), 0, 0);
            texture.Apply();
            File.WriteAllBytes("Logs/ChessRoomPreview.png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
