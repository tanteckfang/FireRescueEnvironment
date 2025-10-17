using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

public class WorldBuilder : MonoBehaviour
{
    // =============== Prefabs / UI ===============
    public GameObject robotPrefab, firePrefab, kitPrefab, survivorPrefab, extPrefab, obstaclePrefab, roomPrefab;
    public Canvas uiCanvas;

    [Header("UI")]
    public Color panelColor  = new Color(0.08f, 0.08f, 0.08f, 0.75f);
    public Color buttonColor = new Color(0.18f, 0.18f, 0.18f, 0.90f);
    public Color buttonHover = new Color(0.28f, 0.28f, 0.28f, 0.95f);
    public Color buttonActive= new Color(0.10f, 0.40f, 0.10f, 0.95f);
    public int   fontSize    = 20;

    // =============== State ===============
    [Header("Runtime State")]
    public Dictionary<string, Transform> id2obj = new();
    public Dictionary<string, string>    entityRoom = new();
    public List<RobotAgent> robots = new();

    [Header("FoV / Observability")]
    public float robot1FovDeg = 120f, robot1Range = 10f;
    public float robot2FovDeg = 140f, robot2Range = 14f;
    public bool  drawFovCones = true;

    [Header("Human Control")]
    public bool enableKeyboard = true;
    public RobotAgent selected;
    public float keyStepMeters = 2.0f;
    TextMeshProUGUI hud;

    [Header("Tags (Collision Only)")]
    public string obstacleTag = "Obstacle";
    public string floorTag    = "Floor";

    [Header("Rescue")]
    public Transform safeZone;

    // =============== NEW: Dynamics Controls ===============
    [Header("Dynamics")]
    public bool dynamicsEnabled  = true;     // turn ALL dynamics on/off
    public bool onStepChanges    = true;     // apply a random change after each click/keypress
    [Range(5, 600)]
    public int fireSpreadSeconds = 45;       // periodic fire spawn cadence

    Coroutine spreadRoutine;

    // ======================== SCENE BUILD ========================
    public void BuildFromSpec(JObject world)
    {
        EnsureEventSystem();
        EnsureUICanvas();
        EnsureTags(); // only for obstacle/floor (fires use component)

        // Floor
        if (!GameObject.Find("Floor"))
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(4, 1, 4);
            var r = floor.GetComponent<Renderer>(); if (r) r.material.color = new Color(0.95f, 0.95f, 0.95f);
            TryAssignTag(floor, floorTag);
        }

