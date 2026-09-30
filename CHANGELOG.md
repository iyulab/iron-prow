# Changelog

All notable changes to this project are documented in this file. Versions follow
[Semantic Versioning](https://semver.org/); while the major version is 0, a minor release may contain
breaking changes, and each one is marked **Breaking** with a migration note.

## [0.10.3] - Unreleased

### Changed
- `UseFluxGuard` builds its guard through `FluxGuardBuilder` rather than FluxGuard's static factory, which FluxGuard
  0.18.0 removes. Behaviour is unchanged: the standard preset, then your `configure` action.
- Re-pinned sibling package(s) `FluxGuard` 0.17.1 -> 0.18.0, `IronHive.Extensions.AI` 0.45.1 -> 0.45.2, `IronHive.Providers.Anthropic` 0.45.1 -> 0.45.2, `IronHive.Providers.GoogleAI` 0.45.1 -> 0.45.2, `IronHive.Providers.OpenAI` 0.45.1 -> 0.45.2, `IronHive.Providers.OpenAI.Compatible` 0.45.1 -> 0.45.2, `LMSupply.Generator` 0.93.1 -> 0.94.0.

## [0.10.2] - 2026-09-30

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.17.0 -> 0.17.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.10.1] - 2026-09-30

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.93.0 -> 0.93.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.10.0] - 2026-09-29

### Removed
- **Breaking**: `EnvNormalizer`. Nothing in the gateway or its adapters called it, and no environment variable was ever
  read through it; keys reach a provider through the adapter's `configure`. Read your variables where you register.
- **Breaking**: `ProviderRegistrationMarker` is internal. It was DI plumbing for `AddProvider`; register providers
  through `AddProvider` or an adapter.

### Changed
- **Breaking**: a second `AddTenantResolver` call throws `InvalidOperationException`. It was silently ignored, so every
  tenant went through the first resolver.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.92.1 -> 0.93.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`.

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
- Re-pinned sibling package(s) `LMSupply.Generator` 0.92.0 -> 0.92.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`.

## [0.8.14] - 2026-09-29

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.45.0 -> 0.45.1, `IronHive.Providers.Anthropic` 0.45.0 -> 0.45.1, `IronHive.Providers.GoogleAI` 0.45.0 -> 0.45.1, `IronHive.Providers.OpenAI` 0.45.0 -> 0.45.1, `IronHive.Providers.OpenAI.Compatible` 0.45.0 -> 0.45.1, `LMSupply.Generator` 0.91.0 -> 0.92.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.13] - 2026-09-29

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.90.0 -> 0.91.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.12] - 2026-09-29

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.89.0 -> 0.90.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.11] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.44.0 -> 0.45.0, `IronHive.Providers.Anthropic` 0.44.0 -> 0.45.0, `IronHive.Providers.GoogleAI` 0.44.0 -> 0.45.0, `IronHive.Providers.OpenAI` 0.44.0 -> 0.45.0, `IronHive.Providers.OpenAI.Compatible` 0.44.0 -> 0.45.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.10] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.88.0 -> 0.89.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.9] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.43.1 -> 0.44.0, `IronHive.Providers.Anthropic` 0.43.1 -> 0.44.0, `IronHive.Providers.GoogleAI` 0.43.1 -> 0.44.0, `IronHive.Providers.OpenAI` 0.43.1 -> 0.44.0, `IronHive.Providers.OpenAI.Compatible` 0.43.1 -> 0.44.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.8] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.43.0 -> 0.43.1, `IronHive.Providers.Anthropic` 0.43.0 -> 0.43.1, `IronHive.Providers.GoogleAI` 0.43.0 -> 0.43.1, `IronHive.Providers.OpenAI` 0.43.0 -> 0.43.1, `IronHive.Providers.OpenAI.Compatible` 0.43.0 -> 0.43.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.7] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Extensions.AI` 0.42.0 -> 0.43.0, `IronHive.Providers.Anthropic` 0.42.0 -> 0.43.0, `IronHive.Providers.GoogleAI` 0.42.0 -> 0.43.0, `IronHive.Providers.OpenAI` 0.42.0 -> 0.43.0, `IronHive.Providers.OpenAI.Compatible` 0.42.0 -> 0.43.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`.

### Fixed

