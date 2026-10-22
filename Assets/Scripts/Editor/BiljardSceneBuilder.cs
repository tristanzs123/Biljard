using System.Collections.Generic;
using Billiards.AI;
using Billiards.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu "Biljard > Zgradi sceno": builds the whole game from scratch (no external assets):
/// 9-ft table with cushions, rounded jaws, pockets and diamonds, 16 numbered balls, a lathe-modelled cue,
/// lights, camera, aim assist, GameManager, AI and HUD. Everything is saved under Assets/_Project.
/// All sizes in metres (1 unit = 1 m).
/// </summary>
public static class BiljardSceneBuilder
{
    // playing surface of a 9-ft table
    const float L = 2.54f;          // length (X)
    const float W = 1.27f;          // width (Z)
    const float R = 0.028575f;      // ball radius (57.15 mm)

    const float CushionT = 0.05f;   // cushion depth
    const float CushionH = 0.045f;  // cushion height (nose at ~ball centre height)
    const float RailW = 0.12f;      // wooden rail width
    const float RailTop = 0.05f;    // rail top height
    const float CornerGap = 0.08f;  // corner pocket opening along the cushion -> mouth ~0.113 m
    const float SideGap = 0.065f;   // half of the side pocket opening -> mouth 0.13 m

    const string Root = "Assets/_Project";
    const string TexDir = Root + "/Textures";
    const string MatDir = Root + "/Materials";
    const string PhysDir = Root + "/Materials/Physics";
    const string MeshDir = Root + "/Meshes";
    const string PrefabDir = Root + "/Prefabs";
    const string SceneDir = Root + "/Scenes";
    const string DifficultyDir = Root + "/ScriptableObjects/AI";
    const string ScenePath = SceneDir + "/Biljard.unity";
    const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";

    static Mesh pocketRimCorner, pocketRimSide;
    static Material chromeMat;

    static readonly string[] OldAssets =
    {
        "Assets/Eight-ball Rack", "Assets/Materials", "Assets/Models", "Assets/TutorialInfo",
        "Assets/Readme.asset", "Assets/Scenes"
    };

