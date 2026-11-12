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
/// 9-ft table with angled-jaw cushions (WPA 142°/104°), rails with round pocket cut-outs, chrome pocket plates, diamonds, 16 numbered balls, a lathe-modelled cue,
/// lights, camera, aim assist, GameManager, AI and HUD. Everything is saved under Assets/_Project.
/// All sizes in metres (1 unit = 1 m).
/// </summary>
public static class BiljardSceneBuilder
{
    // playing surface of a 9-ft table
    const float L = 2.54f;          // length (X)
    const float W = 1.27f;          // width (Z)
    const float R = 0.028575f;      // ball radius (57.15 mm)

    const float CushionT = 0.05f;      // cushion depth
    const float CushionNoseH = 0.037f; // cushion nose height (~64 % of the ball diameter, like a real K-66 profile)
    const float CushionBackH = 0.046f; // cushion top at the rail (the top slopes up to the rail)
    const float RailW = 0.12f;         // wooden rail width
    const float RailTop = 0.05f;       // rail top height
    const float RailBottom = -0.045f;  // rail bottom (apron)
    const float CornerGap = 0.09f;     // corner: cushion nose ends 9 cm from the corner -> mouth 12.7 cm (5")
    const float SideGap = 0.068f;      // side: half of the mouth -> mouth 13.6 cm (5.4")
    const float CornerJawAngle = 142f; // angle between cushion nose and jaw face (WPA: 142° corner)
    const float SideJawAngle = 104f;   // side pocket jaw angle (WPA: 104°)
    const float CornerHoleR = 0.068f;  // radius of the pocket hole
    const float SideHoleR = 0.066f;
    static readonly Vector2 CornerHoleOffset = new Vector2(0.035f, 0.035f); // hole centre behind the corner
    const float SideHoleOffset = 0.05f;

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
        // cloth: NO PhysX friction (Minimum combine -> 0). Sliding/rolling/spin is simulated in Ball.cs
        PhysicsMaterial feltPM = PhysMaterial("Felt", 0f, 0.0f);
        feltPM.frictionCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(feltPM);
        PhysicsMaterial cushionPM = PhysMaterial("Cushion", 0.2f, 0.81f); // ball-cushion: 0.965·0.81 ≈ 0.78
        PhysicsMaterial woodPM = PhysMaterial("Wood", 0.3f, 0.5f);

