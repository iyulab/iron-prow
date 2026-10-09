# Changelog

All notable changes to this project are documented in this file. Versions follow
[Semantic Versioning](https://semver.org/); while the major version is 0, a minor release may contain
breaking changes, and each one is marked **Breaking** with a migration note.

## [Unreleased]

### Changed
- **A refusal the account cannot pay reads as HTTP 402.** `IronHiveHttpFailureReader` maps IronHive 0.59.0's
  `BillingException` (402, or OpenAI `insufficient_quota` sent as 429) to status 402 with no retry hint, so the gateway
  and IronHive.Agent see the status. It is never retried; like a credential refusal it stays eligible for fallback to
  another provider (`DefaultErrorClassifier` remarks say how to make it terminal).

### Dependencies
- Re-pinned sibling package(s) `IronHive.*` 0.58.0 -> 0.59.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.111.0 -> 0.113.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.113.0 -> 0.115.1.

## [0.15.12] - 2026-10-07

### Changed
- Re-pinned sibling package(s) `Iyu.Conventions.Testing` 0.4.0 -> 0.5.0, `LMSupply.Generator` 0.110.0 -> 0.111.0. No source changes.
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.57.0 -> 0.58.0, `IronHive.Providers.Anthropic` 0.57.0 -> 0.58.0, `IronHive.Providers.GoogleAI` 0.57.0 -> 0.58.0, `IronHive.Providers.OpenAI` 0.57.0 -> 0.58.0, `IronHive.Providers.OpenAI.Compatible` 0.57.0 -> 0.58.0.

## [0.15.11] - 2026-10-07

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.109.0 -> 0.110.0. No source changes.

## [0.15.10] - 2026-10-07

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.56.0 -> 0.57.0, `IronHive.Providers.Anthropic` 0.56.0 -> 0.57.0, `IronHive.Providers.GoogleAI` 0.56.0 -> 0.57.0, `IronHive.Providers.OpenAI` 0.56.0 -> 0.57.0, `IronHive.Providers.OpenAI.Compatible` 0.56.0 -> 0.57.0. No source changes.

## [0.15.9] - 2026-10-07

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.108.0 -> 0.109.0. No source changes.

## [0.15.8] - 2026-10-07

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.55.1 -> 0.56.0, `IronHive.Providers.Anthropic` 0.55.1 -> 0.56.0, `IronHive.Providers.GoogleAI` 0.55.1 -> 0.56.0, `IronHive.Providers.OpenAI` 0.55.1 -> 0.56.0, `IronHive.Providers.OpenAI.Compatible` 0.55.1 -> 0.56.0. No source changes.

## [0.15.7] - 2026-10-06

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.55.0 -> 0.55.1, `IronHive.Providers.Anthropic` 0.55.0 -> 0.55.1, `IronHive.Providers.GoogleAI` 0.55.0 -> 0.55.1, `IronHive.Providers.OpenAI` 0.55.0 -> 0.55.1, `IronHive.Providers.OpenAI.Compatible` 0.55.0 -> 0.55.1. No source changes.

## [0.15.6] - 2026-10-06

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.54.0 -> 0.55.0, `IronHive.Providers.Anthropic` 0.54.0 -> 0.55.0, `IronHive.Providers.GoogleAI` 0.54.0 -> 0.55.0, `IronHive.Providers.OpenAI` 0.54.0 -> 0.55.0, `IronHive.Providers.OpenAI.Compatible` 0.54.0 -> 0.55.0. No source changes.

## [0.15.5] - 2026-10-06

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.107.0 -> 0.108.0. No source changes.

## [0.15.4] - 2026-10-06

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.20.0 -> 0.21.0, `IronHive.Extensions.AI` 0.53.1 -> 0.54.0, `IronHive.Providers.Anthropic` 0.53.1 -> 0.54.0, `IronHive.Providers.GoogleAI` 0.53.1 -> 0.54.0, `IronHive.Providers.OpenAI` 0.53.1 -> 0.54.0, `IronHive.Providers.OpenAI.Compatible` 0.53.1 -> 0.54.0, `LMSupply.Generator` 0.106.1 -> 0.107.0. No source changes.

## [0.15.3] - 2026-10-06

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.53.0 -> 0.53.1, `IronHive.Providers.Anthropic` 0.53.0 -> 0.53.1, `IronHive.Providers.GoogleAI` 0.53.0 -> 0.53.1, `IronHive.Providers.OpenAI` 0.53.0 -> 0.53.1, `IronHive.Providers.OpenAI.Compatible` 0.53.0 -> 0.53.1, `LMSupply.Generator` 0.106.0 -> 0.106.1.

### Fixed
- **Breaking** (released as a patch) — **cancelling a call now cancels it.** 2 method(s) that take a `CancellationToken` caught every exception to
  return a fallback (`null`, an empty result, a failure value) or to log and continue, and treated the caller's own
  cancellation the same way. They now let the caller's `OperationCanceledException` through; other failures behave
  as before. The resilience client no longer retries the caller's own cancellation even when a replaced error classifier calls it retryable.
  Migration: code that relied on a cancelled call returning `null`, an empty result or a failure value now
  receives `OperationCanceledException` — catch it where a cancellation is expected.

## [0.15.2] - 2026-10-05

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.52.0 -> 0.53.0, `IronHive.Providers.Anthropic` 0.52.0 -> 0.53.0, `IronHive.Providers.GoogleAI` 0.52.0 -> 0.53.0, `IronHive.Providers.OpenAI` 0.52.0 -> 0.53.0, `IronHive.Providers.OpenAI.Compatible` 0.52.0 -> 0.53.0. No source changes.

## [0.15.1] - 2026-10-05

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.19.1 -> 0.20.0, `LMSupply.Generator` 0.105.2 -> 0.106.0. No source changes.

## [0.15.0] - 2026-10-05

### Fixed
- **A 503 with `Retry-After` from an IronHive OpenAI-compatible provider is retried after the hint, like the OpenAI
  SDK client's.** IronHive 0.52.0 throws `ProviderHttpException` (status + retry hint) for HTTP errors other than 429;
  `IronHiveHttpFailureReader` reads it, so `DefaultErrorClassifier` treats a 503/429 with a hint as retryable and
  `ResilienceChatClient` waits it, instead of falling back to the next provider.
- **Registered `IHttpFailureReader`s are asked before the built-in `HttpStatusFailureReader`.** The built-in reader
  reads any `HttpRequestException`'s status but never a retry hint, and `AddIronProw` registers it first, so it
  answered before a provider library's own reader could.

### Dependencies
- IronHive 0.52.0.

## [0.14.3] - 2026-10-05

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.51.0 -> 0.51.1, `IronHive.Providers.Anthropic` 0.51.0 -> 0.51.1, `IronHive.Providers.GoogleAI` 0.51.0 -> 0.51.1, `IronHive.Providers.OpenAI` 0.51.0 -> 0.51.1, `IronHive.Providers.OpenAI.Compatible` 0.51.0 -> 0.51.1, `LMSupply.Generator` 0.105.1 -> 0.105.2. No source changes.

## [0.14.2] - 2026-10-05

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.19.0 -> 0.19.1, `IronHive.Extensions.AI` 0.50.0 -> 0.51.0, `IronHive.Providers.Anthropic` 0.50.0 -> 0.51.0, `IronHive.Providers.GoogleAI` 0.50.0 -> 0.51.0, `IronHive.Providers.OpenAI` 0.50.0 -> 0.51.0, `IronHive.Providers.OpenAI.Compatible` 0.50.0 -> 0.51.0, `LMSupply.Generator` 0.105.0 -> 0.105.1.

### Dependencies
- Microsoft.Extensions.AI 10.10.0, Microsoft.Extensions.AI.Abstractions 10.10.1.

## [0.14.1] - 2026-10-04

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.104.0 -> 0.105.0. No source changes.

## [0.14.0] - 2026-10-04

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.103.0 -> 0.104.0.

### Added
- **`ByoEndpoint.ApiKeyPlacement`: a bring-your-own endpoint behind a gateway that wants `Authorization: Basic <key>`, a
  bare `Authorization: <key>`, or the key in its own header (`api-key`) is registered and probed with that form.**
  Registration (`AddIronHiveByo`) and `ProbeAsync` send the same form; a key placed in another header is not also sent
  as a bearer. Applies to the OpenAI-wire presets; `Validate` refuses a placement on the Anthropic and Gemini presets, and
  a `Headers` entry named like the placement's header.

### Dependencies
- IronHive 0.49.0 -> 0.50.0 (`CredentialPlacement`).

## [0.13.8] - 2026-10-04

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.48.0 -> 0.49.0, `IronHive.Providers.Anthropic` 0.48.0 -> 0.49.0, `IronHive.Providers.GoogleAI` 0.48.0 -> 0.49.0, `IronHive.Providers.OpenAI` 0.48.0 -> 0.49.0, `IronHive.Providers.OpenAI.Compatible` 0.48.0 -> 0.49.0, `LMSupply.Generator` 0.102.0 -> 0.103.0. No source changes.

## [0.13.7] - 2026-10-04

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.101.0 -> 0.102.0. No source changes.

## [0.13.6] - 2026-10-03

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.100.0 -> 0.101.0. No source changes.

## [0.13.5] - 2026-10-03

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.99.0 -> 0.100.0. No source changes.

## [0.13.4] - 2026-10-03

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.98.2 -> 0.99.0. No source changes.

## [0.13.3] - 2026-10-03

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.18.2 -> 0.19.0. No source changes.

## [0.13.2] - 2026-10-03

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.47.0 -> 0.48.0, `IronHive.Providers.Anthropic` 0.47.0 -> 0.48.0, `IronHive.Providers.GoogleAI` 0.47.0 -> 0.48.0, `IronHive.Providers.OpenAI` 0.47.0 -> 0.48.0, `IronHive.Providers.OpenAI.Compatible` 0.47.0 -> 0.48.0. No source changes.

## [0.13.1] - 2026-10-02

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.46.1 -> 0.47.0, `IronHive.Providers.Anthropic` 0.46.1 -> 0.47.0, `IronHive.Providers.GoogleAI` 0.46.1 -> 0.47.0, `IronHive.Providers.OpenAI` 0.46.1 -> 0.47.0, `IronHive.Providers.OpenAI.Compatible` 0.46.1 -> 0.47.0. No source changes.

## [0.13.0] - 2026-10-02

### Added
- **`ByoProbeResult.Failure` — why a probe failed, as a kind an application can word itself.** `ByoProbeFailure`:
  `Unauthorized` (401/403) · `HttpStatus` · `Timeout` · `Refused` · `HostNotFound` · `Unreachable` · `Transport` ·
  `Invalid` (the entry failed `Validate`). It is read from the exception chain, so it does not depend on the operating
  system's language. `Error` stays the human text — it may be localized and may repeat the address.

### Fixed
- **A refused connection is `Refused` on every wire.** On Windows a closed local port on the OpenAI-compatible and
  GPUStack wires read as a connect timeout, because their connect timeout (2 s) equalled the time Windows takes to report
  the refusal. IronHive 0.46.1 raises it to 3 s.

### Changed
- Re-pinned `IronHive.*` 0.46.0 -> 0.46.1.

## [0.12.2] - 2026-10-02

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.98.1 -> 0.98.2. No source changes.

## [0.12.1] - 2026-10-02

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.18.1 -> 0.18.2, `LMSupply.Generator` 0.98.0 -> 0.98.1. No source changes.

## [0.12.0] - 2026-10-02

### Added
- **`ByoPresets.ProbeAsync` returns the ids the endpoint lists.** `ByoProbeResult.ModelIds` (server order) and
  `ByoProbeResult.Lists(modelId)` — an app can warn "the server is up but does not serve this model" before a request
  fails. `ModelCount` remains (now `ModelIds.Count`).
- **`ByoEndpoint.Headers`: extra request headers** (a gateway's own token, a routing header), sent by both
  `AddIronHiveByo` and `ProbeAsync` through the provider's `Headers` setting. A header the provider reserves for the
  credential (`Authorization`; for Anthropic also `x-api-key`) is refused by `Validate`, without contacting anything.

### Changed
- **`ProbeAsync` sends one request.** The SDK's retries are off for the check (`MaxRetries = 0`, Google
  `Attempts = 1`), so an unreachable host answers after one attempt instead of four with backoff (about 8.6 s with a
  2 s connect timeout), and `Error` names the cause (the refusal with its message, the connection failure, or
  "No answer within N s.") instead of "Retry failed after 4 tries". Requires IronHive 0.46.0.
- **Breaking**: `ByoProbeResult`'s second positional parameter is `IReadOnlyList<string> ModelIds` instead of
  `int ModelCount`. Code that reads `ModelCount` is unaffected; code that constructs or deconstructs the record changes.

### Fixed
- **Packages now carry the license text.** Each `.nupkg` includes `LICENSE` next to the `MIT` expression,
  so an application that ships third-party notices can copy the copyright line from the package.

## [0.11.0] - 2026-10-01

### Added
- **`ByoPresets`: a catalogue of bring-your-own endpoint presets** (`openai`, `anthropic`, `gemini`, `grok`, `ollama`,
  `gpustack`, `custom`) with each one's kind, default base URL and key requirement — so an app's settings screen, its
  validation and its gateway registration read one source.
- **`ByoPresets.Validate(endpoint)`** states why an entered endpoint cannot be used (unknown preset, missing or non-http(s)
  base URL, missing key) without contacting anything.
- **`AddIronHiveByo(id, priority, modelId, endpoint)`** registers an endpoint through the provider its preset names.
- **`ByoPresets.ProbeAsync(endpoint)` checks the connection for real**: one authenticated model-list request through
  the provider's own model finder, frontier providers included — a wrong key comes back as a failure with its HTTP
  status (401/400) and the provider's message, not as "connected".

## [0.10.8] - 2026-10-01

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.97.0 -> 0.98.0. No source changes.

## [0.10.7] - 2026-10-01

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.45.3 -> 0.45.4, `IronHive.Providers.Anthropic` 0.45.3 -> 0.45.4, `IronHive.Providers.GoogleAI` 0.45.3 -> 0.45.4, `IronHive.Providers.OpenAI` 0.45.3 -> 0.45.4, `IronHive.Providers.OpenAI.Compatible` 0.45.3 -> 0.45.4. No source changes.

## [0.10.6] - 2026-10-01

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.96.0 -> 0.97.0. No source changes.

## [0.10.5] - 2026-09-30

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.95.0 -> 0.96.0. No source changes.

## [0.10.4] - 2026-09-30

### Changed
- **Documentation comments describe behaviour only.** Comments no longer refer to internal tracking or planning records.
- **The repository no longer carries a `CHARTER.md` planning document.** The README states the scope instead: one safe call (provider selection, guardrail, resilience, length-bounding); agent loops, sessions, MCP and human-in-the-loop belong to the host.
- Re-pinned sibling package(s) `FluxGuard` 0.18.0 -> 0.18.1, `IronHive.Extensions.AI` 0.45.2 -> 0.45.3, `IronHive.Providers.Anthropic` 0.45.2 -> 0.45.3, `IronHive.Providers.GoogleAI` 0.45.2 -> 0.45.3, `IronHive.Providers.OpenAI` 0.45.2 -> 0.45.3, `IronHive.Providers.OpenAI.Compatible` 0.45.2 -> 0.45.3, `LMSupply.Generator` 0.94.0 -> 0.95.0.

## [0.10.3] - 2026-09-30

### Changed
- `UseFluxGuard` builds its guard through `FluxGuardBuilder` rather than FluxGuard's static factory, which FluxGuard
  0.18.0 removes. Behaviour is unchanged: the standard preset, then your `configure` action.
- Re-pinned sibling package(s) `FluxGuard` 0.17.1 -> 0.18.0, `IronHive.Extensions.AI` 0.45.1 -> 0.45.2, `IronHive.Providers.Anthropic` 0.45.1 -> 0.45.2, `IronHive.Providers.GoogleAI` 0.45.1 -> 0.45.2, `IronHive.Providers.OpenAI` 0.45.1 -> 0.45.2, `IronHive.Providers.OpenAI.Compatible` 0.45.1 -> 0.45.2, `LMSupply.Generator` 0.93.1 -> 0.94.0.

## [0.10.2] - 2026-09-30

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.17.0 -> 0.17.1 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.10.1] - 2026-09-30

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.93.0 -> 0.93.1 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.10.0] - 2026-09-29