    [MenuItem("Biljard/Zgradi sceno")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        foreach (string d in new[] { TexDir, MatDir, PhysDir, MeshDir, PrefabDir, SceneDir, DifficultyDir }) EnsureFolder(d);

        // ---- materials ----
        Material felt = Lit("Felt", new Color(0.12f, 0.62f, 0.30f), 0.05f, Tex("Felt"), new Vector2(5f, 2.5f));
        Material cushionMat = Lit("Cushion", new Color(0.10f, 0.53f, 0.26f), 0.05f, Tex("Felt"), new Vector2(4f, 0.2f));
        Material wood = Lit("Wood", Color.white, 0.6f, Tex("Wood"), new Vector2(2f, 1f));
        Material woodDark = Lit("WoodDark", new Color(0.45f, 0.4f, 0.38f), 0.5f, Tex("Wood"), new Vector2(1f, 1f));
        Material pocketMat = Lit("Pocket", new Color(0.01f, 0.01f, 0.01f), 0f, null, Vector2.one);
        Material leather = Lit("PocketLeather", new Color(0.12f, 0.07f, 0.04f), 0.35f, null, Vector2.one);
        Material diamond = Lit("Diamond", new Color(0.95f, 0.93f, 0.85f), 0.9f, null, Vector2.one);
        Material carpet = Lit("Carpet", Color.white, 0.05f, Tex("Carpet"), new Vector2(25f, 25f));
        Material shade = Lit("LampShade", new Color(0.05f, 0.18f, 0.1f), 0.7f, null, Vector2.one);
        Material maple = Lit("CueShaft", new Color(1f, 0.86f, 0.62f), 0.7f, Tex("Wood"), new Vector2(0.3f, 1f));
        Material buttMat = Lit("CueButt", new Color(0.35f, 0.18f, 0.1f), 0.75f, Tex("Wood"), new Vector2(0.3f, 1f));
        Material wrap = Lit("CueWrap", new Color(0.04f, 0.04f, 0.04f), 0.2f, null, Vector2.one);
        Material ferrule = Lit("CueFerrule", new Color(0.95f, 0.95f, 0.92f), 0.8f, null, Vector2.one);
        Material tip = Lit("CueTip", new Color(0.15f, 0.35f, 0.8f), 0.1f, null, Vector2.one);
        Material chrome = Lit("Chrome", new Color(0.82f, 0.82f, 0.85f), 0.88f, null, Vector2.one);
        chrome.SetFloat("_Metallic", 1f);
        Material aimMat = AimMaterial();
        Material ghostMat = GhostMaterial();
        var ballMats = new Material[16];
        for (int i = 0; i < 16; i++) ballMats[i] = Lit("Ball_" + i.ToString("00"), Color.white, 0.93f, Tex("Ball_" + i.ToString("00")), Vector2.one);

        // ---- physics materials (bounce multiplies, friction averages) ----
        PhysicsMaterial ballPM = PhysMaterial("Ball", 0.06f, 0.965f);      // ball-ball: 0.965² ≈ 0.93
        PhysicsMaterial feltPM = PhysMaterial("Felt", 0.4f, 0.0f);
        PhysicsMaterial cushionPM = PhysMaterial("Cushion", 0.2f, 0.81f); // ball-cushion: 0.965·0.81 ≈ 0.78
        PhysicsMaterial woodPM = PhysMaterial("Wood", 0.3f, 0.5f);

        // ---- meshes for the cue ----
        Mesh mButt = Lathe("Cue_Butt", -0.725f, -0.30f, 0.0148f, 0.0138f);
        Mesh mWrap = Lathe("Cue_Wrap", -0.30f, -0.05f, 0.0139f, 0.0133f);
        Mesh mShaft = Lathe("Cue_Shaft", -0.05f, 0.700f, 0.0128f, 0.0064f);
        Mesh mFerrule = Lathe("Cue_Ferrule", 0.700f, 0.718f, 0.0064f, 0.0064f);
        Mesh mTip = Lathe("Cue_Tip", 0.718f, 0.725f, 0.0062f, 0.0058f);

        pocketRimCorner = Torus("Pocket_Rim_Corner", 0.13f / 2f + 0.006f, 0.009f);
        pocketRimSide = Torus("Pocket_Rim_Side", 0.14f / 2f + 0.006f, 0.009f);
        chromeMat = chrome;

        AssetDatabase.SaveAssets();

        // ---- new scene ----
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ---- table ----
        GameObject table = BuildTable(felt, cushionMat, wood, woodDark, pocketMat, leather, diamond, feltPM, cushionPM, woodPM);
        PrefabUtility.SaveAsPrefabAssetAndConnect(table, PrefabDir + "/PoolTable.prefab", InteractionMode.AutomatedAction);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Tla";
        floor.transform.position = new Vector3(0f, -0.79f, 0f);
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        floor.GetComponent<Renderer>().sharedMaterial = carpet;

        // ---- balls ----
        GameObject ballsRoot = new GameObject("Krogle");
        float step = 2f * R + 0.0003f;
        int number = 1;
        for (int row = 0; row < 5; row++)
        for (int j = 0; j <= row; j++)
        {
            var pos = new Vector3(L / 4f + row * step * 0.8660254f, R, (j - row * 0.5f) * step);
            MakeBall("Krogla " + number, number, pos, ballMats[number], ballPM, ballsRoot.transform);
            number++;
        }
        Ball cue = MakeBall("Bela", 0, new Vector3(-L / 4f, R, 0f), ballMats[0], ballPM, null);

        // ---- camera ----
        Camera cam = Object.FindFirstObjectByType<Camera>();
        cam.transform.position = new Vector3(0f, 2.3f, -1.45f);
        cam.transform.LookAt(new Vector3(0f, 0f, 0.05f));
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.05f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.025f);
        var camData = cam.GetUniversalAdditionalCameraData();
        if (camData != null) camData.renderPostProcessing = true;
        CameraViews views = cam.gameObject.AddComponent<CameraViews>();

