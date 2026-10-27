using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GameplayTemplateSetup
{
    public const string ScenePath = "Assets/Scenes/GameplayTemplate.unity";

    [MenuItem("Tools/Albedo/Build Gameplay Template")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        EditorSceneManager.SaveScene(scene, ScenePath);
        scene = EditorSceneManager.OpenScene(ScenePath);
        var obstacles = GameObject.Find("Obstacle Playground");
        if (obstacles != null) Object.DestroyImmediate(obstacles);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("GAMEPLAY_TEMPLATE_BUILT " + ScenePath);
    }
}
