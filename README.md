# iron-prow

> **safe-inference gateway** — every LLM call's first gate.  
> provider selection (frontier ∨ LAN GpuStack) · guardrail · resilience — delivered as a standard `Microsoft.Extensions.AI.IChatClient`.

두 갈래 필수: frontier/LAN 게이트웨이(A) **and** local-provider safety(B).  
역할·범위·설계 근거는 [`CHARTER.md`](./CHARTER.md) 참조.

## Packages

| Package | 역할 |
|---|---|
| `IronProw.Core` | 게이트웨이 계약, resilience, 선택 로직 — iyulab 구현 의존 0 |
| `IronProw.IronHive` | IronHive OpenAI / Anthropic provider 어댑터 |
| `IronProw.FluxGuard` | FluxGuard guardrail 어댑터 (`IGuard` 구현) |
| `IronProw.LMSupply` | 로컬 추론 안전 래퍼 (length-bounding + readiness gate) |

```bash
dotnet add package IronProw.Core
dotnet add package IronProw.IronHive    # frontier provider adapters
dotnet add package IronProw.FluxGuard  # guardrail adapter
dotnet add package IronProw.LMSupply   # local-provider safety adapter
```

## Quick Start

### A. Frontier / LAN 게이트웨이

IronHive 기반 frontier provider + FluxGuard guardrail을 등록한다.  
DI 컨테이너가 표준 `IChatClient`를 resolve하며, 게이트웨이가 selection·retry·입출력 검사를 자동 처리한다.

```csharp
using IronProw.Core;
using IronProw.IronHive;
using IronProw.FluxGuard;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

services.AddIronProw()
        .AddIronHiveOpenAI(
            id: "openai",
            priority: 10,
            modelId: "gpt-4o",
            configure: cfg => cfg.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!)
        .AddIronHiveAnthropic(
            id: "anthropic",
            priority: 5,
            modelId: "claude-opus-4-5",
            configure: cfg => cfg.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!)
        .UseFluxGuard();  // Standard preset (L1 regex guards, offline)

// 표준 Microsoft.Extensions.AI.IChatClient 반환
IChatClient chat = host.Services.GetRequiredService<IChatClient>();
var response = await chat.GetResponseAsync("Hello");
```

`AddIronHiveOpenAI` / `AddIronHiveAnthropic` 는 `IronProw.IronHive` 패키지에 있다.  
`UseFluxGuard()` (파라미터 없음) 는 Standard preset(L1 regex, offline)을 적용한다.  
커스텀 FluxGuard 인스턴스를 주입하려면 `UseFluxGuard(IFluxGuard)` 오버로드를 사용한다.

### B. Local-provider safety (lm-supply / ONNX / DirectML)

로컬 추론 클라이언트를 iron-prow 안전 레이어로 감싼다.

```csharp
using IronProw.Core;
using IronProw.LMSupply;
using IronProw.FluxGuard;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// rawClient: IChatClient 브리지 (예: IronHive.Host.Core에서 구성).
//            lm-supply는 IChatClient를 직접 노출하지 않으므로 호출자가 브리지를 준비한다.
// probe:     IReadinessProbe — 모델 로드 상태를 보고하는 구현체.
//            lm-supply GeneratorPool 기반은 GeneratorPoolProbe를 사용한다.
services.AddIronProw()
        .AddLMSupplyLocal(
            id: "local-phi3",
            priority: 20,
            rawClient: rawClient,
            probe: probe,
            options: new LocalSafetyOptions { DefaultMaxOutputTokens = 1024 })
        .UseFluxGuard();

IChatClient chat = host.Services.GetRequiredService<IChatClient>();
```

`AddLMSupplyLocal`이 제공하는 safety:
- **model-ID preflight** — `IReadinessProbe.GetAvailableModelIdsAsync`로 모델 존재 검증
- **readiness gate** — `IReadinessProbe.IsReadyAsync`로 로드 완료 확인
- **length-bounding** — `LocalSafetyOptions.DefaultMaxOutputTokens` (미설정 호출에 자동 적용, 기본 512)

### 두 갈래 조합

두 갈래를 같은 DI 등록에서 조합할 수 있다. `priority`가 높은 provider가 먼저 선택되고, fallback 시 낮은 우선순위 provider로 강등된다.

```csharp
services.AddIronProw()
        .AddIronHiveOpenAI("openai", priority: 10, "gpt-4o",
            cfg => cfg.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!)
        .AddLMSupplyLocal("local", priority: 20, rawClient, probe)
        .UseFluxGuard()
        .Configure(opt =>
        {
            opt.EnableFallback = true;                              // 기본값
            opt.Resilience.MaxRetries = 3;                         // 기본값: 2
            opt.Resilience.BaseDelay = TimeSpan.FromMilliseconds(300); // 기본값: 200ms
        });
```

## Crash-fallback 제한

`LocalSafetyChatClient`(갈래 B)는 `IReadinessProbe`로 로컬 추론 불가를 감지하고, 게이트웨이 `SelectingChatClient`의 provider-level fallback으로 승격한다. **이것은 게이트웨이 수준 fallback(M2-4 범위)이다.**

진짜 하드웨어 수준 fallback(예: ONNX GenAI DirectML → CPU)은 upstream lm-supply의 책임이다.  
lm-supply 0.35.x 기준, ONNX GenAI 생성 경로는 DirectML → CPU 폴백이 없다(임베딩 경로·llama-server 경로는 보유). iron-prow는 이 gap을 *감싸지 않는다* — upstream이 수정되면 iron-prow 변경 없이 이득이 흡수된다.

## 두 갈래 필수 설계

iron-prow는 두 시나리오를 독립적이면서도 조합 가능하게 커버한다:

| 갈래 | 진입점 | 책임 |
|---|---|---|
| **A. Frontier / LAN** | `AddIronHiveOpenAI` · `AddIronHiveAnthropic` | provider 레지스트리, 우선순위 선택, retry, provider-level fallback |
| **B. local-provider safety** | `AddLMSupplyLocal` | model-ID preflight, readiness gate, length-bounding, crash→fallback |

어느 한 갈래만 구현하면 수요의 절반을 놓친다 (CHARTER §기능 표면).  
`UseFluxGuard()`는 두 갈래 공통 — 관문에서 일괄 적용된다.

> ⚠️ The guard is opt-in. `AddIronProw()` installs a default `NullGuard` that allows all traffic. A gateway without `UseFluxGuard()` (or a custom `UseGuard(...)`) performs NO input/output guardrail checks. Always register a guard in production.

## See also

- [`CHARTER.md`](./CHARTER.md) — iron-prow 정체성, 범위, 의존 규칙, 로드맵 앵커

## License

MIT
