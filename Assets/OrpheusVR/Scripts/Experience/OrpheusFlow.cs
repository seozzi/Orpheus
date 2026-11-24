using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrpheusVR
{
    /// <summary>
    /// 체험 전체 흐름 (최종 기획서 1117 기준, 약 3분).
    /// S1 하데스의 방 → S2-1 리라 통로 → S2-2 갈림길 → S2-3 침수·공명 → S3 선택 → 엔딩(길 / 모래시계) → 궤적.
    /// 뒤돌아봄이 확정되면 에우리디케를 잃고 남은 경로는 홀로 진행.
    /// 대사는 지금은 자막(OrpheusSubtitle)으로만 나오고, TTS 음성(VO)은 다음 단계에서 같은 자리에 연결.
    /// </summary>
    public class OrpheusFlow : MonoBehaviour
    {
        public enum Stage { Intro, Passage, Fork, Flood, Choice, Waiting, ExitWalk, Epilogue, Finished }

        [Header("시스템")]
        public OrpheusPlayer player;
        public LookBackJudge judge;
        public OrpheusRoute route;
        public OrpheusSubtitle subtitle;
        public TrajectoryRecorder recorder;

        [Header("S1 하데스의 방")]
        public Transform spawn;
        public Transform hades;
        public Transform eurydiceEnter;
        public Transform handhold;
        [Tooltip("에우리디케 2D 홀로그램 (EurydiceHologram 프리팹 + EurydiceCompanion)")]
        public EurydiceCompanion eurydice;
        public GameObject handholdGlow;

        [Header("S2")]
        public Transform lyre;
        public GameObject alternateGuide;
        public Transform deadEnd;
        public Transform eurydiceFall;
        public Transform water;
        public Transform resonanceStone;
        public Transform[] pathStones;
        public float floodRise = 1.8f;
        public float floodRiseSpeed = 0.22f;
        public float drainSpeed = 0.65f;
        [Tooltip("물이 차오르기 시작하고 바위가 울리기까지 (초)")]
        public float resonanceDelay = 5f;

        [Header("S3")]
        public Transform waitChoice;
        public Transform leaveChoice;
        public Transform reunion;
        public Transform endingGaze;
        public float waitSeconds = 4f;
        public float epilogueSeconds = 25f;

        [Header("상태 (읽기 전용)")]
        public Stage stage = Stage.Intro;
        public bool lost, lyrePlayed, guideShown, flooding, drained, choseWait, stayed;
        public float station;

        const string HADES = "하데스", EURY = "에우리디케", NARR = "";
        float waterBase, nextWarnPulse, floodStartTime;
        float fogBase;
        bool fogTouched, choiceAsked, journeyStarted;

        void OnEnable() { if (judge) judge.Confirmed += OnLookedBack; }
        void OnDisable() { if (judge) judge.Confirmed -= OnLookedBack; }

        void OnDestroy()
        {
            if (fogTouched) RenderSettings.fogDensity = fogBase;
        }

        IEnumerator Start()
        {
            if (water) waterBase = water.position.y;
            fogBase = RenderSettings.fogDensity;
            if (eurydice) eurydice.Hide(0f);
            if (handholdGlow) handholdGlow.SetActive(false);
            if (alternateGuide) alternateGuide.SetActive(false);
            yield return null;
            yield return null; // 헤드셋 추적이 잡힐 때까지 한두 프레임

            // ── S1 하데스의 방 ─────────────────────────────
            player.Fade(1f, 0f);
            player.TeleportTo(spawn, hades);
            yield return player.Fade(0f, 1.5f);
            stage = Stage.Intro;

            subtitle.Say(HADES, "잘 왔다, 오르페우스. 불의의 사고로 저승으로 온 너의 아내, 에우리디케를 데리러 왔다고 했지?");
            subtitle.Say(HADES, "너의 아름다운 리라 연주에 탄복했기에 에우리디케를 다시 지상세계로 데려갈 기회를 주겠다. 하지만 이곳 지하 세계에서 마냥 쉽게 탈출할 수 있도록 할 수는 없지.");
            subtitle.Say(HADES, "지상으로 길을 올라가는 동안, 에우리디케는 네 뒤를 따라갈 것이다. 다만 너는 어떤 일이 있어도 그녀를 뒤돌아봐서는 안 된다.");
            subtitle.Say(HADES, "둘 중 하나라도 지상에 발을 디디지 못한 상태에서 둘이 얼굴을 마주한다면, 에우리디케는 영영 지하 세계에서 빠져나오지 못할 것이다.");
            subtitle.Say(HADES, "자, 아직 여정이 시작되기 전이니 동굴을 둘러보거라. 고개를 천천히 뒤로 돌려보아라.");
            yield return WaitSubtitles(0.5f);

            // 회전 경고 연습: 하데스 쪽이 정면
            Vector3 toHades = hades ? hades.position - player.HeadPosition : player.HeadForwardFlat;
            toHades.y = 0f;
            judge.practiceForward = toHades.sqrMagnitude > 1e-4f ? toHades.normalized : Vector3.forward;
            judge.practice = true;
            judge.armed = true;
            int before = judge.WarningCount;
            float practiceEnd = Time.time + 15f;
            while (Time.time < practiceEnd && (judge.WarningCount == before || subtitle.Busy)) yield return null;
            yield return new WaitForSeconds(2f);
            judge.armed = false;

            // 에우리디케 등장, 손 내밀기
            if (eurydice)
            {
                // 왼쪽 입구에서 나타나 손잡는 자리 옆까지 걸어옴
                Vector3 from = eurydiceEnter ? eurydiceEnter.position : player.FeetPosition + player.HeadForwardFlat * 3f;
                Vector3 to = handhold ? Vector3.Lerp(from, handhold.position, 0.85f) : from;
                eurydice.Appear(from, to);
            }
            if (handholdGlow) handholdGlow.SetActive(true);
            subtitle.Say(EURY, "다시 만나면, 이렇게 내 손을 잡아 줘.");
            subtitle.Say(NARR, "(그녀의 손을 잡으세요. 손을 뻗거나, 가까이 가서 A 버튼)", 6f);
            while (!player.Reached(handhold) && !DebugSkip()) yield return null;
            player.Haptic(0.5f, 0.35f);
            if (handholdGlow) handholdGlow.SetActive(false);
            subtitle.Clear();

            subtitle.Say(HADES, "이제 시작이다.");
            subtitle.Say(HADES, "절대 뒤돌아보지 말고 사랑하는 사람을 지키도록. 행운을 빈다.");
            yield return WaitSubtitles(0);

            // 손이 놓이고 귀환 경로 시작점으로 지점 이동
            yield return player.Fade(1f, 0.8f);
            if (eurydice) eurydice.Hide(0f);
            if (route && route.IsValid) player.TeleportTo(route.points[0], route.points[1]);
            yield return new WaitForSeconds(0.3f);
            if (eurydice) eurydice.StartFollowing(); // 이제부터 플레이어 뒤에서 따라옴 (뒤돌아보면 보임)
            yield return player.Fade(0f, 1f);
            BeginJourney();
        }

        void BeginJourney()
        {
            journeyStarted = true;
            stage = Stage.Passage;
            judge.practice = false;
            judge.armed = true;
            recorder.Begin();
            recorder.MarkSection("구간 1");
        }

        void Update()
        {
            UpdateWarning();
            if (!journeyStarted || !route) return;
            station = route.Station(player.FeetPosition);

            switch (stage)
            {
                case Stage.Passage:
                    if (!lyrePlayed && player.Reached(lyre, 2.5f))
                    {
                        lyrePlayed = true;
                        player.Haptic(0.3f, 0.4f, false);
                        subtitle.Say(NARR, "(리라 연주 소리가 들리자 정령들이 순식간에 조용해진다)");
                        if (!lost) subtitle.Say(EURY, "후후, 역시 너의 리라 연주는 아름다워.");
                    }
                    if (station >= route.forkStart) { stage = Stage.Fork; recorder.MarkSection("구간 2"); }
                    break;

                case Stage.Fork:
                    if (!guideShown && alternateGuide && deadEnd && Flat(player.FeetPosition - deadEnd.position) < 3.5f)
                    {
                        guideShown = true;
                        alternateGuide.SetActive(true);
                        subtitle.Say(NARR, "(막다른 길이다. 뒤돌아보지 말고 뒷걸음질로 갈림길까지 돌아가자)");
                    }
                    if (station >= route.floodStart) { stage = Stage.Flood; recorder.MarkSection("구간 3"); StartCoroutine(FloodSequence()); }
                    break;

                case Stage.Flood:
                    if (flooding && !drained && Time.time - floodStartTime >= resonanceDelay && player.Reached(resonanceStone, 2.5f))
                        StartCoroutine(Resonate());
                    if (station >= route.choiceStart) EnterChoice();
                    break;

                case Stage.Choice:
                    if (!lost && choiceAsked)
                    {
                        if (player.Reached(waitChoice, 2.5f)) StartCoroutine(WaitSequence());
                        else if (player.Reached(leaveChoice, 2.5f)) ChooseLeave();
                    }
                    if (station >= route.exitStart) { if (!lost && !choseWait) ChooseLeave(); EnterEpilogue(); }
                    break;

                case Stage.ExitWalk:
                    if (station >= route.exitStart) EnterEpilogue();
                    break;
            }

            if (flooding && water)
            {
                float target = drained ? waterBase : waterBase + floodRise;
                Vector3 p = water.position;
                p.y = Mathf.MoveTowards(p.y, target, Time.deltaTime * (drained ? drainSpeed : floodRiseSpeed));
                water.position = p;
            }
        }

        // ── 회전 경고: 상태 문구 + 진동 (경고 음성·경고음은 다음 단계) ─────────
        void UpdateWarning()
        {
            if (!judge || !judge.armed || !judge.IsWarning) { subtitle.SetStatus(""); return; }
            subtitle.SetStatus(judge.practice ? "(이 각도부터 경고가 울린다)" : judge.Hold01 > 0f ? "돌아보지 마라!" : "돌아보지 마라.");
            if (Time.time >= nextWarnPulse)
            {
                player.Haptic(0.15f + 0.6f * judge.Warning01, 0.08f);
                nextWarnPulse = Time.time + Mathf.Lerp(0.6f, 0.2f, judge.Warning01);
            }
        }

        void OnLookedBack()
        {
            if (lost || stayed) return;
            // 선택 지점에서 뒤돌아보기 = 세 번째 선택지 '뒤돌아보고 함께 머물기'
            if (stage == Stage.Choice && choiceAsked) { StartCoroutine(StaySequence()); return; }

            lost = true;
            player.Haptic(0.9f, 0.6f);
            subtitle.Clear();
            subtitle.Say(HADES, "너는 그녀를 뒤돌아보았다. 에우리디케는 이제 영원히 지하 세계에 머문다.");
            subtitle.Say(NARR, "(남은 길은 홀로 가야 한다)");
            StartCoroutine(ShowLostEurydice());
        }

        IEnumerator ShowLostEurydice()
        {
            if (!eurydice) yield break;
            // 뒤에서 따라오던(또는 넘어져 있던) 그 자리에서 잠깐 마주친 뒤 빛으로 흩어짐
            if (!eurydice.Visible) eurydice.StandAt(player.FeetPosition + player.HeadForwardFlat * 1.5f, false, 0.3f);
            yield return new WaitForSeconds(1.2f);
            eurydice.Vanish();
        }

        IEnumerator StaySequence()
        {
            stayed = true;
            stage = Stage.Epilogue;
            judge.armed = false;
            player.locomotionEnabled = false;
            player.Haptic(0.5f, 0.6f);
            subtitle.Clear();
            if (eurydice) eurydice.Reveal();
            subtitle.Say(HADES, "너는 뒤를 돌아보는 길을 선택했다. 두 사람은 영원히 이 지하 세계에 함께 머문다.");
            yield return WaitSubtitles(1f);
            recorder.Reveal(endingGaze);
            yield return new WaitForSeconds(epilogueSeconds);
            subtitle.Say(NARR, "(체험이 끝났습니다)", 6f);
            stage = Stage.Finished;
            yield return new WaitForSeconds(5f);
            yield return player.Fade(1f, 2f);
        }

        IEnumerator FloodSequence()
        {
            if (!lost)
            {
                if (eurydice) eurydice.Fall(eurydiceFall);
                subtitle.Say(EURY, "아아! 잠깐만, 기다려줘!", 3f);
                player.Haptic(0.6f, 0.3f);
            }
            yield return new WaitForSeconds(1f);
            subtitle.Say(NARR, "(정령들이 다시 차오르고, 벽의 균열에서 물이 쏟아진다)");
            flooding = true;
            floodStartTime = Time.time;
            fogTouched = true;
            float t = 0f;
            while (t < resonanceDelay && !drained)
            {
                t += Time.deltaTime;
                RenderSettings.fogDensity = Mathf.Lerp(fogBase, fogBase * 2.5f, t / resonanceDelay);
                yield return null;
            }
            if (!drained) subtitle.Say(NARR, "(물에 잠기지 않은 바위가, 앞서 연주한 리라의 선율로 울린다. 바위에 손을 얹어 보자)", 7f);
        }

        IEnumerator Resonate()
        {
            drained = true;
            player.Haptic(0.4f, 1f);
            subtitle.Say(NARR, "(선율이 바위에서 울려 나오고, 돌들이 차례로 공명한다. 물이 빠져나간다)");
            float t = 0f, from = RenderSettings.fogDensity;
            if (pathStones != null)
            {
                foreach (var s in pathStones)
                {
                    if (!s) continue;
                    StartCoroutine(Rise(s, 0.35f, 1.2f));
                    yield return new WaitForSeconds(0.35f);
                }
            }
            while (t < 2f) { t += Time.deltaTime; RenderSettings.fogDensity = Mathf.Lerp(from, fogBase, t / 2f); yield return null; }
            subtitle.Say(NARR, "(길 위쪽에서 빛이 새어든다. 최종 출구다)");
            if (!lost) subtitle.Say(NARR, "(뒤에서 에우리디케가 리라 선율을 작게 따라 부르다, 아픈 숨에 끊긴다)", 5f);
        }

        static IEnumerator Rise(Transform s, float height, float seconds)
        {
            Vector3 a = s.position, b = a + Vector3.up * height;
            for (float t = 0f; t < seconds; t += Time.deltaTime) { s.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t / seconds)); yield return null; }
            s.position = b;
        }

        void EnterChoice()
        {
            stage = Stage.Choice;
            recorder.MarkSection("선택");
            if (!drained && flooding) { drained = true; }
            if (lost)
            {
                subtitle.Say(HADES, "그녀는 이미 지하 세계에 머물게 되었다. 남은 길을 홀로 가거라.");
                stage = Stage.ExitWalk;
                return;
            }
            StartCoroutine(AskChoice());
        }

        IEnumerator AskChoice()
        {
            subtitle.Clear();
            subtitle.Say(HADES, "에우리디케가 움직일 수 없는 상황에 처했다.");
            subtitle.Say(HADES, "선택하라. 홀로 나갈 것인지, 앞을 보고 기약 없이 그녀가 일어나기만을 기다릴 것인지, 뒤를 돌아보고 영원히 그녀와 지하 세계에 머물 것인지.");
            yield return WaitSubtitles(0);
            subtitle.Say(NARR, "(빛나는 길로 나아가거나, 빛나는 모래시계에 손을 대세요)", 6f);
            choiceAsked = true;
        }

        void ChooseLeave()
        {
            if (stage != Stage.Choice) return;
            stage = Stage.ExitWalk;
            player.Haptic(0.3f, 0.2f);
            subtitle.Clear();
            subtitle.Say(HADES, "너는 기약 없는 기다림보다 홀로 나아가기를 택했다.");
            subtitle.Say(HADES, "가끔은 소중한 것을 잃기 위한 용기도 필요한 법이라고 인간들에게 들은 것 같은데, 그 마음은 대체 어떤 마음일지.");
            subtitle.Say(HADES, "좋다, 너의 길을 가거라. 나가서는 뒤를 돌아봐도 된다.");
        }

        IEnumerator WaitSequence()
        {
            if (stage != Stage.Choice) yield break;
            stage = Stage.Waiting;
            choseWait = true;
            player.locomotionEnabled = false;
            player.Haptic(0.3f, 0.2f);
            subtitle.Clear();
            subtitle.Say(HADES, "너는 홀로 나아가기보다 기약이 없더라도 사랑하는 이를 기다리기를 택했다.");
            subtitle.Say(HADES, "인간들은 어떻게 이렇게 비이성적인 선택을 하는 건지. 대체 사랑이란 건 무엇이길래.");
            subtitle.Say(HADES, "좋다, 너의 사랑에 힘입어 회복할 에우리디케를 기다려보거라.");
            yield return WaitSubtitles(0);
            yield return new WaitForSeconds(waitSeconds);
            if (eurydice) eurydice.Recover(); // 일어나 다시 뒤에서 따라옴
            subtitle.Say(EURY, "오르페우스, 나를 기다려줘서 고마워.");
            subtitle.Say(EURY, "이제 일어날 수 있을 것 같아. 우리 함께 이곳에서 나가자.");
            yield return WaitSubtitles(0);
            player.locomotionEnabled = true;
            stage = Stage.ExitWalk;
        }

        void EnterEpilogue()
        {
            stage = Stage.Epilogue;
            judge.armed = false;
            recorder.Reveal(endingGaze);
            StartCoroutine(Epilogue());
        }

        IEnumerator Epilogue()
        {
            if (choseWait && !lost)
            {
                subtitle.Say(EURY, "우리 드디어 온전히 밖으로 나왔어! 나를 뒤돌아봐도 돼, 오르페우스.");
                if (eurydice && reunion) eurydice.StandAt(reunion.position, false);
            }
            else
            {
                subtitle.Say(NARR, "(바깥세상에 발을 디뎠다. 이제 지나온 통로를 뒤돌아보자)");
                // 통로 속을 떠도는 에우리디케의 영혼: 출구에서 뒤돌아봤을 때 보이는 마지막 직선 구간에
                if (eurydice && route && route.IsValid)
                    eurydice.StandAt(route.points[route.points.Length - 2].position, true);
            }
            yield return new WaitForSeconds(epilogueSeconds);
            subtitle.Say(NARR, "(체험이 끝났습니다)", 6f);
            stage = Stage.Finished;
            yield return new WaitForSeconds(5f);
            yield return player.Fade(1f, 2f);
        }

        IEnumerator WaitSubtitles(float extraSeconds)
        {
            while (subtitle.Busy) yield return null;
            if (extraSeconds > 0f) yield return new WaitForSeconds(extraSeconds);
        }

        /// <summary>개발용: 데스크톱에서 Enter로 손잡기 건너뛰기</summary>
        static bool DebugSkip()
        {
            var kb = Keyboard.current;
            return kb != null && kb.enterKey.wasPressedThisFrame;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
