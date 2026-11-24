# Orpheus VR — 프로젝트 규칙 (Claude Code + MCP for Unity)

가상현실입문 5조 「사랑으로 그린 망설임」. 사용자는 오르페우스가 되어 지하세계에서 에우리디케를 데리고 나온다.
뒤돌아보면(120° 이상 2초) 그녀를 잃는다. 끝나면 지나온 길과 머문 시간이 빛의 궤적으로 보인다.

## 환경 (버전 고정, 바꾸지 말 것)
- Unity 6000.3.26f1, URP 17.3, Meta XR SDK 207.0.1 (OVRCameraRig, OVRInput), Input System
- MCP for Unity (CoplayDev) v10.3.0, Claude Code
- 개발 PC는 macOS → Quest Link 불가. 테스트는 ① 에디터 Play 데스크톱 미리보기(WASD, 우클릭 시점, E), ② Meta XR Simulator, ③ Quest 3에 Build And Run

## 폴더
- `Assets/OrpheusVR/Scenes/Orpheus_VR_Main.unity` ← 작업 씬 (빌드 0번). `Orpheus_Underworld.unity`는 원본 맵, 수정 금지
- `Assets/OrpheusVR/Scripts/Experience/` ← 체험 코드 (namespace `OrpheusVR`, 파일명 = 클래스명)
- `Assets/OrpheusVR/Editor/` ← 에디터 도구 (`OrpheusCaveBuilder` 맵 생성기, `OrpheusVRSetup` VR 조립)
- 새 에셋: Audio, Characters(2D 홀로그램 PNG), Materials, Prefabs는 `Assets/OrpheusVR/` 아래 해당 폴더
- `Assets/Oculus`, `Assets/XR`, `ProjectSettings`, `Packages`는 사용자 승인 없이 수정 금지

## 구조
| 스크립트 | 역할 |
|---|---|
| OrpheusFlow | 전체 흐름. S1 하데스 방 → S2-1 리라 → S2-2 갈림길 → S2-3 침수·공명 → S3 선택 → 엔딩 → 궤적 |
| OrpheusPlayer | OVRCameraRig + CharacterController. 왼손 스틱 이동, 오른손 스냅 회전, A/오른손 트리거 상호작용, 페이드, 진동 |
| OrpheusRoute | 경유점, '현재 통로 정면'(접선), 진행값 station(≈v1 맵 Z좌표 m) |
| LookBackJudge | 70° 경고 / 120° 2초 확정 / 이탈 시 초기화. practice 모드는 경고만 |
| TrajectoryRecorder | 위치·정지·구간 시간 기록, 엔딩에 실제 궤적 + 축소 지도 표시 |
| OrpheusSubtitle | 대사 자막 (TTS 음성 연결 전 임시, 이후 자막용) |

- 씬 조립은 `Tools > Orpheus VR > Setup VR Experience` 메뉴로 다시 만들 수 있음 (VR 오브젝트만 지우고 재조립)
- 맵 오브젝트는 이름으로 찾음: `Player spawn`, `Hades apparition - replace with face asset`, `Handhold interaction`, `REF_Lyre`, `Dead end / retreat trigger`, `Resonating path stone 0~5`, `REF_Wait`, `REF_Leave`, `Ending gaze / journey trace` 등. 이름을 바꾸면 Setup 메뉴도 같이 수정

## 코드 규칙 (Quest 3)
- Update에서 Find, GetComponent 매 프레임 호출 금지. 참조는 인스펙터 또는 Setup 메뉴로 연결
- 입력은 OVRInput(헤드셋) + Input System Keyboard/Mouse(데스크톱 미리보기) 둘 다 지원
- 카메라 흔들기 금지(멀미). 긴박감은 소리, 진동, 조명, 파티클로
- RenderSettings(Fog 등)를 바꾸면 OnDestroy에서 복구
- 포스트 프로세싱, 굴절, 실시간 반사, 볼류메트릭 금지 (72fps 목표)
- 씬·프리팹·.meta 파일을 텍스트로 직접 고치지 말고 MCP 도구로

## 검증 루프
1. 작업 후 refresh_unity → read_console 에러 0
2. Play 모드 실행 후 에러 재확인 (데스크톱 미리보기로 흐름 확인 가능)
3. 결과는 표로 보고 (바꾼 것, 파일, 확인 결과)
4. 같은 오류 3회 실패하면 중단하고 원인 후보를 보고
5. 씬을 바꿨으면 반드시 저장 (manage_scene save)
6. 기능 하나가 동작하면 사용자 확인 후 커밋: `한 줄 요약` 형식