### Removed
- **Breaking**: `EnvNormalizer`. Nothing in the gateway or its adapters called it, and no environment variable was ever
  read through it; keys reach a provider through the adapter's `configure`. Read your variables where you register.
- **Breaking**: `ProviderRegistrationMarker` is internal. It was DI plumbing for `AddProvider`; register providers
  through `AddProvider` or an adapter.

### Changed
- **Breaking**: a second `AddTenantResolver` call throws `InvalidOperationException`. It was silently ignored, so every
  tenant went through the first resolver.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.92.1 -> 0.93.0 — re-consumption of already-consumed iyulab packages.

### Documentation
- The README's C# blocks are complete programs and are compiled against the current API by a test.
- New: what `GuardException` carries, `UseFluxGuard(configure:)` and `UseGuard`, decorating `IErrorClassifier`,
  `ProviderKind` as metadata for a custom `IProviderSelector` (the default selector orders by priority only),
  `DegenerationOptions`, and that `ChatClientBuilder` needs the `Microsoft.Extensions.AI` package.
- The local safety steps are listed in the order they run (readiness, then model-ID preflight), and preflight is
  documented as running only when `ChatOptions.ModelId` is set.

## [0.9.0] - 2026-09-29

### Fixed
- **Streamed output goes through the output guard.** `GetStreamingResponseAsync` inspected only the input; the output of a
  stream was never inspected. The aggregated output is now inspected when the stream ends, and a blocked output ends the
  stream with `GuardException`. **Breaking** (behavior): chunks are yielded as they are generated, so a streaming consumer
  may receive text before the exception — discard or retract it when the exception arrives.
