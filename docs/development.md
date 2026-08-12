# 개발과 로컬 실행

## 요구 사항

- .NET SDK 10
- PostgreSQL 18 또는 호환 버전
- Windows 클라이언트 빌드에는 Windows x64 SDK
- D2RLoader 검증에는 고정된 1.0.1-beta 원본 ZIP

## 빌드

```powershell
dotnet restore JmServer.slnx
dotnet build JmServer.slnx --configuration Release
dotnet test JmServer.slnx --configuration Release
```

## 서버

```powershell
$env:JM_DATABASE = 'Host=127.0.0.1;Database=jm_server;Username=jm_server;Password=...'
$env:JM_LISTEN_IP = '127.0.0.1'
$env:JM_LISTEN_PORT = '15570'

dotnet run --project src/JmServer.Server -- migrate
dotnet run --project src/JmServer.Server -- bootstrap-device player1 dev-pc
dotnet run --project src/JmServer.Server -- import-character player1 Hero Paladin C:\saves\Hero.d2s
dotnet run --project src/JmServer.Server
```

마이그레이션 `002_account_vaults`는 기존 `characters.save_data`를 계정 프로필 파일로 승격합니다. 이후 플레이 체크인은 캐릭터, 보조 파일, 공유 창고를 하나의 계정 리비전으로 저장합니다.

마이그레이션 `004_device_settings`는 D2R `Settings.json`을 계정·장치별로 저장합니다. 프로토콜 v7의 get/put 요청은 최대 256 KiB의 JSON 객체와 SHA-256을 검증하며, 같은 내용의 재업로드는 리비전을 올리지 않습니다. 플레이 시작 시 로컬 파일이 있으면 로컬을 우선하고, 없을 때만 서버 백업을 설치합니다. 게임 실행 세션 동안 정만서버 설정을 D2R 기본 저장 폴더에 임시 적용하고 종료 시 변경분을 모드 저장 폴더로 회수한 뒤 기존 기본 설정을 복원합니다. 중단된 교환 작업은 다음 실행 또는 복구 체크인에서 복구합니다. 이 데이터는 캐릭터 프로필 체크인과 분리되어 설정 저장 실패가 세이브 회수를 막지 않습니다.

개발·복구 점검에서는 CLI의 `settings-push`로 현재 장치 설정을 올리고 `settings-pull`로 서버 저장본을 내려받을 수 있습니다. 일반 사용자는 별도 명령 없이 플레이 시작·종료 흐름에서 자동 동기화됩니다.

## 클라이언트

```powershell
dotnet run --project src/JmServer.Launcher.Cli -- probe
dotnet run --project src/JmServer.Launcher.Cli -- list
dotnet run --project src/JmServer.Launcher.Cli -- `
  play <character-guid> 'C:\Program Files (x86)\Diablo II Resurrected'
```

개별 캐릭터 체크아웃·체크인 명령은 프로토콜 v2에서 제거했습니다. 개발 중 강제 종료로 프로필이 남으면 `recover-checkin`을 사용합니다.

## 인게임 보급소 데이터 다시 생성

생성기는 D2R `3.2.92777`에 대응하는 고정 원본 리비전만 받습니다. 입력 폴더에는 고정 Excel 테이블과 현재 D2R에서 추출한 `items.json`, `item-names.json`, `ui.json`이 함께 있어야 하며, 입력 파일 해시가 하나라도 다르면 중단합니다.

```powershell
dotnet run --project src/JmServer.Launcher.Cli -- `
  build-supply-mod C:\path\to\D2R-Excel `
  C:\path\to\generated-mod
```

현재 소스 리비전은 `pinkufairy/D2R-Excel`의 `cb27a8f574b873807e15cf9613d04d655cabdb60`입니다. 생성된 `package-manifest.json`의 파일별 SHA-256은 런처 설치와 실행 전 검증에 사용됩니다. 생성 결과를 교체한 뒤 전체 테스트와 실제 D2R 테이블 로드 시험을 모두 다시 수행해야 합니다.

## 배포 산출물

```powershell
.\scripts\publish-windows.ps1
.\scripts\publish-windows-client.ps1
```

결과는 `artifacts\jm-server-win-x64.zip`과 `artifacts\jm-launcher-win-x64.zip`에 생성됩니다.
