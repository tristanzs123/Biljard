using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Meni "Biljard > Zgradi sceno": postavi biljardno mizo (9-ft), krogle iz "Eight-ball Rack",
/// belo kroglo, kamero in luč ter vse shrani v Assets/Scenes/Biljard.unity.
/// Vse mere so v metrih (1 enota = 1 m).
/// </summary>
public static class BiljardSceneBuilder
{
    // igralna površina 9-ft mize
    const float L = 2.54f;          // dolžina (os X)
    const float W = 1.27f;          // širina (os Z)
    const float R = 0.028575f;      // polmer krogle (57,15 mm)

    const float CushionT = 0.05f;   // debelina bande
    const float CushionH = 0.045f;  // višina bande
    const float RailW = 0.10f;      // širina lesenega roba
    const float CornerGap = 0.06f;  // odprtina kotne luknje (od vogala vzdolž bande)
    const float SideGap = 0.065f;   // polovica odprtine srednje luknje

    const string MatDir = "Assets/Materials";
    const string PhysDir = "Assets/Materials/Physics";
    const string TableDir = "Assets/Models/PoolTable";
    const string BallMatDir = "Assets/Eight-ball Rack/Materials";
    const string RackPath = "Assets/Eight-ball Rack/Prefabs/Rack.prefab";
    const string ScenePath = "Assets/Scenes/Biljard.unity";

    [MenuItem("Biljard/Zgradi sceno")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(MatDir);
        EnsureFolder(PhysDir);
        EnsureFolder(TableDir);

        ConvertBallMaterialsToURP();

        // materiali
        Material felt = LitMaterial("Felt", new Color(0.05f, 0.42f, 0.18f), 0.1f);
        Material cushion = LitMaterial("Cushion", new Color(0.04f, 0.33f, 0.14f), 0.1f);
        Material wood = LitMaterial("Wood", new Color(0.36f, 0.20f, 0.09f), 0.55f);
        Material pocketMat = LitMaterial("Pocket", new Color(0.02f, 0.02f, 0.02f), 0.0f);
        Material floorMat = LitMaterial("Floor", new Color(0.25f, 0.25f, 0.27f), 0.2f);
        Material cueBallMat = LitMaterial("CueBall", new Color(0.95f, 0.95f, 0.92f), 0.85f);
        Material aimMat = AimMaterial();

        // fizikalni materiali (bounce se množi, trenje povpreči)
        PhysicsMaterial ballPM = PhysMaterial("Ball", 0.1f, 0.95f);
        PhysicsMaterial feltPM = PhysMaterial("Felt", 0.4f, 0.0f);
        PhysicsMaterial cushionPM = PhysMaterial("Cushion", 0.2f, 0.8f);
        PhysicsMaterial woodPM = PhysMaterial("Wood", 0.3f, 0.5f);

        AssetDatabase.SaveAssets();

        // nova scena s kamero in lučjo
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ---- miza ----
        GameObject tableGO = BuildTable(felt, cushion, wood, pocketMat, feltPM, cushionPM, woodPM);
        GameObject tablePrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            tableGO, TableDir + "/PoolTable.prefab", InteractionMode.AutomatedAction);
        if (tablePrefab == null) Debug.LogWarning("Prefaba mize ni bilo mogoče shraniti - miza je samo v sceni.");

        // tla
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Tla";
        floor.transform.position = new Vector3(0f, -0.79f, 0f);
        floor.transform.localScale = new Vector3(3f, 1f, 3f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        // ---- krogle ----
        GameObject rackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RackPath);
        if (rackPrefab == null)
        {
            Debug.LogError("Ne najdem " + RackPath);
            return;
        }