        // ---- meshes for the cue ----
        Mesh mButt = Lathe("Cue_Butt", -0.725f, -0.30f, 0.0148f, 0.0138f);
        Mesh mWrap = Lathe("Cue_Wrap", -0.30f, -0.05f, 0.0139f, 0.0133f);
        Mesh mShaft = Lathe("Cue_Shaft", -0.05f, 0.700f, 0.0128f, 0.0064f);
        Mesh mFerrule = Lathe("Cue_Ferrule", 0.700f, 0.718f, 0.0064f, 0.0064f);
        Mesh mTip = Lathe("Cue_Tip", 0.718f, 0.725f, 0.0062f, 0.0058f);

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
        cam.transform.position = new Vector3(0f, 2.6f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // always top view
        cam.fieldOfView = 40f;
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

        // only the lights: the lamp body would block the top-view camera
        GameObject lamp = new GameObject("Svetilka");
        lamp.transform.position = new Vector3(0f, 1.25f, 0f);
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
        gameGO.AddComponent<AudioManager>();

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
            b.slidingFriction = defaults.slidingFriction;
            b.sideSpinFriction = defaults.sideSpinFriction;
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

        // slate with cloth (top at y = 0), reaches under the cushions up to the rail
        Box("Sukno", root, new Vector3(0f, -0.02f, 0f), new Vector3(L + 2f * CushionT, 0.04f, W + 2f * CushionT), felt, feltPM, true);

        // ---- pocket hole centres
        var holes = new List<Vector3>(); // x, z, radius
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            holes.Add(new Vector3(sx * (L / 2f + CornerHoleOffset.x), sz * (W / 2f + CornerHoleOffset.y), CornerHoleR));
        foreach (float sz in new[] { -1f, 1f })
            holes.Add(new Vector3(0f, sz * (W / 2f + SideHoleOffset), SideHoleR));

        // ---- cushions: 6 convex prisms with angled jaws (corner 142°, side 104°) like a real table / 8 Ball Pool
        float cornerBack = CushionT * Mathf.Tan((CornerJawAngle - 90f) * Mathf.Deg2Rad); // jaw face leans towards the pocket
        float sideBack = CushionT * Mathf.Tan((SideJawAngle - 90f) * Mathf.Deg2Rad);
        int c = 0;
        foreach (float sz in new[] { -1f, 1f })
        foreach (float sx in new[] { -1f, 1f })
        {
            // long cushion from the side pocket to the corner pocket (in "local" coords: x along, z outwards)
            float x0 = SideGap, x1 = L / 2f - CornerGap;
            Vector2[] poly =
            {
                new Vector2(sx * x0, sz * (W / 2f)),
                new Vector2(sx * x1, sz * (W / 2f)),
                new Vector2(sx * (x1 + cornerBack), sz * (W / 2f + CushionT)),
                new Vector2(sx * (x0 - sideBack), sz * (W / 2f + CushionT))
            };
            CushionPiece("Blazina " + ++c, root, poly, cushion, cushionPM);
        }
        foreach (float sx in new[] { -1f, 1f })
        {
            float z1 = W / 2f - CornerGap;
            Vector2[] poly =
            {
                new Vector2(sx * (L / 2f), -z1),
                new Vector2(sx * (L / 2f), z1),
                new Vector2(sx * (L / 2f + CushionT), z1 + cornerBack),
                new Vector2(sx * (L / 2f + CushionT), -(z1 + cornerBack))
            };
            CushionPiece("Blazina " + ++c, root, poly, cushion, cushionPM);
        }

        // ---- wooden rails with round pocket cut-outs
        foreach (float s in new[] { -1f, 1f })
        {
            RailStrip("Rob dolg " + (s > 0 ? "zgoraj" : "spodaj"), root, true, s, holes, wood, pocketMat, woodPM);
            RailStrip("Rob kratek " + (s > 0 ? "desno" : "levo"), root, false, s, holes, wood, pocketMat, woodPM);
        }

        // diamonds (sights)
        float railZ = W / 2f + CushionT + RailW / 2f;
        float railX = L / 2f + CushionT + RailW / 2f;
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
        Box("Ohišje", root, new Vector3(0f, -0.13f, 0f), new Vector3(L + 2f * (CushionT + RailW) - 0.02f, 0.17f, W + 2f * (CushionT + RailW) - 0.02f), woodDark, woodPM, true);
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Box("Noga", root, new Vector3(sx * 1.15f, -0.5f, sz * 0.52f), new Vector3(0.14f, 0.6f, 0.14f), woodDark, woodPM, true);

        // pockets
        for (int i = 0; i < holes.Count; i++)
            PocketAt("Luknja " + (i + 1), root, holes[i], pocketMat, i < 4 ? 0.045f : 0.04f);

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

    /// <summary>
    /// One cushion: a convex prism over a quad in the XZ plane (nose0, nose1, back1, back0).
    /// The top slopes from the nose (CushionNoseH) up to the rail (CushionBackH). Convex MeshCollider.
    /// </summary>
    static void CushionPiece(string name, Transform parent, Vector2[] poly, Material mat, PhysicsMaterial pm)
    {
        float[] top = { CushionNoseH, CushionNoseH, CushionBackH, CushionBackH };
        var b = new MeshBuilder();
        Vector3 centre = Vector3.zero;
        for (int i = 0; i < 4; i++) centre += new Vector3(poly[i].x, 0f, poly[i].y) * 0.25f;

        // top (two triangles), bottom
        Vector3[] t = new Vector3[4], d = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            t[i] = new Vector3(poly[i].x, top[i], poly[i].y);
            d[i] = new Vector3(poly[i].x, 0f, poly[i].y);
        }
        b.Quad(t[0], t[1], t[2], t[3], Vector3.up, 4f);
        b.Quad(d[0], d[1], d[2], d[3], Vector3.down, 4f);
        // sides (nose, jaws, back)
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Vector3 mid = (d[i] + d[j]) * 0.5f;
            Vector3 outward = mid - centre;
            outward.y = 0f;
            b.Quad(d[i], d[j], t[j], t[i], outward.normalized, 4f);
        }

