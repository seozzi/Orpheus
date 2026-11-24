# 작업 맥락 요약 (Claude Code 인수인계용, 2026-11-24)

새 세션은 이 파일과 루트 `CLAUDE.md`를 먼저 읽고 시작한다.

## 1. 작품
- 「사랑으로 그린 망설임」, 가상현실입문 5조(공공인자). Quest 3 단독 실행 VR, 약 3분.
- 사용자 = 오르페우스(1인칭). 하데스(AI)의 조건: 지상에 닿기 전 뒤돌아보면 에우리디케를 영영 잃는다.
- 메시지: 계산하는 AI와 달리 인간은 결과를 모르는 선택 앞에서 망설인다. 마지막에 그 망설임(이동 경로·정지·시간)을 빛의 궤적으로 보여 준다.
- 기준 문서: 최종 기획서(1117). PPT는 톤 참고만. 키비주얼 = 어두운 청록 동굴, 청백색으로 빛나는 반투명 에우리디케, 출구의 따뜻한 역광, 바닥의 푸른 빛 물길.

## 2. 시나리오 (최종 기획서)
| 구간 | 시간 | 내용 | 사용자 행동 |
|---|---|---|---|
| S1 하데스의 방 | 0:00–0:40 | 하데스 얼굴이 정면 3걸음 거리에 떠 있음. 귀환 조건 설명 → "고개를 천천히 뒤로 돌려보아라" → 70° 넘으면 경고음 확인 → 왼쪽에서 에우리디케 등장, 손 내밀기 | 둘러보기, 손잡기 |
| S2-1 통로 | 0:41–1:10 | 바닥 불빛 유도, 머리 위 반투명 정령 속삭임("그녀가 과연 따라오고 있을까?", "죽은 자는 다시 살아날 수 없어"). 코너에 리라(엣지 하이라이트) | 리라를 집어 줄을 가로질러 연주 → 정령 조용, 뒤에서 에우리디케 작은 웃음 |
| S2-2 갈림길 | 1:11–1:30 | 안내 없는 두 갈래. 한쪽은 3~4걸음 뒤 막다른 길 | 뒤돌아보지 않고 뒷걸음질로 복귀 → 다른 길 점등 |
| S2-3 침수 | 1:31–2:20 | 더 밝은 통로. 뒤에서 넘어짐·비명("아아! 잠깐만, 기다려줘!") → 정령 재등장, 균열에서 물 → 5초 뒤 바위가 리라 선율로 울림 | 바위에 손바닥 → 돌들 공명, 물 빠짐, 돌 솟음, 출구 드러남. 에우리디케가 선율을 따라 부르다 끊김 |
| S3 결말 | 2:21–3:00 | 하데스: 홀로 나갈지 / 기약 없이 기다릴지 / 뒤돌아보고 함께 머물지. 빛나는 길과 모래시계 등장 | 길: 홀로 나감 → 뒤돌아보면 떠도는 영혼 + 궤적. 모래시계: 약 4초 뒤 회복 → 함께 나감 → "뒤돌아봐도 돼" → 웃는 에우리디케 + 궤적 |

- 회전 판정 (현재 통로 정면 기준): 70° 미만 경고 없음 / 70°부터 경고, 뒤로 갈수록 강하게 / 120° 이상 2초 연속 = 뒤돌아봄 확정 / 확정 전에 벗어나면 초기화.
- 상실 후에는 남은 경로를 홀로 진행.

## 3. 결정된 것
- 캐릭터는 3D 모델 대신 **2D 투명 PNG 홀로그램**: Quad + Unlit 투명 + Y축 빌보드 + 디졸브(노이즈→임계값→알파). 대사는 TTS로 오디오 파일을 만들어 넣을 예정(현재 보류, 자막만).
- 물: Plane 한 장 + 가벼운 물 셰이더, 수위 상한 가슴 높이. 긴박감은 소리·진동·조명·낙석으로. 카메라 흔들기 금지. 포스트프로세싱·굴절·실시간 반사 금지. 목표 72fps.
- 이동: 왼손 스틱(머리 방향 기준, 뒤로 당기면 뒷걸음질), 오른손 스냅 회전. 동굴 경사는 CharacterController로 따라감.
- 엔딩 궤적: 통로가 꺾여 지상에서 다 안 보이므로, 실제 통로 위 궤적 + 출구 위 축소 지도 두 가지로 보여 줌.
- 개발 PC가 macOS라 Quest Link 불가 → 에디터 데스크톱 미리보기, Meta XR Simulator, Build And Run으로 테스트.

## 4. 현재 코드 상태 (main)
두 갈래 구현이 공존한다. **다음 작업의 핵심은 이 둘을 하나로 합치는 것.**

### A. 1단계 VR 뼈대 (`Assets/OrpheusVR/Scripts/Experience/`, 씬 `Orpheus_VR_Main`)
- `OrpheusFlow`: S1→S2-1→S2-2→S2-3→S3→엔딩 전체 흐름(코루틴). 대사는 `OrpheusSubtitle` 자막.
- `OrpheusPlayer`: OVRCameraRig + CharacterController. OVRInput + 데스크톱(WASD/우클릭/E) 입력, `TeleportTo`, `Reached`(손 근접 또는 A/트리거), `Haptic`, `Fade`.
- `OrpheusRoute`: 경유점·막다른 길 가지, `Tangent`(현재 통로 정면), `Station`(진행값 ≈ v1 맵 Z m).
- `LookBackJudge`: 70/120/2초, practice 모드, `Confirmed` 이벤트.
- `TrajectoryRecorder`: 위치·정지·망설임·구간 시간 기록 → `Reveal()`로 빛의 선 + 축소 지도.
- `Editor/OrpheusVRSetup`: 메뉴 `Tools > Orpheus VR > Setup VR Experience`. v1 맵을 복제해 리그·흐름을 조립(멱등). 원본 씬 보존.
- 상태: Unity 컴파일 성공 확인. **Setup 메뉴 실행·Play·헤드셋 검증 아직 안 함.** 에우리디케는 임시 캡슐.

