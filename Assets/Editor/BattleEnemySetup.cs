using System.IO;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BattleEnemySetup
{
    public static void Build()
    {
        const string materialPath = "Assets/Materials/Party/EnemyBlack.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "Enemy Black";
            material.SetColor("_BaseColor", new Color(.035f, .04f, .05f));
            material.SetFloat("_Smoothness", .3f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var root = new GameObject("Enemy Capsule");
        var collider = root.AddComponent<CapsuleCollider>(); collider.center = new Vector3(0, .8f, 0); collider.height = 1.6f; collider.radius = .3f;
        var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeRotation;
        root.AddComponent<BattleEnemyView>();
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule); visual.name = "Capsule Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>()); visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0, .8f, 0); visual.transform.localScale = new Vector3(.6f, .8f, .6f);
        visual.GetComponent<Renderer>().sharedMaterial = material;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Prototype/EnemyCapsule.prefab"); Object.DestroyImmediate(root);
        const string partyPath = "Assets/Prefabs/Prototype/PartyChain.prefab";
        var partyRoot = PrefabUtility.LoadPrefabContents(partyPath);
        Reorder(partyRoot.GetComponent<PartyChainController>());
        PrefabUtility.SaveAsPrefabAsset(partyRoot, partyPath); PrefabUtility.UnloadPrefabContents(partyRoot);
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        var party = Object.FindFirstObjectByType<PartyChainController>(); Reorder(party);
        var spawner = party.GetComponent<BattleEnemySpawner>();
        if (spawner == null) spawner = party.gameObject.AddComponent<BattleEnemySpawner>();
        spawner.Configure(prefab.GetComponent<BattleEnemyView>());
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        Debug.Log("ENEMY_SPAWN_BUILT");
    }
    private static void Reorder(PartyChainController party)
    {
        var a = party.Members[0].transform; var b = party.Members[1].transform; var c = party.Members[2].transform;
        if (Vector3.Distance(a.position, b.position) >= Vector3.Distance(a.position, c.position)) return;
        var pos = b.localPosition; b.localPosition = c.localPosition; c.localPosition = pos;
        PrefabUtility.RecordPrefabInstancePropertyModifications(b); PrefabUtility.RecordPrefabInstancePropertyModifications(c);
    }
}
