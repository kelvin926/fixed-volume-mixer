# 고정 볼륨 믹서

짧게 소리를 내고 사라지는 프로그램도 목록에 남겨두는 Windows용 볼륨 믹서입니다.

## 실행

`dist/FixedVolumeMixer.exe`를 다운로드해 실행하세요. 설치나 관리자 권한이 필요 없습니다.
Windows x64 및 .NET Framework 4.x 환경용입니다. 현재 Windows 11 PC에서 동작을 확인했습니다.

## 기능

- 한 번 발견한 앱은 재생이 끝나거나 프로세스가 종료되어도 목록에 유지
- 앱별 0~100% 볼륨을 0.1% 단위로 조절하고 음소거
- 실행 중이지 않은 앱의 볼륨도 미리 저장하고 다음 오디오 세션 생성 시 적용
- 실행 파일을 선택해 앱을 미리 등록
- 설정과 앱 목록 자동 저장, 외부 믹서에서 바꾼 값 반영
- 기본 출력 장치 변경 감지
- 트레이 실행 및 선택 가능한 Windows 시작 시 자동 실행

PowerShell/Codex 알림과 휴대폰과 연결 항목을 각각 1%로 미리 등록합니다. 다른 앱은 처음 발견한 볼륨을 그대로 사용합니다.

창의 X 버튼은 트레이로 숨기며, `완전히 종료`를 누르면 프로그램이 종료됩니다.
설정 파일은 `%LOCALAPPDATA%\FixedVolumeMixer\settings.json`입니다.

## 동작 범위

Windows 기본 믹서의 항목을 고정하는 대신, 이 프로그램 자체의 목록을 유지합니다.
Windows Core Audio의 세션 생성 콜백과 주기적인 확인을 함께 사용합니다.

- 같은 실행 파일 이름은 하나의 항목으로 관리합니다. PowerShell 항목은 Codex 외에 `powershell.exe`에서 재생하는 다른 소리에도 적용됩니다.
- 알림과 통화가 같은 세션이면 두 소리의 볼륨이 함께 바뀝니다.
- 현재 기본 출력 장치의 앱 세션을 관리하며, 장치가 바뀌면 저장한 앱 볼륨을 적용합니다.
- 처음 보는 앱의 첫 소리 시작 부분까지 차단하는 하드 리미터는 아닙니다.
- 앱이 아직 한 번도 재생되지 않았다면 실행 파일을 미리 등록하거나 소리를 한 번 재생해야 합니다.
- 프로그램이 꺼진 동안에는 새 세션을 감시하지 않습니다. 이미 적용한 Windows 볼륨 설정은 유지됩니다.

## 빌드

Windows PowerShell에서 다음을 실행합니다. 별도 NuGet 패키지는 사용하지 않습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Windows에 포함된 .NET Framework 64비트 C# 컴파일러를 이용해 `dist/FixedVolumeMixer.exe`를 만듭니다.

## 테스트

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

테스트에는 활성 기본 출력 장치가 필요합니다. 테스트 프로그램의 오디오 세션만 조절하며 소리를 재생하지 않습니다.
검증 항목:

- 설정 저장·복원과 음소거/볼륨 API 동작
- 새로 생성된 짧은 오디오 세션에 저장한 볼륨 적용
- 프로세스 종료 후 목록 유지
- 종료된 동안 편집한 값의 다음 실행 적용
- 외부 볼륨 변경 반영
- 처음 발견한 앱의 자동 등록 및 재시작 후 목록 복원

테스트 중인 앱 이름은 `MixerSessionProbe.exe`, `MixerDiscovered.exe`입니다. 임시 결과는 `.build`에만 저장합니다.

## 파일

- `src/FixedVolumeMixer.cs`: Windows Forms UI, 저장 및 Core Audio 제어
- `dist/FixedVolumeMixer.exe`: 실행 파일
- `tests/`: 오디오 세션 통합 테스트
- `사용방법.txt`: 간단한 사용 설명
