# Changelog

All notable changes to this project are documented in this file. Versions follow
[Semantic Versioning](https://semver.org/); while the major version is 0, a minor release may contain
breaking changes, and each one is marked **Breaking** with a migration note.

## [0.5.0] - unreleased

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
