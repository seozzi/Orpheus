using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrpheusVR
{
    /// <summary>
    /// 망설임의 궤적. 귀환 경로를 걷는 동안 위치·정지·경고를 기록하고, 엔딩에서 빛의 선으로 보여 줌.
    /// 실제 통로 위 궤적 + 출구 앞에 띄우는 축소 지도(통로가 꺾여 지상에서 안 보이는 문제 대비) 두 가지를 그림.
    /// </summary>
    public class TrajectoryRecorder : MonoBehaviour
    {
        public OrpheusPlayer player;
        public LookBackJudge judge;
        public Material lineMaterial;
        public Material markerMaterial;

        [Header("기록")]
        public float sampleInterval = 0.2f;
        public float minStep = 0.05f;
        [Tooltip("이 속도 미만이 stopSeconds 이상 이어지면 정지 지점")]
        public float stopSpeed = 0.15f;
        public float stopSeconds = 1f;

        [Header("표시")]
        public float lineWidth = 0.06f;
        public float revealSeconds = 3f;
        [Tooltip("축소 지도 배율")]
        public float miniatureScale = 0.025f;

        public struct Stop { public Vector3 position; public float seconds; public bool lookedBack; }
        public struct Section { public string label; public float start, end; public Vector3 at; }

        readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> times = new List<float>();
        readonly List<Stop> stops = new List<Stop>();
        readonly List<Section> sections = new List<Section>();

        public bool Recording { get; private set; }
        public float StartTime { get; private set; }
        public IReadOnlyList<Stop> Stops => stops;
        public IReadOnlyList<Section> Sections => sections;

        float nextSample, slowSince = -1f;
        Vector3 slowAnchor;
        bool slowLookedBack;
        Transform drawn;

        public void Begin()
        {
            points.Clear(); times.Clear(); stops.Clear(); sections.Clear();
            Recording = true; StartTime = Time.time; nextSample = 0f; slowSince = -1f;
        }

        public void End()
        {
            if (!Recording) return;
            CloseStop();
            if (sections.Count > 0) { var s = sections[sections.Count - 1]; if (s.end <= 0f) { s.end = Time.time; sections[sections.Count - 1] = s; } }
            Recording = false;
        }

        /// <summary>구간 시작 표시 (이전 구간은 여기서 끝남)</summary>
        public void MarkSection(string label)
        {
            if (!Recording || !player) return;
            if (sections.Count > 0) { var s = sections[sections.Count - 1]; s.end = Time.time; sections[sections.Count - 1] = s; }
            sections.Add(new Section { label = label, start = Time.time, end = 0f, at = player.FeetPosition });
        }

        void Update()
        {
            if (!Recording || !player || Time.time < nextSample) return;
            nextSample = Time.time + sampleInterval;
            Vector3 p = player.FeetPosition;
            if (points.Count == 0 || (p - points[points.Count - 1]).sqrMagnitude > minStep * minStep)
            {
                points.Add(p); times.Add(Time.time);
            }

            // 정지 판정: 최근 위치에서 거의 움직이지 않음
            if (slowSince < 0f) { slowSince = Time.time; slowAnchor = p; slowLookedBack = false; }
            float elapsed = Time.time - slowSince;
            if (Vector3.Distance(p, slowAnchor) > stopSpeed * Mathf.Max(elapsed, sampleInterval) + 0.1f)
            {
                CloseStop();
                slowSince = Time.time; slowAnchor = p; slowLookedBack = false;
            }
            else if (judge && judge.IsWarning) slowLookedBack = true;
        }

        void CloseStop()
        {
            if (slowSince < 0f) return;
            float d = Time.time - slowSince;
            if (d >= stopSeconds) stops.Add(new Stop { position = slowAnchor, seconds = d, lookedBack = slowLookedBack });
            slowSince = -1f;
        }

        /// <summary>엔딩: 실제 궤적과 축소 지도를 그림. miniatureAnchor가 있으면 그 앞에 축소 지도</summary>
        public void Reveal(Transform miniatureAnchor)
        {
            End();
            if (drawn) Destroy(drawn.gameObject);
            drawn = new GameObject("Journey trace (revealed)").transform;
            drawn.SetParent(transform, false);
            StartCoroutine(RevealRoutine(Build(drawn, Vector3.zero, Vector3.zero, 1f, "Trace - in cave")));

            if (miniatureAnchor && points.Count > 1)
            {
                // 지도 중심을 출구 앞 앵커 위 1.1m에 둠 (약 2.7m 길이의 축소 지도)
                Vector3 min = points[0], max = points[0];
                foreach (var q in points) { min = Vector3.Min(min, q); max = Vector3.Max(max, q); }
                Vector3 center = (min + max) * 0.5f;
                Vector3 place = miniatureAnchor.position + Vector3.up * 1.1f;
                StartCoroutine(RevealRoutine(Build(drawn, center, place, miniatureScale, "Trace - miniature map")));
            }
        }

        LineRenderer Build(Transform parent, Vector3 origin, Vector3 place, float scale, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = scale < 1f ? Mathf.Max(lineWidth * 0.25f, 0.006f) : lineWidth;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (lineMaterial) lr.sharedMaterial = lineMaterial;
            lr.startColor = new Color(1f, .82f, .55f);
            lr.endColor = new Color(.7f, .95f, 1f);

            var world = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++) world[i] = Map(points[i] + Vector3.up * 0.08f, origin, place, scale);
            lr.positionCount = 0;
            pending[lr] = world;

            // 정지 지점: 머문 시간에 비례한 빛 구슬 + 초 라벨
            foreach (var s in stops)
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(m.GetComponent<Collider>());
                m.name = "Stop " + s.seconds.ToString("0.0") + "s";
                m.transform.SetParent(go.transform, false);
                m.transform.position = Map(s.position + Vector3.up * 0.25f, origin, place, scale);
                float size = Mathf.Clamp(0.12f + s.seconds * 0.04f, 0.12f, 0.7f) * (scale < 1f ? 0.5f : 1f);
                m.transform.localScale = Vector3.one * size;
                if (markerMaterial) m.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                if (scale >= 1f || s.seconds >= 3f)
                {
                    var label = Label(go.transform, m.transform.position + Vector3.up * size * 0.7f, s.seconds.ToString("0.0") + "초" + (s.lookedBack ? " · 망설임" : ""), scale < 1f ? 0.004f : 0.02f);
                    label.gameObject.SetActive(false);
                }
                m.SetActive(false);
            }
            // 구간별 소요 시간
            foreach (var sec in sections)
            {
                var anchor = new GameObject("Section " + sec.label).transform;
                anchor.SetParent(go.transform, false);
                anchor.position = Map(sec.at + Vector3.up * 1.4f, origin, place, scale);
                float dur = (sec.end > 0f ? sec.end : Time.time) - sec.start;
                Label(anchor, anchor.position, sec.label + "\n" + dur.ToString("0") + "초", scale < 1f ? 0.005f : 0.03f);
                anchor.gameObject.SetActive(false);
            }
            return lr;
        }

        static Vector3 Map(Vector3 p, Vector3 origin, Vector3 place, float scale)
        {
            return scale >= 1f ? p : place + (p - origin) * scale;
        }

        TextMesh Label(Transform parent, Vector3 position, string text, float size)
        {
            var tm = OrpheusText.Create(parent, "Label", size, TextAnchor.LowerCenter);
            tm.transform.position = position;
            tm.text = text;
            tm.color = new Color(1f, .9f, .75f);
            tm.gameObject.AddComponent<FaceViewer>();
            return tm;
        }

        readonly Dictionary<LineRenderer, Vector3[]> pending = new Dictionary<LineRenderer, Vector3[]>();

        IEnumerator RevealRoutine(LineRenderer lr)
        {
            var all = pending[lr];
            float t = 0f;
            while (t < revealSeconds)
            {
                t += Time.deltaTime;
                int n = Mathf.Clamp(Mathf.CeilToInt(all.Length * t / revealSeconds), 0, all.Length);
                lr.positionCount = n;
                for (int i = 0; i < n; i++) lr.SetPosition(i, all[i]);
                yield return null;
            }
            lr.positionCount = all.Length;
            lr.SetPositions(all);
            foreach (Transform c in lr.transform) c.gameObject.SetActive(true);
        }
    }
}
