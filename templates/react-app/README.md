# NetWiv + React

React 19 + Vite 8 + 공식 `@tauri-apps/api@2.11.1` + NetWiv 예제입니다. .NET 8 이상 SDK와 Node.js 20.19+ 또는 22.12+가 필요합니다. Windows에서는 WebView2 Runtime, Linux에서는 GTK3·WebKitGTK 4.1, macOS 12 이상에서는 시스템 WKWebView/AppKit을 사용합니다. Rust는 필요하지 않습니다. 자동 프런트엔드 생성기는 이전 `0.1.0-beta.1` 패키지에 포함되지 않았으므로 새 SDK 패키지를 사용해야 합니다.

프로젝트의 `NuGet.Config`에 NetWiv 로컬 패키지 폴더를 추가하세요. 모든 NetWiv 패키지는 `src-netwiv/ReactExample.csproj`의 `NetWivVersion`과 같은 버전이어야 합니다. 프레임워크가 제공한 템플릿 패키지에서 생성할 수도 있습니다:

```powershell
dotnet new install /path/to/NetWiv.Templates.nupkg
dotnet new netwiv-react -n MyApp --NetWivVersion 0.1.0-dev
```

## 바로 실행

`ReactExample.sln`를 열면 C# 앱과 `ReactExample.Frontend.esproj`가 함께 표시됩니다.
실행/디버그 대상으로 C# 프로젝트 `ReactExample`를 선택하세요. 솔루션 빌드와
C# 프로젝트 단독 빌드 모두 프런트엔드를 먼저 빌드하고 자산을 앱에 포함합니다.
`.esproj`는 MIT 라이선스의 `Microsoft.Build.NoTargets/3.7.134`를 사용합니다.
이 SDK는 최초 빌드 시 NuGet에서 복원되며 Rust는 필요하지 않습니다.

```powershell
npm ci
npm run desktop
```

먼저 C# 명령으로부터 `src/generated/commands.js`와 `commands.d.ts`를 자동 생성하고, React를 빌드한 뒤 C# 앱을 실행합니다. 앱 창에서 이름을 입력하고 **C#에 인사하기**를 누르세요. 표시되는 호출 횟수는 C#의 `State<GreetingCounter>`에서 관리합니다.

## React 개발 서버 사용

Rider나 Visual Studio에서는 `ReactExample.sln`를 열고 C# 프로젝트의 일반 Debug 실행을
사용할 수도 있습니다. MSBuild가 npm 의존성 복원, C# 바인딩 생성, React 빌드와 자산
포함을 자동 처리합니다. Node.js/npm이 IDE 프로세스의 PATH에 있어야 합니다.
이 경로는 빌드한 React 파일을 사용하므로 화면을 수정한 뒤 다시 빌드·실행하세요.
C# 코드에는 IDE 중단점을 사용할 수 있습니다. 아래 개발 서버 명령은 React 수정의
실시간 반영이 필요할 때 사용합니다.

프런트엔드만 빌드하려면 `dotnet build ReactExample.Frontend.esproj`를 사용하세요.
프런트엔드는 바인딩 생성용 C# 빌드만 호출하며, 이 내부 빌드는 프런트엔드 프로젝트
참조를 제외합니다. `beforeBuildCommand`를 별도로 추가하면 그 훅도 실행되므로
템플릿은 중복 `npm run build` 훅을 지정하지 않습니다.

```powershell
npm run desktop:dev
```

프로젝트에 고정된 `NetWiv.Cli` 도구를 복원한 뒤, NetWiv CLI가 Vite를 시작하고 준비를 기다린 후 앱을 실행합니다. React·CSS 수정은 Vite를 통해 앱 창에 반영됩니다. C# 수정은 바인딩 생성과 앱 재시작으로 반영됩니다. 개발 세션을 종료하려면 시작한 터미널에서 Ctrl+C를 누르세요. 로컬 NuGet 피드에는 SDK와 같은 버전의 `NetWiv.Cli` 패키지도 있어야 합니다.