        // Rooms
        var roomList = new List<Transform>();
        foreach (var r in (JArray)world["map"]["rooms"])
        {
            var id  = (string)r["id"];
            var pos = ToVec3((JArray)r["pos"]);
            var go  = roomPrefab ? Instantiate(roomPrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = id; go.transform.position = pos; go.transform.localScale = new Vector3(10, 0.2f, 10);
            var rc = go.GetComponent<Renderer>(); if (rc) rc.material.color = new Color(0.92f, 0.92f, 0.92f);
            id2obj[id] = go.transform;
            roomList.Add(go.transform);
        }

        // Safe Zone (outside)
        if (safeZone == null)
        {
            float minZ = 0f; bool set = false;
            foreach (var tr in roomList) { if (!set) { minZ = tr.position.z; set = true; } else minZ = Mathf.Min(minZ, tr.position.z); }
            Vector3 szPos = (set ? new Vector3(0, 0.5f, minZ - 6f) : new Vector3(0, 0.5f, -12f));
            var sz = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sz.name = "SafeZone"; sz.transform.position = szPos; sz.transform.localScale = new Vector3(2f, 0.2f, 2f);
            var rc = sz.GetComponent<Renderer>(); if (rc) rc.material.color = new Color(0f, 0.9f, 0.9f, 0.9f);
            safeZone = sz.transform;
            id2obj["SafeZone"] = safeZone;
        }

        // Obstacles
        foreach (var o in (JArray)world["map"].SelectToken("obstacles") ?? new JArray())
        {
            var id = (string)o["id"]; var room = (string)o["room"];
            var min = ToVec3((JArray)o["aabb"][0]); var max = ToVec3((JArray)o["aabb"][1]);
            var center = (min + max) / 2f; var size = (max - min);

            var go = obstaclePrefab ? Instantiate(obstaclePrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = id;
            go.transform.position = id2obj[room].position + new Vector3(center.x, 0.5f, center.z);
            go.transform.localScale = new Vector3(size.x, 1f, size.z);
            var rc = go.GetComponent<Renderer>(); if (rc) rc.material.color = new Color(0.55f, 0.55f, 0.55f);
            TryAssignTag(go, obstacleTag);
            id2obj[id] = go.transform; entityRoom[id] = room; go.SetActive(false);
        }

        // Entities helper
        void SpawnList(string key, GameObject prefab, PrimitiveType pb, Color color)
        {
            foreach (var e in (JArray)world["entities"][key])
            {
                var id   = (string)e["id"];
                var room = (string)e["room"];
                var origin = id2obj[room].position;
                var offset = new Vector3(Random.Range(-3f, 3f), 0.5f, Random.Range(-3f, 3f));

                var go = prefab ? Instantiate(prefab) : GameObject.CreatePrimitive(pb);
                go.name = id; go.transform.position = origin + offset;
                if (!prefab) go.GetComponent<Renderer>().material.color = color;

                if (key == "fires") // 🔥 mark as fire (component, not tag)
                {
                    var f = go.GetComponent<Fire>(); if (f == null) f = go.AddComponent<Fire>(); f.id = id;
                    go.transform.localScale = Vector3.one * 1.0f; // one sphere = one fire
                }

                id2obj[id] = go.transform; entityRoom[id] = room; go.SetActive(false);
            }
        }

        SpawnList("fires",          firePrefab, PrimitiveType.Sphere,   Color.red);
        SpawnList("first_aid_kits", kitPrefab,  PrimitiveType.Cube,     Color.green);
        SpawnList("survivors",      survivorPrefab, PrimitiveType.Capsule, new Color(1f, .75f, .3f));
        SpawnList("extinguishers",  extPrefab,  PrimitiveType.Cylinder, new Color(0.7f, 0.2f, 0.85f));

        // Robots
        robots.Clear();
        foreach (var r in (JArray)world["robots"])
        {
            var id = (string)r["id"]; var room = (string)r["room"];
            var origin = id2obj[room].position;

            var go = robotPrefab ? Instantiate(robotPrefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = id; go.transform.position = origin + new Vector3(0, 0.5f, 0);

            var agent = go.GetComponent<RobotAgent>() ?? go.AddComponent<RobotAgent>();
            agent.id = id; agent.builder = this;

            if (id == "robot1")
            { agent.fovDeg = robot1FovDeg; agent.range = robot1Range; go.GetComponent<Renderer>().material.color = new Color(0.2f, 0.5f, 1f); }
            else
            { agent.fovDeg = robot2FovDeg; agent.range = robot2Range; go.GetComponent<Renderer>().material.color = new Color(0.2f, 1f, 0.9f); }

            agent.EnableFovCone(false);
            robots.Add(agent);
            id2obj[id] = go.transform; entityRoom[id] = room;
        }

        foreach (var a in robots) a.RevealInFov();
        if (robots.Count > 0) SelectRobot(robots[0]);

        // Start/stop spread loop according to the Inspector toggle
        RestartSpreadLoop();
    }

    // ======================== UI SETUP ========================
    public void SetupActionUI(JArray candidates)
    {
        EnsureEventSystem(); EnsureUICanvas();

        var old = GameObject.Find("ActionPanel");
        if (old) Destroy(old);

        var panel = new GameObject("ActionPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(uiCanvas.transform, false);
        panel.GetComponent<Image>().color = panelColor;
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(0, 1); prt.pivot = new Vector2(0, 1);
        prt.anchoredPosition = new Vector2(20, -20); prt.sizeDelta = new Vector2(420, 560);
        var vl = panel.GetComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(12, 12, 12, 12); vl.spacing = 8; vl.childControlWidth = true; vl.childControlHeight = true;

        var scroll = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect));
        scroll.transform.SetParent(panel.transform, false);
        var srt = scroll.GetComponent<RectTransform>();
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scroll.transform, false);
        var vtr = viewport.GetComponent<RectTransform>();
        vtr.anchorMin = Vector2.zero; vtr.anchorMax = Vector2.one; vtr.offsetMin = Vector2.zero; vtr.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.18f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
        crt.anchoredPosition = Vector2.zero; crt.sizeDelta = new Vector2(0, 0);
        var cvl = content.GetComponent<VerticalLayoutGroup>();
        cvl.padding = new RectOffset(4, 4, 4, 4); cvl.spacing = 6; cvl.childControlWidth = true; cvl.childControlHeight = true;
        var csf = content.GetComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sr = scroll.GetComponent<ScrollRect>();
        sr.viewport = vtr; sr.content = crt; sr.horizontal = false; sr.vertical = true;

        // Section: Robot select
        var header = CreateHeader("Select Robot"); header.transform.SetParent(content.transform, false);
        foreach (var r in robots)
        {
            var btn = CreateButton($"Select {r.id}"); btn.transform.SetParent(content.transform, false);
            btn.GetComponent<Button>().onClick.AddListener(() => { SelectRobot(r); LogHUD($"Selected {r.id} — WASD/E/F/R to control."); });
        }

        // Section: Actions
        var divider = CreateHeader("Actions"); divider.transform.SetParent(content.transform, false);

        foreach (var step in candidates)
        {
            var label = $"{(string)step["robot"]}: {(string)step["action"]}({(string)step["target"]})";
            var btn = CreateButton(label); btn.transform.SetParent(content.transform, false);
            var b = btn.GetComponent<Button>(); var img = btn.GetComponent<Image>();
            string robot = (string)step["robot"]; string action = (string)step["action"]; string target = (string)step["target"];
            b.onClick.AddListener(() => StartCoroutine(ExecuteWithFeedback(b, img, robot, action, target)));
        }

        // Built-in quick actions
        AddQuick(content.transform, "robot1: extinguish_all_fov", () => Execute("robot1", "extinguish_all_fov", ""));
        AddQuick(content.transform, "robot2: rescue_fov",       () => Execute("robot2", "rescue_fov", ""));
        AddQuick(content.transform, "robot1: move_forward",     () => Execute("robot1", "move_forward", ""));
        AddQuick(content.transform, "robot1: move_back",        () => Execute("robot1", "move_back", ""));
        AddQuick(content.transform, "robot1: move_left",        () => Execute("robot1", "move_left", ""));
        AddQuick(content.transform, "robot1: move_right",       () => Execute("robot1", "move_right", ""));
    }

