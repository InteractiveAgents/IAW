# Orleans Scheduling Mechanisms

**Grain Timers vs Reminders v1 vs Durable Jobs (Reminders v2)**

A comprehensive comparison of all scheduling primitives available in .NET Orleans 9/10, including the new Durable Jobs system introduced via PR #9717.

---

## 1. Grain Timers

In-memory timers bound to a specific grain activation. When the activation is deactivated or the silo restarts, all timers are lost. Ideal for high-frequency, short-lived periodic work.

### API (Orleans 8.2+)

```csharp
this.RegisterGrainTimer(
    callback,
    state,
    new GrainTimerCreationOptions
    {
        DueTime = TimeSpan.FromSeconds(5),
        Period = TimeSpan.FromSeconds(10),
        Interleave = false,
        KeepAlive = false
    });
```

Returns `IGrainTimer` with `Change(dueTime, period)` and `Dispose()` methods. The old `RegisterTimer` API is marked `[Obsolete]` since Orleans 8.2.

### Key Characteristics

- **Non-persistent** — dies with the activation. No storage dependency.
- **Single-threaded** — callback never runs concurrently with itself; next tick is scheduled only after the previous completes.
- **Interleave = false by default** — unlike the old API which interleaved by default. Set `Interleave = true` to restore old behavior.
- **KeepAlive** — when `true`, each tick extends the activation's lifetime, preventing idle collection.
- **CancellationToken** — callback receives a token that is cancelled when the timer is disposed or the grain begins deactivation.
- **Granularity** — milliseconds to minutes. Suitable for polling, heartbeats, local cache refresh.
- **Call filters & tracing** — callbacks are subject to grain call filters and are visible in distributed tracing.

---

## 2. Reminders (v1 — Current)

Persistent, grain-level reminders that survive deactivation and cluster restarts. Designed for infrequent periodic work measured in minutes, hours, or days.

### API

```csharp
// Schedule
IGrainReminder reminder = await RegisterOrUpdateReminder(
    "myReminder",
    dueTime: TimeSpan.FromMinutes(5),
    period: TimeSpan.FromHours(1));

// Cancel
await UnregisterReminder(reminder);

// Grain must implement IRemindable
Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
{
    // handle reminder tick
    return Task.CompletedTask;
}
```

### Key Characteristics

- **Persistent** — reminder definitions are written to storage and survive cluster restarts.
- **Grain-scoped, not activation-scoped** — if the grain has no activation when a reminder ticks, Orleans creates one automatically.
- **Delivered as messages** — subject to the same interleaving semantics as normal grain calls.
- **Minimum period: 1 minute** — not suitable for high-frequency work.
- **Requires storage provider** — Azure Table, ADO.NET, etc. via `Use*ReminderService` extension methods.
- **Explicit cancellation** — must call `UnregisterReminder()`; reminders do not auto-expire.
- **Missed ticks on downtime** — if the cluster is down when a tick is due, it is skipped entirely. Only the next scheduled tick fires.

### Known Limitations

- **Memory pressure** — all reminder state is loaded/cached on silos, causing GC overhead at scale.
- **No backpressure** — simultaneous reminder firings (e.g. after restart) can overwhelm the cluster.
- **No catch-up** — ticks missed during outages are permanently lost.
- **Forces activation** — you cannot schedule or cancel a reminder without activating the target grain.

---

## 3. Durable Jobs (Reminders v2)

A complete redesign of the persistent scheduling subsystem, introduced in Orleans via PR #9717 (merged, with follow-up work tracked in issue #9750). Durable Jobs address all major limitations of Reminders v1.

### Design Goals

- **One-shot by default** — the core API schedules single-execution tasks. Recurring scheduling is available via optional helper extensions.
- **Memory-efficient** — lazy, partitioned access keeps only a small working set in memory instead of loading all jobs onto silo heaps.
- **Rate limiting & backpressure** — cluster- and silo-level throttles smooth catch-up after outages, preventing thundering herd problems.
- **Guaranteed delivery** — tasks scheduled during downtime are not lost; they are processed when the cluster recovers (subject to an optional delivery deadline).
- **At-least-once semantics** — handlers must be idempotent.
- **Management without activation** — scheduling/canceling a job does not force the target grain to activate; the grain activates only on delivery.
- **Horizontal scalability** — time-sharded/partitioned scheduling with cooperative shard leasing, designed to scale to millions of tasks.
- **Pluggable storage** — first-class support for Cosmos DB and Azure Blob Storage.

