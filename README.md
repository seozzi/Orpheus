# Orpheus VR

가상현실입문 5조 VR 프로젝트 — 오르페우스와 에우리디케 신화를 바탕으로 한 저승 동굴 탐험 프로토타입.

- Unity Editor **6000.3.26f1** (URP, Meta XR SDK 207.0.1)
- 기본 씬: `Assets/OrpheusVR/Scenes/Orpheus_Underworld.unity`
- 자세한 실행 방법과 조작법: [`먼저읽기_실행방법.txt`](먼저읽기_실행방법.txt)

## 조작 (에디터 Play 모드)

| 키 | 동작 |
|---|---|
| WASD | 이동 |
| 마우스 오른쪽 드래그 | 시점 회전 |
| E | 가까운 소품 상호작용 |
| F | 자동 투어 켜기/끄기 |
| H | 화면 안내 켜기/끄기 |

## MCP for Unity (Claude Code 연결)

`Packages/manifest.json`에 [MCP for Unity](https://github.com/CoplayDev/unity-mcp) v10.3.0이 포함되어 있습니다.

1. 프로젝트를 열면 패키지가 자동 설치됩니다 (Python, uv, Git 필요).
2. `Window > MCP for Unity`에서 **Start Server** (HTTP Local, `127.0.0.1:8080`) → Session Active 확인.
3. 프로젝트 폴더에서 `claude` 실행 → `/mcp`에 ✔ UnityMCP가 보이면 연결 완료.
   (루트의 `.mcp.json`이 서버 주소를 등록해 둡니다. Unity의 Configure 버튼을 써도 됩니다.)

## 에셋 출처

Kenney Nature Kit 2.1 (CC0) — `Assets/OrpheusVR/ThirdParty/KenneyNature/License.txt`
