# iron-prow

> **safe-inference gateway** — every LLM call's first gate.  
> provider selection (frontier ∨ LAN GpuStack) · guardrail · resilience — delivered as a standard `Microsoft.Extensions.AI.IChatClient`.

두 갈래 필수: frontier/LAN 게이트웨이(A) **and** local-provider safety(B).  
역할·범위·설계 근거는 [`CHARTER.md`](./CHARTER.md) 참조.  
이미 자체 배선(provider·resilience·guardrail)을 가진 앱의 이관은 [`docs/ADOPTION.md`](./docs/ADOPTION.md) 참조.

## Packages

| Package | 역할 |
|---|---|
| `IronProw.Core` | 게이트웨이 계약, resilience, 선택 로직 — iyulab 구현 의존 0 |
| `IronProw.IronHive` | IronHive provider 어댑터 — OpenAI · Anthropic · GoogleAI (frontier) · GpuStack · OpenAI-Compatible/Ollama (LAN) |
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

`IronProw.IronHive`는 다섯 가지 provider 어댑터를 제공한다:
- `AddIronHiveOpenAI` · `AddIronHiveAnthropic` · `AddIronHiveGoogleAI` — frontier (`ProviderKind.Frontier`)
- `AddIronHiveGpuStack` — LAN GpuStack (`ProviderKind.Lan`, key-optional). `cfg => cfg.BaseUrl = "http://gpustack.lan:8080"` 형태로 endpoint 지정.
- `AddIronHiveOpenAICompatible` — LAN generic OpenAI-호환(Ollama·LMStudio·vLLM·llama.cpp server, `ProviderKind.Lan`, key-optional). 표준 `/v1` API 표면을 노출하는 엔드포인트를 대상으로 하며 기본 endpoint는 Ollama의 `http://localhost:11434`. `cfg => cfg.BaseUrl = "http://localhost:1234"`(LMStudio)처럼 override.

```csharp
services.AddIronProw()
        .AddIronHiveOpenAICompatible(
            id: "ollama",
            priority: 20,               // LAN 우선 — frontier보다 높게 두면 로컬 먼저 시도
            modelId: "llama3.2",
            configure: cfg => cfg.BaseUrl = "http://localhost:11434");  // key 불필요
```

`UseFluxGuard()` (파라미터 없음) 는 Standard preset(L1 regex, offline)을 적용한다.  
커스텀 FluxGuard 인스턴스를 주입하려면 `UseFluxGuard(IFluxGuard)` 오버로드를 사용한다.  
**fail mode**: 기본은 fail-closed(불확실 verdict 차단). 가용성을 우선하는 소비자는 `UseFluxGuard(failMode: FluxGuardFailMode.Open)`으로 opt-in — Flagged/NeedsEscalation을 통과시킨다(정의된 Block은 mode 무관 항상 차단).

### B. Local-provider safety (lm-supply / ONNX / DirectML)

로컬 추론을 iron-prow 안전 레이어로 감싼다. lm-supply 생성자(`IGeneratorModel`/`ITextGenerator`)를 **그대로** 넘기면 iron-prow가 내부 `GeneratorChatClient` 브리지로 `IChatClient`에 적응시킨다 — 소비자가 브리지를 직접 작성할 필요가 없다.

```csharp
using IronProw.Core;
using IronProw.LMSupply;
using IronProw.FluxGuard;
using LMSupply.Generator;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// generator: lm-supply가 로드한 IGeneratorModel/ITextGenerator (생명주기는 호출자 소유).
//            예: var generator = await LocalGenerator.LoadAsync("gguf:default", options, null, ct);
// probe:     IReadinessProbe — 모델 로드 상태를 보고하는 구현체.
//            lm-supply GeneratorPool 기반은 GeneratorPoolProbe를 사용한다.
//            GeneratorPool 없이 LoadAsync로 단일 모델을 직접 로드하는 경우
//            (예: textree)는 LazyReadinessProbe(() => loaded, [modelId])를 사용한다.
services.AddIronProw()
        .AddLMSupplyLocal(
            id: "local-phi3",
            priority: 20,
            generator: generator,
            probe: probe,
            options: new LocalSafetyOptions { DefaultMaxOutputTokens = 1024 })
        .UseFluxGuard();

IChatClient chat = host.Services.GetRequiredService<IChatClient>();
```