    void AddQuick(Transform parent, string label, System.Action clicked)
    {
        var btn = CreateButton(label); btn.transform.SetParent(parent, false);
        var b = btn.GetComponent<Button>(); var img = btn.GetComponent<Image>();
        b.onClick.AddListener(() => StartCoroutine(ClickFX()));
        IEnumerator ClickFX()
        {
            Color orig = img.color; img.color = buttonActive; clicked?.Invoke();
            yield return new WaitForSeconds(0.2f); img.color = orig;
            if (onStepChanges) DynamicTickOnStep();
        }
    }

    IEnumerator ExecuteWithFeedback(Button btn, Image img, string robot, string action, string target)
    {
        Color orig = img.color; img.color = buttonActive;
        Execute(robot, action, target);
        yield return new WaitForSeconds(0.2f);
        img.color = orig;
        if (onStepChanges) DynamicTickOnStep();
    }

    public void Execute(string robot, string action, string target)
    {
        var agent = robots.Find(r => r.id == (string.IsNullOrEmpty(robot) ? (selected ? selected.id : "robot1") : robot));
        if (agent == null) { Debug.LogWarning("No robot " + robot); return; }

        switch (action)
        {
            case "move_left":          agent.MoveDir("left",    keyStepMeters); break;
            case "move_right":         agent.MoveDir("right",   keyStepMeters); break;
            case "move_forward":       agent.MoveDir("forward", keyStepMeters); break;
            case "move_back":          agent.MoveDir("back",    keyStepMeters); break;

            case "pick_extinguisher":  agent.PickExtinguisher(target); break;
            case "extinguish_fire":    agent.ExtinguishAllInFov(); break; // alias
            case "extinguish_all_fov": agent.ExtinguishAllInFov(); break;

            case "rescue_fov":         agent.RescueNearestInFov(); break;

            case "move":               agent.GoTo(target); break;
            case "pick":               agent.Pick(target); break;
            case "drop":               agent.Drop(target); break;
            case "deliver":            agent.Deliver(target); break;

            default: Debug.LogWarning("Unknown action " + action); break;
        }
    }

