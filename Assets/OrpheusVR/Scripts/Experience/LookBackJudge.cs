using System;
using UnityEngine;

namespace OrpheusVR
{
    /// <summary>
    /// 뒤돌아보기 판정 (기획서 2장).
    /// 정면·주변 탐색: 현재 통로 정면에서 좌우 warnAngle 미만 → 경고 없음
    /// 경고: warnAngle부터, 후방에 가까울수록 Warning01이 1에 가까워짐
    /// 확정: lossAngle 이상을 holdSeconds 연속 유지 → Confirmed
    /// 취소: 확정 전에 후방 영역을 벗어나면 유지 시간 초기화
    /// practice 모드(하데스 방 연습)는 확정하지 않고 경고만.
    /// </summary>
    public class LookBackJudge : MonoBehaviour
    {
        public OrpheusPlayer player;
        public OrpheusRoute route;

        [Header("판정 값 (테스트 시작값)")]
        public float warnAngle = 70f;
        public float lossAngle = 120f;
        public float holdSeconds = 2f;

        [Header("상태")]
        [Tooltip("켜져 있을 때만 판정")]
        public bool armed;
        [Tooltip("연습: 경고만 하고 확정하지 않음. 정면은 practiceForward")]
        public bool practice;
        public Vector3 practiceForward = Vector3.forward;

        public float Angle { get; private set; }
        /// <summary>0 = 경고 없음, 0~1 = 경고 강도 (warnAngle에서 0, 180도에서 1)</summary>
        public float Warning01 { get; private set; }
        /// <summary>확정까지 진행률 0~1</summary>
        public float Hold01 { get; private set; }
        public bool IsWarning => Warning01 > 0f;
        public int WarningCount { get; private set; }

        public event Action WarningStarted;
        public event Action Confirmed;

        float hold;
        bool wasWarning;

        void Update()
        {
            if (!armed || !player)
            {
                ClearState();
                return;
            }
            Vector3 forward = practice || route == null || !route.IsValid ? practiceForward : route.Tangent(player.FeetPosition);
            forward.y = 0f;
            Angle = Vector3.Angle(forward, player.HeadForwardFlat);
            Warning01 = Angle < warnAngle ? 0f : Mathf.Max(0.05f, Mathf.InverseLerp(warnAngle, 180f, Angle));

            if (IsWarning && !wasWarning) { WarningCount++; WarningStarted?.Invoke(); }
            wasWarning = IsWarning;

            if (Angle >= lossAngle)
            {
                hold += Time.deltaTime;
                Hold01 = Mathf.Clamp01(hold / holdSeconds);
                if (hold >= holdSeconds && !practice)
                {
                    armed = false;
                    ClearState();
                    Confirmed?.Invoke();
                }
            }
            else
            {
                hold = 0f;
                Hold01 = 0f;
            }
        }

        void ClearState()
        {
            hold = 0f; Hold01 = 0f; Warning01 = 0f; wasWarning = false;
        }
    }
}
