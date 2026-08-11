# D2RLoader 사용 범위와 소스 상태

D2RLoader는 Blizzard가 제공하거나 지원하는 구성 요소가 아닌 타사 베타 소프트웨어입니다. 정만서버 런처는 고정된 배포 ZIP과 핵심 파일의 SHA-256을 검증한 뒤 TCP/IP 전용 설정으로 설치합니다.

## 고정 버전

```text
D2RLoader 1.0.1-beta ZIP
989D1D30F54324DAF3C68C8F28E25B329CBCA7C77E7C87C7E0EBE4B0F75F3B9F

D2RLoader.exe
0FE9C136673BD6F5EC53A062A1A9782DA2B18E6C65B3BFCA8460EE2D93A96A6B

D2RCore.dll
151242286E64A0A98502726571646809B562D4A0B39A45EEE243E0EA200AEA76
```

## 소스 코드 확인 결과

2026-08-08 기준 제작자의 공개 릴리스 ZIP에는 실행 파일, DLL, TOML 설정만 있고 소스 코드, 빌드 프로젝트, 라이선스 파일이 없습니다. 제작자의 공개 GitHub 저장소에서도 D2RLoader 또는 D2RCore 소스 저장소를 찾지 못했습니다. 따라서 현재 배포 DLL을 역컴파일한 결과를 소스 포크로 취급하거나 재배포하지 않습니다.

제작자 릴리스 설명에는 공개 Plugin SDK, JSON 메모리 패치, `d2rl init/convert/update/unpack` 도구가 안내되어 있습니다. 정식 소스 저장소와 라이선스를 받기 전까지는 다음 경계를 사용합니다.

- D2RLoader/D2RCore: 해시가 고정된 업스트림 런타임
- 정만서버 서버/런처: 이 저장소에서 빌드하는 C# 소스
- 정만서버 모드 데이터·JSON 패치·플러그인: SDK와 라이선스가 확보되면 별도 소스 폴더에서 빌드

업스트림 소스 확보 상태와 수용 조건은 [`external/D2RLoader/README.md`](../external/D2RLoader/README.md)에 기록합니다.

## 제한 설치

`prepare-client`는 TCP/IP 버튼을 켜고 전역·모드 플러그인, 개발자 콘솔, 디버그 도구를 끕니다. `mods/JMServer/JMServer.mpq/data/global/.DISABLE_DEBUG`도 설치합니다. D2RLoader는 방을 상시 호스팅하는 전용 게임 서버가 아니며, 실제 게임 방은 한 플레이어의 Windows D2R 프로세스가 TCP 4000에서 호스트합니다.

정만서버 프로토콜 v6의 PK 로비는 2인 방 예약, 호스트 IPv4 공유, 만료·갱신을 담당합니다. 공개된 D2RLoader 1.0.1-beta 설정에는 게임 포트 변경이나 자동 참가 인자가 없으므로, 런처가 공개 TCP 15571과 D2R의 로컬 TCP 4000을 양쪽 PC에서 중계합니다. 방 생성·참가 선택은 게임 안에서 직접 수행합니다.