- **`WithDegenerationStop()` around the gateway no longer turns the output guard off.** It serves `GetResponseAsync`
  through the inner stream and abandons that stream when it stops a repetition, so no call wrapped by it — streaming or
  not — was output-guarded. The output is now inspected when the stream ends or when its consumer stops reading early.

### Changed
- README: the "lightweight path" (`BuildLocalSafeClient`) is described as what it is — readiness, preflight and length
  bounds, no input/output guard.
- README: `GetService<ChatClientMetadata>()` returns the LMSupply metadata on the `BuildLocalSafeClient` client only; the
  gateway selects a provider per call and returns `null` (read `ChatResponse.ModelId`).
- Re-pinned sibling package(s) `LMSupply.Generator` 0.92.0 -> 0.92.1 — re-consumption of already-consumed iyulab packages.

## [0.8.14] - 2026-09-29

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.45.0 -> 0.45.1, `IronHive.Providers.Anthropic` 0.45.0 -> 0.45.1, `IronHive.Providers.GoogleAI` 0.45.0 -> 0.45.1, `IronHive.Providers.OpenAI` 0.45.0 -> 0.45.1, `IronHive.Providers.OpenAI.Compatible` 0.45.0 -> 0.45.1, `LMSupply.Generator` 0.91.0 -> 0.92.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.13] - 2026-09-29

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.90.0 -> 0.91.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.12] - 2026-09-29

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.89.0 -> 0.90.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.11] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.44.0 -> 0.45.0, `IronHive.Providers.Anthropic` 0.44.0 -> 0.45.0, `IronHive.Providers.GoogleAI` 0.44.0 -> 0.45.0, `IronHive.Providers.OpenAI` 0.44.0 -> 0.45.0, `IronHive.Providers.OpenAI.Compatible` 0.44.0 -> 0.45.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.10] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.88.0 -> 0.89.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.9] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.43.1 -> 0.44.0, `IronHive.Providers.Anthropic` 0.43.1 -> 0.44.0, `IronHive.Providers.GoogleAI` 0.43.1 -> 0.44.0, `IronHive.Providers.OpenAI` 0.43.1 -> 0.44.0, `IronHive.Providers.OpenAI.Compatible` 0.43.1 -> 0.44.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.8] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.43.0 -> 0.43.1, `IronHive.Providers.Anthropic` 0.43.0 -> 0.43.1, `IronHive.Providers.GoogleAI` 0.43.0 -> 0.43.1, `IronHive.Providers.OpenAI` 0.43.0 -> 0.43.1, `IronHive.Providers.OpenAI.Compatible` 0.43.0 -> 0.43.1 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.7] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.42.0 -> 0.43.0, `IronHive.Providers.Anthropic` 0.42.0 -> 0.43.0, `IronHive.Providers.GoogleAI` 0.42.0 -> 0.43.0, `IronHive.Providers.OpenAI` 0.42.0 -> 0.43.0, `IronHive.Providers.OpenAI.Compatible` 0.42.0 -> 0.43.0 — re-consumption of already-consumed iyulab packages.

