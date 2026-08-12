# 정만서버

두 사람이 Diablo II: Resurrected TCP/IP 모드에서 사용할 서버 전용 캐릭터 보관소와 실행기입니다. Battle.net 계정·캐릭터·프로토콜은 사용하지 않습니다.

구성은 다음과 같습니다.

- D2RLoader 1.0.1-beta: Windows D2R 실행과 TCP/IP 방 생성·접속
- 정만서버 서버: 계정, 장치 인증, 세이브 프로필 임대, 버전 이력, 감사 기록
- PostgreSQL: 캐릭터 원본, 보조 파일, 공유 창고, 이전 리비전 저장
- 정만서버 Windows 런처: 캐릭터 선택, 전체 화면·창 모드와 주요 해상도 선택, 서버 프로필 체크아웃, D2RLoader 실행, 자동 체크인·복구
- 서버 전용 캐릭터 생성: 모든 새 캐릭터를 레벨 99로 생성, 전 난이도 퀘스트·웨이포인트 완료, 영구 퀘스트 보상(안야 모든 저항 +30 포함) 및 호라드릭 큐브 기본 지급
- 캐릭터 관리: 서버 승인 이름 변경, 총 포인트 보존 스탯 초기화, 삭제·복구 휴지통
- PK 로비: 2인 방 생성·참가 예약, 호스트 주소 공유, 플레이 중 자동 만료 갱신
- 인게임 아이템 보급소: 레어 중심 도박(유니크 1%·세트 1%·레어 80%·매직 18%), 유니크·세트·룬워드·제작식 가변 수치와 베이스 방어력 상위 25%, 매직·레어 접사와 직업 전용 자동 옵션 상위 10% 보장. 고레벨 접사는 최대 15배 가중하며, 퀘스트 아이템을 제외한 유니크·세트 550종 정확 선택 카드와 액트 1 아카라 통합 카탈로그를 제공
- 인게임 베이스·재료 보급: 무기 8분류·방어구 7분류로 나눈 일반/에테리얼 베이스 508종과 룬·보석·작은/큰 부적·주얼 71종을 반복 생성하는 선택 카드
- 전체 자동지도: 1,092개 유효 레벨 프리셋의 `AutoMap`을 활성화해 구역에 진입하면 전체 지도를 즉시 공개
- 웹 사용 가이드: 런처에 포함된 `guide/index.html`을 기본 브라우저로 바로 열기

프로필은 캐릭터 `.d2s`, 캐릭터 보조 파일, 소프트코어·하드코어 공유 창고를 계정 단위로 함께 저장합니다. 기본 싱글 저장 폴더는 건드리지 않고 `Saved Games\Diablo II Resurrected\Mods\JMServer`만 임시 작업 폴더로 사용합니다. 플레이가 끝나면 서버 체크인 후 관리 대상 로컬 파일을 제거합니다.

D2R의 `savepath: JMServer/` 기능이 게임플레이·그래픽·소리 설정을 `Mods\JMServer\Settings.json`에 직접 분리 저장합니다. 이 파일은 해당 PC의 로컬 설정으로만 사용하며 서버에 다운로드하거나 업로드하지 않습니다. 런처는 기본 배틀넷용 `Settings.json`을 복사하거나 교체하지 않고, 기존 정만서버 설정도 화면 모드·해상도가 실제로 달라질 때만 두 항목을 수정합니다. 캐릭터별 키·조작 설정 파일인 `.key/.keyo/.ctl/.ctlo`는 캐릭터 프로필과 함께 체크인합니다. 장치 토큰, 서버 주소, 게임 설치 경로 같은 런처 연결 정보도 동기화하지 않습니다.

첫 캐릭터를 생성하면 최신 DLC 형식의 소프트코어 공유 창고를 함께 만들고, 일반 탭 5개에 각각 최대 250만 골드를 초기 지급합니다. Advanced Stash에는 게임 데이터상 중첩 가능한 전체 91종을 실제 사용 가능한 최대치인 99개씩 지급하고 새 캐릭터 생성 때 다시 보충합니다. 같은 계정의 소프트코어 캐릭터는 이 창고와 골드를 공유하며 하드코어 창고는 별도입니다.

일반 플레이는 Windows용 `정만서버.exe` 그래픽 런처에서 진행합니다. CLI 프로젝트는 개발·진단용으로만 유지합니다.

PK 로비는 실제 게임 트래픽을 중계하는 전용 게임 서버가 아닙니다. 정만서버는 두 플레이어의 방 예약과 접속 주소를 관리하고 D2RLoader를 실행합니다. 런처는 공개 TCP `15571`과 D2R의 로컬 TCP `4000` 사이를 자동 중계하므로 공유기와 Windows 방화벽에는 `15571`만 공개합니다. 정만서버 인증·캐릭터·로비 연결은 TCP `15570`을 사용합니다.

## 빌드와 테스트

```powershell
dotnet restore JmServer.slnx
dotnet build JmServer.slnx --configuration Release
dotnet test JmServer.slnx --configuration Release

dotnet run --project src/JmServer.Launcher.Cli -- `
  verify-loader third_party/downloads/D2RLoader-1.0.1-beta.zip
```

## 서버 관리 명령

```powershell
$env:JM_DATABASE = 'Host=127.0.0.1;Database=jm_server;Username=jm_server;Password=...'

dotnet run --project src/JmServer.Server -- migrate
dotnet run --project src/JmServer.Server -- bootstrap-device player1 player1-pc
dotnet run --project src/JmServer.Server -- import-character player1 Hero Paladin C:\saves\Hero.d2s
dotnet run --project src/JmServer.Server -- import-stash player1 C:\saves\ModernSharedStashSoftCoreV2.d2i
dotnet run --project src/JmServer.Server -- set-stash-gold player1
dotnet run --project src/JmServer.Server -- refill-stash-materials player1
dotnet run --project src/JmServer.Server -- reset-shared-stash player1
dotnet run --project src/JmServer.Server -- inspect-unique-charms player1
dotnet run --project src/JmServer.Server -- rotate-device-token <device-id>
dotnet run --project src/JmServer.Server
```

`bootstrap-device`가 출력하는 토큰은 DB에서 복구할 수 없습니다. 비밀번호 관리자에 저장하고 공개된 토큰은 폐기·재발급해야 합니다. 비루프백 주소에서 서버를 열 때는 TLS 인증서가 필수입니다.

## 문서

- [Windows 클라이언트 사용법](docs/client-quickstart.ko.md)
- [Windows 서버 설치](deploy/windows/README.md)
- [D2RLoader 사용 범위와 소스 상태](docs/d2rloader-poc.md)
- [인게임 아이템 보급소](docs/unidentified-item-supply.ko.md)
- [편의 기능과 호라드림 작업대](docs/qol-features.ko.md)
- [브라우저용 통합 사용 가이드](docs/web/index.html)
- [개발 명령](docs/development.md)
- [배포와 백업](docs/deployment.md)

## 현재 보안 경계

런처는 개별 `.d2s` 다운로드·업로드 명령을 노출하지 않고, 서버에 등록되지 않은 로컬 캐릭터를 격리합니다. 공유 창고도 서버 리비전에 포함됩니다. 캐릭터 생성과 관리 작업은 서버 정책으로만 실행하며 변경 전 리비전과 감사 기록을 남깁니다. 다만 사용자가 프로토콜을 직접 구현하거나 메모리를 수정하는 것까지 완전히 막는 안티치트는 아니므로 다음 단계에서 체크인 시 D2S 변경 정책 검사를 강화합니다.
