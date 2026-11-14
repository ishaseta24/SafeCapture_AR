// Editor-only. Run via menu: Ohlone AR Quest > Build Fountain Scene
// Recreates the Fountain Stop scene for Quest using positions extracted from
// the mobile Fountain_Stop.unity. Assets must be imported from QuestStops/Fountain/.
// Run ONCE — it creates and saves Assets/QuestStops/Fountain/Scenes/FountainStop_Quest.unity

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class FountainSceneBuilder
{
    private const string ScenePath = "Assets/QuestStops/Fountain/Scenes/FountainStop_Quest.unity";

    [MenuItem("Ohlone AR Quest/Build Fountain Scene")]
    public static void BuildScene()
    {
        // Ensure output folder exists
        System.IO.Directory.CreateDirectory(
            System.IO.Path.Combine(Application.dataPath, "QuestStops/Fountain/Scenes"));

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── Directional Light ──────────────────────────────────────────────
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightGO.transform.SetPositionAndRotation(
            new Vector3(0, 3, 0),
            Quaternion.Euler(90, 0, 0));

        // ── VPS Anchor Root (replaces ARLocation on mobile) ────────────────
        // Parent all AR content here. FountainStopManager moves this once
        // the VPS2 anchor reaches Tracking state.
        var anchorRoot = new GameObject("VpsAnchorRoot");
        anchorRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // ── Fountain Mesh Container ────────────────────────────────────────
        var meshContainer = new GameObject("MeshContainer");
        meshContainer.transform.SetParent(anchorRoot.transform);
        meshContainer.transform.localPosition = Vector3.zero;
        meshContainer.transform.localRotation = Quaternion.identity;
        meshContainer.transform.localScale = Vector3.one;

        var fountainMesh = new GameObject("University Center Fountain");
        fountainMesh.transform.SetParent(meshContainer.transform);
        fountainMesh.transform.localPosition = Vector3.zero;
        fountainMesh.transform.localRotation = Quaternion.identity;
        fountainMesh.transform.localScale = Vector3.one;

        // ── Hut Spawn Points (positions from mobile scene) ─────────────────
        // HutSpawn1: pos(-4.35, 0, 6.72) rot(y=-0.918, w=0.395)
        // HutSpawn2: pos(5.52,  0, -6.7) rot(y=0.577,  w=-0.816)
        // HutSpawn3: pos(7.54,  0, 8.15) rot(y=0.956,  w=-0.292)
        CreateHutSpawn(anchorRoot.transform, "HutSpawn1",
            new Vector3(-4.35f, 0f, 6.72f),
            new Quaternion(0, -0.91852576f, 0, 0.39536113f));
        CreateHutSpawn(anchorRoot.transform, "HutSpawn2",
            new Vector3(5.52f, 0f, -6.7f),
            new Quaternion(0, 0.5775016f, 0, -0.8163896f));
        CreateHutSpawn(anchorRoot.transform, "HutSpawn3",
            new Vector3(7.54f, 0f, 8.15f),
            new Quaternion(0, 0.95618737f, 0, -0.29275545f));

        // ── Art Banner — OhloneVillage image quad ──────────────────────────
        // pos(2.65, 6.74, 1.51) rot(y=0.458, w=0.888) scale(0.554)
        var artBanner = GameObject.CreatePrimitive(PrimitiveType.Quad);
        artBanner.name = "OhloneVillage";
        artBanner.transform.SetParent(anchorRoot.transform);
        artBanner.transform.localPosition = new Vector3(2.65f, 6.74f, 1.51f);
        artBanner.transform.localRotation = new Quaternion(-0f, 0.4582032f, -0f, 0.8888475f);
        artBanner.transform.localScale = Vector3.one * 0.55377f;
        artBanner.AddComponent<QuestBillboard>();
        Object.DestroyImmediate(artBanner.GetComponent<Collider>());

        // ── Author Credit quad ─────────────────────────────────────────────
        // pos(2.65, 2.66, 1.47) rot(y=0.458, w=0.888) scale(0.821)
        var creditBanner = GameObject.CreatePrimitive(PrimitiveType.Quad);
        creditBanner.name = "AuthorCredit";
        creditBanner.transform.SetParent(anchorRoot.transform);
        creditBanner.transform.localPosition = new Vector3(2.65f, 2.66f, 1.47f);
        creditBanner.transform.localRotation = new Quaternion(0, 0.45819962f, 0, 0.8888493f);
        creditBanner.transform.localScale = Vector3.one * 0.8207552f;
        creditBanner.AddComponent<QuestBillboard>();
        Object.DestroyImmediate(creditBanner.GetComponent<Collider>());

        // ── Audio Sources ──────────────────────────────────────────────────
        var narrationGO = new GameObject("NarrationAudioSource");
        narrationGO.transform.SetParent(anchorRoot.transform);
        narrationGO.transform.localPosition = Vector3.zero;
        var narrationAudio = narrationGO.AddComponent<AudioSource>();
        narrationAudio.playOnAwake = false;
        narrationAudio.spatialBlend = 0f; // 2D — narration plays in headphones
        narrationAudio.loop = false;

        var soundscapeGO = new GameObject("VillageSoundscapeAudioSource");
        soundscapeGO.transform.SetParent(anchorRoot.transform);
        soundscapeGO.transform.localPosition = Vector3.zero;
        var soundscapeAudio = soundscapeGO.AddComponent<AudioSource>();
        soundscapeAudio.playOnAwake = false;
        soundscapeAudio.spatialBlend = 0.7f; // mostly spatial
        soundscapeAudio.loop = true;

        var fireGO = new GameObject("FireAudioSource");
        fireGO.transform.SetParent(anchorRoot.transform);
        fireGO.transform.localPosition = Vector3.zero;
        var fireAudio = fireGO.AddComponent<AudioSource>();
        fireAudio.playOnAwake = false;
        fireAudio.spatialBlend = 1f; // fully spatial — crackle comes from huts
        fireAudio.loop = true;

        // ── Scene Manager ──────────────────────────────────────────────────
        var managerGO = new GameObject("FountainStopManager");
        var manager = managerGO.AddComponent<FountainStopManager>();

        // ── World Space Captions Canvas ────────────────────────────────────
        // Positioned in front of where user faces anchor, at eye height
        var canvasGO = new GameObject("CaptionsCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();
        canvasGO.transform.SetParent(anchorRoot.transform);
        canvasGO.transform.localPosition = new Vector3(0f, 1.5f, 2.5f);
        canvasGO.transform.localRotation = Quaternion.identity;
        canvasGO.transform.localScale = Vector3.one * 0.002f; // world-space scale
        var rectTransform = canvasGO.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(600, 400);
        canvasGO.AddComponent<QuestCaptions>();
        canvasGO.SetActive(false); // hidden until user opens it

        // Background panel
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.85f);
        var panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        // Close button
        var closeBtnGO = new GameObject("CloseButton");
        closeBtnGO.transform.SetParent(canvasGO.transform, false);
        var closeBtnRect = closeBtnGO.AddComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1, 1);
        closeBtnRect.anchorMax = new Vector2(1, 1);
        closeBtnRect.anchoredPosition = new Vector2(-30, -30);
        closeBtnRect.sizeDelta = new Vector2(50, 50);
        closeBtnGO.AddComponent<Image>().color = Color.white;
        closeBtnGO.AddComponent<Button>();

        // ── EventSystem ────────────────────────────────────────────────────
        var eventSystemGO = new GameObject("EventSystem");
        eventSystemGO.AddComponent<EventSystem>();
        eventSystemGO.AddComponent<StandaloneInputModule>();

        // ── Save scene ─────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[FountainSceneBuilder] Scene saved to {ScenePath}");
        Debug.Log("[FountainSceneBuilder] NEXT STEPS in Inspector:");
        Debug.Log("  1. Assign VpsAnchorRoot to FountainStopManager._vpsAnchorRoot");
        Debug.Log("  2. Assign Hut prefab (Hut_With_Fire_Particles_Light) to FountainStopManager._hutPrefab");
        Debug.Log("  3. Assign HutSpawn1/2/3 to FountainStopManager._hutSpawnPoints");
        Debug.Log("  4. Assign audio clips to the AudioSource components");
        Debug.Log("  5. Assign OhloneVillage-New.png texture to the OhloneVillage quad material");
        Debug.Log("  6. Assign CaptionsCanvas to FountainStopManager._captionsPanel");
        Debug.Log("  7. Add XR Interaction Toolkit ray interactors or connect button OnClick events");
        Debug.Log("  8. Drag VPS2LocalizationSample's anchor-tracking event to FountainStopManager.OnAnchorTracking()");
    }

    private static GameObject CreateHutSpawn(Transform parent, string name, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = Vector3.one;
        return go;
    }
}
#endif
