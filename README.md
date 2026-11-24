# Orpheus VR

가상현실입문 5조 VR 프로젝트 — 오르페우스와 에우리디케 신화를 바탕으로 한 저승 동굴 탐험 프로토타입.

- Unity Editor **6000.3.26f1** (URP, Meta XR SDK 207.0.1)
- 기본 테스트 씬: `Assets/OrpheusVR/Scenes/Orpheus_Underworld_HologramTest.unity`
- 원래 v1 동굴: `Assets/OrpheusVR/Scenes/Orpheus_Underworld.unity`
- 최신 실행 방법: [`홀로그램 테스트 안내`](Assets/OrpheusVR/Hologram/Documentation/테스트_실행안내.txt)

## 조작 (에디터 Play 모드)

| 키 | 동작 |
|---|---|
| WASD | 이동 |
| 마우스 오른쪽 드래그 | 시점 회전 |
| Tab | 도입 자막을 건너뛰고 에우리디케 등장 |
| E / Enter | 등장 자막이 끝난 뒤 손잡기·출발 (E는 소품 상호작용도 수행) |
| F | 자동 시나리오 테스트 |
| 1–4 | 등장 / 동행 / 넘어짐 / 선택 지점 테스트 |
| 5–8 | 기다림 / 홀로 나가기 / 상실 / 함께 남기 테스트 |
| R | 시나리오 처음부터 다시 |
| H | 화면 안내 켜기/끄기 |

## 에우리디케 2D 홀로그램 테스트

v1 동선을 유지하면서 배경 투명 PNG 2장을 등신대 빌보드로 표시합니다. 청록색 광채를 약하게 조정한 `*_softglow.png`가 적용되어 있습니다.

- 기능: 왼쪽에서 등장·손 내밀기, 이동 궤적을 따라 동행, 넘어짐과 포즈 전환, 디졸브 등장/상실, 기다림 후 회복·재회와 분기 결말.
- 수동 모드: 진행 방향에서 70° 이상 돌아보면 경고, 120° 이상 2초 유지하면 상실하며 그 전에 정면으로 돌아오면 취소됩니다.
- 자동 모드: NavMesh 경로로 이동하며 기다림 결말을 진행합니다. 자동 카메라의 경로 보정은 상실 판정에서 제외합니다.
- 머티리얼: `Assets/OrpheusVR/Hologram/Materials/Eurydice_Hologram.mat`. `Cyan Glow Opacity`는 청록색 픽셀만 조절하며 현재 `0.12`, 전체 `Opacity`는 `0.7`입니다.
- TTS는 요청에 따라 보류했습니다. 대사는 자막으로 표시하며 `EurydiceDirector > Lines > Clip`에 추후 오디오를 연결할 수 있습니다.
- 동작 검증: 원본 작업 프로젝트의 에디터 Play 모드에서 [시나리오 검사 14개](Assets/OrpheusVR/Hologram/Documentation/scenario_test_results.json)를 통과했습니다. 이 저장소에서의 재실행 및 실제 VR 헤드셋 검증은 별도입니다.

첫 임포트 후 테스트 씬이 한 번 자동으로 열립니다. 자동으로 열리지 않으면 기본 테스트 씬을 직접 열고 **Play → Tab → E**, 또는 **Play → F**로 확인하세요. Game 탭이 없으면 **Window > General > Game**을 선택합니다.

실제 손 추적·그랩이나 3D 보행 애니메이션은 포함하지 않은 PNG 이동/포즈 전환 프로토타입입니다. 체험 시간은 사용자 이동과 선택에 따라 달라집니다.

## MCP for Unity (Claude Code 연결)

`Packages/manifest.json`에 [MCP for Unity](https://github.com/CoplayDev/unity-mcp) v10.3.0이 포함되어 있습니다.

1. 프로젝트를 열면 패키지가 자동 설치됩니다 (Python, uv, Git 필요).
2. `Window > MCP for Unity`에서 **Start Server** (HTTP Local, `127.0.0.1:8080`) → Session Active 확인.
3. 프로젝트 폴더에서 `claude` 실행 → `/mcp`에 ✔ UnityMCP가 보이면 연결 완료.
   (루트의 `.mcp.json`이 서버 주소를 등록해 둡니다. Unity의 Configure 버튼을 써도 됩니다.)

## 에셋 출처

Kenney Nature Kit 2.1 (CC0) — `Assets/OrpheusVR/ThirdParty/KenneyNature/License.txt`
