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
    /// Tools > Orpheus VR > Apply Key Visual
    /// 키비주얼(어두운 청록 동굴, 젖은 바위에 맺히는 출구의 따뜻한 역광, 바닥의 푸른 빛 물길)을 Orpheus_VR_Main에 적용.
    /// - 바닥 주황 길 표시 끄기
    /// - 동굴 벽을 고폴리 + 바위 결 노이즈로 다시 생성(보이는 메시만, 충돌은 기존 저폴리 유지), 바위 고폴리 교체
    /// - 젖은 현무암 재질(광택 + 생성 노멀맵, 하늘 반사 끔)
    /// - 출구 키라이트·빛 카드·빛줄기, 통로 푸른 바닥광, 주변광·안개
    /// - 푸른 물길 리본, 반딧불, 물안개, 벽 반짝임 (갈림길 구간은 '다른 길 점등'에 묶음)
    /// 원본 맵 씬은 건드리지 않음. 다시 실행하면 이 도구가 만든 것만 지우고 새로 만듦(멱등).
    /// Quest 3: 포스트프로세싱·볼류메트릭·실시간 반사 없음, 그림자는 기존 저폴리 셸만.
    /// </summary>
    public static class OrpheusKeyVisual
    {
        const string ScenePath = "Assets/OrpheusVR/Scenes/Orpheus_VR_Main.unity";
        const string RootName = "ORPHEUS - Key Visual";
        const string Prefix = "KV ";
        const string MeshDir = "Assets/OrpheusVR/Meshes/KeyVisual";
        const string MatDir = "Assets/OrpheusVR/Materials/KeyVisual";
        const string TexDir = "Assets/OrpheusVR/Textures/KeyVisual";

        // 고폴리 셸 해상도 (m). 기존 1m → 0.36m
        const float Step = 0.36f;
        const int TileCells = 22; // 타일 한 변 = 22칸 × 0.36m ≈ 7.9m. 전역 격자에 맞춰 타일 경계 정점이 정확히 일치(틈 없음)
        const float GridX0 = -12f, GridX1 = 43f, GridZ0 = -12f, GridZ1 = 100f, GridY0 = -2f, GridY1 = 18f;

        static readonly Color StreamBlue = new Color(0.45f, 0.75f, 1f);
        static readonly Color Firefly = new Color(0.6f, 0.88f, 1f);
        static readonly Color FireflyGold = new Color(1f, 0.82f, 0.5f);
        static readonly Color ExitWarm = new Color(1f, 0.8f, 0.52f);

        static Transform root;
        static Material wetRock, glowAdd, mistAlpha, streamAdd;
        static Texture2D dotTex, mistTex, streamTex;

        [MenuItem("Tools/Orpheus VR/Apply Key Visual (Lighting, Particles, HD Cave)")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            try
            {
                EditorUtility.DisplayProgressBar("Orpheus Key Visual", "준비", 0f);
                Directory.CreateDirectory(MeshDir); Directory.CreateDirectory(MatDir); Directory.CreateDirectory(TexDir);
                OrpheusCaveBuilder.SetupSegments();
                InitNoise();

                // 이전 적용분 정리
                foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == RootName) Object.DestroyImmediate(go);
                foreach (Transform t in AllTransforms(scene).Where(t => t && t.name.StartsWith(Prefix)).ToList()) if (t) Object.DestroyImmediate(t.gameObject);
                root = new GameObject(RootName).transform;

                var report = new List<string>();
                Textures();
                MaterialsSetup();
                report.Add(HidePathTrack(scene));
                report.Add(HdShell(scene));
                report.Add(HdRocks(scene));
                report.Add(Lighting(scene));
                Transform altGuide = AllTransforms(scene).FirstOrDefault(t => t.name == "S2-2 - Alternative route reveal");
                List<Vector3> route = RoutePoints(scene);
                report.Add(Stream(route, altGuide));
                report.Add(Particles(route, altGuide));

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[OrpheusVR] 키비주얼 적용 완료\n- " + string.Join("\n- ", report));
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static IEnumerable<Transform> AllTransforms(Scene scene) =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));

        // ─────────────────────────────── 1. 길 표시 ───────────────────────────────
        static string HidePathTrack(Scene scene)
        {
            int n = 0;
            foreach (Transform t in AllTransforms(scene))
                if (t.name == "Amber floor inlay") { t.gameObject.SetActive(false); n++; }
            return "바닥 주황 길 표시 끔: " + n + "개";
        }

        // ─────────────────────────────── 2. 고폴리 셸 ───────────────────────────────
        static float Detail(Vector3 p)
        {
            float g = OrpheusCaveBuilder.Ground(p.z);
            float wall = Mathf.Clamp01((p.y - g - 0.2f) / 0.9f);
            // 벽: 큰 각진 면(릿지) + 중간 결 + 잔결 + 수평 층리
            // (격자 0.36m가 표현할 수 있는 크기까지만. 더 잔 결은 노멀맵이 담당)
            float d = (Ridge(p * 0.45f) - 0.55f) * 0.65f
                      + Noise(p * 1.1f) * 0.16f
                      + Noise(new Vector3(p.x * 0.3f, p.y * 1.25f, p.z * 0.3f)) * 0.1f;
            // 바닥: 젖은 돌판 같은 요철. 충돌 바닥보다 살짝 아래로만 (발이 묻히지 않게)
            float floorBump = Ridge(p * 0.9f) * 0.06f - 0.06f;
            return Mathf.Lerp(floorBump, d, wall);
        }

        static float VisualField(Vector3 p) => OrpheusCaveBuilder.Field(p) + Detail(p);

        static Vector3 VisualNormal(Vector3 p)
        {
            const float e = 0.1f; // 격자보다 잔 변화는 무시해 면과 법선이 어긋나지 않게
            return -new Vector3(
                VisualField(p + Vector3.right * e) - VisualField(p - Vector3.right * e),
                VisualField(p + Vector3.up * e) - VisualField(p - Vector3.up * e),
                VisualField(p + Vector3.forward * e) - VisualField(p - Vector3.forward * e)).normalized;
        }

        static readonly int[,] Tets = { { 0, 5, 1, 6 }, { 0, 1, 2, 6 }, { 0, 2, 3, 6 }, { 0, 3, 7, 6 }, { 0, 7, 4, 6 }, { 0, 4, 5, 6 } };
        static readonly Vector3Int[] Corner = { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 1), new Vector3Int(0, 1, 1) };

        static string HdShell(Scene scene)
        {
            // 기존 저폴리 셸: 충돌 + 그림자만 담당 (보이지 않음)
            int lowCount = 0;
            foreach (Transform t in AllTransforms(scene))
            {
                if (!t.name.StartsWith("CaveShell_")) continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r) { r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; lowCount++; }
            }

            // 이전에 만든 고폴리 타일 메시 정리 (타일 구성이 바뀌어도 쓰레기 에셋이 남지 않게)
            foreach (string guid in AssetDatabase.FindAssets("KV_CaveHD_ t:Mesh", new[] { MeshDir }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

            var parent = new GameObject("HD cave shell (visual only)").transform;
            parent.SetParent(root, false);
            int tiles = 0; long tris = 0;
            int cellsX = Mathf.CeilToInt((GridX1 - GridX0) / Step), cellsZ = Mathf.CeilToInt((GridZ1 - GridZ0) / Step);
            int nx = Mathf.CeilToInt((float)cellsX / TileCells), nz = Mathf.CeilToInt((float)cellsZ / TileCells), done = 0;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    int gx = ix * TileCells, gz = iz * TileCells;
                    EditorUtility.DisplayProgressBar("Orpheus Key Visual", "고폴리 동굴 생성 " + (done + 1) + "/" + (nx * nz), (float)done++ / (nx * nz));
                    Mesh m = BuildTile(gx, gz, Mathf.Min(TileCells, cellsX - gx), Mathf.Min(TileCells, cellsZ - gz));
                    if (m == null) continue;
                    string path = MeshDir + "/" + m.name + ".asset";
                    var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (old) { EditorUtility.CopySerialized(m, old); m = old; }
                    else AssetDatabase.CreateAsset(m, path);
                    var go = new GameObject(m.name);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = wetRock;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = true;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    WallGlints(go, mr);
                    tiles++; tris += m.triangles.Length / 3;
                }
            return "고폴리 동굴: 타일 " + tiles + "개, " + tris.ToString("N0") + " 폴리곤 (기존 저폴리 셸 " + lowCount + "개는 충돌·그림자 전용)";
        }

        static Vector3 GridPoint(int gi, int gj, int gk) => new Vector3(GridX0 + gi * Step, GridY0 + gj * Step, GridZ0 + gk * Step);

        static Mesh BuildTile(int gx, int gz, int sx, int sz)
        {
            int sy = Mathf.CeilToInt((GridY1 - GridY0) / Step);
            var val = new float[sx + 1, sy + 1, sz + 1];
            bool any = false, neg = false, pos = false;
            for (int i = 0; i <= sx; i++)
                for (int k = 0; k <= sz; k++)
                {
                    // 바닥 아래 깊은 곳·천장 위는 계산 생략
                    for (int j = 0; j <= sy; j++)
                    {
                        Vector3 gp = GridPoint(gx + i, j, gz + k);
                        float v = OrpheusCaveBuilder.Field(gp);
                        if (Mathf.Abs(v) < 1.2f) v += Detail(gp);
                        val[i, j, k] = v;
                        if (v < 0) neg = true; else pos = true;
                    }
                }
            any = neg && pos;
            if (!any) return null;

            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var weld = new Dictionary<Vector3Int, int>();
            var p = new Vector3[8]; var f = new float[8];
            for (int i = 0; i < sx; i++)
                for (int j = 0; j < sy; j++)
                    for (int k = 0; k < sz; k++)
                    {
                        int n = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            Vector3Int o = Corner[c];
                            f[c] = val[i + o.x, j + o.y, k + o.z];
                            p[c] = GridPoint(gx + i + o.x, j + o.y, gz + k + o.z);
                            if (f[c] < 0) n++;
                        }
                        if (n == 0 || n == 8) continue;
                        for (int t = 0; t < 6; t++)
                        {
                            var ins = new List<int>(4); var outs = new List<int>(4);
                            for (int q = 0; q < 4; q++) { int c = Tets[t, q]; if (f[c] < 0) ins.Add(c); else outs.Add(c); }
                            if (ins.Count == 0 || outs.Count == 0) continue;
                            System.Func<int, int, Vector3> cut = (a, b) => Vector3.Lerp(p[a], p[b], f[a] / (f[a] - f[b]));
                            // 앞면 = 빈 공간(음수) 쪽. 법선 대신 격자 부호로 방향을 정해 뒤집힌 면(구멍)이 생기지 않게
                            Vector3 air = Vector3.zero, solid = Vector3.zero;
                            foreach (int c in ins) air += p[c]; foreach (int c in outs) solid += p[c];
                            Vector3 toAir = air / ins.Count - solid / outs.Count;
                            if (ins.Count == 1) Tri(cut(ins[0], outs[0]), cut(ins[0], outs[1]), cut(ins[0], outs[2]), toAir, verts, norms, uvs, tris, weld);
                            else if (ins.Count == 3) Tri(cut(outs[0], ins[0]), cut(outs[0], ins[1]), cut(outs[0], ins[2]), toAir, verts, norms, uvs, tris, weld);
                            else
                            {
                                Vector3 a = cut(ins[0], outs[0]), b = cut(ins[0], outs[1]), c2 = cut(ins[1], outs[0]), d = cut(ins[1], outs[1]);
                                Tri(a, b, c2, toAir, verts, norms, uvs, tris, weld); Tri(b, d, c2, toAir, verts, norms, uvs, tris, weld);
                            }
                        }
                    }
            if (tris.Count == 0) return null;
            var m = new Mesh { name = "KV_CaveHD_" + gx + "_" + gz, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 toAir, List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t, Dictionary<Vector3Int, int> weld)
        {
            if ((b - a).sqrMagnitude < 1e-8f || (c - a).sqrMagnitude < 1e-8f || (c - b).sqrMagnitude < 1e-8f) return;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), toAir) < 0) { var s = b; b = c; c = s; }
            t.Add(Index(a, v, n, u, weld)); t.Add(Index(b, v, n, u, weld)); t.Add(Index(c, v, n, u, weld));
        }

        static Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 500f), Mathf.RoundToInt(p.y * 500f), Mathf.RoundToInt(p.z * 500f));

        static int Index(Vector3 p, List<Vector3> v, List<Vector3> n, List<Vector2> u, Dictionary<Vector3Int, int> weld)
        {
            var key = Key(p);
            if (weld.TryGetValue(key, out int i)) return i;
            i = v.Count;
            v.Add(p); n.Add(VisualNormal(p));
            u.Add(new Vector2(p.x + p.y * 0.7f, p.z + p.y * 0.9f) * 0.22f);
            weld[key] = i;
            return i;
        }

        // ─────────────────────────────── 3. 고폴리 바위 ───────────────────────────────
        static string HdRocks(Scene scene)
        {
            var variants = new Mesh[3];
            for (int i = 0; i < 3; i++) variants[i] = RockMesh(i);
            int swapped = 0, mats = 0;
            string[] rockMats = { "Slate - cave stone", "Dark basalt", "Basalt floor" };
            foreach (Transform t in AllTransforms(scene))
            {
                if (t.IsChildOf(root)) continue;
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<MeshRenderer>();
                if (mf && mf.sharedMesh && (mf.sharedMesh.name == "Reusable fractured boulder" || mf.sharedMesh.name == "FracturedBoulder" || mf.sharedMesh.name.StartsWith("KV_Boulder")))
                {
                    // 충돌체는 기존 저폴리 메시를 그대로 참조 (MeshCollider.sharedMesh는 별도)
                    int h = Mathf.Abs((t.position.x * 73.1f + t.position.z * 19.7f + t.position.y * 7.3f).GetHashCode());
                    mf.sharedMesh = variants[h % 3];
                    swapped++;
                }
                if (mr && !t.name.StartsWith("CaveShell_"))
                {
                    var arr = mr.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] && rockMats.Contains(arr[i].name)) { arr[i] = wetRock; changed = true; }
                    if (changed) { mr.sharedMaterials = arr; mats++; }
                }
            }
            return "바위 고폴리 교체: " + swapped + "개 (3종, 각 " + (variants[0].triangles.Length / 3) + " 폴리곤), 젖은 재질 적용: " + mats + "개";
        }

        static Mesh RockMesh(int variant)
        {
            // 정이십면체 3번 분할 + 노이즈 변위
            var verts = new List<Vector3>(); var tris = new List<int>();
            float tt = (1f + Mathf.Sqrt(5f)) / 2f;
            verts.AddRange(new[] {
                new Vector3(-1, tt, 0), new Vector3(1, tt, 0), new Vector3(-1, -tt, 0), new Vector3(1, -tt, 0),
                new Vector3(0, -1, tt), new Vector3(0, 1, tt), new Vector3(0, -1, -tt), new Vector3(0, 1, -tt),
                new Vector3(tt, 0, -1), new Vector3(tt, 0, 1), new Vector3(-tt, 0, -1), new Vector3(-tt, 0, 1) }.Select(v => v.normalized));
            tris.AddRange(new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 });
            for (int s = 0; s < 3; s++)
            {
                var mid = new Dictionary<long, int>(); var next = new List<int>();
                System.Func<int, int, int> M = (a, b) =>
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int r)) return r;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized); mid[key] = verts.Count - 1; return verts.Count - 1;
                };
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2], ab = M(a, b), bc = M(b, c), ca = M(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            Vector3 off = new Vector3(variant * 17.3f, variant * 5.1f, variant * 9.7f);
            var uv = new List<Vector2>();
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 d = verts[i];
                float r = 1f + (Ridge(d * 1.4f + off) - 0.5f) * 0.32f + Noise(d * 3.2f + off) * 0.08f + Noise(d * 7f + off) * 0.025f;
                // 아래쪽은 평평하게 (바닥에 앉도록)
                Vector3 v = d * r;
                if (v.y < -0.55f) v.y = Mathf.Lerp(v.y, -0.55f, 0.7f);
                verts[i] = v;
                uv.Add(new Vector2(v.x + v.y * 0.7f, v.z + v.y * 0.9f) * 0.5f);
            }
            var m = new Mesh { name = "KV_Boulder_" + variant };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.SetUVs(0, uv);
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            string path = MeshDir + "/" + m.name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { EditorUtility.CopySerialized(m, old); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ─────────────────────────────── 4. 조명 ───────────────────────────────
        static string Lighting(Scene scene)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.075f, 0.1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.02f, 0.04f, 0.055f);
            RenderSettings.fogDensity = 0.022f;
            RenderSettings.reflectionIntensity = 0.25f;

            int blue = 0, gold = 0;
            Light exitLight = null;
            foreach (Transform t in AllTransforms(scene))
            {
                var l = t.GetComponent<Light>();
                if (!l) continue;
                if (t.name.StartsWith("Passage fill "))
                {
                    int i = int.Parse(t.name.Substring("Passage fill ".Length));
                    Vector3 wp = t.position;
                    float g = OrpheusCaveBuilder.Ground(wp.z);
                    if (i >= 10) { l.color = new Color(1f, 0.72f, 0.42f); l.intensity = 2.2f; l.range = 11f; wp.y = g + 3f; gold++; }
                    else if (i % 3 == 2) { l.color = new Color(0.95f, 0.68f, 0.4f); l.intensity = 1.3f; l.range = 8f; wp.y = g + 3.2f; gold++; }
                    else { l.color = new Color(0.32f, 0.55f, 1f); l.intensity = 1.8f; l.range = 8.5f; wp.y = g + 0.6f; blue++; }
                    t.position = wp;
                    l.shadows = LightShadows.None;
                }
                if (t.name == "Daylight entering exit") exitLight = l;
            }

            string exit = "출구 조명 없음";
            if (exitLight)
            {
                exitLight.color = ExitWarm; exitLight.intensity = 18f; exitLight.range = 26f; exitLight.shadows = LightShadows.None;
                Vector3 e = exitLight.transform.position;
                Vector3 inward = (OrpheusCaveBuilder.route[9] - OrpheusCaveBuilder.route[11]); inward.y = 0; inward.Normalize();
                // 통로 안쪽으로 쏘는 키라이트 (그림자 없음)
                var spotGo = new GameObject("Exit key spot");
                spotGo.transform.SetParent(root, false);
                spotGo.transform.position = e + Vector3.up * 1.5f - inward * 1f;
                spotGo.transform.rotation = Quaternion.LookRotation(inward + Vector3.down * 0.18f);
                var spot = spotGo.AddComponent<Light>();
                spot.type = LightType.Spot; spot.color = ExitWarm; spot.intensity = 26f; spot.range = 34f; spot.spotAngle = 75f; spot.innerSpotAngle = 30f; spot.shadows = LightShadows.None;
                ExitGlow(e, inward);
                exit = "출구 키라이트 + 스포트 + 빛 카드·빛줄기";
            }
            return "조명: 푸른 바닥광 " + blue + "개, 금빛 " + gold + "개, " + exit + ", 주변광·안개 어두운 청록";
        }

        static void ExitGlow(Vector3 exitPos, Vector3 inward)
        {
            var g = new GameObject("Exit glow").transform;
            g.SetParent(root, false);
            g.position = exitPos + Vector3.up * 1.4f - inward * 0.8f; // 출구 입구에 걸치게 → 바위 가장자리까지 빛이 번짐
            // 출구를 꽉 채우는 따뜻한 빛 카드 (항상 플레이어를 향함)
            var card = Quad("Exit light card", g, Vector3.zero, new Vector2(12f, 12f), glowAdd, new Color(1f, 0.88f, 0.66f, 1f));
            card.AddComponent<FaceViewer>();
            // 가짜 빛줄기: 출구에서 통로 안쪽으로 뻗는 판들을 빛 방향 축을 중심으로 부채꼴로 배치.
            // 통로 안에서 출구를 보면 판의 옆면이 출구에서 퍼져 나오는 빛줄기처럼 보임
            for (int i = 0; i < 5; i++)
            {
                var ray = new GameObject("God ray " + i);
                ray.transform.SetParent(g, false);
                ray.transform.localPosition = Vector3.Cross(Vector3.up, inward) * ((i % 2 == 0 ? 1 : -1) * 0.3f * i) + Vector3.up * (1.2f - i * 0.15f);
                ray.transform.rotation = Quaternion.LookRotation(inward + Vector3.down * (0.12f + i * 0.03f)) * Quaternion.Euler(0f, 0f, 15f + i * 36f);
                var mf = ray.AddComponent<MeshFilter>();
                mf.sharedMesh = RayMesh(1.2f + i * 0.2f, 11f + i * 1.5f);
                var mr = ray.AddComponent<MeshRenderer>();
                mr.sharedMaterial = streamAdd; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            }
        }

        static Mesh RayMesh(float width, float length)
        {
            string path = MeshDir + "/KV_GodRay_" + width.ToString("0.0") + "_" + length.ToString("0") + ".asset";
            var exist = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (exist) return exist;
            var m = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            // 길이 방향(+Z)으로 넓어지며 흐려지는 판
            m.SetVertices(new List<Vector3> { new Vector3(-width * 0.3f, 0, 0), new Vector3(width * 0.3f, 0, 0), new Vector3(-width, 0, length), new Vector3(width, 0, length) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            Color a = new Color(1f, 0.82f, 0.55f, 0.16f), b = new Color(1f, 0.8f, 0.5f, 0f);
            m.SetColors(new List<Color> { a, a, b, b });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0); // 재질이 양면(Cull Off)
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ─────────────────────────────── 5. 푸른 물길 ───────────────────────────────
        static List<Vector3> RoutePoints(Scene scene)
        {
            Transform main = AllTransforms(scene).FirstOrDefault(t => t.name == "Walkable main route");
            if (main && main.childCount >= 2) return main.Cast<Transform>().Select(t => t.position).ToList();
            return OrpheusCaveBuilder.route.Select(r => new Vector3(r.x, OrpheusCaveBuilder.Ground(r.z), r.z)).ToList();
        }

        static bool ForkSegment(int i) => i == 5 || i == 6; // 갈림길: 처음엔 안내 없음

        static string Stream(List<Vector3> route, Transform altGuide)
        {
            var parent = new GameObject("Blue stream").transform;
            parent.SetParent(root, false);
            Physics.SyncTransforms();
            int pieces = 0; float total = 0f;
            float s = 0f;
            for (int i = 0; i < route.Count - 1; i++)
            {
                Vector3 a = route[i], b = route[i + 1];
                float len = Vector3.Distance(Flat(a), Flat(b));
                Transform holder = ForkSegment(i) && altGuide ? altGuide : parent;
                var core = new List<Vector3>();
                Vector3 dir = (Flat(b) - Flat(a)).normalized, side = Vector3.Cross(Vector3.up, dir);
                for (float d = 0f; d <= len + 0.01f; d += 0.2f)
                {
                    float u = s + d;
                    Vector3 p = Vector3.Lerp(a, b, d / len);
                    // 모서리 부드럽게: 끝 1.5m는 다음 구간 방향과 섞기
                    float wiggle = Mathf.Sin(u * 0.85f) * 0.38f + Mathf.Sin(u * 2.1f + 1.3f) * 0.12f;
                    p += side * wiggle;
                    core.Add(OnFloor(p) + Vector3.up * 0.025f);
                }
                s += len;
                total += len;
                var go = new GameObject(Prefix + "Stream " + i);
                go.transform.SetParent(holder, false);
                go.AddComponent<MeshFilter>().sharedMesh = Ribbon("KV_Stream_" + i, core);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = streamAdd; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                pieces++;
            }
            return "푸른 물길: " + pieces + "구간, " + total.ToString("0") + "m (갈림길 2구간은 '다른 길 점등'에 묶음)";
        }

        static Mesh Ribbon(string name, List<Vector3> pts)
        {
            // 두 겹: 가는 심(밝게) + 넓은 번짐(흐리게). UV.x = 폭 방향 → 가우시안 텍스처
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var t = new List<int>();
            foreach (var layer in new[] { (w: 0.11f, c: new Color(0.45f, 0.75f, 1f, 0.75f)), (w: 0.55f, c: new Color(0.3f, 0.55f, 1f, 0.22f)) })
            {
                int start = v.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 fwd = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]); fwd.y = 0; fwd.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, fwd);
                    float pulse = 0.75f + 0.25f * Mathf.Sin(i * 0.9f) * Mathf.Sin(i * 0.23f);
                    float end = Mathf.Clamp01(Mathf.Min(i, pts.Count - 1 - i) / 3f);
                    Color c = layer.c; c.a *= pulse * Mathf.Lerp(0.6f, 1f, end);
                    v.Add(pts[i] - side * layer.w); v.Add(pts[i] + side * layer.w);
                    uv.Add(new Vector2(0, i * 0.2f)); uv.Add(new Vector2(1, i * 0.2f));
                    col.Add(c); col.Add(c);
                    if (i > 0) { int k = start + i * 2; t.AddRange(new[] { k - 2, k, k - 1, k - 1, k, k + 1 }); }
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds();
            string path = MeshDir + "/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { EditorUtility.CopySerialized(m, old); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Vector3 OnFloor(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            p.y = OrpheusCaveBuilder.Ground(p.z);
            return p;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        // ─────────────────────────────── 6. 파티클 ───────────────────────────────
        static string Particles(List<Vector3> route, Transform altGuide)
        {
            var parent = new GameObject("Fireflies and mist").transform;
            parent.SetParent(root, false);
            int ff = 0, mist = 0;
            for (int i = 0; i < route.Count - 1; i++)
            {
                Vector3 a = route[i], b = route[i + 1];
                Vector3 mid = (a + b) * 0.5f;
                float len = Vector3.Distance(Flat(a), Flat(b));
                Quaternion rot = Quaternion.LookRotation(Flat(b) - Flat(a));
                Transform holder = ForkSegment(i) && altGuide ? altGuide : parent;
                float floorY = OnFloor(mid).y;

                FireflySystem(Prefix + "Fireflies " + i, holder, new Vector3(mid.x, floorY + 1.4f, mid.z), rot, len);
                ff++;
                MistSystem(Prefix + "Mist " + i, holder, new Vector3(mid.x, floorY + 0.25f, mid.z), rot, len);
                mist++;
            }
            return "반딧불 " + ff + "구간, 물안개 " + mist + "구간, 벽 반짝임 타일별 (갈림길 구간은 '다른 길 점등'에 묶음)";
        }

        static ParticleSystem NewSystem(string name, Transform parent, Vector3 pos, Quaternion rot, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.rotation = rot;
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        static void FireflySystem(string name, Transform parent, Vector3 pos, Quaternion rot, float len)
        {
            var ps = NewSystem(name, parent, pos, rot, glowAdd);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.duration = 8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            main.startColor = new ParticleSystem.MinMaxGradient(Firefly, FireflyGold);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.gravityModifier = -0.004f;
            var em = ps.emission; em.rateOverTime = Mathf.Clamp(len * 0.22f, 1f, 3.5f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(2.2f, 1.8f, len);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.35f; noise.scrollSpeed = 0.15f; noise.quality = ParticleSystemNoiseQuality.Low;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.35f, 0.4f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0.5f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        static void MistSystem(string name, Transform parent, Vector3 pos, Quaternion rot, float len)
        {
            var ps = NewSystem(name, parent, pos, rot, mistAlpha);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.duration = 12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.72f, 0.9f, 0.09f), new Color(0.7f, 0.82f, 0.95f, 0.14f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 12;
            var em = ps.emission; em.rateOverTime = Mathf.Clamp(len * 0.07f, 0.3f, 0.8f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3f, 0.3f, len);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.maxParticleSize = 0.6f; // 눈앞을 덮지 않게 (화면 비율)
            r.sortingFudge = 10f;
        }

        static void WallGlints(GameObject tile, MeshRenderer mr)
        {
            var ps = NewSystem(Prefix + "Wall glints", tile.transform, tile.transform.position, Quaternion.identity, glowAdd);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.8f, 1f, 0.9f), new Color(0.85f, 0.95f, 1f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 30;
            var em = ps.emission; em.rateOverTime = 4f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.MeshRenderer; sh.meshRenderer = mr; sh.normalOffset = 0.02f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        // ─────────────────────────────── 재질·텍스처 ───────────────────────────────
        static void MaterialsSetup()
        {
            wetRock = LoadOrCreate(MatDir + "/KV Wet basalt.mat", "Universal Render Pipeline/Lit");
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/OrpheusVR/Textures/cave-rock-albedo.png");
            if (albedo) wetRock.SetTexture("_BaseMap", albedo);
            wetRock.SetColor("_BaseColor", new Color(0.24f, 0.26f, 0.28f));
            wetRock.SetFloat("_Smoothness", 0.74f);
            wetRock.SetFloat("_Metallic", 0f);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/KV_RockNormal.png");
            if (normal) { wetRock.SetTexture("_BumpMap", normal); wetRock.SetFloat("_BumpScale", 0.55f); wetRock.EnableKeyword("_NORMALMAP"); }
            // 하늘(주간 스카이박스) 반사 끔 → 동굴 안이 파랗게 번들거리지 않음. 조명 하이라이트는 유지
            wetRock.SetFloat("_EnvironmentReflections", 0f); wetRock.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            wetRock.SetFloat("_SpecularHighlights", 1f); wetRock.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            wetRock.enableInstancing = true;
            EditorUtility.SetDirty(wetRock);

            glowAdd = Particle(MatDir + "/KV Glow additive.mat", dotTex, true);
            streamAdd = Particle(MatDir + "/KV Stream additive.mat", streamTex, true);
            mistAlpha = Particle(MatDir + "/KV Mist.mat", mistTex, false);
        }

        static Material LoadOrCreate(string path, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) { m.shader = Shader.Find(shader); return m; }
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material Particle(string path, Texture2D tex, bool additive)
        {
            var m = LoadOrCreate(path, "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Quad(string name, Transform parent, Vector3 localPos, Vector2 size, Material mat, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            string path = MeshDir + "/KV_Quad_" + size.x.ToString("0") + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                mesh = new Mesh { name = "KV_Quad_" + size.x.ToString("0") };
                float w = size.x * 0.5f, h = size.y * 0.5f;
                mesh.SetVertices(new List<Vector3> { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0) });
                mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
                mesh.SetColors(new List<Color> { color, color, color, color });
                mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return go;
        }

        static void Textures()
        {
            dotTex = MakeTex("KV_SoftDot", 64, (u, v) => { float d = (new Vector2(u, v) - Vector2.one * 0.5f).magnitude * 2f; float a = Mathf.Exp(-d * d * 5f) + Mathf.Exp(-d * d * 60f) * 0.6f; return new Color(1, 1, 1, Mathf.Clamp01(a) * Mathf.Clamp01((1f - d) * 4f)); }, false);
            mistTex = MakeTex("KV_Mist", 128, (u, v) =>
            {
                float d = (new Vector2(u, v) - Vector2.one * 0.5f).magnitude * 2f;
                float n = 0.65f + 0.35f * Noise(new Vector3(u * 5f, v * 5f, 3.3f)) + 0.15f * Noise(new Vector3(u * 11f, v * 11f, 7.1f));
                return new Color(1, 1, 1, Mathf.Clamp01((1f - d * d) * n));
            }, false);
            streamTex = MakeTex("KV_Stream", 64, (u, v) => { float d = Mathf.Abs(u - 0.5f) * 2f; return new Color(1, 1, 1, Mathf.Exp(-d * d * 7f) * Mathf.Clamp01((1f - d) * 6f)); }, false);
            RockNormal();
        }

        static Texture2D MakeTex(string name, int size, System.Func<float, float, Color> f, bool normal)
        {
            string path = TexDir + "/" + name + ".png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) tex.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.alphaIsTransparency = !normal;
            imp.wrapMode = normal ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void RockNormal()
        {
            const int size = 512, period = 8;
            var h = new float[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float acc = 0f, amp = 1f; int freq = period;
                    for (int o = 0; o < 5; o++)
                    {
                        float n = 1f - Mathf.Abs(TileNoise(u * freq, v * freq, freq, o * 31));
                        acc += n * n * amp; amp *= 0.5f; freq *= 2;
                    }
                    h[x, y] = acc;
                }
            MakeTex("KV_RockNormal", size, (u, v) =>
            {
                int x = Mathf.Clamp((int)(u * size), 0, size - 1), y = Mathf.Clamp((int)(v * size), 0, size - 1);
                float dx = h[(x + 1) % size, y] - h[(x - 1 + size) % size, y];
                float dy = h[x, (y + 1) % size] - h[x, (y - 1 + size) % size];
                Vector3 n = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }, true);
        }

        // ─────────────────────────────── 노이즈 ───────────────────────────────
        static int[] perm;
        static void InitNoise()
        {
            if (perm != null) return;
            var r = new System.Random(4242);
            var p = Enumerable.Range(0, 256).OrderBy(_ => r.Next()).ToArray();
            perm = new int[512];
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static float Grad(int h, float x, float y, float z)
        {
            int k = h & 15; float u = k < 8 ? x : y, v = k < 4 ? y : (k == 12 || k == 14 ? x : z);
            return ((k & 1) == 0 ? u : -u) + ((k & 2) == 0 ? v : -v);
        }

        /// <summary>3D Perlin, 대략 -1..1</summary>
        static float Noise(Vector3 p)
        {
            InitNoise();
            int X = Mathf.FloorToInt(p.x), Y = Mathf.FloorToInt(p.y), Z = Mathf.FloorToInt(p.z);
            float x = p.x - X, y = p.y - Y, z = p.z - Z;
            X &= 255; Y &= 255; Z &= 255;
            float u = Fade(x), v = Fade(y), w = Fade(z);
            int A = perm[X] + Y, AA = perm[A] + Z, AB = perm[A + 1] + Z, B = perm[X + 1] + Y, BA = perm[B] + Z, BB = perm[B + 1] + Z;
            return Mathf.Lerp(
                Mathf.Lerp(Mathf.Lerp(Grad(perm[AA], x, y, z), Grad(perm[BA], x - 1, y, z), u), Mathf.Lerp(Grad(perm[AB], x, y - 1, z), Grad(perm[BB], x - 1, y - 1, z), u), v),
                Mathf.Lerp(Mathf.Lerp(Grad(perm[AA + 1], x, y, z - 1), Grad(perm[BA + 1], x - 1, y, z - 1), u), Mathf.Lerp(Grad(perm[AB + 1], x, y - 1, z - 1), Grad(perm[BB + 1], x - 1, y - 1, z - 1), u), v), w);
        }

        /// <summary>날카로운 능선 노이즈 0..1</summary>
        static float Ridge(Vector3 p)
        {
            float a = 1f - Mathf.Abs(Noise(p));
            float b = 1f - Mathf.Abs(Noise(p * 2.03f + new Vector3(5.2f, 1.3f, 7.7f)));
            return a * a * 0.7f + b * b * 0.3f;
        }

        /// <summary>주기 period로 반복되는 2D 노이즈 (텍스처 이음매 없음)</summary>
        static float TileNoise(float x, float y, int period, int seed)
        {
            InitNoise();
            int X = Mathf.FloorToInt(x), Y = Mathf.FloorToInt(y);
            float fx = x - X, fy = y - Y;
            System.Func<int, int, float> h = (i, j) =>
            {
                i = ((i % period) + period) % period; j = ((j % period) + period) % period;
                return perm[(perm[(i + seed) & 255] + j) & 255] / 127.5f - 1f;
            };
            float u = Fade(fx), v = Fade(fy);
            return Mathf.Lerp(Mathf.Lerp(h(X, Y), h(X + 1, Y), u), Mathf.Lerp(h(X, Y + 1), h(X + 1, Y + 1), u), v);
        }
    }
}
