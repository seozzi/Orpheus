using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OrpheusVR.Editor
{
    /// <summary>
    /// Tools > Orpheus VR > Setup VR Experience
    /// v1 동굴 맵(Orpheus_Underworld)을 복제한 Orpheus_VR_Main 씬에 VR 리그와 체험 흐름을 조립.
    /// 원본 씬은 건드리지 않음. 다시 실행하면 VR 오브젝트만 지우고 새로 조립(멱등).
    /// </summary>
    public static class OrpheusVRSetup
    {
        const string Source = "Assets/OrpheusVR/Scenes/Orpheus_Underworld.unity";
        const string Target = "Assets/OrpheusVR/Scenes/Orpheus_VR_Main.unity";
        const string RigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string MatFolder = "Assets/OrpheusVR/Materials";
        const string ExperienceRoot = "ORPHEUS - VR Experience";
        const string PlayerRoot = "Orpheus Player (VR)";

        [MenuItem("Tools/Orpheus VR/Setup VR Experience")]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(Target) && !AssetDatabase.CopyAsset(Source, Target))
            {
                Debug.LogError("[OrpheusVR] 씬 복제 실패: " + Source);
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Single);
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == ExperienceRoot || go.name == PlayerRoot) Object.DestroyImmediate(go);

            List<Transform> all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
            Transform Find(string n) => all.FirstOrDefault(t => t && t.name == n);

            OrpheusCaveExperience old = Object.FindFirstObjectByType<OrpheusCaveExperience>(FindObjectsInactive.Include);
            if (old == null || old.route == null || old.route.Length < 2)
            {
                Debug.LogError("[OrpheusVR] 기존 맵의 OrpheusCaveExperience(경로 정보)를 찾지 못함. v1 맵 씬인지 확인");
                return;
            }
            old.gameObject.SetActive(false); // 데스크톱 미리보기 플레이어는 꺼 두고 보관

            var report = new List<string>();

            // ── 플레이어: CharacterController + OVRCameraRig ──────────────
            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (rigPrefab == null)
            {
                Debug.LogError("[OrpheusVR] OVRCameraRig 프리팹 없음: " + RigPrefab + " (Meta XR Core SDK 설치 확인)");
                return;
            }
            Transform spawn = Find("Player spawn");
            var playerGo = new GameObject(PlayerRoot);
            playerGo.transform.position = spawn ? spawn.position : old.transform.position;
            var cc = playerGo.AddComponent<CharacterController>();
            cc.radius = 0.25f; cc.height = 1.7f; cc.center = new Vector3(0f, 0.88f, 0f);
            cc.slopeLimit = 50f; cc.stepOffset = 0.35f; cc.skinWidth = 0.03f; cc.minMoveDistance = 0f;

            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, playerGo.transform);
            rigGo.transform.localPosition = Vector3.zero;
            rigGo.transform.localRotation = Quaternion.identity;
            var rig = rigGo.GetComponent<OVRCameraRig>();
            var manager = rigGo.GetComponent<OVRManager>();
            if (manager)
            {
                var so = new SerializedObject(manager);
                var origin = so.FindProperty("_trackingOriginType");
                if (origin != null) { origin.intValue = (int)OVRManager.TrackingOrigin.FloorLevel; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            Transform centerEye = rigGo.transform.Find("TrackingSpace/CenterEyeAnchor");
            if (centerEye)
            {
                var cam = centerEye.GetComponent<Camera>();
                if (cam) { cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f; }
                if (!rigGo.GetComponentInChildren<AudioListener>(true)) centerEye.gameObject.AddComponent<AudioListener>();
                var fade = centerEye.GetComponent<OVRScreenFade>();
                if (!fade) fade = centerEye.gameObject.AddComponent<OVRScreenFade>();
                fade.fadeOnStart = false;
            }
            var player = playerGo.AddComponent<OrpheusPlayer>();
            player.rig = rig;
            player.head = centerEye;
            report.Add("플레이어: " + PlayerRoot + " (CharacterController + OVRCameraRig, 바닥 기준 추적)");

            // ── 체험 흐름 ─────────────────────────────────────
            var exp = new GameObject(ExperienceRoot);
            var route = exp.AddComponent<OrpheusRoute>();
            route.points = old.route;
            route.stations = old.routeStations != null && old.routeStations.Length == old.route.Length ? old.routeStations : null;
            Transform fork = Find("Fork - no guidance initially"), deadEnd = Find("Dead end / retreat trigger");
            if (fork && deadEnd && !old.loopLayout)
            {
                var branch = new GameObject("Dead-end branch").transform;
                branch.SetParent(exp.transform, false);
                Transform a = Point(branch, "Branch 0 (fork)", fork.position);
                Transform b = Point(branch, "Branch 1", new Vector3(9f, Mathf.Lerp(fork.position.y, deadEnd.position.y, 0.5f), 52f));
                Transform c = Point(branch, "Branch 2 (dead end)", deadEnd.position);
                route.deadEndBranch = new[] { a, b, c };
            }

            var judge = exp.AddComponent<LookBackJudge>();
            judge.player = player; judge.route = route;

            var subtitle = exp.AddComponent<OrpheusSubtitle>();
            subtitle.player = player;

            var recorder = exp.AddComponent<TrajectoryRecorder>();
            recorder.player = player; recorder.judge = judge;
            recorder.lineMaterial = MakeMaterial(MatFolder + "/VR Trace line.mat", new Color(1f, .85f, .6f, 1f), false);
            recorder.markerMaterial = MakeMaterial(MatFolder + "/VR Trace stop.mat", new Color(.75f, .95f, 1f, .75f), true);
            Material soul = MakeMaterial(MatFolder + "/VR Eurydice soul (placeholder).mat", new Color(.65f, .9f, 1f, .45f), true);

            var flow = exp.AddComponent<OrpheusFlow>();
            flow.player = player; flow.judge = judge; flow.route = route; flow.subtitle = subtitle; flow.recorder = recorder;
            flow.spawn = spawn;
            flow.hades = Find("Hades apparition - replace with face asset") ?? Find("Hades face / dialogue anchor");
            flow.eurydiceEnter = Find("Eurydice enters from left");
            flow.handhold = Find("Handhold interaction");
            flow.lyre = old.lyre ? old.lyre : Find("REF_Lyre");
            flow.alternateGuide = old.alternateGuide;
            flow.deadEnd = deadEnd;
            flow.eurydiceFall = Find("Eurydice fall / sound anchor");
            flow.water = old.water;
            flow.resonanceStone = old.resonanceStone ? old.resonanceStone : Find("REF_Resonance");
            flow.pathStones = Enumerable.Range(0, 6).Select(i => Find("Resonating path stone " + i)).Where(t => t).ToArray();
            flow.waitChoice = old.waitChoice ? old.waitChoice : Find("REF_Wait");
            flow.leaveChoice = old.leaveChoice ? old.leaveChoice : Find("REF_Leave");
            flow.reunion = Find("Eurydice recovery and reunion");
            flow.endingGaze = Find("Ending gaze / journey trace");

            // 에우리디케 임시 형상: 반투명 캡슐 (2D 홀로그램 PNG가 오면 교체)
            var figure = new GameObject("Eurydice (placeholder - replace with hologram)");
            figure.transform.SetParent(exp.transform, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(figure.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            body.transform.localScale = new Vector3(0.45f, 0.85f, 0.45f);
            body.GetComponent<Renderer>().sharedMaterial = soul;
            figure.AddComponent<FaceViewer>();
            figure.SetActive(false);
            flow.eurydiceFigure = figure;

            if (flow.handhold)
            {
                var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(glow.GetComponent<Collider>());
                glow.name = "Handhold glow";
                glow.transform.SetParent(exp.transform, false);
                glow.transform.position = flow.handhold.position;
                glow.transform.localScale = Vector3.one * 0.12f;
                glow.GetComponent<Renderer>().sharedMaterial = soul;
                glow.SetActive(false);
                flow.handholdGlow = glow;
            }

            // 비어 있는 참조 보고
            foreach (var f in typeof(OrpheusFlow).GetFields())
            {
                if (!typeof(Object).IsAssignableFrom(f.FieldType)) continue;
                if ((Object)f.GetValue(flow) == null) report.Add("⚠ 연결 안 됨: OrpheusFlow." + f.Name);
            }

            // ── 빌드 씬 목록 ───────────────────────────────────
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Target, true),
                new EditorBuildSettingsScene(Source, false),
            };

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = exp;
            report.Add("씬 저장: " + Target + " (빌드 씬 0번)");
            report.Add("다음: Meta > Tools > Project Setup Tool에서 Android 탭 Fix All (OpenXR 로더), 그다음 헤드셋 빌드");
            Debug.Log("[OrpheusVR] VR 체험 조립 완료\n- " + string.Join("\n- ", report));
        }

        static Transform Point(Transform parent, string name, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = position;
            return t;
        }

        static Material MakeMaterial(string path, Color color, bool transparent)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) { Debug.LogWarning("[OrpheusVR] URP Unlit 셰이더 없음"); return null; }
            var m = new Material(shader);
            m.SetColor("_BaseColor", color);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
