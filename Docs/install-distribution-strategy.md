# CodeScan 간편 설치 배포 전략 검토

## 문서 목적

이 문서는 CodeScan 빌드 산출물을 운영체제별 패키지 채널로 배포하기 위한 초기 전략 검토 문서다.

현재 채택 확정 문서가 아니며, 기술 검토 후 실제 배포 방식과 자동화 범위를 결정한다. 앞으로 CodeScan의 지식, 아키텍처, 간편 설치, 배포 운영 같은 기술 주제 문서는 `Docs/` 하위에 보관한다. 단, `Docs/` 문서가 항상 현재 구현에 즉시 적용된다는 의미는 아니며, 검토 중인 기술 방향과 운영 기준도 함께 다룬다.

## 목표

- 사용자가 플랫폼별 익숙한 패키지 매니저로 CodeScan을 설치할 수 있게 한다.
- 설치 명령은 최대한 짧고 예측 가능하게 유지한다.
- 설치 후 `codescan` 명령이 PATH에서 바로 동작해야 한다.
- CLI, TUI, GUI 기능은 동일한 바이너리에서 제공한다.
- 패키지 채널은 단순 설치 진입점으로 보고, 실제 산출물은 GitHub Release 중심으로 관리한다.

## 현재 상태

- CodeScan은 .NET 10 기반 Native AOT 단일 실행 파일을 목표로 한다.
- Windows PowerShell 환경에서 우선 테스트되고 있다.
- Linux 계열 환경은 현재 직접 빌드해서 사용할 수 있다.
- macOS/Linux 호환 CLI 사용법과 스킬 명령 래퍼는 준비 예정이다.
- Windows 배포 스크립트는 `Script/deploy-win.ps1`로 로컬 설치를 지원한다.

## 제안 배포 채널

| 플랫폼 | 제안 채널 | 사용자 설치 경험 | 역할 |
|--------|-----------|------------------|------|
| Windows | winget | `winget install psmon.CodeScan` | 공식 Windows 설치 경로 |
| Linux 계열 | npm | `npm install -g codescan-cli` | 배포 진입점 및 바이너리 설치 래퍼 |
| macOS | Homebrew | `brew install codescan` | 공식 macOS 설치 경로 |

## 공통 배포 원칙

1. GitHub Release를 원본 배포 지점으로 둔다.
2. 각 플랫폼 패키지는 GitHub Release의 버전별 바이너리를 다운로드하거나 포함한다.
3. 버전은 `version.txt`와 GitHub Release 태그를 일치시킨다.
4. 패키지 매니저별 manifest는 최소 정보만 유지하고, 복잡한 설치 로직은 스크립트나 release asset 규칙으로 분리한다.
5. 설치 검증은 `codescan --version`, `codescan --help`, `codescan query --help`, `codescan gui --help`를 기준으로 한다.

## 산출물 구조 제안

GitHub Release에는 다음 asset을 제공하는 방향을 검토한다.

```text
codescan-win-x64.zip
codescan-linux-x64.tar.gz
codescan-linux-arm64.tar.gz
codescan-osx-x64.tar.gz
codescan-osx-arm64.tar.gz
checksums.txt
```

각 압축 파일에는 최소한 다음을 포함한다.

```text
codescan(.exe)
README.md 또는 LICENSE
```

Windows는 `.exe`, Linux/macOS는 실행 권한이 있는 `codescan` 바이너리를 제공한다.

## Windows: winget 전략

### 설치 경험

```powershell
winget install psmon.CodeScan
codescan --version
```

### 패키징 방향

- GitHub Release의 `codescan-win-x64.zip` 또는 설치용 `.msi`를 대상으로 한다.
- 초기에는 zip 기반 portable 설치보다 installer 방식이 winget 검수에서 단순할 수 있다.
- 설치 후 `codescan.exe`가 PATH에 등록되어야 한다.

### 검토 항목

- winget manifest 패키지 식별자: `psmon.CodeScan` 또는 조직명 기준 재검토
- 설치 방식: portable zip, exe installer, msi 중 선택
- 코드 서명 필요성
- 업그레이드 시 기존 `~/.codescan/db` 보존 보장
- GUI 서버 포트, DB 경로 같은 런타임 데이터는 설치 폴더와 분리 유지

### 장점

- Windows 사용자에게 가장 자연스러운 설치 경로다.
- PowerShell 기반 테스트와 운영 문서화가 쉽다.
- 기존 Windows 배포 스크립트와 연결하기 쉽다.

### 리스크

- manifest 제출 및 업데이트 절차가 필요하다.
- unsigned binary에 대한 신뢰 경고가 발생할 수 있다.
- zip portable 방식은 PATH 처리 정책을 확인해야 한다.

## Linux: npm 전략

### 설치 경험

```bash
npm install -g codescan-cli
codescan --version
```

### 패키징 방향

npm 패키지는 JavaScript CLI 자체가 아니라 플랫폼별 CodeScan 바이너리를 설치하는 얇은 래퍼로 둔다.

검토 가능한 방식은 두 가지다.

1. `postinstall`에서 OS/arch를 판별하고 GitHub Release 바이너리를 다운로드한다.
2. npm 패키지 안에 플랫폼별 바이너리 패키지를 분리한다.

예상 구조:

```text
codescan-cli
├── bin/codescan.js
├── scripts/install.js
└── package.json
```

`codescan.js`는 실제 바이너리 위치를 찾아 실행한다.