        GameObject rack = (GameObject)PrefabUtility.InstantiatePrefab(rackPrefab);
        PrefabUtility.UnpackPrefabInstance(rack, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        rack.name = "Krogle";
        // vrh trikotnika (krogla 1) na "foot spot", trikotnik obrnjen proti zadnji bandi
        rack.transform.SetPositionAndRotation(new Vector3(L / 4f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));

        // bela krogla = kopija krogle 1 z belim materialom
        Transform one = rack.transform.Find("1");
        GameObject cueBall = Object.Instantiate(one != null ? one.gameObject : rack.transform.GetChild(0).gameObject);
        cueBall.name = "Bela";
        cueBall.transform.position = new Vector3(-L / 4f, R, 0f);
        cueBall.GetComponent<Renderer>().sharedMaterial = cueBallMat;

        foreach (Transform t in rack.transform)
            SetupBall(t.gameObject, ballPM, false);
        Ball cue = SetupBall(cueBall, ballPM, true);

        // ---- kamera ----
        Camera cam = Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 2.4f, -1.35f);
            cam.transform.LookAt(new Vector3(0f, 0f, 0.05f));
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
        }

        // ---- luč ----
        Light sun = Object.FindFirstObjectByType<Light>();
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
        }

        // ---- merilna črta + kontrola udarca ----
        GameObject aimGO = new GameObject("Merilna crta");
        LineRenderer line = aimGO.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.008f;
        line.sharedMaterial = aimMat;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;

        CueController controller = cue.gameObject.AddComponent<CueController>();
        controller.cam = cam;
        controller.aimLine = line;

        // ---- shrani ----
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuild(ScenePath);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = cue.gameObject;
        Debug.Log("Biljard: scena zgrajena -> " + ScenePath + ". Pritisni Play.");
    }

    /// <summary>
    /// Vsem kroglam v odprti sceni nastavi vrednosti kotaljenja na privzete iz Ball.cs.
    /// (Unity si vrednosti shrani v sceno, zato sprememba v kodi sama ne vpliva na obstoječe krogle.)
    /// </summary>
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

    // ------------------------------------------------------------------ miza

    static GameObject BuildTable(Material felt, Material cushion, Material wood, Material pocketMat,
                                 PhysicsMaterial feltPM, PhysicsMaterial cushionPM, PhysicsMaterial woodPM)
    {
        GameObject table = new GameObject("BiljardnaMiza");
        Transform root = table.transform;

        // igralna površina (zgornja ploskev je na y = 0)
        Box("Podlaga", root, new Vector3(0f, -0.02f, 0f),
            new Vector3(L + 2f * CushionT, 0.04f, W + 2f * CushionT), felt, feltPM);

        // bande (6 kosov, vmes luknje)
        float cy = CushionH / 2f;
        float longLen = L / 2f - CornerGap - SideGap;
        float longX = (L / 2f - CornerGap + SideGap) / 2f;
        float bandZ = W / 2f + CushionT / 2f;
        float bandX = L / 2f + CushionT / 2f;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Box("Banda", root, new Vector3(sx * longX, cy, sz * bandZ),
                new Vector3(longLen, CushionH, CushionT), cushion, cushionPM);
        foreach (float sx in new[] { -1f, 1f })
            Box("Banda", root, new Vector3(sx * bandX, cy, 0f),
                new Vector3(CushionT, CushionH, W - 2f * CornerGap), cushion, cushionPM);

        // lesen rob
        float railH = 0.09f;
        float railY = 0.05f - railH / 2f;
        float railZ = W / 2f + CushionT + RailW / 2f;
        float railX = L / 2f + CushionT + RailW / 2f;
        foreach (float s in new[] { -1f, 1f })
        {
            Box("Rob", root, new Vector3(0f, railY, s * railZ),
                new Vector3(L + 2f * (CushionT + RailW), railH, RailW), wood, woodPM);
            Box("Rob", root, new Vector3(s * railX, railY, 0f),
                new Vector3(RailW, railH, W + 2f * CushionT), wood, woodPM);
        }

        // ohišje in noge
        Box("Ohisje", root, new Vector3(0f, -0.115f, 0f),
            new Vector3(L + 2f * (CushionT + RailW), 0.15f, W + 2f * (CushionT + RailW)), wood, woodPM);
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Box("Noga", root, new Vector3(sx * 1.2f, -0.49f, sz * 0.55f),
                new Vector3(0.12f, 0.6f, 0.12f), wood, woodPM);

        // luknje: 4 kotne + 2 srednji
        int n = 1;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            PocketAt("Luknja " + n++, root, new Vector3(sx * (L / 2f + 0.02f), 0f, sz * (W / 2f + 0.02f)), 0.12f, pocketMat);
        foreach (float sz in new[] { -1f, 1f })
            PocketAt("Luknja " + n++, root, new Vector3(0f, 0f, sz * (W / 2f + 0.035f)), 0.13f, pocketMat);

        return table;
    }

    static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, PhysicsMaterial pm)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.GetComponent<Collider>().sharedMaterial = pm;
        go.isStatic = true;
        return go;
    }

    static void PocketAt(string name, Transform parent, Vector3 pos, float diameter, Material mat)
    {
        // črn krog na mizi (samo izgled)
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = name;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = pos + new Vector3(0f, 0.0012f, 0f);
        visual.transform.localScale = new Vector3(diameter, 0.001f, diameter);
        visual.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.isStatic = true;

        // trigger, ki "pogoltne" kroglo
        GameObject trigger = new GameObject(name + " trigger");
        trigger.transform.SetParent(visual.transform.parent, false);
        trigger.transform.localPosition = pos + new Vector3(0f, R, 0f);
        SphereCollider sc = trigger.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 0.045f;
        trigger.AddComponent<Pocket>();
    }

    // ------------------------------------------------------------------ krogle

    static Ball SetupBall(GameObject go, PhysicsMaterial pm, bool isCue)
    {
        SphereCollider col = go.GetComponent<SphereCollider>();
        if (col == null) col = go.AddComponent<SphereCollider>();
        col.sharedMaterial = pm;

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.17f;
        rb.linearDamping = 0.08f;
        rb.angularDamping = 0.05f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        Ball ball = go.GetComponent<Ball>();
        if (ball == null) ball = go.AddComponent<Ball>();
        ball.isCueBall = isCue;
        return ball;
    }

    // ------------------------------------------------------------------ materiali

    static Shader LitShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) Debug.LogError("Ne najdem URP Lit shaderja - je URP nameščen?");
        return s;
    }

    /// <summary>Materiali krogel so za Built-in (Standard) -> v URP bi bili rožnati. Prestavi jih na URP/Lit.</summary>
    static void ConvertBallMaterialsToURP()
    {
        Shader lit = LitShader();
        if (lit == null) return;

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { BallMatDir }))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m == null || m.shader == lit) continue;

            Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Color col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

            m.shader = lit;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.85f);
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(m);
        }
    }

    static Material LitMaterial(string name, Color color, float smoothness)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(LitShader());
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
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

    // ------------------------------------------------------------------ pomožno

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    static void AddSceneToBuild(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
