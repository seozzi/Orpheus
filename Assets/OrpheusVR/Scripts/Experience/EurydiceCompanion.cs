using System.Collections;
using System.Collections.Generic;
using OrpheusVR.Hologram;
using UnityEngine;

namespace OrpheusVR
{
    /// <summary>
    /// 에우리디케 2D 홀로그램(EurydiceHologramActor, 상준 v2)을 VR 흐름(OrpheusFlow)에 연결.
    /// 등장·동행(플레이어가 지나온 길을 일정 거리 뒤에서 따라옴)·넘어짐·회복·소멸을 담당.
    /// 뒤돌아보기 판정은 하지 않음 (LookBackJudge가 단독 담당).
    /// </summary>
    public class EurydiceCompanion : MonoBehaviour
    {
        public EurydiceHologramActor actor;
        public OrpheusPlayer player;
        public OrpheusRoute route;

        [Header("동행")]
        [Tooltip("플레이어 뒤 거리 (m, 지나온 길 기준)")]
        public float followDistance = 2.5f;
        public float followSpeed = 1.7f;
        [Tooltip("궤적 점 간격 (m)")]
        public float trailStep = 0.2f;
        public float ghostOpacity = 0.45f;

        public bool Following { get; private set; }
        public bool Visible => actor && actor.IsVisible;

        readonly List<Vector3> trail = new List<Vector3>();
        Coroutine routine;

        void Start() => Hide(0f);

        void Update()
        {
            if (!Following || !actor || !player) return;
            Vector3 feet = player.FeetPosition;
            if (trail.Count == 0 || (trail[trail.Count - 1] - feet).sqrMagnitude > trailStep * trailStep) trail.Add(feet);
            if (trail.Count > 500) trail.RemoveRange(0, 100);

            Vector3 target = trail[0];
            float length = 0f;
            for (int i = trail.Count - 1; i > 0; i--)
            {
                float d = Vector3.Distance(trail[i], trail[i - 1]);
                if (length + d >= followDistance) { target = Vector3.Lerp(trail[i], trail[i - 1], (followDistance - length) / Mathf.Max(d, 0.001f)); break; }
                length += d;
            }
            actor.SetFeet(Vector3.MoveTowards(actor.transform.position, target, followSpeed * Time.deltaTime));
        }

        void Run(IEnumerator r)
        {
            if (routine != null) StopCoroutine(routine);
            routine = r != null ? StartCoroutine(r) : null;
        }

        /// <summary>즉시(또는 디졸브로) 숨김. 동행 중지</summary>
        public void Hide(float seconds = 0.5f)
        {
            if (!actor) return;
            Following = false;
            Run(null);
            if (seconds <= 0f) { actor.CancelFade(); actor.SetDissolve(1f); }
            else actor.Fade(false, seconds);
        }

        /// <summary>from에서 디졸브로 나타나 to까지 걸어옴 (S1 등장)</summary>
        public void Appear(Vector3 from, Vector3 to, float walkSeconds = 1.5f)
        {
            if (!actor) return;
            Following = false;
            actor.SetOpacity(0.7f);
            actor.SetPose(EurydiceHologramActor.Pose.Standing);
            actor.SetFeet(from);
            actor.Fade(true);
            Run(Walk(from, to, walkSeconds));
        }

        IEnumerator Walk(Vector3 a, Vector3 b, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) { actor.SetFeet(Vector3.Lerp(a, b, t / seconds)); yield return null; }
            actor.SetFeet(b);
            routine = null;
        }

        /// <summary>플레이어 바로 뒤(통로 반대 방향)에 서서 따라오기 시작</summary>
        public void StartFollowing(float fadeSeconds = 0.6f)
        {
            if (!actor || !player) return;
            Run(null);
            actor.SetOpacity(0.7f);
            actor.SetPose(EurydiceHologramActor.Pose.Standing);
            Vector3 back = route ? route.Tangent(player.FeetPosition) : player.HeadForwardFlat;
            actor.SetFeet(player.FeetPosition - back * followDistance);
            trail.Clear();
            trail.Add(actor.transform.position);
            trail.Add(player.FeetPosition);
            Following = true;
            actor.Fade(true, fadeSeconds);
        }

        /// <summary>넘어짐 (S2-3). at이 없으면 현재 위치에서</summary>
        public void Fall(Transform at)
        {
            if (!actor) return;
            Following = false;
            Run(FallRoutine(at ? at.position : actor.transform.position));
        }

        IEnumerator FallRoutine(Vector3 p)
        {
            actor.Fade(false, 0.22f);
            yield return new WaitForSeconds(0.25f);
            actor.SetFeet(p);
            actor.SetPose(EurydiceHologramActor.Pose.Fallen);
            actor.Fade(true, 0.35f);
            routine = null;
        }

        /// <summary>기다림 끝에 일어나 다시 플레이어 뒤로</summary>
        public void Recover()
        {
            if (!actor) return;
            Run(RecoverRoutine());
        }

        IEnumerator RecoverRoutine()
        {
            actor.Fade(false, 0.35f);
            yield return new WaitForSeconds(0.4f);
            routine = null;
            StartFollowing(0.75f);
        }

        /// <summary>뒤돌아본 순간 빛으로 흩어짐 (지금 서 있는 자리에서)</summary>
        public void Vanish(float seconds = 0.85f)
        {
            if (!actor) return;
            Following = false;
            Run(null);
            actor.Fade(false, seconds);
        }

        /// <summary>특정 위치에 바로 세우기 (엔딩 연출). ghost면 흐린 영혼</summary>
        public void StandAt(Vector3 p, bool ghost, float fadeSeconds = 1f)
        {
            if (!actor) return;
            Following = false;
            Run(null);
            actor.SetPose(EurydiceHologramActor.Pose.Standing);
            actor.SetOpacity(ghost ? ghostOpacity : 1f);
            actor.SetFeet(p);
            actor.Fade(true, fadeSeconds);
        }

        /// <summary>그 자리에서 일어나 또렷해짐 ('함께 남기')</summary>
        public void Reveal()
        {
            if (!actor) return;
            Following = false;
            Run(null);
            actor.SetPose(EurydiceHologramActor.Pose.Standing);
            actor.SetOpacity(1f);
            actor.Fade(true, 1f);
        }
    }
}