### Fixed

- **`GeneratorChatClient` offers declaration-only tools to the model.** A tool in `ChatOptions.Tools` created with
  `AIFunctionFactory.CreateDeclaration` (the caller runs it, the model may call it) was dropped from the tool list sent to
  the local model, which then answered as if no tool existed. Every `AIFunctionDeclaration` is now sent with its name,
  description and parameter schema; invoking tools stays the caller's job.

## [0.8.6] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.87.0 -> 0.88.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.5] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.86.0 -> 0.87.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.4] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.85.0 -> 0.86.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.3] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.84.0 -> 0.85.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.2] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.83.0 -> 0.84.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.1] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.81.1 -> 0.83.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.8.0] - 2026-09-27

### Changed
- **`IronProw.IronHive` no longer depends on `IronHive.Core`.** Its provider adapters get `ChatClientAdapter` from the new
  `IronHive.Extensions.AI` 0.42.0, which depends only on `IronHive.Abstractions`. A host that uses IronProw's IronHive
  providers no longer ships Core's document, SQLite (native), MessagePack and template stack. **Breaking** only for a host
  that used `IronHive.Core` types without referencing that package itself: add the reference.

### Dependencies
- IronHive providers 0.41.0 -> 0.42.0.

## [0.7.3] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.80.0 -> 0.81.1 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.7.2] - 2026-09-26

