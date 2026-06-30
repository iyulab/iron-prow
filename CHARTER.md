# iron-prow — Charter

> **한 줄 정체성**: 모든 LLM 호출이 가장 먼저 가르고 지나는 **안전 추론 단일 관문(safe-inference gateway)**.
> "prow(뱃머리)" = 함대의 모든 호출이 처음 부딪는 곳.
>
> 권위: 조직 전략 `dev-works/org/docs/product-middleware.md` §⑥ · umbrella 실행 `ironhive-umbrella/docs/MIDDLEWARE-ALIGNMENT.md` M2.
> 본 문서는 이 repo의 **정체성(불변)**을 명문화한다. 실행 태스크·일정은 MIDDLEWARE-ALIGNMENT.md M2가 권위(앵커 §끝).

---

## 위상

| 항목 | 값 |
|---|---|
| 층 | **low 라이브러리** (바깥을 향함 — 범용 .NET OSS 생태계 + iyulab 양쪽 소비) |
| 브랜드 | `iron-*` (견고함 브랜드 — **ironhive 종속 아님**) |
| repo 판정 | new repo (5질문 Q1·Q3 충족) — **현재 placeholder** (LICENSE + README만) |
| 배포 위상 | **인프로세스 SDK** (`IChatClient` 데코레이터). 무거운 로컬 추론만 별도 프로세스(GpuStack LAN) |
| umbrella 페이즈 | **M2** |

---

## Charter 5항목 (존재 이유)

### 1. 책임
GpuStack(LAN) ∨ frontier 중 **골라 + 보안 감싼 `IChatClient`를 뱉는 단일 관문.** 한 번의 안전한 호출을 보장한다.

### 2. 경계
- **도메인 0** — 순수 추론 인프라(low 자격). 재고도 차변도 청크도 모른다.
- **에이전트 루프 아님** — 루프·모드·세션·MCP·HITL은 ⑤ `ironhive-host`의 책임. iron-prow는 *루프 안에서 호출되는* 한 번의 안전한 호출만 책임진다.
- **provider 선택까지가 경계** — 어느 모델로 보낼지 + 안전하게 보낼지. 보낸 *내용*의 의미는 모른다.

### 3. 소비자 (rule-of-two 실증 = 4/4)
- ⑤ `ironhive-host` (guarded `IChatClient` 주입) · ③ HoneAI · ② Formbase · **모든 LLM 호출**(단일 관문) · 외부인(범용 .NET).
- 역산 증거(`claudedocs/plans/2026-06-29-middleware-demand-reverse-engineering.md`): SMI.AIMS(2 inference 스택) · Filer(resilience 3×) · vault-ai(guardrail 전무) · textree(local-safety 절반) — **4앱이 독립적으로 같은 표면을 hand-roll.**

### 4. 의존 (without-ironhive — MW-P1 적용)
low 층이므로 **core + adapter** 구조. core는 표준 추상화 + BCL만, iyulab 구현 결합은 adapter에만.
- `iron-prow.core` — `Microsoft.Extensions.AI.IChatClient` 표준. **iyulab 구현 컴파일 의존 0.**
- `iron-prow.ironhive` — ironhive provider 어댑터 (**기본 구현**).
- `iron-prow.lmsupply` — 로컬 추론 어댑터 (LAN/iGPU).
- `iron-prow.fluxguard` — 보안(guardrail) 어댑터.
- 집행: `iron-prow.core`가 ironhive/flux **구현**을 (직접/전이) 참조하면 **CI fail** (M4-1 validator).

### 5. 추출조건
- 게이트 **OPEN** (rule-of-two 4/4 실증, 2026-06-29 역산).
- **단 게이트 충족 ≠ 구현 승인.** repo 껍데기는 존재하나 *코드 착수*는 **owner 승인 게이트** + Track 1(특히 MU-7 length-safety 검증 완료) 선행 권장 (CONSTITUTION §2 정합 — MIDDLEWARE-ALIGNMENT §0).

---

## 기능 표면 (목표 — 두 갈래 **필수**)

> 항목 수준으로 명세한다. API 시그니처는 구현 게이트(owner 승인) 통과 후 plan에서 확정한다.
> **두 갈래 중 하나라도 빠지면 안 된다** — frontier-게이트웨이만 짓고 local-safety를 빠뜨리면 textree류(로컬 전용) 수요를 놓친다.