`AddLMSupplyLocal`이 제공하는 safety:
- **bridge** — `GeneratorChatClient`가 lm-supply 생성자를 `IChatClient`로 적응(role 매핑, `MaxOutputTokens`→`MaxNewTokens`, sampler/tool 전파, streaming flatten). 이미 브리지된 `IChatClient`를 보유한 호출자(예: ironhive-host)는 `AddLMSupplyLocal(..., IChatClient rawClient, ...)` 오버로드를 쓸 수 있다.
- **model-ID preflight** — `IReadinessProbe.GetAvailableModelIdsAsync`로 모델 존재 검증
- **readiness gate** — `IReadinessProbe.IsReadyAsync`로 로드 완료 확인
- **length-bounding** — `LocalSafetyOptions.DefaultMaxOutputTokens` (미설정 호출에 자동 적용, 기본 512)

#### 경량 경로 — 단일 local provider (게이트웨이 없이)

폴백 대상 2번째 provider가 없는 **local-first 단일 provider** 소비자(예: textree)에게는 게이트웨이의 registry·selection·resilience 레이어가 전부 inert하다. 이 경우 `BuildLocalSafeClient`가 브리지+안전wrap만 조립한 plain `IChatClient`를 등록 없이 반환한다:

```csharp
using IronProw.LMSupply;
using Microsoft.Extensions.AI;

// 게이트웨이(AddIronProw/빌더/레지스트리) 없이 guarded local client 직접 조립.
IChatClient chat = LMSupplyExtensions.BuildLocalSafeClient(
    generator,                                                 // lm-supply ITextGenerator (호출자 소유)
    probe,                                                     // IReadinessProbe
    new LocalSafetyOptions { DefaultMaxOutputTokens = 1024 }); // 선택 (기본 512)
```

동일한 safety(preflight·readiness gate·length-bounding)를 받되 selection/fallback/resilience 오버헤드가 없다. 다중 provider·우선순위 선택·provider-level fallback이 필요해지면 `AddLMSupplyLocal`로 전환한다.

### 두 갈래 조합

두 갈래를 같은 DI 등록에서 조합할 수 있다. `priority`가 높은 provider가 먼저 선택되고, fallback 시 낮은 우선순위 provider로 강등된다.

```csharp
services.AddIronProw()
        .AddIronHiveOpenAI("openai", priority: 10, "gpt-4o",
            cfg => cfg.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!)
        .AddLMSupplyLocal("local", priority: 20, generator, probe)
        .UseFluxGuard()
        .Configure(opt =>
        {
            opt.EnableFallback = true;                              // 기본값
            opt.Resilience.MaxRetries = 3;                         // 기본값: 2
            opt.Resilience.BaseDelay = TimeSpan.FromMilliseconds(300); // 기본값: 200ms
            opt.OnTransition = t =>                                 // retry/fallback/exhausted 이벤트 (UI 칩 등)
                Console.WriteLine($"[{t.Kind}] {t.ProviderId} ({t.ProviderIndex + 1}/{t.TotalProviders})");
        });
```

`OnTransition`(선택)은 각 게이트웨이 전환(retry / fallback / exhausted)마다 호출되는 best-effort 콜백이다. 소비자가 어느 provider로 강등됐는지 UI에 표시(예: resilience 칩)할 수 있다. 콜백이 던지는 예외는 삼켜지며 추론을 절대 깨지 않는다. 미설정 시 동작은 기존과 동일(무보고).

### 멀티테넌트 — per-tenant provider resolution

기본 `AddProvider`/`AddLMSupplyLocal` 경로는 **provider 집합이 프로세스 수명 동안 고정**인 소비자(데스크탑 에이전트, 단일 유저)를 위한 것이다. 워크스페이스마다 provider 집합·config·secret이 다른 **멀티테넌트 서버 소비자**는 `AddTenantResolver`로 per-tenant 게이트웨이를 런타임에 build한다.