### Added
- **Usage from the local model reports prompt tokens served from the server's cache.** `GeneratorChatClient` sets `UsageDetails.CachedInputTokenCount`, on both the response and the streamed final update. The value comes from llama-server's timings, which LMSupply 0.80.0 exposes. Those tokens are still counted in `InputTokenCount`; they were not evaluated. The value is null when the backend reports no timings (the ONNX path).

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.79.1 -> 0.80.0.

## [0.7.1] - 2026-09-26

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.40.0 -> 0.41.0, `IronHive.Providers.Anthropic` 0.40.0 -> 0.41.0, `IronHive.Providers.GoogleAI` 0.40.0 -> 0.41.0, `IronHive.Providers.OpenAI` 0.40.0 -> 0.41.0, `IronHive.Providers.OpenAI.Compatible` 0.40.0 -> 0.41.0, `LMSupply.Generator` 0.79.0 -> 0.79.1 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.7.0] - 2026-09-26

### Added
- **A streamed response from the local model now reports token usage.** `GeneratorChatClient.GetStreamingResponseAsync` carries the backend's count (LMSupply 0.79.0 puts it on the final stream chunk, reasoning included) as a `UsageContent` on the finishing update, so `ToChatResponse()` yields the same `Usage` the non-streaming path gives. Backends that report none (the ONNX path) still report none.

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.78.0 -> 0.79.0 (streamed usage on the final chunk).

