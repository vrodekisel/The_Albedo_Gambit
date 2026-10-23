using System.IO;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Explicit editor command. Does not run on import or rebuild the room.
public static class BattleHudSetup
{
    private static Font font;
    private static Sprite round;
    private static readonly Color Ink = Hex("454153"), Paper = Hex("f3eedf");
    private const string Art = "Assets/Art/UI/BattleFigma/";
    [MenuItem("Tools/Albedo Gambit/Build Battle HUD")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Prefabs/UI");
        MakeRoundSprite();
        foreach (string path in Directory.GetFiles(Art, "*.png"))
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/UI/Fonts/EduNSWACTFoundation.ttf");
        round = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/RoundedPanel.png");
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        var old = Object.FindFirstObjectByType<BattleHud>(FindObjectsInactive.Include);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Battle HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(BattleHud));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1440); scaler.matchWidthOrHeight = .5f;
        var hud = root.GetComponent<BattleHud>();
        hud.content = new GameObject("Battle Content", typeof(RectTransform)); hud.content.transform.SetParent(root.transform, false); Stretch((RectTransform)hud.content.transform);
        var content = hud.content.transform;
        var selectors = Panel(content, "Party", 48, 48, 716, 175);
        hud.heroes = new BattleHud.HeroArt[4]; hud.heroButtons = new Button[4];
        string[] names = { "Eric", "Mia", "Joseph", "Evelyn" };
        string[] colors = { "ced0e7", "e7b9ad", "cfddb9", "b4d6e2" };
        string[] avatarColors = { "cbcfea", "e5b0a8", "bac9ae", "b1d4e5" };
        string[][] abilities = { new[] { "No ability", "No ability", "Fusion" }, new[] { "Fireball", "Flame Strike", "Fusion" }, new[] { "Heal", "Mana Orb", "Fusion" }, new[] { "Freeze", "Ice Shard", "Fusion" } };
        for (int i = 0; i < 4; i++)
        {
            var data = new BattleHud.HeroArt { name = names[i], avatar = Load("Select" + names[i]), portrait = Load(names[i] + "Portrait"), color = Hex(colors[i]), abilities = abilities[i], icons = new Sprite[3] };
            for (int k = 0; k < 3; k++) data.icons[k] = Load(names[i] + "Ability" + (i == 0 && k == 1 ? 0 : k));
            data.portraitCrop = i == 3 ? new Vector4(1.2375f, 1.2971f, -.1375f, -.0229f) : new Vector4(1.0803f, 1.2328f, i == 0 ? -.0819f : -.0414f, i == 0 ? -.0458f : i == 2 ? -.0169f : .0001f);
            hud.heroes[i] = data;
            var avatar = Panel(selectors.transform, names[i], 24 + i * 157, 24, 127, 127, Hex(avatarColors[i]), false);
            avatar.pixelsPerUnitMultiplier = 26f / 20;
            avatar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var image = Image(avatar.transform, "Avatar", 0, 0, 127, 127, data.avatar, Color.white); Stretch(image.rectTransform); image.raycastTarget = false;
            hud.heroButtons[i] = Button(avatar);
        }
        var card = Panel(content, "Hero Card", 48, 48, 430, 628); Anchor(card.rectTransform, new Vector2(0, 0), new Vector2(48, 676));
        var frame = Panel(card.transform, "Portrait Frame", 24, 24, 382, 370, Color.white, false); frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        frame.pixelsPerUnitMultiplier = 26f / 18;
        hud.portraitBackground = frame; hud.portrait = Image(frame.transform, "Portrait", 0, 0, 382, 370, null, Color.white); hud.portrait.raycastTarget = false;
        hud.heroName = Label(card.transform, "Name", 24, 404, 382, 50, 40, "Eric");
        hud.healthLabel = Label(card.transform, "Health", 24, 464, 382, 33, 26, "Health");
        Panel(card.transform, "Health Track", 24, 507, 382, 22, Hex("d9ddce"), false).pixelsPerUnitMultiplier = 2.6f;
        hud.healthFill = Panel(card.transform, "Health Value", 24, 507, 382, 22, Hex("70a689"), false);
        hud.healthFill.pixelsPerUnitMultiplier = 2.6f;
        hud.manaLabel = Label(card.transform, "Mana", 24, 539, 382, 33, 26, "Ultimate Mana   — / 3");
        for (int i = 0; i < 3; i++) Panel(card.transform, "Mana Unavailable " + i, 24 + i * 130, 582, 122, 22, Hex("cfccd8"), false).pixelsPerUnitMultiplier = 2.6f;
        var skills = Panel(content, "Abilities", 528, 54, 487, 298); Anchor(skills.rectTransform, Vector2.zero, new Vector2(528, 352));
        Label(skills.transform, "Title", 24, 18, 430, 38, 30, "Abilities");
        hud.abilityButtons = new Button[3]; hud.abilityIcons = new Image[3]; hud.abilityBackgrounds = new Image[3]; hud.abilityLabels = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            float x = 24 + i * 155.5f;
            var slot = Panel(skills.transform, "Ability " + i, x, 68, 128, 128, Color.white, false);
            slot.pixelsPerUnitMultiplier = 26f / 18;
            hud.abilityBackgrounds[i] = slot; hud.abilityButtons[i] = Button(slot);
            hud.abilityIcons[i] = Image(slot.transform, "Icon", 0, 0, 128, 128, null, Ink); hud.abilityIcons[i].preserveAspect = true; hud.abilityIcons[i].raycastTarget = false;
            hud.abilityLabels[i] = Label(skills.transform, "Ability Label " + i, x - 10, 204, 148, 36, 25, ""); hud.abilityLabels[i].alignment = TextAnchor.MiddleCenter;
        }
        hud.hint = Label(skills.transform, "Availability", 24, 248, 439, 32, 25, "");
        var menu = Panel(content, "Menu", 0, 0, 166, 75); Anchor(menu.rectTransform, Vector2.one, new Vector2(-214, -48));
        Image(menu.transform, "Menu Icon", 26, 23.5f, 28, 28, Load("Menu"), Ink).raycastTarget = false;
        Label(menu.transform, "Label", 66, 16, 90, 43, 34, "Menu"); hud.menuButton = Button(menu);
        var end = Panel(content, "End Turn", 0, 0, 218, 95); Anchor(end.rectTransform, new Vector2(1, 0), new Vector2(-270, 147));
        Label(end.transform, "Label", 15, 17, 188, 60, 44, "End Turn").alignment = TextAnchor.MiddleCenter; hud.endTurnButton = Button(end);
        hud.turnLabel = Label(content, "Turn Feedback", 0, 0, 1100, 48, 30, ""); Anchor(hud.turnLabel.rectTransform, new Vector2(.5f, 1), new Vector2(-550, -48)); hud.turnLabel.alignment = TextAnchor.MiddleCenter;
        var shade = Image(content, "Pause Overlay", 0, 0, 2560, 1440, null, new Color(.12f, .1f, .17f, .65f)); Stretch(shade.rectTransform); hud.pauseOverlay = shade.gameObject;
        var pauseCard = Panel(shade.transform, "Pause Card", 0, 0, 500, 280); Anchor(pauseCard.rectTransform, new Vector2(.5f, .5f), new Vector2(-250, 140));
        Label(pauseCard.transform, "Title", 30, 30, 440, 65, 48, "Paused").alignment = TextAnchor.MiddleCenter;
        var resume = Panel(pauseCard.transform, "Resume", 85, 140, 330, 85, Hex("ced0e7"), false); Label(resume.transform, "Label", 0, 10, 330, 65, 40, "Resume").alignment = TextAnchor.MiddleCenter; hud.resumeButton = Button(resume);
        hud.pauseOverlay.SetActive(false); hud.content.SetActive(false);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/UI/BattleHud.prefab"); Object.DestroyImmediate(root);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.GetComponent<BattleHud>().party = Object.FindFirstObjectByType<PartyChainController>();
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<BattleHud>());
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("UI EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        Debug.Log("BATTLE_HUD_BUILT");
    }
    private static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
    private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pos) { rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = pos; }
    private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    private static Image Image(Transform parent, string name, float x, float y, float w, float h, Sprite sprite, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
        var r = (RectTransform)obj.transform; r.pivot = new Vector2(0, 1); Anchor(r, new Vector2(0, 1), new Vector2(x, -y)); r.sizeDelta = new Vector2(w, h);
        var image = obj.GetComponent<Image>(); image.sprite = sprite; image.color = color; return image;
    }
    private static Image Panel(Transform parent, string name, float x, float y, float w, float h, Color? color = null, bool shadow = true)
    {
        var image = Image(parent, name, x, y, w, h, round, color ?? Paper); image.type = UnityEngine.UI.Image.Type.Sliced;
        if (shadow) { var s = image.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(57 / 255f, 62 / 255f, 72 / 255f, .48f); s.effectDistance = new Vector2(0, -6); }
        return image;
    }
    private static Button Button(Image target)
    {
        var b = target.gameObject.AddComponent<Button>(); b.targetGraphic = target;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        var colors = b.colors; colors.highlightedColor = new Color(.93f, .93f, 1); colors.pressedColor = new Color(.8f, .8f, .9f); b.colors = colors; return b;
    }
    private static Text Label(Transform parent, string name, float x, float y, float w, float h, int size, string text)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
        var r = (RectTransform)obj.transform; r.pivot = new Vector2(0, 1); Anchor(r, new Vector2(0, 1), new Vector2(x, -y)); r.sizeDelta = new Vector2(w, h);
        var label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size; label.color = Ink; label.text = text; label.raycastTarget = false; label.verticalOverflow = VerticalWrapMode.Overflow; label.horizontalOverflow = HorizontalWrapMode.Wrap; return label;
    }
    private static void MakeRoundSprite()
    {
        const string path = "Assets/Art/UI/RoundedPanel.png";
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float dx = Mathf.Max(Mathf.Abs(x - 31.5f) - 6, 0), dy = Mathf.Max(Mathf.Abs(y - 31.5f) - 6, 0);
            t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(26 - Mathf.Sqrt(dx * dx + dy * dy))));
        }
        t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(26, 26, 26, 26); importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
    }
}