```csharp
// startup — 단일 테넌트 AddProvider 경로와 병존(무회귀)
services.AddIronProw()
        .UseFluxGuard()
        .AddTenantResolver((sp, tenant) =>                           // 신규 표면
            sp.GetRequiredService<ProviderService>()                // consumer 구현
              .ResolveRegistrations(tenant));                       // per-workspace 집합 → ProviderRegistration[]

// per-request (요청 스코프에서 resolve)
var client = scopedSp.GetRequiredService<IIronProwFactory>().ForTenant(workspaceId);
await client.GetResponseAsync(msgs, options, ct);                    // guarded: select/retry/fallback/guard
```

- `ForTenant(tenant)`은 해당 테넌트의 provider 집합으로 `SelectingChatClient`를 재조립한다 — selector/guard/classifier/options는 공유(재사용), **registry만 per-tenant**. tenant 키는 iron-prow에 opaque(resolver가 해석).
- `IIronProwFactory`는 **scoped**로 등록되므로 **요청 스코프에서 resolve**해야 resolver·provider factory가 요청 범위 서비스(예: 복호화된 워크스페이스 secret)를 본다.
- **async는 상류에서**: resolver와 `ProviderRegistration.ClientFactory`는 모두 sync다. 워크스페이스 secret의 async DB 로드·복호화는 consumer의 요청 미들웨어에서 수행해 scoped 서비스에 stash하고, resolver는 그것을 sync로 읽는다. (요청당 async 로드가 필수라면 향후 `ForTenantAsync` 오버로드가 순수 additive로 추가될 수 있다.)
- 반환된 client는 매 요청 build(연결 없음·저비용)다. consumer가 provider factory에서 `HttpClient` 등 disposable을 쥐면 수명은 consumer 책임이다.
- 단일 테넌트 경로(`AddProvider`/`AddLMSupplyLocal`, singleton `IChatClient`)는 완전 무변경으로 병존한다.

## Crash-fallback 제한

`LocalSafetyChatClient`(갈래 B)는 `IReadinessProbe`로 로컬 추론 불가를 감지하고, 게이트웨이 `SelectingChatClient`의 provider-level fallback으로 승격한다. **이것은 게이트웨이 수준 fallback(M2-4 범위)이다.**

진짜 하드웨어 수준 fallback(예: ONNX GenAI DirectML → CPU)은 upstream lm-supply의 책임이다.  
lm-supply 0.35.x 기준, ONNX GenAI 생성 경로는 DirectML → CPU 폴백이 없다(임베딩 경로·llama-server 경로는 보유). iron-prow는 이 gap을 *감싸지 않는다* — upstream이 수정되면 iron-prow 변경 없이 이득이 흡수된다.

## 두 갈래 필수 설계

iron-prow는 두 시나리오를 독립적이면서도 조합 가능하게 커버한다:

| 갈래 | 진입점 | 책임 |
|---|---|---|
| **A. Frontier / LAN** | `AddIronHiveOpenAI` · `AddIronHiveAnthropic` · `AddIronHiveGoogleAI` · `AddIronHiveGpuStack` · `AddIronHiveOpenAICompatible` | provider 레지스트리, 우선순위 선택, retry, provider-level fallback, 전환 이벤트(`OnTransition`) |
| **B. local-provider safety** | `AddLMSupplyLocal` | model-ID preflight, readiness gate, length-bounding, crash→fallback |

어느 한 갈래만 구현하면 수요의 절반을 놓친다 (CHARTER §기능 표면).  
`UseFluxGuard()`는 두 갈래 공통 — 관문에서 일괄 적용된다.

> ⚠️ The guard is opt-in. `AddIronProw()` installs a default `NullGuard` that allows all traffic. A gateway without `UseFluxGuard()` (or a custom `UseGuard(...)`) performs NO input/output guardrail checks. Always register a guard in production.

## See also

- [`CHARTER.md`](./CHARTER.md) — iron-prow 정체성, 범위, 의존 규칙, 로드맵 앵커

## License

MIT
