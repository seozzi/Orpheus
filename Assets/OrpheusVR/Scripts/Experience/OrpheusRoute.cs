using UnityEngine;

namespace OrpheusVR
{
    /// <summary>
    /// 귀환 경로. 본선 경유점(출구 쪽으로 순서대로)과 막다른 길 가지를 들고,
    /// 플레이어 위치에서 '현재 통로 정면'(접선)과 진행도(station)를 계산.
    /// station은 v1 맵 기준 대략 Z좌표(m)와 같은 단위: 9 하데스 방 끝, 43 갈림길, 58 침수, 82 선택, 100 출구.
    /// </summary>
    public class OrpheusRoute : MonoBehaviour
    {
        [Tooltip("본선 경유점. 시작(하데스 방 출구) → 지상 출구 순서")]
        public Transform[] points;
        [Tooltip("경유점별 진행값. 비우면 경유점의 Z좌표 사용 (v1 맵)")]
        public float[] stations;
        [Tooltip("막다른 길 가지. 갈림길 → 막다른 끝 순서")]
        public Transform[] deadEndBranch;

        [Header("진행 단계 경계 (station)")]
        public float passageStart = 9f;
        public float forkStart = 43f;
        public float floodStart = 58f;
        public float choiceStart = 84f;
        public float exitStart = 101f;

        public bool IsValid => points != null && points.Length >= 2;

        float StationAt(int i)
        {
            return stations != null && stations.Length == points.Length ? stations[i] : points[i].position.z;
        }

        /// <summary>위치에서 가장 가까운 본선 위의 진행값</summary>
        public float Station(Vector3 p)
        {
            if (!IsValid) return p.z;
            float best = float.MaxValue, result = StationAt(0);
            for (int i = 0; i < points.Length - 1; i++)
            {
                float t; float d = SqrDistToSegment(p, points[i].position, points[i + 1].position, out t);
                if (d < best) { best = d; result = Mathf.Lerp(StationAt(i), StationAt(i + 1), t); }
            }
            return result;
        }

        /// <summary>현재 통로 정면(수평, 정규화). 막다른 길 안에서는 막다른 끝 쪽이 정면</summary>
        public Vector3 Tangent(Vector3 p)
        {
            Vector3 result = Vector3.forward;
            float best = float.MaxValue;
            Scan(points, p, ref best, ref result);
            Scan(deadEndBranch, p, ref best, ref result);
            return result;
        }

        /// <summary>막다른 길 가지 위에 있는지 (본선보다 가지에 더 가까움)</summary>
        public bool InDeadEnd(Vector3 p)
        {
            if (deadEndBranch == null || deadEndBranch.Length < 2 || !IsValid) return false;
            Vector3 dummy = Vector3.zero;
            float main = float.MaxValue, branch = float.MaxValue;
            Scan(points, p, ref main, ref dummy);
            Scan(deadEndBranch, p, ref branch, ref dummy);
            return branch < main;
        }

        static void Scan(Transform[] list, Vector3 p, ref float best, ref Vector3 result)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length - 1; i++)
            {
                if (!list[i] || !list[i + 1]) continue;
                float t; float d = SqrDistToSegment(p, list[i].position, list[i + 1].position, out t);
                if (d < best)
                {
                    Vector3 dir = list[i + 1].position - list[i].position; dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    best = d; result = dir.normalized;
                }
            }
        }

        static float SqrDistToSegment(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            a.y = b.y = p.y = 0f;
            Vector3 d = b - a;
            t = d.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
            return (p - a - d * t).sqrMagnitude;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, .7f, .2f);
            if (points != null) for (int i = 0; i < points.Length - 1; i++) if (points[i] && points[i + 1]) Gizmos.DrawLine(points[i].position, points[i + 1].position);
            Gizmos.color = new Color(1f, .3f, .3f);
            if (deadEndBranch != null) for (int i = 0; i < deadEndBranch.Length - 1; i++) if (deadEndBranch[i] && deadEndBranch[i + 1]) Gizmos.DrawLine(deadEndBranch[i].position, deadEndBranch[i + 1].position);
        }
    }
}