## [0.6.3] - 2026-09-26

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.39.0 -> 0.40.0, `IronHive.Providers.Anthropic` 0.39.0 -> 0.40.0, `IronHive.Providers.GoogleAI` 0.39.0 -> 0.40.0, `IronHive.Providers.OpenAI` 0.39.0 -> 0.40.0, `IronHive.Providers.OpenAI.Compatible` 0.39.0 -> 0.40.0, `LMSupply.Generator` 0.77.0 -> 0.78.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.6.2] - 2026-09-26

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.38.0 -> 0.39.0, `IronHive.Providers.Anthropic` 0.38.0 -> 0.39.0, `IronHive.Providers.GoogleAI` 0.38.0 -> 0.39.0, `IronHive.Providers.OpenAI` 0.38.0 -> 0.39.0, `IronHive.Providers.OpenAI.Compatible` 0.38.0 -> 0.39.0, `LMSupply.Generator` 0.76.0 -> 0.77.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.6.1] - 2026-09-25

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.37.0 -> 0.38.0, `IronHive.Providers.Anthropic` 0.37.0 -> 0.38.0, `IronHive.Providers.GoogleAI` 0.37.0 -> 0.38.0, `IronHive.Providers.OpenAI` 0.37.0 -> 0.38.0, `IronHive.Providers.OpenAI.Compatible` 0.37.0 -> 0.38.0, `LMSupply.Generator` 0.75.0 -> 0.76.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.6.0] - 2026-09-24

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.36.0 -> 0.37.0, `IronHive.Providers.Anthropic` 0.36.0 -> 0.37.0, `IronHive.Providers.GoogleAI` 0.36.0 -> 0.37.0, `IronHive.Providers.OpenAI` 0.36.0 -> 0.37.0, `IronHive.Providers.OpenAI.Compatible` 0.36.0 -> 0.37.0 — re-consumption of already-consumed iyulab packages. No source changes.