    void Update()
    {
        if (enableKeyboard && selected == null && robots.Count > 0 && Input.anyKeyDown)
            SelectRobot(robots[0]);

        if (enableKeyboard && selected != null && Application.isFocused)
        {
            bool acted = false;
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))    { selected.MoveDir("forward", keyStepMeters); acted = true; }
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))  { selected.MoveDir("back",    keyStepMeters); acted = true; }
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))  { selected.MoveDir("left",    keyStepMeters); acted = true; }
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) { selected.MoveDir("right",   keyStepMeters); acted = true; }

            if (Input.GetKeyDown(KeyCode.E)) { selected.PickExtinguisher(""); acted = true; }
            if (Input.GetKeyDown(KeyCode.F)) { selected.ExtinguishAllInFov(); acted = true; }
            if (Input.GetKeyDown(KeyCode.R)) { selected.RescueNearestInFov(); acted = true; }

            if (acted && onStepChanges) DynamicTickOnStep();
        }
    }

    // ======================== DYNAMICS ========================
    public void RestartSpreadLoop()
    {
        if (spreadRoutine != null) StopCoroutine(spreadRoutine);
        if (dynamicsEnabled) spreadRoutine = StartCoroutine(PeriodicFireSpread());
    }

    IEnumerator PeriodicFireSpread()
    {
        while (dynamicsEnabled)
        {
            yield return new WaitForSeconds(Mathf.Max(1, fireSpreadSeconds));
            if (!dynamicsEnabled) break;
            SpawnFire();
        }
    }

    void DynamicTickOnStep()
    {
        if (!dynamicsEnabled) return;
        if (Random.value < 0.40f) SpawnFire();
        if (Random.value < 0.20f) NudgeRandomObstacle();
    }

    // 👉 Public helper your RobotAgent can call to avoid instant re-spawn confusion
    public Coroutine PauseDynamicsBriefly(float seconds = 1.0f)
    {
        return StartCoroutine(_PauseDyn(seconds));
    }
    IEnumerator _PauseDyn(float seconds)
    {
        bool old = dynamicsEnabled;
        dynamicsEnabled = false;
        yield return new WaitForSeconds(seconds);
        dynamicsEnabled = old;
        RestartSpreadLoop();
    }

    // 🔥 spawn one red sphere (with Fire component)
    void SpawnFire()
    {
        var rooms = new List<string>();
        foreach (var kv in id2obj) if (kv.Key.StartsWith("Room")) rooms.Add(kv.Key);
        if (rooms.Count == 0) return;

        string room  = rooms[Random.Range(0, rooms.Count)];
        var origin   = id2obj[room].position;

        var go = firePrefab ? Instantiate(firePrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Fire" + Random.Range(100, 999);
        go.transform.position = origin + new Vector3(Random.Range(-3f, 3f), 0.5f, Random.Range(-3f, 3f));
        go.transform.localScale = Vector3.one * 1.0f;
        if (!firePrefab) go.GetComponent<Renderer>().material.color = Color.red;

        var f = go.GetComponent<Fire>(); if (f == null) f = go.AddComponent<Fire>(); f.id = go.name;

        id2obj[go.name] = go.transform; entityRoom[go.name] = room; go.SetActive(true);
    }

    void NudgeRandomObstacle()
    {
        var keys = new List<string>();
        foreach (var kv in id2obj) if (kv.Key.StartsWith("Obstacle")) keys.Add(kv.Key);
        if (keys.Count == 0) return;

        string id = keys[Random.Range(0, keys.Count)];
        var tr = id2obj[id];
        tr.position += new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f));
    }
    // Accepts a JSON payload (what your Bridge likely sends)
    public void SetupDynamics(JObject dyn)
    {
        if (dyn == null) { RestartSpreadLoop(); return; }

        // read with fallbacks to current Inspector values
        dynamicsEnabled   = dyn["enabled"]?.Value<bool>()           ?? dynamicsEnabled;
        onStepChanges     = dyn["on_step"]?.Value<bool>()           ?? onStepChanges;
        fireSpreadSeconds = dyn["fire_spread_seconds"]?.Value<int>()?? fireSpreadSeconds;

        RestartSpreadLoop();   // start/stop the periodic spread coroutine accordingly
    }

    // Convenience overload if your Bridge passes raw values
    public void SetupDynamics(bool enabled, bool onStep, int spreadSeconds)
    {
        dynamicsEnabled   = enabled;
        onStepChanges     = onStep;
        fireSpreadSeconds = spreadSeconds;
        RestartSpreadLoop();
    }


    // ======================== COLLISION-AWARE STEP ========================
    public Vector3 ClampStep(Vector3 start, Vector3 delta)
    {
        Vector3 desired = start + delta;
        desired.x = Mathf.Clamp(desired.x, -20, 20);
        desired.z = Mathf.Clamp(desired.z, -20, 20);

        Collider[] hits = Physics.OverlapBox(
            desired, new Vector3(0.3f, 0.6f, 0.3f),
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

        foreach (var h in hits)
        {
            if (h == null) continue;
            if (h.CompareTag(obstacleTag)) return start; // blocked
            if (h.CompareTag(floorTag)) continue;
        }
        return desired;
    }

    // ======================== HELPERS ========================
    void SelectRobot(RobotAgent r)
    {
        selected = r;
        foreach (var rr in robots) rr.EnableFovCone(rr == selected && drawFovCones);
    }

    GameObject CreateHeader(string text)
    {
        var go = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(0, 28);
        var t  = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = fontSize + 2; t.color = Color.white * 0.9f; t.fontStyle = FontStyles.Bold;
        t.margin = new Vector4(4, 2, 4, 6);
        return go;
    }

    GameObject CreateButton(string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(0, 44);
        var le = go.GetComponent<LayoutElement>(); le.minHeight = 44; le.preferredHeight = 44; le.flexibleWidth = 1;
        var img = go.GetComponent<Image>(); img.color = buttonColor;

        var colors = new ColorBlock
        {
            colorMultiplier = 1f,
            normalColor = buttonColor,
            highlightedColor = buttonHover,
            pressedColor = buttonActive,
            selectedColor = buttonHover,
            disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.6f)
        };
        go.GetComponent<Button>().colors = colors;

        var labelGO = new GameObject("Text", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = fontSize; tmp.color = Color.white;
        tmp.enableWordWrapping = false; tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = new Vector4(12, 6, 12, 6); tmp.alignment = TextAlignmentOptions.MidlineLeft;

        return go;
    }

    void EnsureEventSystem()
    {
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var es = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    void EnsureUICanvas()
    {
        if (uiCanvas != null) return;
        var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        uiCanvas = go.GetComponent<Canvas>(); uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
    }

    static Vector3 ToVec3(JArray arr) => new((float)arr[0], (float)arr[1], (float)arr[2]);

    void LogHUD(string msg)
    {
        if (hud == null)
        {
            var go = new GameObject("HUD", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(uiCanvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(20, 20); rt.sizeDelta = new Vector2(520, 48);
            hud = go.GetComponent<TextMeshProUGUI>();
            hud.fontSize = fontSize - 2; hud.color = Color.white * 0.9f;
            hud.enableWordWrapping = false; hud.alignment = TextAlignmentOptions.BottomLeft;
        }
        hud.text = msg;
    }

    // ====== Editor helpers for tags (Obstacle/Floor only) ======
    void TryAssignTag(GameObject go, string tagName)
    {
#if UNITY_EDITOR
        if (!IsTagDefinedEditor(tagName)) AddTagEditor(tagName);
#endif
        if (TagExists(tagName)) go.tag = tagName;
    }

    void EnsureTags()
    {
#if UNITY_EDITOR
        if (!IsTagDefinedEditor(obstacleTag)) AddTagEditor(obstacleTag);
        if (!IsTagDefinedEditor(floorTag))    AddTagEditor(floorTag);
#endif
    }

    bool TagExists(string tag)
    {
        try { _ = GameObject.FindGameObjectsWithTag(tag); return true; }
        catch { return false; }
    }

#if UNITY_EDITOR
    static bool IsTagDefinedEditor(string tag)
    {
        foreach (var t in InternalEditorUtility.tags)
            if (t == tag) return true;
        return false;
    }

    static void AddTagEditor(string tag)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;

        var so = new SerializedObject(assets[0]);
        var tagsProp = so.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                return;

        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }
#endif
}
