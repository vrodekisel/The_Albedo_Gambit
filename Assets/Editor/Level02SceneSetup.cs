using System;
using System.Collections.Generic;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds a second scene from the same gameplay prefabs and the exported Blender art.
public static class Level02SceneSetup
{
    private const string Root = "Assets/Art/Level02";
    private const string ScenePath = "Assets/Scenes/Level02Prototype.unity";

    [MenuItem("Tools/Albedo/Clean Level 02 Collision Proxies")]
    public static void RemoveRedundantProxies()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var proxies = GameObject.Find("Level 02 Collision Proxies");
        if (!proxies) throw new InvalidOperationException("Level 02 collision root is missing.");
        CleanProxies(proxies, false);
        AlignProxies(proxies, GameObject.Find("Level 02 Art"));
        EditorSceneManager.SaveScene(scene);
        Debug.Log("LEVEL02_REDUNDANT_PROXIES_REMOVED");
    }

    private static void CleanProxies(GameObject root, bool createColliders)
    {
        // The old Blender contour covers a smaller zone. The full Ground mesh
        // has its own collider and perimeter; house bodies already block roofs.
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name.StartsWith("COL_BOUNDARY") || filter.name == "COL_WALKABLE_GROUND" ||
                filter.name.EndsWith("_ROOF"))
            {
                UnityEngine.Object.DestroyImmediate(filter.gameObject);
                continue;
            }
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer) renderer.enabled = false;
            if (!createColliders || filter.TryGetComponent<BoxCollider>(out _)) continue;
            var box = filter.gameObject.AddComponent<BoxCollider>();
            box.center = filter.sharedMesh.bounds.center;
            box.size = filter.sharedMesh.bounds.size;
        }
    }

    private static void AlignProxies(GameObject proxies, GameObject visual)
    {
        if (!visual) throw new InvalidOperationException("Level 02 art is missing.");
        var art = new Dictionary<string, Transform>();
        foreach (var item in visual.GetComponentsInChildren<Transform>(true)) art[item.name] = item;
        var fence = art["Garden_Fence"].GetComponent<Renderer>().bounds;
        int aligned = 0;
        foreach (var box in proxies.GetComponentsInChildren<BoxCollider>(true))
        {
            string name = box.name;
            Vector3 position;
            if (name == "COL_Garden_Back_Fence")
                position = new Vector3(fence.center.x, 0, fence.max.z - box.bounds.extents.z);
            else if (name == "COL_Garden_Right_Fence")
                position = new Vector3(fence.max.x - box.bounds.extents.x, 0, fence.center.z);
            else
            {
                string visualName = name.Substring(4);
                if (visualName.EndsWith("_TRUNK")) visualName = visualName.Substring(0, visualName.Length - 6);
                else if (visualName.EndsWith("_BODY")) visualName = visualName.Substring(0, visualName.Length - 5);
                else if (visualName.StartsWith("SHRUB_") && int.TryParse(visualName.Substring(6), out int shrubIndex))
                    visualName = "Shrub_" + shrubIndex.ToString("00");
                if (!art.TryGetValue(visualName, out var target))
                    throw new InvalidOperationException("No visible model for collision proxy " + name);
                position = target.position;
            }
            position.y += box.bounds.extents.y;
            box.transform.position = position;
            aligned++;
        }
        Debug.Log("LEVEL02_PROXIES_ALIGNED " + aligned);
    }

    [Serializable] private sealed class MaterialCatalog { public MaterialEntry[] materials; }
    [Serializable] private sealed class MaterialEntry { public string name; public float[] color; public string texture; public float roughness; public float metallic; }
    [Serializable] private sealed class Layout
    {
        public Vector3 cameraPosition, referencePosition, forward, up;
        public float viewWidth, referenceWidth, aspect;
        public Edge[] edges;
    }
    [Serializable] private sealed class Edge { public Vector3 a, b; }

    [MenuItem("Tools/Albedo/Build Level 02 Prototype")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        var layout = JsonUtility.FromJson<Layout>(AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/layout.json").text);
        var importer = (ModelImporter)AssetImporter.GetAtPath(Root + "/Models/Level02_Visual.fbx");
        if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        var source = EditorSceneManager.OpenScene(GameplayTemplateSetup.ScenePath);
        EditorSceneManager.SaveScene(source, ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath);
        UnityEngine.Object.DestroyImmediate(GameObject.Find("Room"));
        UnityEngine.Object.DestroyImmediate(GameObject.Find("Obstacle Playground"));

        var visualAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Level02_Visual.fbx");
        var proxyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Level02_CollisionProxies.fbx");
        if (visualAsset == null || proxyAsset == null) throw new InvalidOperationException("Import both Level 02 FBX models first.");
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualAsset);
        visual.name = "Level 02 Art";
        var materials = BuildMaterials();
        int mappedMaterials = 0;
        var missingMaterials = new HashSet<string>();
        foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            var assigned = renderer.sharedMaterials;
            for (int i = 0; i < assigned.Length; i++)
                if (assigned[i] != null)
                {
                    if (materials.TryGetValue(assigned[i].name, out var replacement))
                    { assigned[i] = replacement; mappedMaterials++; }
                    else missingMaterials.Add(assigned[i].name);
                }
            renderer.sharedMaterials = assigned;
        }
        Debug.Log("LEVEL02_MATERIALS mapped=" + mappedMaterials + " unmatched=" + string.Join(", ", missingMaterials));
        MeshCollider ground = null;
        foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            if (filter.name == "Ground")
            {
                ground = filter.gameObject.AddComponent<MeshCollider>();
                ground.sharedMesh = filter.sharedMesh;
            }
            else if (filter.name.StartsWith("Scale_Guide")) filter.gameObject.SetActive(false);

        var proxies = (GameObject)PrefabUtility.InstantiatePrefab(proxyAsset);
        proxies.name = "Level 02 Collision Proxies";
        CleanProxies(proxies, true);
        AlignProxies(proxies, visual);

        var navigation = new GameObject("Level 02 Navigation").AddComponent<PrototypeNavigation>();
        navigation.ConfigureGround(ground);
        var perimeter = new GameObject("Floor Perimeter - Invisible Walls");
        for (int i = 0; i < layout.edges.Length; i++)
        {
            var edge = layout.edges[i];
            var delta = edge.b - edge.a;
            if (delta.sqrMagnitude < .0001f) continue;
            var wall = new GameObject("Floor Edge " + i);
            wall.transform.SetParent(perimeter.transform);
            wall.transform.position = (edge.a + edge.b) * .5f + Vector3.up * 2;
            wall.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            // Overlap endpoints and extend below floor to prevent corner/jump escapes.
            wall.AddComponent<BoxCollider>().size = new Vector3(.22f, 5, delta.magnitude + .22f);
        }

        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        if (board == null || party == null) throw new InvalidOperationException("The gameplay prefabs are missing from the template.");
        board.transform.position = new Vector3(5, .04f, 14);
        board.transform.rotation = Quaternion.Euler(0, 180, 0);
        party.transform.position += new Vector3(0, .05f, 17) - party.Members[0].transform.position;

        var camera = Camera.main;
        camera.transform.position = layout.cameraPosition;
        camera.transform.rotation = Quaternion.LookRotation(layout.forward, layout.up);
        camera.orthographic = true;
        camera.orthographicSize = layout.viewWidth / layout.aspect * .5f;
        camera.farClipPlane = 400;
        var follow = camera.gameObject.AddComponent<BoundedFollowCamera>();
        follow.Configure(party, board, layout.referencePosition,
            new Vector2(layout.referenceWidth * .5f - .35f, layout.referenceWidth / layout.aspect * .5f - .35f));
        follow.Follow(.02f, true);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.type == LightType.Directional) { light.color = Color.white; light.intensity = 1; }
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("LEVEL02_SCENE_BUILT " + ScenePath);
    }

    private static Dictionary<string, Material> BuildMaterials()
    {
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/materials.json");
        if (json == null) throw new InvalidOperationException("Missing Level 02 material manifest.");
        var catalog = JsonUtility.FromJson<MaterialCatalog>(json.text);
        var lookup = new Dictionary<string, Material>();
        string folder = Root + "/Materials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Materials");
        foreach (var entry in catalog.materials)
        {
            string safe = string.Join("_", entry.name.Split(System.IO.Path.GetInvalidFileNameChars()))
                .Replace("/", "_").Replace("|", "_");
            string path = folder + "/" + safe + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(entry.name.StartsWith("Backdrop |")
                    ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                material = new Material(shader) { name = entry.name };
                AssetDatabase.CreateAsset(material, path);
            }
            var color = entry.color != null && entry.color.Length >= 3
                ? new Color(entry.color[0], entry.color[1], entry.color[2], 1).gamma : Color.white;
            if (entry.name.StartsWith("Backdrop |")) entry.texture = "Backdrop_2D.png";
            if (!string.IsNullOrEmpty(entry.texture))
            {
                var image = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + entry.texture);
                if (image != null) { material.SetTexture("_BaseMap", image); color = Color.white; }
            }
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1 - entry.roughness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", entry.metallic);
            EditorUtility.SetDirty(material);
            lookup[entry.name] = material;
            lookup[entry.name.Replace("|", "_")] = material;
        }
        return lookup;
    }
}
