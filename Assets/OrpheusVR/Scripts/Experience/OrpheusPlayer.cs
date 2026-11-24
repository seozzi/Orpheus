using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace OrpheusVR
{
    /// <summary>
    /// 오르페우스(플레이어) 리그. OVRCameraRig 부모에 붙고 CharacterController로 동굴 바닥·경사를 따라 이동.
    /// 헤드셋: 양팔 흔들기로 앞으로 걷기(흔드는 속도에 비례, 머리 정면 방향), 왼손 스틱 보조 이동(뒤로 당기면 뒷걸음질),
    /// 오른손 스틱 스냅 회전, A 또는 오른손 트리거 상호작용.
    /// 에디터 데스크톱 미리보기(헤드셋 없음): WASD 이동, 마우스 오른쪽 드래그 시점, E 상호작용.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class OrpheusPlayer : MonoBehaviour
    {
        [Header("References")]
        public OVRCameraRig rig;
        [Tooltip("비우면 rig.centerEyeAnchor")]
        public Transform head;

        [Header("Movement")]
        public float moveSpeed = 1.3f;
        [Tooltip("오른손 스틱 스냅 회전 각도. 0이면 스냅 회전 끔 (몸으로만 회전)")]
        public float snapAngle = 30f;
        [Range(0f, 0.9f)] public float deadZone = 0.2f;
        public bool locomotionEnabled = true;

        [Header("Arm swing (팔 흔들기 이동)")]
        public bool armSwingEnabled = true;
        [Tooltip("켜면 그립 버튼을 쥐고 흔들 때만 이동 (일반 손동작 오작동 방지)")]
        public bool armSwingRequireGrip = false;
        [Tooltip("이보다 느린 팔 움직임(앞뒤 성분, m/s)은 무시")]
        public float armSwingMinSpeed = 0.35f;
        [Tooltip("팔 흔드는 속도 → 걷는 속도 배율")]
        public float armSwingGain = 1.2f;
        public float armSwingMaxSpeed = 2.0f;
        [Tooltip("한 손만 움직일 때 인정 비율 (양손이 서로 반대로 흔들리면 1)")]
        [Range(0f, 1f)] public float armSwingOneHandFactor = 0.3f;
        [Tooltip("속도가 따라가는 빠르기. 클수록 즉각 반응, 작을수록 부드러움")]
        public float armSwingSmoothing = 6f;
        [Tooltip("흔들기를 멈춘 뒤 서서히 멈추는 빠르기")]
        public float armSwingStopSmoothing = 10f;
        [Tooltip("팔이 앞뒤 끝에서 잠깐 멈추는 순간에도 속도가 끊기지 않게 최근 최대값을 유지하는 시간 (초)")]
        public float armSwingHoldSeconds = 0.3f;

        [Header("Desktop preview")]
        public float desktopEyeHeight = 1.6f;
        public float mouseSensitivity = 0.12f;

        [Header("Interaction")]
        [Tooltip("손이 이 거리 안에 들어오면 버튼 없이도 닿은 것으로 판정 (m)")]
        public float handTouchDistance = 0.3f;

        CharacterController body;
        OVRScreenFade fader;
        float pitch, fallSpeed;
        bool turnReady = true;
        Vector3 lastLeftLocal, lastRightLocal;
        bool swingPrimed;
        float swingSpeed, swingEnvelope;

        /// <summary>현재 팔 흔들기로 나오는 걷기 속도 (m/s, 디버그·튜닝용)</summary>
        public float ArmSwingSpeed => swingSpeed;
        Coroutine hapticRoutine, fadeRoutine;

        public bool IsXR => XRSettings.isDeviceActive;
        public Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * desktopEyeHeight;
        public Vector3 FeetPosition { get { Vector3 h = HeadPosition; return new Vector3(h.x, transform.position.y, h.z); } }

        /// <summary>머리 정면의 수평 방향 (정규화)</summary>
        public Vector3 HeadForwardFlat
        {
            get
            {
                if (!head) return transform.forward;
                Vector3 f = head.forward; f.y = 0f;
                if (f.sqrMagnitude < 0.01f) { f = head.up; f.y = 0f; }
                return f.sqrMagnitude < 1e-6f ? transform.forward : f.normalized;
            }
        }

        public Vector3 RightHandPosition => IsXR && rig ? rig.rightHandAnchor.position : HeadPosition + HeadForwardFlat * 0.45f - Vector3.up * 0.3f;
        public Vector3 LeftHandPosition => IsXR && rig ? rig.leftHandAnchor.position : HeadPosition + HeadForwardFlat * 0.45f - Vector3.up * 0.3f;

        /// <summary>이번 프레임에 상호작용 버튼을 눌렀는지 (A, 오른손 트리거, E)</summary>
        public bool InteractDown { get; private set; }

        void Awake()
        {
            body = GetComponent<CharacterController>();
            if (!rig) rig = GetComponentInChildren<OVRCameraRig>(true);
            if (!head && rig) head = rig.centerEyeAnchor ? rig.centerEyeAnchor : rig.transform.Find("TrackingSpace/CenterEyeAnchor");
        }

        void Start()
        {
            if (head) fader = head.GetComponent<OVRScreenFade>();
            ApplyEyeHeight();
        }

        void ApplyEyeHeight()
        {
            if (!rig || !rig.trackingSpace) return;
            // 헤드셋은 바닥 기준 추적이라 0, 데스크톱은 눈높이만큼 올림
            rig.trackingSpace.localPosition = IsXR ? Vector3.zero : Vector3.up * desktopEyeHeight;
            if (IsXR) rig.trackingSpace.localRotation = Quaternion.identity;
        }

        void Update()
        {
            ApplyEyeHeight();
            var kb = Keyboard.current;
            InteractDown = (kb != null && kb.eKey.wasPressedThisFrame)
                           || OVRInput.GetDown(OVRInput.RawButton.A)
                           || OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger);

            Vector2 move = Vector2.zero;
            if (IsXR)
            {
                move = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
                float tx = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).x;
                if (snapAngle > 0f && locomotionEnabled)
                {
                    if (turnReady && Mathf.Abs(tx) > 0.7f) { transform.RotateAround(HeadPosition, Vector3.up, Mathf.Sign(tx) * snapAngle); turnReady = false; }
                    else if (Mathf.Abs(tx) < 0.3f) turnReady = true;
                }
                FitCapsuleToHead();
            }
            else
            {
                if (kb != null)
                {
                    move.x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                    move.y = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
                }
                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    transform.Rotate(0f, d.x * mouseSensitivity, 0f);
                    pitch = Mathf.Clamp(pitch - d.y * mouseSensitivity, -65f, 65f);
                    if (rig && rig.trackingSpace) rig.trackingSpace.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                }
            }

            Vector3 velocity = Vector3.zero;
            if (locomotionEnabled && move.magnitude > deadZone)
            {
                Vector3 fwd = HeadForwardFlat;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                velocity = fwd * move.y + right * move.x;
                if (velocity.sqrMagnitude > 1f) velocity.Normalize();
                velocity *= moveSpeed;
            }
            UpdateArmSwing();
            if (locomotionEnabled && swingSpeed > 0.01f)
            {
                // 스틱과 함께 쓰면 큰 쪽을 따름 (더해서 과속하지 않게)
                Vector3 swing = HeadForwardFlat * swingSpeed;
                if (swing.sqrMagnitude > velocity.sqrMagnitude) velocity = swing;
            }
            fallSpeed = body.isGrounded ? -1f : fallSpeed - 9.81f * Time.deltaTime;
            if (body.enabled) body.Move((velocity + Vector3.up * fallSpeed) * Time.deltaTime);
        }

        /// <summary>
        /// 팔 흔들기 → 걷기 속도.
        /// 손 위치를 머리 기준 상대 위치(추적 공간)로 비교해 몸 이동·스냅 회전 영향을 빼고,
        /// 속도의 앞뒤(머리 정면) 성분만 사용. 양손이 서로 반대 방향일 때 온전히, 한 손만이면 줄여서 인정.
        /// </summary>
        void UpdateArmSwing()
        {
            float target = 0f;
            float dt = Time.deltaTime;
            bool active = armSwingEnabled && locomotionEnabled && IsXR && rig && head && rig.trackingSpace && dt > 1e-5f;
            if (active)
            {
                Vector3 headLocal = head.localPosition;
                Vector3 l = rig.leftHandAnchor.localPosition - headLocal;
                Vector3 r = rig.rightHandAnchor.localPosition - headLocal;
                if (swingPrimed)
                {
                    // 추적 공간 속도 → 월드 방향 → 머리 정면 성분
                    Vector3 fwd = HeadForwardFlat;
                    float vl = Vector3.Dot(rig.trackingSpace.TransformDirection((l - lastLeftLocal) / dt), fwd);
                    float vr = Vector3.Dot(rig.trackingSpace.TransformDirection((r - lastRightLocal) / dt), fwd);
                    bool gripOk = !armSwingRequireGrip
                                  || (OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger) > 0.5f && OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger) > 0.5f);
                    if (gripOk)
                    {
                        float al = Mathf.Abs(vl), ar = Mathf.Abs(vr);
                        bool opposite = vl * vr < 0f;
                        float both = (al + ar) * 0.5f;
                        float swing = opposite ? both : Mathf.Max(al, ar) * armSwingOneHandFactor;
                        // 진자 운동의 끝점(속도 0)에서 끊기지 않도록 최근 최대값을 서서히 낮추며 유지
                        swingEnvelope = Mathf.Max(swing, swingEnvelope * Mathf.Exp(-dt / Mathf.Max(armSwingHoldSeconds, 0.01f)));
                    }
                    else swingEnvelope = 0f;
                    if (swingEnvelope > armSwingMinSpeed)
                        target = Mathf.Min((swingEnvelope - armSwingMinSpeed) * armSwingGain + 0.3f, armSwingMaxSpeed);
                }
                lastLeftLocal = l; lastRightLocal = r;
                swingPrimed = true;
            }
            else { swingPrimed = false; swingEnvelope = 0f; }

            float k = target > swingSpeed ? armSwingSmoothing : armSwingStopSmoothing;
            swingSpeed = Mathf.Lerp(swingSpeed, target, 1f - Mathf.Exp(-k * dt));
            if (target == 0f && swingSpeed < 0.02f) swingSpeed = 0f;
        }

        /// <summary>몸을 기울이거나 실제로 걸어도 충돌 캡슐이 머리 아래에 오도록</summary>
        void FitCapsuleToHead()
        {
            if (!head) return;
            Vector3 local = transform.InverseTransformPoint(head.position);
            float h = Mathf.Clamp(local.y, 1.0f, 2.1f);
            body.height = h;
            body.center = new Vector3(local.x, h * 0.5f + body.skinWidth, local.z);
        }

        /// <summary>머리가 target(바닥 위치) 위에 오고, 수평 시선이 yaw(도)를 보도록 리그를 옮김</summary>
        public void TeleportTo(Vector3 feetTarget, float yaw)
        {
            bool was = body.enabled;
            body.enabled = false;
            float headYaw = Mathf.Atan2(HeadForwardFlat.x, HeadForwardFlat.z) * Mathf.Rad2Deg;
            transform.RotateAround(HeadPosition, Vector3.up, Mathf.DeltaAngle(headYaw, yaw));
            Vector3 offset = HeadPosition - transform.position; offset.y = 0f;
            transform.position = feetTarget - offset;
            pitch = 0f;
            if (!IsXR && rig && rig.trackingSpace) rig.trackingSpace.localRotation = Quaternion.identity;
            fallSpeed = 0f;
            body.enabled = was;
        }

        public void TeleportTo(Transform anchor, Transform lookAt)
        {
            if (!anchor) return;
            Vector3 d = lookAt ? lookAt.position - anchor.position : anchor.forward;
            d.y = 0f;
            TeleportTo(anchor.position, d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : anchor.eulerAngles.y);
        }

        /// <summary>손이 target에 닿았는지 (헤드셋), 또는 가까이서 버튼을 눌렀는지</summary>
        public bool Reached(Transform target, float buttonRange = 2.5f)
        {
            if (!target) return false;
            Vector3 p = target.position;
            if (IsXR && (Vector3.Distance(RightHandPosition, p) < handTouchDistance || Vector3.Distance(LeftHandPosition, p) < handTouchDistance)) return true;
            return InteractDown && Vector3.Distance(HeadPosition, p) < buttonRange;
        }

        public void Haptic(float amplitude, float seconds, bool bothHands = true)
        {
            if (hapticRoutine != null) StopCoroutine(hapticRoutine);
            hapticRoutine = StartCoroutine(HapticRoutine(amplitude, seconds, bothHands));
        }

        IEnumerator HapticRoutine(float amplitude, float seconds, bool both)
        {
            var mask = both ? OVRInput.Controller.Touch : OVRInput.Controller.RTouch;
            OVRInput.SetControllerVibration(1f, Mathf.Clamp01(amplitude), mask);
            yield return new WaitForSeconds(Mathf.Min(seconds, 1.9f));
            OVRInput.SetControllerVibration(0f, 0f, mask);
            hapticRoutine = null;
        }

        /// <summary>화면을 검게(1) 또는 밝게(0). 헤드셋에서는 OVRScreenFade 사용</summary>
        public Coroutine Fade(float to, float seconds)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadeRoutine(to, seconds));
            return fadeRoutine;
        }

        float fadeLevel;
        IEnumerator FadeRoutine(float to, float seconds)
        {
            float from = fadeLevel, t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                fadeLevel = Mathf.Lerp(from, to, t / Mathf.Max(seconds, 0.01f));
                if (fader) fader.SetUIFade(fadeLevel);
                yield return null;
            }
            fadeLevel = to;
            if (fader) fader.SetUIFade(fadeLevel);
            fadeRoutine = null;
        }

        void OnGUI()
        {
            if (IsXR || fadeLevel <= 0.001f) return;
            var c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fadeLevel);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }

        void OnDisable()
        {
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.Touch);
        }
    }
}
