# Security Audit

Audit date: 2026-03-07
Scope: V3 tool classes, Orleans grain isolation, serialization

---

## Task 26: FileTools Audit

**File:** `src/Core/V3/Tools/FileTools.cs`

### Path Traversal Protection

`ValidateInsideWorkspace` uses `Path.GetFullPath()` on both the target path and workspace path, then checks `StartsWith` with `OrdinalIgnoreCase`. This canonicalizes the path, which means:

- `../../etc/passwd` resolves to an absolute path and is rejected if outside workspace
- `./foo/../../../etc/passwd` is similarly canonicalized and rejected
- UNC paths (`\\server\share`) will be rejected if workspace is a local path

**Finding:** Null byte injection is not a concern on .NET since `Path.GetFullPath` throws `ArgumentException` for paths containing null characters.

**Finding:** `ReadFileAsync` does NOT call `ValidateInsideWorkspace` -- only `WriteFileAsync` does. Read operations can read any file the process has access to if given an absolute path outside the workspace.

**Recommendation:** Add `ValidateInsideWorkspace` to `ReadFileAsync` and `SearchCode` for defense in depth.

### Output Capping

- `MaxResults = 500` caps `ListFiles` and `SearchCode` output
- `EnumerateFiles` excludes `.git`, `bin`, `obj`, `node_modules`, etc.

### Excluded Directories

Hard-coded exclusion list prevents indexing of build artifacts and VCS internals.

**Status: ACCEPTABLE with noted read-path gap.**

---

## Task 27: ShellTools Audit

**File:** `src/Core/V3/Tools/ShellTools.cs`

### Command Construction

- `RunDotnetAsync`: Passes arguments directly to `dotnet` process. The `fileName` is hard-coded to `"dotnet"`, so the LLM controls only the arguments. No shell interpretation occurs since `UseShellExecute = false`.
- `RunShellAsync`: Passes command through `cmd.exe /c` (Windows) or `/bin/sh -c` (Linux). The LLM has full shell access -- this is by design for a coding agent.

**Finding:** Shell injection is inherent to the design. The LLM can execute arbitrary commands. This is intentional for a development agent but should be documented as a trust boundary.

### Timeout Enforcement

- `TimeoutMs = 120_000` (2 minutes) via `CancellationTokenSource`
- Applied to both stdout/stderr reading and `WaitForExitAsync`
- Output truncated at 8,000 characters

**Finding:** If `ReadToEndAsync` or `WaitForExitAsync` throws `OperationCanceledException` on timeout, the process is not explicitly killed. The `using var process` will dispose it, but the child process may continue running.

**Recommendation:** Add `process.Kill(entireProcessTree: true)` in a `catch`/`finally` block for timeout scenarios.

**Status: ACCEPTABLE for development use. Document trust boundary.**

---

## Task 28: WebTools Audit

**File:** `src/Core/V3/Tools/WebTools.cs`

### SSRF Protection (added in this audit)

The following protections were added:

1. **Scheme validation**: Only `http` and `https` are allowed. Blocks `file://`, `ftp://`, `gopher://`, etc.
2. **Blocked host patterns**: Rejects requests to `localhost`, `127.0.0.1`, `::1`, `0.0.0.0`, `metadata.google`, `169.254.169.254` (AWS/GCP metadata endpoints).
3. **IP range blocking**: Hostname-level check for `10.x`, `192.168.x`, `172.x` prefixes.
4. **DNS resolution check**: After hostname checks, resolves the hostname and validates the resolved IP addresses against:
   - Loopback addresses
   - RFC 1918 private ranges (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16)
   - Link-local addresses (169.254.0.0/16, IPv6 link-local)
5. **DNS failure = block**: If DNS resolution fails, the request is blocked (fail-closed).

**Remaining risk:** DNS rebinding attacks where the first resolution returns a public IP and a subsequent resolution (by HttpClient) returns a private IP. Full mitigation would require a custom `SocketsHttpHandler` with `ConnectCallback` that validates the resolved address. This is an advanced attack vector acceptable for v1.

### Content Truncation

- Response body truncated at 50,000 characters
- Exceptions are caught and returned as error strings (no stack traces leaked)

**Status: PROTECTED. SSRF mitigations added.**

---

## Task 29: Orleans Grain Security

### Grain ID Isolation

All V3 grains use `IGrainWithStringKey`:

- `IAgent` / `IDynamicAgent`: String-keyed. Each agent instance has its own durable state isolated by grain ID.
- `IAgentRegistryGrain`: String-keyed (uses `"global"` as the singleton key).

**State isolation model:**
- Each `[Memory("name")]` parameter creates a storage key scoped to `{GrainType}/{GrainId}/{MemoryName}`
- Grain A cannot access Grain B's state through Orleans APIs
- The `IGrainFactory.GetGrain<IAgent>("some-id")` call requires knowing the grain ID, but grain IDs are not secret -- any code with access to `IGrainFactory` can call any grain

**Finding:** There is no authorization layer on grain method calls. Any Orleans client or grain can call any other grain's methods if they know the grain ID and interface. This is standard for Orleans and acceptable for a development agent runtime.

**Recommendation:** For production multi-tenant deployments, consider adding an authorization filter (`IIncomingGrainCallFilter`) that validates caller identity.

**Status: ACCEPTABLE for single-tenant development use.**

---

## Task 30: Serialization Security

### Dictionary<string, object> in AgentEvent.Payload

`AgentEvent.Payload` is typed as `Dictionary<string, object>`. Orleans serialization will serialize/deserialize the actual runtime types of the values.

**Risks:**
1. **Type confusion**: A malicious payload could contain unexpected types. Orleans uses `[GenerateSerializer]` which only serializes known types -- unknown types will fail to deserialize.
2. **Large payloads**: No size limit on the payload dictionary. A grain could store arbitrarily large events in the durable log.
3. **Object graph depth**: Deeply nested objects could cause stack overflows during serialization.

**Same pattern in:**
- `StateEntry.Value` (type: `object`)
- `AgentResponse.Metadata` (type: `Dictionary<string, object>?`)

**Mitigation:** Orleans' `[GenerateSerializer]` system only serializes types that are explicitly marked with `[GenerateSerializer]` or are built-in types (string, int, etc.). This limits the attack surface to known types.

**Recommendation:** For a stricter API, consider replacing `Dictionary<string, object>` with `Dictionary<string, string>` (JSON-serialized values) in a future major version. This would eliminate type confusion entirely.

**Status: ACCEPTABLE. Orleans serializer provides type safety for known types.**

---

## Summary

| Area | Status | Key Finding |
|------|--------|-------------|
| FileTools | Acceptable | Path traversal blocked for writes. Reads lack workspace validation. |
| ShellTools | Acceptable | Full shell access by design. Timeout enforced but process not killed. |
| WebTools | Protected | SSRF mitigations added (scheme, host, DNS resolution checks). |
| Grain isolation | Acceptable | State isolated by grain ID. No authorization layer (standard Orleans). |
| Serialization | Acceptable | `Dictionary<string, object>` uses Orleans type-safe serialization. |