### A. frontier + LAN 게이트웨이 — guarded `IChatClient` (M2-2)
- **provider 선택**: LAN(GpuStack) ∨ frontier 중 정책 기반 선택.
- **FluxGuard 보안 내장**: 입출력 guardrail을 관문에서 일괄 적용 (소비자가 잊을 수 없게).
- **resilience**: retry · fallback(provider 강등) · error-classify(재시도 가능/불가 분류).
- **provider registry + priority + env normalize + key boundary** (M2-3): 환경변수 정규화(`GPUSTACK_*`·`OPENAI_*`…), 우선순위, API 키 경계 격리.

### B. local-provider safety (M2-4)
- **exec-provider 핀**: 로컬 추론 실행 프로바이더 고정.
- **crash-fallback**: 로컬 추론 크래시 시 대체 경로.
- **length-bounding**: unbounded-generation 방어 (저사양/양자화 모델 run-on 차단).
- **readiness signal**: 로컬 모델 로드 완료 신호.
- **model-ID preflight**: 호출 전 모델 ID 존재 검증.

### C. host ← iron-prow 주입 (M2-5)
- host는 iron-prow가 만든 **guarded `IChatClient`를 `UseChatClient(IChatClient)`로 주입**받는다.
- 이로써 host는 **provider 중립을 유지** (host가 provider를 알면 §1-1 단일관문 긴장). M1-2 검증 PASS → 주입 READY.

---

## ⑤ ironhive-host 와의 경계 분담 (중요)

host와 iron-prow는 **한 쌍**이며 책임이 인접해 혼동되기 쉽다. 선을 명확히 한다:

| 책임 | 주인 | 근거 |
|---|---|---|
| 에이전트 루프 · 모드 · 세션 · MCP · 권한 · HITL · compaction | ⑤ host | "여러 번의 호출을 엮는 것" |
| **한 번의 안전한 호출** (provider 선택 + guardrail + resilience + length-bound) | ⑥ iron-prow | "호출 자체의 안전" |

> **이관 후보**: host에 현재 baked-in된 안전 데코레이터 — `TokenBudgetChatClient`(length-bounding), `ResilientFunctionInvoker`(error-recovery)는 **"안전 추론" 책임이므로 iron-prow로 수렴하는 것이 정석**이다. host는 이들을 직접 구성하지 않고 iron-prow가 제공하는 guarded client를 소비하면 된다. (구현 게이트 통과 시 M2-2/M2-4에서 이관 검토 — 지금은 경계 선언만.)

---

## 현재 상태 & 게이트

- **상태**: **M2 core + adapters 구현 완료 (0.1.0)**. Branch `feat/m2` — 41 tests green.
  - `IronProw.Core` — 계약, resilience, 선택 로직, M4-1 without-ironhive validator
  - `IronProw.IronHive` — OpenAI / Anthropic provider 어댑터
  - `IronProw.FluxGuard` — FluxGuard guardrail 어댑터
  - `IronProw.LMSupply` — local-provider safety (readiness, length-bounding, crash-fallback)
- **게이트**: rule-of-two **OPEN** (4앱 실증). Owner 승인 수령 후 `feat/m2` → main merge 예정.
- **선행 권장**: Track 1 MU-7(lm-supply unbounded-gen) ✅ 0.35.0 RESOLVED 확인 — local-safety 토대 안정. 나머지 Track 1(MU-2/3/6) 배포 완료.
- **안티패턴 경계**: Track 1을 건너뛰고 iron-prow가 upstream 결함을 *감싸기만* 하면 부채 2겹 (MIDDLEWARE-ALIGNMENT §0.2). 깨끗한 라이브러리 위에 얇게 선다.

---

## 로드맵 앵커

실행 태스크·일정·게이트 상태의 권위는 **`ironhive-umbrella/docs/MIDDLEWARE-ALIGNMENT.md`**:

- **M2** — iron-prow 안전 추론 단일 관문: M2-1(core) · M2-2(guarded IChatClient) · M2-3(registry/priority/env/key) · M2-4(local-provider safety) · M2-5(host ← 주입).
- **M4-1** — without-ironhive validator (core가 impl 참조 시 CI fail). 초안 `claudedocs/issues/ISSUE-iron-prow-20260629-013104-m41-without-ironhive-core-validator.md`. **M2 코드 투입과 반드시 동반.**

## License

MIT