브라우저만 미리 보려면 `npm run dev`를 사용하세요. 별도 터미널의 Vite에 앱만 연결하려면 `npm run desktop:dev:backend`를 사용합니다. 일반 브라우저에서는 C# 호출 버튼이 비활성화되므로 IPC는 NetWiv 창에서 확인하세요.

서버는 `127.0.0.1:5173`에 고정했습니다. 포트가 사용 중이면 Vite가 오류를 내며 임의의 다른 포트를 선택하지 않습니다. 앱은 Debug에서 `--dev`를 지정했을 때만 이 로컬 origin을 허용합니다.

## 핵심 코드

`src/App.jsx`:

```jsx
import { commands } from './generated/commands.js';
const result = await commands.greet({ name });
setGreeting(result);
```

`src-netwiv/Program.cs`:

```csharp
[Command]
public static Greeting Greet(string name, State<GreetingCounter> counter)
    => new($"Hello, {name}!", counter.Value.Next());
```

명령은 `.InvokeHandler("greet")`로 등록합니다. `[assembly: GenerateFrontendBindings]`가 JS/TS 생성을 켭니다. 새 반환 DTO를 만들면 `AppJsonContext`에 `[JsonSerializable(typeof(MyDto))]`를 추가하세요. 생성된 파일은 직접 수정하지 마세요. 원래 `invoke` API도 그대로 쓸 수 있습니다.

## NativeAOT 배포

```powershell
dotnet publish src-netwiv -c Release -r win-x64 -p:PublishAot=true -o publish/win-x64
```

Windows NativeAOT 빌드에는 C++ Build Tools와 Windows SDK가 필요합니다. 실행 파일은 `publish/win-x64/ReactExample.exe`이며 DLL을 포함한 publish 폴더 전체가 필요합니다. Linux에서는 해당 OS에서 `-r linux-x64`로 빌드하고 GTK3·WebKitGTK 4.1을 설치하세요.

macOS에서는 Xcode Command Line Tools와 CPU에 맞는 .NET SDK·NetWiv 네이티브 패키지가 필요합니다.
Apple Silicon은 `-r osx-arm64`, Intel은 `-r osx-x64`로 해당 Mac에서 빌드하세요.
실행 파일과 `.dylib`를 포함한 publish 폴더 전체를 사용하세요. AppKit 이벤트 루프는
프로세스의 메인 스레드에서 실행해야 하므로 생성된 동기 `Main`을 유지하세요.

앱 프로젝트 루트에서 다음 명령으로 `.app`과 `.dmg`를 생성할 수 있습니다:

```sh
dotnet tool restore
dotnet tool run netwiv -- build --target osx-arm64 --bundles app,dmg --no-sign
```

결과는 `target/osx-arm64/release/bundle/`에 생성됩니다. Intel에서는 타깃을
`osx-x64`로 바꾸세요. `--no-sign`도 실행에 필요한 ad-hoc 서명은 갱신합니다.
Developer ID 신뢰를 제공하지 않으며 공증 자동화는 구현되지 않았습니다.
Intel 실기 실행과 macOS 패키지 전용 소비자 검증도 아직 완료되지 않았습니다.

빌드된 React 파일은 실행 파일의 리소스에 포함됩니다. 배포 실행에는 Node.js나 Vite 서버가 필요하지 않습니다. NuGet.Config가 로컬 packages 폴더를 사용한다면 폴더 관계를 유지하세요.

## 자동 확인

```powershell
npm run smoke
# Vite가 실행 중인 경우:
dotnet run --project src-netwiv -p:NetWivDevelopment=true -- --dev --smoke-test
```

실제 React 폼 제출 → 공식 invoke → 생성된 C# 명령 → JSON 응답을 검사하고 종료합니다. 일반 실행에는 검사 종료 명령을 등록하지 않습니다. 전체 TAURI® 호환성·보안·플랫폼 검증이 완료된 버전은 아니며, 베타 SDK의 기존 제한이 적용됩니다.

구성 참고: [React의 Vite 기반 앱 안내](https://react.dev/learn/build-a-react-app-from-scratch), [Vite 사용법](https://vite.dev/guide/).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