### Added
- **`WithDegenerationStop()` / `DegenerationStopChatClient` stops a generation stuck repeating itself** (opt-in, on any
  `IChatClient` including the gateway). When the output ends in four or more back-to-back copies of a short unit
  (≤ 60 characters, containing a letter, within the last 240 characters), the inner stream is abandoned and the
  stop is reported: `FinishReason` is `DegenerationStopChatClient.FinishReason` ("degeneration") and the unit is under
  `AdditionalProperties[RepeatedUnitKey]`. Markdown structure and punctuation runs are not repetition.
  Non-streaming calls are served through the stream so they stop early too. `DegenerationOptions` sets the window,
  unit length and repeat count; `DegenerationDetector.FindRepeatingUnit` is the rule on its own.

## [0.5.0] - 2026-09-24

### Changed
- **HTTP failures are classified by status code, whatever exception carries it.** The OpenAI SDK's
  `ClientResultException` used to be fallback-eligible for every status (a 500 was never retried on the same
  provider), and every `HttpRequestException` was retryable (a 401 was retried). Now, for
  `ClientResultException`, `HttpRequestException.StatusCode` and ironhive's `RateLimitException`:
  408/500/502/504 retry on the same provider; 429/503 retry only when the provider sent a retry hint, otherwise the
  gateway moves to the next provider at once; any other status (400, 401, 403, 404, 409, …) moves to the next
  provider. A consumer that gives a status a domain meaning decorates `IErrorClassifier` for those codes.
- **A retry waits the provider's `Retry-After` / `retry-after-ms`** when it is longer than the backoff, up to the new
  `ResilienceOptions.MaxRetryAfter` (default 10 s); a longer hint is not waited — the gateway moves to the next
  provider.
- Re-pinned sibling package(s) `IronHive.Core` 0.35.0 -> 0.36.0, `IronHive.Providers.Anthropic` 0.35.0 -> 0.36.0, `IronHive.Providers.GoogleAI` 0.35.0 -> 0.36.0, `IronHive.Providers.OpenAI` 0.35.0 -> 0.36.0, `IronHive.Providers.OpenAI.Compatible` 0.35.0 -> 0.36.0, `LMSupply.Generator` 0.74.0 -> 0.75.0 — re-consumption of already-consumed iyulab packages. No source changes.

### Added
- **`IHttpFailureReader` / `HttpFailure`** — how the gateway learns the status and retry hint an exception describes.
  `AddIronProw()` registers `HttpStatusFailureReader` (`ClientResultException`, `HttpRequestException`); every
  `AddIronHive*` method registers `IronHiveHttpFailureReader` (`RateLimitException`). Register your own with
  `TryAddEnumerable`. `DefaultErrorClassifier` takes the readers (`new DefaultErrorClassifier(readers)`);
  `ResilienceChatClient` takes them as an optional last argument.
- **`IronProwBuilder.Services`** — the service collection, for adapters that contribute gateway services.

