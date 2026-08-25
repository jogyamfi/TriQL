---
name: "C# Test Standards"
description: "Use when writing or reviewing .NET unit tests, integration tests, benchmarks, or test fixtures. Covers naming, arrange-act-assert, determinism, async test patterns, fakes over mocks, Testcontainers, and coverage expectations."
applyTo: ["**/tests/**/*.cs", "**/*.Tests/**/*.cs", "**/*.Benchmarks/**/*.cs"]
---

# C# Test Standards

The standards in `csharp.instructions.md` apply here too. These are additional.

## Structure and naming

- Name tests `MethodName_Scenario_ExpectedOutcome`, e.g.
  `GetBytes_NullBuffer_ReturnsTotalLength`.
- One logical assertion per test. Multiple `Assert` calls are fine when they verify one behaviour.
- Follow arrange / act / assert, separated by blank lines. No comment headers needed.
- Test the public contract, not private implementation. If a private method needs direct testing,
  that usually signals a missing type.

## Determinism

- **No `Thread.Sleep` and no wall-clock waits.** Use `TimeProvider`, a test scheduler, or an
  awaited signal.
- No dependence on test execution order, ambient culture, machine time zone, or network access.
- Seed randomness explicitly and log the seed on failure.
- A test that fails intermittently is a broken test — fix or delete it, never retry it.

```csharp
// ❌ slow and flaky
await Task.Delay(500);
Assert.True(buffer.IsDrained);

// ✅ deterministic
await buffer.DrainedSignal.WaitAsync(ct);
Assert.True(buffer.IsDrained);
```

## Async tests

- Test methods return `Task`, never `async void`.
- Pass a real `CancellationToken` and assert cancellation behaviour explicitly — including that
  the operation stopped, not just that it threw.
- Assert on `OperationCanceledException` / `TimeoutException` distinctly; conflating them hides
  bugs.
- Use `Assert.ThrowsAsync`, never a `try/catch` with a `Assert.Fail()` fallthrough.

## Fakes over mocks

- Prefer a hand-written fake or an in-memory implementation over a mocking framework. Fakes stay
  readable and fail with meaningful messages.
- For HTTP, intercept with a custom `HttpMessageHandler` rather than mocking `HttpClient`.
- Never assert on mock call counts as a substitute for asserting observable behaviour.
- A test double built from the same documentation as the implementation proves consistency, not
  correctness — pair it with at least one real-dependency test.

## Integration tests

- Provision real dependencies with Testcontainers; never rely on a developer's local install.
- Share fixtures across a test collection; never start a container per test.
- Skip by capability detection when Docker is unavailable — **never silently pass**.
- Include a negative control where a misconfiguration could otherwise make the suite pass while
  testing nothing.

## Coverage and boundaries

- Cover the boundary, not just the happy path: nulls, empty, zero, one, maximum, overflow, and the
  value just past each limit.
- Every fixed bug gets a regression test that fails without the fix.
- Security-relevant behaviour gets adversarial tests — injection payloads, oversized inputs,
  malformed responses.
- Coverage gates are a floor, not a goal. High coverage with weak assertions is worse than honest
  lower coverage.

## Benchmarks

- Use BenchmarkDotNet, never a stopwatch loop.
- Include `[MemoryDiagnoser]` when allocation is part of the claim.
- Compare against a baseline in the same run rather than an absolute threshold — absolute numbers
  vary too much across CI runners to gate on.