- **`GeneratorChatClient` offers declaration-only tools to the model.** A tool in `ChatOptions.Tools` created with
  `AIFunctionFactory.CreateDeclaration` (the caller runs it, the model may call it) was dropped from the tool list sent to
  the local model, which then answered as if no tool existed. Every `AIFunctionDeclaration` is now sent with its name,
  description and parameter schema; invoking tools stays the caller's job.

## [0.8.6] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.87.0 -> 0.88.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.5] - 2026-09-28

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.86.0 -> 0.87.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.4] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.85.0 -> 0.86.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.3] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.84.0 -> 0.85.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.2] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.83.0 -> 0.84.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.8.1] - 2026-09-27

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.81.1 -> 0.83.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

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
- Re-pinned sibling package(s) `LMSupply.Generator` 0.80.0 -> 0.81.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.7.2] - 2026-09-26

### Added
- **Usage from the local model reports prompt tokens served from the server's cache.** `GeneratorChatClient` sets `UsageDetails.CachedInputTokenCount`, on both the response and the streamed final update. The value comes from llama-server's timings, which LMSupply 0.80.0 exposes. Those tokens are still counted in `InputTokenCount`; they were not evaluated. The value is null when the backend reports no timings (the ONNX path).

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.79.1 -> 0.80.0.

## [0.7.1] - 2026-09-26

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.40.0 -> 0.41.0, `IronHive.Providers.Anthropic` 0.40.0 -> 0.41.0, `IronHive.Providers.GoogleAI` 0.40.0 -> 0.41.0, `IronHive.Providers.OpenAI` 0.40.0 -> 0.41.0, `IronHive.Providers.OpenAI.Compatible` 0.40.0 -> 0.41.0, `LMSupply.Generator` 0.79.0 -> 0.79.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.7.0] - 2026-09-26

### Added
- **A streamed response from the local model now reports token usage.** `GeneratorChatClient.GetStreamingResponseAsync` carries the backend's count (LMSupply 0.79.0 puts it on the final stream chunk, reasoning included) as a `UsageContent` on the finishing update, so `ToChatResponse()` yields the same `Usage` the non-streaming path gives. Backends that report none (the ONNX path) still report none.

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.78.0 -> 0.79.0 (streamed usage on the final chunk).

## [0.6.3] - 2026-09-26

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.39.0 -> 0.40.0, `IronHive.Providers.Anthropic` 0.39.0 -> 0.40.0, `IronHive.Providers.GoogleAI` 0.39.0 -> 0.40.0, `IronHive.Providers.OpenAI` 0.39.0 -> 0.40.0, `IronHive.Providers.OpenAI.Compatible` 0.39.0 -> 0.40.0, `LMSupply.Generator` 0.77.0 -> 0.78.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.6.2] - 2026-09-26

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.38.0 -> 0.39.0, `IronHive.Providers.Anthropic` 0.38.0 -> 0.39.0, `IronHive.Providers.GoogleAI` 0.38.0 -> 0.39.0, `IronHive.Providers.OpenAI` 0.38.0 -> 0.39.0, `IronHive.Providers.OpenAI.Compatible` 0.38.0 -> 0.39.0, `LMSupply.Generator` 0.76.0 -> 0.77.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.6.1] - 2026-09-25

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.37.0 -> 0.38.0, `IronHive.Providers.Anthropic` 0.37.0 -> 0.38.0, `IronHive.Providers.GoogleAI` 0.37.0 -> 0.38.0, `IronHive.Providers.OpenAI` 0.37.0 -> 0.38.0, `IronHive.Providers.OpenAI.Compatible` 0.37.0 -> 0.38.0, `LMSupply.Generator` 0.75.0 -> 0.76.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.6.0] - 2026-09-24

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.36.0 -> 0.37.0, `IronHive.Providers.Anthropic` 0.36.0 -> 0.37.0, `IronHive.Providers.GoogleAI` 0.36.0 -> 0.37.0, `IronHive.Providers.OpenAI` 0.36.0 -> 0.37.0, `IronHive.Providers.OpenAI.Compatible` 0.36.0 -> 0.37.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

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
- Re-pinned sibling package(s) `IronHive.Core` 0.35.0 -> 0.36.0, `IronHive.Providers.Anthropic` 0.35.0 -> 0.36.0, `IronHive.Providers.GoogleAI` 0.35.0 -> 0.36.0, `IronHive.Providers.OpenAI` 0.35.0 -> 0.36.0, `IronHive.Providers.OpenAI.Compatible` 0.35.0 -> 0.36.0, `LMSupply.Generator` 0.74.0 -> 0.75.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

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
- Re-pinned sibling package(s) `IronHive.Core` 0.34.0 -> 0.35.0, `IronHive.Providers.Anthropic` 0.34.0 -> 0.35.0, `IronHive.Providers.GoogleAI` 0.34.0 -> 0.35.0, `IronHive.Providers.OpenAI` 0.34.0 -> 0.35.0, `IronHive.Providers.OpenAI.Compatible` 0.34.0 -> 0.35.0, `LMSupply.Generator` 0.73.0 -> 0.74.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.21] - 2026-09-24

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.33.1 -> 0.34.0, `IronHive.Providers.Anthropic` 0.33.1 -> 0.34.0, `IronHive.Providers.GoogleAI` 0.33.1 -> 0.34.0, `IronHive.Providers.OpenAI` 0.33.1 -> 0.34.0, `IronHive.Providers.OpenAI.Compatible` 0.33.1 -> 0.34.0, `LMSupply.Generator` 0.72.1 -> 0.73.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.20] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.72.0 -> 0.72.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.19] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.33.0 -> 0.33.1, `IronHive.Providers.Anthropic` 0.33.0 -> 0.33.1, `IronHive.Providers.GoogleAI` 0.33.0 -> 0.33.1, `IronHive.Providers.OpenAI` 0.33.0 -> 0.33.1, `IronHive.Providers.OpenAI.Compatible` 0.33.0 -> 0.33.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.18] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.71.0 -> 0.72.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.17] - 2026-09-22

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.70.0 -> 0.71.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.16] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.69.0 -> 0.70.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.15] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `LMSupply.Generator` 0.68.3 -> 0.69.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.14] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.16.0 -> 0.17.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.13] - 2026-09-20