### 검토 항목

- 패키지명: `codescan-cli` 사용 가능 여부
- postinstall 다운로드 허용 정책과 프록시 환경 대응
- glibc/musl 호환성
- x64/arm64 지원 범위
- 다운로드 실패 시 명확한 수동 설치 안내
- `npm uninstall -g` 시 설치된 바이너리 정리

### 장점

- Linux 사용자뿐 아니라 Node.js가 있는 CI 환경에서 접근성이 좋다.
- npm global bin으로 PATH 연결이 단순하다.
- 추후 macOS도 npm 경로로 보조 지원 가능하다.

### 리스크

- CodeScan은 Node.js 도구가 아니므로 패키지 정체성이 애매할 수 있다.
- postinstall 네트워크 다운로드가 기업망/보안 정책에서 차단될 수 있다.
- Linux 배포판별 libc 차이를 검증해야 한다.

## macOS: Homebrew 전략

### 설치 경험

```bash
brew install codescan
codescan --version
```

초기에는 공식 Homebrew core 진입보다 tap 운영을 먼저 검토한다.

```bash
brew tap psmon/codescan
brew install codescan
```

### 패키징 방향

- GitHub Release의 macOS tarball을 Homebrew formula에서 다운로드한다.
- Apple Silicon과 Intel macOS를 모두 고려한다.
- Formula는 `bin.install "codescan"` 형태의 단순 설치를 목표로 한다.

### 검토 항목

- tap 저장소명
- notarization 필요성
- arm64/x64 release asset 분리
- quarantine 관련 사용자 경험
- brew audit 통과 가능성

### 장점

- macOS 개발자에게 자연스러운 설치 경로다.
- 버전 업그레이드 경험이 좋다.
- tap으로 시작하면 공식 core보다 운영 부담이 낮다.

### 리스크

- macOS AOT 빌드와 서명/검증 환경이 필요하다.
- GitHub Actions macOS runner 비용과 빌드 시간을 고려해야 한다.
- notarization이 필요해질 경우 Apple Developer 계정 운영이 필요하다.

## 단순 스크립트 인스톨러

패키지 매니저 외에 직접 설치 스크립트도 보조 경로로 제공할 수 있다.

### Windows

```powershell
iwr https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install-win.ps1 -UseB | iex
```

또는 보안상 더 명확한 방식:

```powershell
iwr https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install-win.ps1 -OutFile install-win.ps1
.\install-win.ps1
```

### Linux/macOS

```bash
curl -fsSL https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install.sh | sh
```

보안 권장 방식:

```bash
curl -fsSL https://raw.githubusercontent.com/psmon/CodeScan/main/Script/install.sh -o install.sh
sh install.sh
```

### 스크립트 인스톨러 원칙

- OS와 CPU 아키텍처를 감지한다.
- GitHub Release에서 해당 asset을 다운로드한다.
- checksum을 검증한다.
- 사용자 홈 하위 또는 `/usr/local/bin` 중 설치 대상을 명확히 선택한다.
- 기존 설치가 있으면 버전을 확인하고 교체한다.
- 실패 시 수동 설치 방법을 출력한다.

## CI/CD 검토 방향

GitHub Actions 기준으로 다음 workflow를 검토한다.

1. tag push 또는 release workflow 수동 실행
2. Windows/Linux/macOS matrix build
3. Native AOT publish
4. 압축 및 checksum 생성
5. GitHub Release 업로드
6. winget manifest, Homebrew formula, npm package 업데이트 PR 또는 publish

초기에는 1-5만 자동화하고, 패키지 매니저 반영은 수동 검토 후 진행하는 것이 안전하다.

## 설치 후 검증 체크리스트

```bash
codescan --version
codescan --help
codescan search --help
codescan graph --help
codescan query --help
codescan gui --help
```

GUI 검증:

```bash
codescan gui start --port 8085
```

브라우저에서 `http://127.0.0.1:8085/` 접속 후 Keyword, Graph Search, Query, 2D/3D view를 확인한다.

## 채택 전 결정해야 할 사항

- GitHub Release asset 이름 규칙 확정
- Windows 설치 형식: zip portable, exe installer, msi 중 선택
- macOS tap 저장소 운영 여부
- npm 패키지명과 postinstall 방식 확정
- 코드 서명 및 notarization 필요 범위
- Linux glibc/musl 지원 범위
- x64/arm64 지원 범위
- checksums와 SBOM 제공 여부

## 권장 단계

1. GitHub Release asset 규칙과 checksum 생성을 먼저 확정한다.
2. Windows PowerShell 직접 설치 스크립트를 release asset 기반으로 정리한다.
3. Linux/macOS 직접 설치 스크립트를 추가한다.
4. npm 래퍼 패키지를 실험 브랜치에서 검증한다.
5. Homebrew tap formula를 별도 저장소 또는 `Docs` 예제로 검토한다.
6. winget manifest는 설치 형식이 확정된 뒤 준비한다.

## 현재 결론

초기 채택 우선순위는 다음과 같이 제안한다.

1. GitHub Release + checksum
2. Windows PowerShell installer
3. Linux/macOS shell installer
4. Homebrew tap
5. winget
6. npm global wrapper

다만 사용자가 요청한 최종 사용자 경험 목표는 Windows `winget`, Linux `npm`, macOS `brew`로 두고, 기술 검증 결과에 따라 순서와 구현 방식을 조정한다.
