# 정만서버 Windows 설치와 업그레이드

대상 PC는 `192.168.0.10`, 기본 포트는 `15570/tcp`입니다. 공개 게임 중계 포트는 호스트 플레이 PC의 `15571/tcp`입니다.

## 최초 설치

관리자 PowerShell에서 압축을 푼 폴더로 이동한 뒤 실행합니다.

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\install-jm-server.ps1 `
  -ListenIp 192.168.0.10 `
  -ListenPort 15570 `
  -AllowAnyRemoteAddress
```

설치기는 PostgreSQL `jm_server` 역할의 비밀번호를 보이지 않는 입력창으로 받습니다. 비밀번호는 명령 기록에 남기지 않습니다. `정만서버.exe`, TLS PFX, 로컬 설정은 내부 호환 경로 `C:\Program Files\JM Server`에 저장하고 표시 이름이 `정만서버`인 Windows 서비스를 등록합니다. 내부 서비스 이름은 기존 설치와의 호환을 위해 `JmServer`를 유지합니다.

`-AllowAnyRemoteAddress`는 모든 원격 주소를 Windows 방화벽에서 허용합니다. 공인 포트포워딩을 시작하기 전에는 가능한 한 실제 접속자의 공인 IP만 `-AllowedRemoteAddress`로 허용하십시오. PostgreSQL 5432 포트는 외부로 전달하지 않습니다.

## 기존 서버 업그레이드

프로토콜 v6 서버와 클라이언트는 한 쌍입니다. 먼저 게임을 종료하고 새 서버 ZIP을 푼 폴더의 관리자 PowerShell에서 실행합니다.

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\upgrade-jm-server.ps1 -ListenPort 15570
```

업그레이드 스크립트는 서비스를 중지하고 기존 실행 파일과 로컬 설정을 `C:\Program Files\JM Server\backup`에 보관한 뒤, 수신 포트와 기존 Windows 방화벽 규칙을 갱신합니다. 새 실행 파일로 DB 마이그레이션을 실행하고 서비스를 다시 시작하며 DB 접속 설정과 TLS 인증서는 그대로 유지되므로 클라이언트 인증서 SHA-256 핀도 바뀌지 않습니다.

확인 명령:

```powershell
Get-Service JmServer
Test-NetConnection 192.168.0.10 -Port 15570
(Get-FileHash "$env:ProgramFiles\JM Server\정만서버.exe").Hash
```
