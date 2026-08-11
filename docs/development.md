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