### Dependencies
- `IronProw.Core` references `System.ClientModel` 1.14.0 (the floor the OpenAI SDK 2.12/2.13 already carries).

## [0.4.22] - 2026-09-24

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.34.0 -> 0.35.0, `IronHive.Providers.Anthropic` 0.34.0 -> 0.35.0, `IronHive.Providers.GoogleAI` 0.34.0 -> 0.35.0, `IronHive.Providers.OpenAI` 0.34.0 -> 0.35.0, `IronHive.Providers.OpenAI.Compatible` 0.34.0 -> 0.35.0, `LMSupply.Generator` 0.73.0 -> 0.74.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.21] - 2026-09-24

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.33.1 -> 0.34.0, `IronHive.Providers.Anthropic` 0.33.1 -> 0.34.0, `IronHive.Providers.GoogleAI` 0.33.1 -> 0.34.0, `IronHive.Providers.OpenAI` 0.33.1 -> 0.34.0, `IronHive.Providers.OpenAI.Compatible` 0.33.1 -> 0.34.0, `LMSupply.Generator` 0.72.1 -> 0.73.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.20] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.72.0 -> 0.72.1 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.19] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.33.0 -> 0.33.1, `IronHive.Providers.Anthropic` 0.33.0 -> 0.33.1, `IronHive.Providers.GoogleAI` 0.33.0 -> 0.33.1, `IronHive.Providers.OpenAI` 0.33.0 -> 0.33.1, `IronHive.Providers.OpenAI.Compatible` 0.33.0 -> 0.33.1 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.18] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.71.0 -> 0.72.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.17] - 2026-09-22

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.70.0 -> 0.71.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.16] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.69.0 -> 0.70.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.15] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.68.3 -> 0.69.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.14] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.16.0 -> 0.17.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.13] - 2026-09-20

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.15.1 -> 0.16.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.12] - 2026-09-20

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.32.0 -> 0.33.0, `IronHive.Providers.Anthropic` 0.32.0 -> 0.33.0, `IronHive.Providers.GoogleAI` 0.32.0 -> 0.33.0, `IronHive.Providers.OpenAI` 0.32.0 -> 0.33.0, `IronHive.Providers.OpenAI.Compatible` 0.32.0 -> 0.33.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.11] - 2026-09-19

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.31.0 -> 0.32.0, `IronHive.Providers.Anthropic` 0.31.0 -> 0.32.0, `IronHive.Providers.GoogleAI` 0.31.0 -> 0.32.0, `IronHive.Providers.OpenAI` 0.31.0 -> 0.32.0, `IronHive.Providers.OpenAI.Compatible` 0.31.0 -> 0.32.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.10] - 2026-09-19

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.30.0 -> 0.31.0, `IronHive.Providers.Anthropic` 0.30.0 -> 0.31.0, `IronHive.Providers.GoogleAI` 0.30.0 -> 0.31.0, `IronHive.Providers.OpenAI` 0.30.0 -> 0.31.0, `IronHive.Providers.OpenAI.Compatible` 0.30.0 -> 0.31.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.9] - 2026-09-19

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.29.1 -> 0.29.2, `IronHive.Providers.Anthropic` 0.29.1 -> 0.29.2, `IronHive.Providers.GoogleAI` 0.29.1 -> 0.29.2, `IronHive.Providers.OpenAI` 0.29.1 -> 0.29.2, `IronHive.Providers.OpenAI.Compatible` 0.29.1 -> 0.29.2 — re-consumption of already-consumed iyulab packages. No source changes.
- Re-pinned sibling package(s) `IronHive.Core` 0.29.2 -> 0.30.0, `IronHive.Providers.Anthropic` 0.29.2 -> 0.30.0, `IronHive.Providers.GoogleAI` 0.29.2 -> 0.30.0, `IronHive.Providers.OpenAI` 0.29.2 -> 0.30.0, `IronHive.Providers.OpenAI.Compatible` 0.29.2 -> 0.30.0 — re-consumption of already-consumed iyulab packages. No source changes.

## [0.4.8] - 2026-09-19

This file starts at 0.4.8. Changes in earlier releases were not recorded here; the commit history is
the record for them.