        // ---- post processing (template profile: tonemapping, bloom, vignette) ----
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile != null)
        {
            GameObject volGO = new GameObject("Global Volume");
            Volume vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        // ---- lighting: dark pool hall, lamp above the table ----
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.14f, 0.14f, 0.15f);
        Light sun = Object.FindFirstObjectByType<Light>();
        sun.transform.rotation = Quaternion.Euler(70f, -30f, 0f);
        sun.intensity = 0.2f;
        sun.shadows = LightShadows.Soft;

        GameObject lamp = new GameObject("Svetilka");
        lamp.transform.position = new Vector3(0f, 1.25f, 0f);
        Box("Senčnik", lamp.transform, Vector3.zero, new Vector3(1.7f, 0.1f, 0.38f), shade, null, false);
        Box("Vrvica L", lamp.transform, new Vector3(-0.6f, 0.6f, 0f), new Vector3(0.008f, 1.1f, 0.008f), wrap, null, false);
        Box("Vrvica D", lamp.transform, new Vector3(0.6f, 0.6f, 0f), new Vector3(0.008f, 1.1f, 0.008f), wrap, null, false);
        foreach (float x in new[] { -0.6f, 0f, 0.6f })
        {
            GameObject go = new GameObject("Luč");
            go.transform.SetParent(lamp.transform, false);
            go.transform.localPosition = new Vector3(x, -0.06f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 110f;
            l.innerSpotAngle = 55f;
            l.range = 3.5f;
            l.intensity = 1.6f;
            l.color = new Color(1f, 0.94f, 0.82f);
            l.shadows = x == 0f ? LightShadows.Soft : LightShadows.None;
        }

        // ---- aim assist ----
        LineRenderer aimLine = MakeLine("Merilna crta", aimMat, new Color(1f, 1f, 1f, 0.9f));
        LineRenderer objectLine = MakeLine("Smer krogle", aimMat, new Color(1f, 0.85f, 0.3f, 0.9f));
        LineRenderer deflectLine = MakeLine("Smer bele", aimMat, new Color(0.5f, 0.8f, 1f, 0.8f));
        GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ghost.name = "Ghost ball";
        Object.DestroyImmediate(ghost.GetComponent<Collider>());
        ghost.transform.localScale = Vector3.one * (2f * R);
        ghost.GetComponent<Renderer>().sharedMaterial = ghostMat;
        ghost.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        ghost.SetActive(false);

        // ---- cue stick ----
        GameObject stickGO = new GameObject("Palica");
        Part(stickGO.transform, "Ročaj", mButt, buttMat);
        Part(stickGO.transform, "Ovoj", mWrap, wrap);
        Part(stickGO.transform, "Les", mShaft, maple);
        Part(stickGO.transform, "Obroček", mFerrule, ferrule);
        Part(stickGO.transform, "Konica", mTip, tip);
        CueStick stick = stickGO.AddComponent<CueStick>();
        stickGO.transform.position = new Vector3(-L / 4f - 0.8f, 0.1f, 0f);
        stickGO.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.right);

        CueController controller = cue.gameObject.AddComponent<CueController>();
        controller.cam = cam;
        controller.stick = stick;
        controller.aimLine = aimLine;
        controller.objectLine = objectLine;
        controller.deflectLine = deflectLine;
        controller.ghostBall = ghost.transform;

        // ---- game: GameManager + AI + HUD ----
        AIDifficultySettings[] levels =
        {
            Difficulty("Lahka", 2.6f, 25f, false, false, 0.35f, 0.0f),
            Difficulty("Srednja", 1.2f, 12f, false, true, 0.15f, 0.12f),
            Difficulty("Tezka", 0.5f, 6f, true, true, 0.05f, 0.15f),
            Difficulty("Profesionalna", 0.15f, 3f, true, true, 0f, 0.2f)
        };

        GameObject gameGO = new GameObject("Game");
        AIPlayer ai = gameGO.AddComponent<AIPlayer>();
        GameManager gm = gameGO.AddComponent<GameManager>();
        gameGO.AddComponent<Billiards.Core.Progression>();
        GameHud hud = gameGO.AddComponent<GameHud>();

        var aiSo = new SerializedObject(ai);
        aiSo.FindProperty("cue").objectReferenceValue = controller;
        aiSo.ApplyModifiedPropertiesWithoutUndo();

        var gmSo = new SerializedObject(gm);
        gmSo.FindProperty("cue").objectReferenceValue = controller;
        gmSo.FindProperty("ai").objectReferenceValue = ai;
        gmSo.FindProperty("cameraViews").objectReferenceValue = views;
        SerializedProperty diffs = gmSo.FindProperty("difficulties");
        diffs.arraySize = levels.Length;
        for (int i = 0; i < levels.Length; i++) diffs.GetArrayElementAtIndex(i).objectReferenceValue = levels[i];
        gmSo.ApplyModifiedPropertiesWithoutUndo();

        var hudSo = new SerializedObject(hud);
        hudSo.FindProperty("game").objectReferenceValue = gm;
        hudSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- save ----
        EditorSceneManager.SaveScene(scene, ScenePath);
        SetBuildScenes(ScenePath);

        // ---- remove the old asset-pack based content (moved to the recycle bin, also still in git history) ----
        foreach (string old in OldAssets)
            if (AssetDatabase.IsValidFolder(old) || AssetDatabase.LoadMainAssetAtPath(old) != null)
                AssetDatabase.MoveAssetToTrash(old);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = cue.gameObject;
        Debug.Log("Biljard: vse zgrajeno na novo -> " + ScenePath + ". Pritisni Play.");
    }

    [MenuItem("Biljard/Ponastavi nastavitve krogel")]
    public static void ResetBallSettings()
    {
        GameObject temp = new GameObject("temp", typeof(Rigidbody));
        Ball defaults = temp.AddComponent<Ball>();
        int count = 0;
        foreach (Ball b in Object.FindObjectsByType<Ball>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b == defaults) continue;
            Undo.RecordObject(b, "Ponastavi krogle");
            b.rollingDeceleration = defaults.rollingDeceleration;
            b.linearDamping = defaults.linearDamping;
            b.spinDecay = defaults.spinDecay;
            b.stopSpeed = defaults.stopSpeed;
            EditorUtility.SetDirty(b);
            count++;
        }
        Object.DestroyImmediate(temp);
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Biljard: nastavitve kotaljenja posodobljene na {count} kroglah.");
    }

    // ------------------------------------------------------------------ table

    static GameObject BuildTable(Material felt, Material cushion, Material wood, Material woodDark, Material pocketMat,
                                 Material leather, Material diamond,
                                 PhysicsMaterial feltPM, PhysicsMaterial cushionPM, PhysicsMaterial woodPM)
    {
        GameObject table = new GameObject("BiljardnaMiza");
        Transform root = table.transform;

        // slate with cloth (top at y = 0)
        Box("Sukno", root, new Vector3(0f, -0.02f, 0f), new Vector3(L + 2f * CushionT, 0.04f, W + 2f * CushionT), felt, feltPM, true);

        // cushions: 6 pieces with rounded ends at the pockets ("jaws") so balls never stick on sharp corners
        float cy = CushionH / 2f;
        float bandZ = W / 2f + CushionT / 2f;
        float bandX = L / 2f + CushionT / 2f;
        float r = CushionT / 2f;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            float a = SideGap + r;
            float b = L / 2f - CornerGap - r;
            Box("Blazina", root, new Vector3(sx * (a + b) / 2f, cy, sz * bandZ), new Vector3(b - a, CushionH, CushionT), cushion, cushionPM, true)
                .AddComponent<Cushion>();
            Jaw(root, new Vector3(sx * a, cy, sz * bandZ), cushion, cushionPM);
            Jaw(root, new Vector3(sx * b, cy, sz * bandZ), cushion, cushionPM);
        }
        foreach (float sx in new[] { -1f, 1f })
        {
            float e = W / 2f - CornerGap - r;
            Box("Blazina", root, new Vector3(sx * bandX, cy, 0f), new Vector3(CushionT, CushionH, 2f * e), cushion, cushionPM, true)
                .AddComponent<Cushion>();
            Jaw(root, new Vector3(sx * bandX, cy, e), cushion, cushionPM);
            Jaw(root, new Vector3(sx * bandX, cy, -e), cushion, cushionPM);
        }

        // wooden rails
        float railH = RailTop + 0.04f;
        float railY = RailTop - railH / 2f;
        float railZ = W / 2f + CushionT + RailW / 2f;
        float railX = L / 2f + CushionT + RailW / 2f;
        foreach (float s in new[] { -1f, 1f })
        {
            Box("Rob", root, new Vector3(0f, railY, s * railZ), new Vector3(L + 2f * (CushionT + RailW), railH, RailW), wood, woodPM, true);
            Box("Rob", root, new Vector3(s * railX, railY, 0f), new Vector3(RailW, railH, W + 2f * CushionT), wood, woodPM, true);
        }

        // diamonds (sights)
        for (int k = 1; k <= 7; k++)
        {
            if (k == 4) continue;
            float x = -L / 2f + k * L / 8f;
            foreach (float s in new[] { -1f, 1f }) Diamond(root, new Vector3(x, RailTop, s * railZ), diamond);
        }
        for (int k = 1; k <= 3; k++)
        {
            float z = -W / 2f + k * W / 4f;
            foreach (float s in new[] { -1f, 1f }) Diamond(root, new Vector3(s * railX, RailTop, z), diamond);
        }

        // body and legs
        Box("Ohišje", root, new Vector3(0f, -0.13f, 0f), new Vector3(L + 2f * (CushionT + RailW) - 0.02f, 0.18f, W + 2f * (CushionT + RailW) - 0.02f), woodDark, woodPM, true);
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Box("Noga", root, new Vector3(sx * 1.15f, -0.5f, sz * 0.52f), new Vector3(0.14f, 0.6f, 0.14f), woodDark, woodPM, true);

        // pockets: 4 corners + 2 sides
        int n = 1;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            PocketAt("Luknja " + n++, root, new Vector3(sx * (L / 2f + 0.02f), 0f, sz * (W / 2f + 0.02f)), 0.13f, pocketMat, leather);
        foreach (float sz in new[] { -1f, 1f })
            PocketAt("Luknja " + n++, root, new Vector3(0f, 0f, sz * (W / 2f + 0.035f)), 0.14f, pocketMat, leather);

        return table;
    }

    static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, PhysicsMaterial pm, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (collider) go.GetComponent<Collider>().sharedMaterial = pm;
        else Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    /// <summary>Rounded cushion end (vertical cylinder) at a pocket mouth.</summary>
    static void Jaw(Transform parent, Vector3 center, Material mat, PhysicsMaterial pm)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Blazina (rob ob luknji)";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = new Vector3(CushionT, CushionH / 2f, CushionT);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.GetComponent<Collider>().sharedMaterial = pm;
        go.AddComponent<Cushion>();
        go.isStatic = true;
    }

    static void Diamond(Transform parent, Vector3 pos, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Diamant";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos + new Vector3(0f, 0.0006f, 0f);
        go.transform.localScale = new Vector3(0.014f, 0.0006f, 0.014f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
    }

    static void PocketAt(string name, Transform parent, Vector3 pos, float diameter, Material hole, Material leather)
    {
        // leather rim on the rail + black hole through rail and cloth (visual only)
        float height = RailTop + 0.04f;
        GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = name + " (usnje)";
        Object.DestroyImmediate(rim.GetComponent<Collider>());
        rim.transform.SetParent(parent, false);
        rim.transform.localPosition = new Vector3(pos.x, RailTop - 0.004f, pos.z);
        rim.transform.localScale = new Vector3(diameter + 0.03f, 0.0055f, diameter + 0.03f);
        rim.GetComponent<Renderer>().sharedMaterial = leather;
        rim.isStatic = true;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = name;
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = new Vector3(pos.x, RailTop - height / 2f + 0.0025f, pos.z);
        visual.transform.localScale = new Vector3(diameter, height / 2f, diameter);
        visual.GetComponent<Renderer>().sharedMaterial = hole;
        visual.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        visual.isStatic = true;

        // polished metal pocket rim (like on a real table / in 8 Ball Pool)
        GameObject ring = new GameObject(name + " (obroč)");
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = new Vector3(pos.x, RailTop + 0.003f, pos.z);
        ring.AddComponent<MeshFilter>().sharedMesh = diameter > 0.135f ? pocketRimSide : pocketRimCorner;
        ring.AddComponent<MeshRenderer>().sharedMaterial = chromeMat;
        ring.isStatic = true;

        // deep pocket below the cloth so the hole looks deep from every angle
        GameObject well = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        well.name = name + " (globina)";
        Object.DestroyImmediate(well.GetComponent<Collider>());
        well.transform.SetParent(parent, false);
        well.transform.localPosition = new Vector3(pos.x, -0.12f, pos.z);
        well.transform.localScale = new Vector3(diameter * 0.98f, 0.12f, diameter * 0.98f);
        well.GetComponent<Renderer>().sharedMaterial = hole;
        well.isStatic = true;

        // trigger that swallows the ball
        GameObject trigger = new GameObject(name + " trigger");
        trigger.transform.SetParent(parent, false);
        trigger.transform.localPosition = pos + new Vector3(0f, R, 0f);
        SphereCollider sc = trigger.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 0.045f;
        trigger.AddComponent<Pocket>();
    }

    // ------------------------------------------------------------------ balls, cue, lines

    static Ball MakeBall(string name, int number, Vector3 pos, Material mat, PhysicsMaterial pm, Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = number == 0 ? Quaternion.identity : Quaternion.Euler(Random.Range(-40f, 40f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
        go.transform.localScale = Vector3.one * (2f * R);
        go.GetComponent<Renderer>().sharedMaterial = mat;

        SphereCollider col = go.GetComponent<SphereCollider>();
        col.sharedMaterial = pm;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.17f;
        rb.linearDamping = 0.12f;
        rb.angularDamping = 0.05f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        Ball ball = go.AddComponent<Ball>();
        ball.isCueBall = number == 0;
        ball.number = number;
        return ball;
    }

    static void Part(Transform parent, string name, Mesh mesh, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>Tapered cylinder along +Y (lathe) with caps, saved as a mesh asset.</summary>
    static Mesh Lathe(string name, float y0, float y1, float r0, float r1, int segments = 28)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();

        for (int ring = 0; ring < 2; ring++)
        {
            float y = ring == 0 ? y0 : y1;
            float r = ring == 0 ? r0 : r1;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                uv.Add(new Vector2(i / (float)segments, ring));
            }
        }
        int row = segments + 1;
        for (int i = 0; i < segments; i++)
        {
            int a = i, b = i + 1, c = row + i, d = row + i + 1;
            t.Add(a); t.Add(c); t.Add(b);
            t.Add(c); t.Add(d); t.Add(b);
        }

        // caps
        for (int cap = 0; cap < 2; cap++)
        {
            float y = cap == 0 ? y0 : y1;
            float r = cap == 0 ? r0 : r1;
            int center = v.Count;
            v.Add(new Vector3(0f, y, 0f));
            uv.Add(new Vector2(0.5f, 0.5f));
            int start = v.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < segments; i++)
            {
                if (cap == 1) { t.Add(center); t.Add(start + i + 1); t.Add(start + i); }
                else { t.Add(center); t.Add(start + i); t.Add(start + i + 1); }
            }
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        string path = MeshDir + "/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Torus mesh used as a metal pocket rim.</summary>
    static Mesh Torus(string name, float major, float minor, int segments = 48, int sides = 12)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments * Mathf.PI * 2f;
            for (int j = 0; j <= sides; j++)
            {
                float a = j / (float)sides * Mathf.PI * 2f;
                float rr = major + minor * Mathf.Cos(a);
                v.Add(new Vector3(rr * Mathf.Cos(u), minor * Mathf.Sin(a), rr * Mathf.Sin(u)));
                uv.Add(new Vector2(i / (float)segments, j / (float)sides));
            }
        }
        int row = sides + 1;
        for (int i = 0; i < segments; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = i * row + j, b = (i + 1) * row + j, c = a + 1, d = b + 1;
            t.Add(a); t.Add(c); t.Add(b);   // clockwise when seen from outside (Unity front face)
            t.Add(c); t.Add(d); t.Add(b);
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string path = MeshDir + "/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static LineRenderer MakeLine(string name, Material mat, Color color)
    {
        GameObject go = new GameObject(name);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.006f;
        line.sharedMaterial = mat;
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    // ------------------------------------------------------------------ assets

    static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + name + ".png");

    static Material Lit(string name, Color color, float smoothness, Texture tex, Vector2 tiling)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", tiling);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material AimMaterial()
    {
        string path = MatDir + "/Aim.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        m = new Material(s);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material GhostMaterial()
    {
        string path = MatDir + "/Ghost.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.35f));
        EditorUtility.SetDirty(m);
        return m;
    }

    static PhysicsMaterial PhysMaterial(string name, float friction, float bounce)
    {
        string path = PhysDir + "/" + name + ".asset";
        PhysicsMaterial pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (pm == null)
        {
            pm = new PhysicsMaterial(name);
            AssetDatabase.CreateAsset(pm, path);
        }
        pm.dynamicFriction = friction;
        pm.staticFriction = friction;
        pm.bounciness = bounce;
        pm.frictionCombine = PhysicsMaterialCombine.Average;
        pm.bounceCombine = PhysicsMaterialCombine.Multiply;
        EditorUtility.SetDirty(pm);
        return pm;
    }

    static AIDifficultySettings Difficulty(string name, float angle, float power, bool position, bool safety, float bad, float minScore)
    {
        string path = DifficultyDir + "/AI_" + name + ".asset";
        AIDifficultySettings d = AssetDatabase.LoadAssetAtPath<AIDifficultySettings>(path);
        if (d == null)
        {
            d = ScriptableObject.CreateInstance<AIDifficultySettings>();
            AssetDatabase.CreateAsset(d, path);
        }
        d.displayName = name == "Tezka" ? "Težka" : name;
        d.angleErrorDeg = angle;
        d.powerErrorPercent = power;
        d.usePosition = position;
        d.playSafety = safety;
        d.badDecisionChance = bad;
        d.minPotScore = minScore;
        EditorUtility.SetDirty(d);
        return d;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    static void SetBuildScenes(string path)
    {
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
    }
}
