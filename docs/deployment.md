# Linux 서버와 TLS 배포

## 원칙

- PostgreSQL은 외부 인터넷에 공개하지 않습니다.
- 정만서버 `15570/tcp`는 가능하면 허용할 두 플레이어의 공인 IP만 방화벽에 등록합니다.
- 사설망을 사용해도 정만서버 프로토콜은 원격 바인딩 시 TLS를 요구합니다.
- PFX 개인 키와 `jm-server.env` 권한은 서버 계정만 읽을 수 있게 `0600`으로 둡니다.

## 게시

Linux x64 서버 예시:

```powershell
dotnet publish src/JmServer.Server/JmServer.Server.csproj `
  --configuration Release `
  --runtime linux-x64 `
  --self-contained true `
  --output artifacts/jm-server-linux-x64
```

게시 결과를 `/opt/jm-server`에 복사하고 `deploy/jm-server.service`와
`deploy/jm-server.env.example`을 기준으로 설정합니다.

## TLS

서버 인증서에는 런처가 접속할 DNS 이름이 SAN으로 들어 있어야 합니다. 인증서와
개인 키를 PFX로 준비한 뒤 다음을 설정합니다.

```text
JM_LISTEN_IP=<서버의 사설망 IPv4 주소>
JM_TLS_CERTIFICATE=/etc/jm-server/jm-server.pfx
JM_TLS_PASSWORD=<PFX 암호>
```

`JM_LISTEN_IP=any`, 공인 IP, 사설망 IP 등 루프백이 아닌 모든 바인딩은 인증서가
없으면 실패합니다. 방화벽에서도 두 플레이어의 사설망 주소만 허용해야 합니다.

## 데이터 백업

최소 백업 대상은 PostgreSQL의 `jm` 스키마입니다. `characters`에는 최신 세이브,
`character_versions`에는 이전 리비전, `audit_events`에는 체크아웃·체크인 기록이
저장됩니다. DB 백업의 암호화와 보존 기간은 기존 PostgreSQL 운영 정책에 맞춥니다.