        Mesh mesh = b.Save("Cushion_" + name.Replace(' ', '_'));
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;
        mc.sharedMaterial = pm;
        go.AddComponent<Cushion>();
        go.isStatic = true;
    }

    /// <summary>
    /// Wooden rail along one side, built as a strip of samples so the pocket holes can be cut out as true circles.
    /// Long rails cover the corners; short rails fit between them. The walls of the cut-outs are black (pocket).
    /// </summary>
    static void RailStrip(string name, Transform parent, bool longSide, float sign, List<Vector3> holes,
                          Material wood, Material hole, PhysicsMaterial pm)
    {
        float inner0 = longSide ? W / 2f + CushionT : L / 2f + CushionT;   // distance of the inner edge from the centre
        float outer = inner0 + RailW;
        float extent = longSide ? L / 2f + CushionT + RailW : W / 2f + CushionT;
        int samples = Mathf.CeilToInt(2f * extent / 0.004f);
        const float bevel = 0.012f;

        // sample (t along the rail, d = distance from the table centre line)
        Vector3 ToWorld(float t, float d, float y) => longSide ? new Vector3(t, y, sign * d) : new Vector3(sign * d, y, t);
        Vector3 DirToWorld(float dt, float dd) => longSide ? new Vector3(dt, 0f, sign * dd) : new Vector3(sign * dd, 0f, dt);

        float InnerAt(float t)
        {
            float d = inner0;
            foreach (Vector3 h in holes)
            {
                float ht = longSide ? h.x : h.y;
                float hd = (longSide ? h.y : h.x) * sign;
                if (hd < 0f) continue;
                float dt = t - ht;
                if (Mathf.Abs(dt) >= h.z) continue;
                d = Mathf.Max(d, hd + Mathf.Sqrt(h.z * h.z - dt * dt));
            }
            return Mathf.Min(d, outer - bevel - 0.004f);
        }

        var woodB = new MeshBuilder();
        var holeB = new MeshBuilder();
        float prevT = -extent, prevD = InnerAt(prevT);
        for (int i = 1; i <= samples; i++)
        {
            float t = Mathf.Lerp(-extent, extent, i / (float)samples);
            float d = InnerAt(t);

            // top
            woodB.Quad(ToWorld(prevT, prevD, RailTop), ToWorld(t, d, RailTop), ToWorld(t, outer - bevel, RailTop), ToWorld(prevT, outer - bevel, RailTop), Vector3.up, 2f);
            // rounded (bevelled) outer edge and outer face
            Vector3 bevelN = (Vector3.up + DirToWorld(0f, 1f)).normalized;
            woodB.Quad(ToWorld(prevT, outer - bevel, RailTop), ToWorld(t, outer - bevel, RailTop), ToWorld(t, outer, RailTop - bevel), ToWorld(prevT, outer, RailTop - bevel), bevelN, 2f);
            woodB.Quad(ToWorld(prevT, outer, RailTop - bevel), ToWorld(t, outer, RailTop - bevel), ToWorld(t, outer, RailBottom), ToWorld(prevT, outer, RailBottom), DirToWorld(0f, 1f), 2f);

            // inner wall: towards the table, or into the pocket hole
            Vector3 n = DirToWorld(d - prevD, -(t - prevT)).normalized;
            bool cut = prevD > inner0 + 1e-4f || d > inner0 + 1e-4f;
            (cut ? holeB : woodB).Quad(ToWorld(prevT, prevD, RailTop), ToWorld(t, d, RailTop), ToWorld(t, d, RailBottom), ToWorld(prevT, prevD, RailBottom), n, 2f);

            prevT = t;
            prevD = d;
        }

        // end caps of the long rails (outer corners of the table)
        if (longSide)
            foreach (float e in new[] { -extent, extent })
            {
                float d = InnerAt(e);
                woodB.Quad(ToWorld(e, d, RailTop), ToWorld(e, outer - bevel, RailTop), ToWorld(e, outer, RailBottom), ToWorld(e, d, RailBottom), DirToWorld(Mathf.Sign(e), 0f), 2f);
            }

        Mesh mesh = MeshBuilder.SaveCombined("Rail_" + (longSide ? "Long" : "Short") + (sign > 0 ? "_P" : "_N"), woodB, holeB);
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { wood, hole };
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.sharedMaterial = pm;
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

    /// <summary>
    /// Pocket: black hole on the cloth, chrome arc on the rail around the hole (only over the wood, like the
    /// metal pocket plates in 8 Ball Pool), and the trigger that swallows the ball.
    /// hole = (x, z, radius).
    /// </summary>
    static void PocketAt(string name, Transform parent, Vector3 hole, Material holeMat, float triggerRadius)
    {
        var pos = new Vector3(hole.x, 0f, hole.y);
        float radius = hole.z;

        // black hole: a disc on the cloth + a short dark tube below (the rail cut-out walls are black too)
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = name;
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = pos + new Vector3(0f, -0.022f, 0f);
        disc.transform.localScale = new Vector3(2f * radius, 0.0235f, 2f * radius); // top at y = +0.0015
        disc.GetComponent<Renderer>().sharedMaterial = holeMat;
        disc.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        disc.isStatic = true;

        // chrome arc on the rail (only the part of the ring that lies over the wood)
        GameObject ring = new GameObject(name + " (obroč)");
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = new Vector3(pos.x, RailTop, pos.z);
        ring.AddComponent<MeshFilter>().sharedMesh = PocketArc(name.Replace(' ', '_') + "_Arc", pos, radius + 0.006f, 0.008f);
        ring.AddComponent<MeshRenderer>().sharedMaterial = chromeMat;
        ring.isStatic = true;

        // trigger that swallows the ball
        GameObject trigger = new GameObject(name + " trigger");
        trigger.transform.SetParent(parent, false);
        trigger.transform.localPosition = pos + new Vector3(0f, R, 0f);
        SphereCollider sc = trigger.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = triggerRadius;
        trigger.AddComponent<Pocket>();
    }

    /// <summary>Part of a torus around a pocket: only segments whose centre is over the rail (outside the cushion line).</summary>
    static Mesh PocketArc(string name, Vector3 centre, float major, float minor, int segments = 72, int sides = 12)
    {
        var b = new MeshBuilder();
        float limitX = L / 2f + CushionT, limitZ = W / 2f + CushionT;
        bool OnRail(float a)
        {
            float x = centre.x + Mathf.Cos(a) * major, z = centre.z + Mathf.Sin(a) * major;
            return Mathf.Abs(x) > limitX + 0.002f || Mathf.Abs(z) > limitZ + 0.002f;
        }
        Vector3 P(float u, float a) => new Vector3((major + minor * Mathf.Cos(a)) * Mathf.Cos(u), minor * Mathf.Sin(a), (major + minor * Mathf.Cos(a)) * Mathf.Sin(u));
        Vector3 Nrm(float u, float a) => new Vector3(Mathf.Cos(a) * Mathf.Cos(u), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(u));
        for (int i = 0; i < segments; i++)
        {
            float u0 = i / (float)segments * Mathf.PI * 2f, u1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            if (!OnRail((u0 + u1) * 0.5f)) continue;
            for (int j = 0; j < sides; j++)
            {
                float a0 = j / (float)sides * Mathf.PI, a1 = (j + 1) / (float)sides * Mathf.PI; // upper half tube only
                b.QuadSmooth(P(u0, a0), P(u1, a0), P(u1, a1), P(u0, a1), Nrm(u0, a0), Nrm(u1, a0), Nrm(u1, a1), Nrm(u0, a1));
            }
        }
        return b.Save("Pocket_" + name);
    }

    /// <summary>Small helper to build meshes face by face (flat or smooth quads, automatic winding, simple UVs).</summary>
    class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();

        /// <summary>Flat quad a-b-c-d with a wanted facing direction; UVs are planar, scaled by uvScale.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float uvScale)
        {
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(b - a, c - a);
            normal.Normalize();
            QuadSmooth(a, b, c, d, normal, normal, normal, normal, uvScale);
        }

        public void QuadSmooth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, float uvScale = 4f)
        {
            Vector3 want = na + nb + nc + nd;
            int i = V.Count;
            foreach (var (p, n) in new[] { (a, na), (b, nb), (c, nc), (d, nd) })
            {
                V.Add(p);
                N.Add(n.normalized);
                UV.Add(Planar(p, want) * uvScale);
            }
            // Unity: front face = clockwise, its normal = Cross(b - a, c - a)
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a), want) < 0f;
            if (!flip) { T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3); }
            else { T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2); }
        }

        static Vector2 Planar(Vector3 p, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) return new Vector2(p.x, p.z);
            if (ax >= az) return new Vector2(p.z, p.y);
            return new Vector2(p.x, p.y);
        }

        public Mesh Save(string name) => SaveCombined(name, this);

        /// <summary>Combines builders as sub-meshes (one material each) and saves the mesh as an asset.</summary>
        public static Mesh SaveCombined(string name, params MeshBuilder[] parts)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            foreach (MeshBuilder p in parts) { v.AddRange(p.V); n.AddRange(p.N); uv.AddRange(p.UV); }
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = parts.Length;
            int offset = 0;
            for (int s = 0; s < parts.Length; s++)
            {
                var tri = new List<int>(parts[s].T.Count);
                foreach (int t in parts[s].T) tri.Add(t + offset);
                mesh.SetTriangles(tri, s);
                offset += parts[s].V.Count;
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            string path = MeshDir + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
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
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0f;
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