### Changed
- Re-pinned sibling package(s) `FluxGuard` 0.15.1 -> 0.16.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.12] - 2026-09-20

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.32.0 -> 0.33.0, `IronHive.Providers.Anthropic` 0.32.0 -> 0.33.0, `IronHive.Providers.GoogleAI` 0.32.0 -> 0.33.0, `IronHive.Providers.OpenAI` 0.32.0 -> 0.33.0, `IronHive.Providers.OpenAI.Compatible` 0.32.0 -> 0.33.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.11] - 2026-09-19

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.31.0 -> 0.32.0, `IronHive.Providers.Anthropic` 0.31.0 -> 0.32.0, `IronHive.Providers.GoogleAI` 0.31.0 -> 0.32.0, `IronHive.Providers.OpenAI` 0.31.0 -> 0.32.0, `IronHive.Providers.OpenAI.Compatible` 0.31.0 -> 0.32.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.10] - 2026-09-19

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.30.0 -> 0.31.0, `IronHive.Providers.Anthropic` 0.30.0 -> 0.31.0, `IronHive.Providers.GoogleAI` 0.30.0 -> 0.31.0, `IronHive.Providers.OpenAI` 0.30.0 -> 0.31.0, `IronHive.Providers.OpenAI.Compatible` 0.30.0 -> 0.31.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.9] - 2026-09-19

### Changed
- Re-pinned sibling package(s) `IronHive.Core` 0.29.1 -> 0.29.2, `IronHive.Providers.Anthropic` 0.29.1 -> 0.29.2, `IronHive.Providers.GoogleAI` 0.29.1 -> 0.29.2, `IronHive.Providers.OpenAI` 0.29.1 -> 0.29.2, `IronHive.Providers.OpenAI.Compatible` 0.29.1 -> 0.29.2 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.
- Re-pinned sibling package(s) `IronHive.Core` 0.29.2 -> 0.30.0, `IronHive.Providers.Anthropic` 0.29.2 -> 0.30.0, `IronHive.Providers.GoogleAI` 0.29.2 -> 0.30.0, `IronHive.Providers.OpenAI` 0.29.2 -> 0.30.0, `IronHive.Providers.OpenAI.Compatible` 0.29.2 -> 0.30.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [0.4.8] - 2026-09-19

This file starts at 0.4.8. Changes in earlier releases were not recorded here; the commit history is
the record for them.