### API

```csharp
// Schedule a one-shot job
IScheduledJob job = await _jobManager.ScheduleJobAsync(
    target: grainId,
    jobName: "InvoiceDueJob",
    dueTime: DateTimeOffset.UtcNow.AddDays(30));

// Cancel a scheduled job
bool cancelled = await _jobManager.TryCancelScheduledJobAsync(job);

// Receive jobs — implement IScheduledJobReceiver (IGrainExtension)
public class InvoiceGrain : Grain, IInvoiceGrain, IScheduledJobReceiver
{
    public Task ReceiveScheduledJobAsync(IScheduledJob job)
    {
        // idempotent handler
        return Task.CompletedTask;
    }
}
```

### Key Interfaces

- `IScheduledJob` — represents a scheduled job with `Id`, `Name`, `DueTime`, `TargetGrainId`, `ShardId`.
- `ILocalScheduledJobManager` — injected service for scheduling and canceling jobs.
- `IScheduledJobReceiver` — grain extension interface for receiving due jobs.

### Delivery Semantics

- At-least-once delivery; handlers must be idempotent.
- Catch-up after outages: delayed but not missed (unless delivery deadline exceeded).
- No strict global ordering; best-effort ordering by scheduled time within a target grain.

### Non-Goals (This Release)

- Listing APIs for scheduled tasks.
- Core support for complex calendars/cron (available as optional helpers).

---

## 4. Comparison Matrix

| Feature | Grain Timer | Reminder v1 | Durable Jobs (v2) |
|---|---|---|---|
| **Persistent** | No | Yes | Yes |
| **Survives deactivation** | No | Yes | Yes |
| **Survives cluster restart** | No | Yes | Yes |
| **Granularity** | ms / sec / min | min / hours / days | Any (DateTimeOffset) |
| **Scheduling model** | Periodic | Periodic | One-shot (+ helpers) |
| **Delivery on downtime** | N/A (in-memory) | Missed tick is lost | Guaranteed catch-up |
| **Memory footprint** | Per-activation only | All loaded in silo | Lazy / partitioned |
| **Backpressure** | No | No | Yes (cluster + silo) |
| **Activates grain** | No | Yes (on tick) | Yes (on delivery only) |
| **Manage w/o activation** | N/A | No | Yes |
| **Scalability ceiling** | Per-activation | Limited by memory | Millions of tasks |
| **Storage required** | None | Yes | Yes (Cosmos/Blob) |
| **Interleave default** | false (since 8.2) | Same as grain calls | Same as grain calls |
| **Idempotency required** | No | No | Yes (at-least-once) |

---

## 5. When to Use What

### Use Grain Timers when:

- You need high-frequency periodic work (seconds or sub-second).
- The work is local to the activation and doesn't need to survive deactivation.
- You want to start periodic work from `OnActivateAsync` or a grain method call.
- **Examples:** polling external services, refreshing local caches, heartbeat checks.

### Use Reminders v1 when:

- You need persistent periodic behavior that survives restarts (minutes/hours/days).
- You are on a version of Orleans that does not yet include Durable Jobs.
- Simple use cases where memory pressure and missed ticks are acceptable trade-offs.
- **Tip:** combine with a grain timer for high-resolution persistent scheduling — reminder wakes the grain every N minutes, grain starts a local timer.

### Use Durable Jobs (v2) when:

- You need reliable one-shot or recurring task scheduling at scale.
- Missed tasks during outages are unacceptable (guaranteed catch-up delivery).
- You are scheduling millions of tasks across a large cluster.
- You need to schedule/cancel jobs without activating the target grain.
- You need built-in backpressure and rate limiting after recovery.
- **Examples:** invoice due dates, subscription renewals, SLA deadlines, deferred notifications.

---

*Sources: Microsoft Learn (Orleans Timers & Reminders docs), GitHub dotnet/orleans issue #9718 (Durable Jobs / Reminders v2), PR #9717, follow-up issue #9750. Generated March 2026.*