### B. v2 에우리디케 홀로그램 (상준, `Assets/OrpheusVR/Hologram/`, 씬 `Orpheus_Underworld_HologramTest`)
- `EurydiceHologramActor`: PNG 빌보드, `SetPose(Standing/Fallen)`, `Fade(visible)` 디졸브, `SetFeet`, Opacity 0.7.
- `EurydiceDirector`: 기존 데스크톱 프로토타입(`OrpheusCaveExperience`) 위에서 동작하는 시나리오. 등장·손잡기·약 2.5m 뒤 동행·넘어짐·기다림·홀로·상실·**함께 남기(선택 지점에서 뒤돌아보면)** 결말. 대사 `Lines`에 AudioClip 슬롯, `Audio/dialogue_manifest.json`.
- 셰이더 `Hologram/Shaders/EurydiceHologram.shader`(URP Unlit, _Dissolve/_Opacity/Cyan Glow). 텍스처 `*_softglow.png` 적용본.
- 데스크톱 에디터에서 시나리오 검사 14개 통과(헤드셋 미검증). 테스트 키 1~8, F 자동, R 리셋.
- 주의: 셰이더 이름이 단안/양안(Single Pass Instanced) 대응인지 헤드셋에서 확인 필요.

### 겹치는 부분
회전 판정, 상실 처리, 선택 분기, 자막이 A·B 양쪽에 있음. B는 VR 리그가 아닌 `OrpheusCaveExperience`(데스크톱 카메라)에 묶여 있음.

## 5. 다음 할 일 (순서대로)
1. **A 검증**: refresh_unity → 콘솔 에러 0 → Setup 메뉴 실행 → OrpheusFlow 빈 참조 확인 → Play 10초.
2. **A+B 통합** (plan mode로 계획 먼저, 상준과 합의):
   - VR 쪽(`OrpheusFlow`/`OrpheusPlayer`/`LookBackJudge`)을 기준 뼈대로 두고, 임시 캡슐 대신 `EurydiceHologramActor` 프리팹을 사용.
   - B의 동행(뒤를 따라오는 궤적), 넘어짐 포즈, 디졸브 등장/상실, 기다림 후 회복, '함께 남기' 결말을 `OrpheusFlow`로 옮기거나 `EurydiceDirector`가 `OrpheusFlow` 이벤트를 받도록 바꿈. `OrpheusCaveExperience` 의존 제거.
   - 판정은 `LookBackJudge` 하나로. 자막도 하나로.
   - B의 테스트 씬과 검사 스크립트는 통합 후 정리 여부를 팀과 결정.
3. 헤드셋 준비: Meta Project Setup Tool → Android Fix All(현재 XR 로더 없음), Build Profile Meta Quest, Build And Run.
4. 소리: 경고음(각도에 따라 강도), 에우리디케 발소리(플레이어 뒤 1.2~1.5m 3D), 리라 단음·선율 모티프, 물소리, BGM, TTS 대사.
5. 하데스 얼굴 2D 홀로그램(말할 때 프레임 교체), 정령, 리라 집기·줄 튕기기, 폭포 파티클·물 셰이더.

## 6. 아직 기획이 정할 것
- 회전 경고 음성 대본
- 상실 순간의 연출·대사 (지금은 디졸브 + 자막)
- 에우리디케를 잃은 채 S3에 도착했을 때 흐름 (지금은 하데스 한 줄 후 홀로)
- PPT에 있던 하데스 상황보고 대사를 넣을지
- 리라 선율 모티프 (연주·바위 공명·허밍에 같은 선율)
- 컨트롤러 vs 핸드 트래킹, 자막 유지 여부
- 참고: B는 '선택 지점에서 뒤돌아보면 함께 지하에 남는 결말'을 이미 구현함 → 팀 확정 필요

## 7. 자료 위치
- 에셋 리스트(155개, 우선순위·사양·MCP 활용): Claude 프로젝트 시트 https://claude.ai/artifact/Qg491nn1W41eMfdh8rj184
- 1단계 상세·체크리스트·조정 값: `docs/개발_1단계_VR뼈대.md`
- 홀로그램 테스트 안내: `Assets/OrpheusVR/Hologram/Documentation/테스트_실행안내.txt`
- 물 효과: Quest3 물 효과 구현 가이드(수업 자료) — Plane+셰이더, 폭포는 파티클, 측정은 OVR Metrics Tool

## 8. 협업 규칙
- 작업 전 `git pull`, 기능 하나 끝나면 커밋 후 `git push`. 커밋 메시지는 한 줄 요약.
- 같은 씬(.unity)을 두 사람이 동시에 수정하지 않기. 작업 씬은 미리 나누기.
- 씬·프리팹·.meta는 텍스트로 직접 고치지 말고 MCP 도구로.
