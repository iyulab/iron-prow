# iron-prow 채택 가이드 — 기존 `IChatClient` 소비자용

이미 `Microsoft.Extensions.AI.IChatClient`로 LLM을 호출하고, provider 선택·retry/fallback·guardrail·length-bound를 **직접 배선**한 앱이 iron-prow 게이트웨이로 이관하는 방법을 설명한다. (Frontier/LAN 게이트웨이와 local-safety 진입점 API 자체는 [README](../README.md)를 본다.)

핵심 원칙: **iron-prow 채택은 "scatter-gather 리팩토링"이 아니라 하나의 주입 지점(seam) 교체다** — 단, 그 교체가 성립하려면 provider/resilience/guardrail/length가 **먼저 그 seam 하나 뒤로 모여 있어야** 한다.

---

## 2단계 채택 모델

### 1단계 (prep) — 단일 `IChatClient` 주입 seam으로 통합 *(iron-prow 없이 지금 가능)*

앱 전반의 LLM 호출부가 provider 클라이언트를 **직접** 잡거나, retry/fallback을 호출부마다 감싸고 있으면, 먼저 이를 **하나의 주입된 `IChatClient`** 뒤로 격리한다:

```csharp
// before — 호출부에 provider/resilience가 흩어짐
var openai = new OpenAIChatClient(...);
var resilient = new MyResilientChatClient(openai, fallbacks);   // 자체 retry/fallback
var answer = await resilient.GetResponseAsync(prompt);

// after (prep) — 자체 안전 로직을 단일 seam 뒤로 격리, 호출부는 주입된 IChatClient만 안다
Func<IChatClient, IChatClient> decorator =
    inner => new MyResilientChatClient(inner, fallbacks);       // ← iron-prow가 흡수할 정확한 표면
IChatClient chat = decorator(new OpenAIChatClient(...));
services.AddSingleton(chat);
```

이 단계는 iron-prow와 **무관**하게 유효한 리팩토링이다(단일 책임·테스트 용이성). 완료 판정: **모든 LLM 호출이 주입된 단일 `IChatClient`를 경유**하고, 자체 retry/fallback 로직이 그 seam **뒤 1곳**에 격리됨.

### 2단계 (swap) — iron-prow guarded client로 교체 *(iron-prow ship 후)*

seam이 하나면 교체는 등록 한 블록이다. iron-prow는 게이트웨이를 DI에 등록하고 표준 `IChatClient`를 resolve 가능하게 만든다 — 기존 self-wired 등록을 이 블록으로 대체한다:

```csharp
services.AddIronProw()
    .AddIronHiveOpenAI("openai", priority: 10, "gpt-4o", cfg => cfg.ApiKey = key)
    .AddIronHiveOpenAICompatible("ollama", priority: 20, "llama3.2", cfg => cfg.BaseUrl = "http://localhost:11434")
    .UseFluxGuard();

// 이후 소비자는 표준 IChatClient를 그대로 주입받는다 — provider-중립.
IChatClient chat = provider.GetRequiredService<IChatClient>();
```

host처럼 `UseChatClient(IChatClient)` 주입 seam을 노출하는 소비자는, 위에서 resolve한 `chat`을 그 seam에 그대로 꽂는다. 이제 provider 선택·retry·fallback·guardrail·length-bound를 iron-prow가 소유하고, 앱은 provider-중립이 된다.

---

## ⚠️ relocation 체크리스트 — "one-point swap"의 정직한 범위

**함정**: "한 줄 교체"는 자체 resilience decorator가 흡수하는 표면(=provider + retry/fallback)에만 성립한다. guardrail·length-bound가 **그 decorator 밖**(별도 미들웨어, 호출부 전처리, UI 레이어 등)에 있으면, 교체만으로는 그 기능이 **누락**된다 — 그 부분은 iron-prow 게이트웨이 안으로 **이전(relocate)**해야 한다.

교체 전, 현재 어디서 무엇을 하는지 매핑한다:

| 기능 | 자체 배선 위치(예) | iron-prow 소유 지점 | 조치 |
|---|---|---|---|
| provider 선택·우선순위 | provider 팩토리 / DI | `AddIronHive*` + `priority` | seam 교체로 흡수 |
| retry / fallback | resilience decorator (`Func<IChatClient,IChatClient>`) | `Configure(opt => opt.Resilience...)` + `EnableFallback` | seam 교체로 흡수 |
| guardrail (입출력 검사) | **decorator 밖** 별도 미들웨어일 수 있음 | `UseFluxGuard()` (관문 일괄) | **relocate** — 자체 guard 제거 후 `UseFluxGuard`로 이관 |
| length-bound (context-overflow 차단) | **호출부 전처리**일 수 있음 | local: `LocalSafetyOptions.DefaultMaxOutputTokens` | **relocate** — local provider면 안전 옵션으로 이관 |
| 전환 이벤트(UI 칩 등) | 자체 콜백 | `Configure(opt => opt.OnTransition = ...)` | 재배선 |

**규칙**: guardrail/length가 자체 resilience decorator에 이미 포함돼 있으면 → 순수 one-point swap. **밖에 있으면** → seam 교체 + 해당 기능의 iron-prow 게이트웨이로의 relocation이 함께 필요하다. 교체 전 이 표로 "밖에 있는 것"을 식별하라.

---

## fail mode 결정

`UseFluxGuard()`는 기본 **fail-closed**(불확실 verdict 차단). 가용성 우선 소비자는 `UseFluxGuard(failMode: FluxGuardFailMode.Open)`으로 opt-in(Flagged/NeedsEscalation 통과, 정의된 Block은 항상 차단). 자체 guard가 fail-open이었다면 이 차이를 의식적으로 선택한다.

## local-only 소비자

폴백 대상 2번째 provider가 없는 단일 local provider 앱은 게이트웨이 자체가 과하다 — [README](../README.md) Track B의 "경량 경로"(`BuildLocalSafeClient`)를 쓴다. safety(preflight·readiness·length-bound)는 동일하게 받되 selection/resilience 오버헤드가 없다.
