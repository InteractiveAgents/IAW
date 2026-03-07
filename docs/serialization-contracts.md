# Serialization Contracts

Audit date: 2026-03-07

All Orleans grain-to-grain types use `[GenerateSerializer]` with sequential `[Id(n)]` attributes starting at 0. This document lists every serializable type and its field IDs for contract stability.

## Rules

1. IDs start at 0 and increment sequentially with no gaps
2. IDs must never be reused or reordered after release
3. New fields must be appended with the next sequential ID
4. Removing a field: keep the ID reserved (do not reassign)

## Core Records

### AgentEvent (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | EventName | string |
| 1 | SourceAgentId | string |
| 2 | CorrelationId | string |
| 3 | Timestamp | DateTimeOffset |
| 4 | Payload | Dictionary<string, object> |

### AgentState (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | Entries | Dictionary<string, StateEntry> |

### AgentMetadata (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | AgentType | string |
| 1 | DisplayName | string |
| 2 | Description | string |
| 3 | Kind | AgentKind |
| 4 | Capabilities | string[] |
| 5 | Publishes | string[] |
| 6 | Subscribes | string[] |

### AgentCapabilities (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | HasMemory | bool |
| 1 | HasP2P | bool |
| 2 | HasEvents | bool |
| 3 | HasTimers | bool |
| 4 | IsCancellable | bool |
| 5 | IsMultiState | bool |
| 6 | HasTools | bool |
| 7 | IsSecure | bool |

### AgentResponse (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | Kind | AgentResponseKind |
| 1 | Content | string |
| 2 | ToolName | string? |
| 3 | Metadata | Dictionary<string, object>? |

### AgentConfiguration (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | DisplayName | string? |
| 1 | SystemPrompt | string? |
| 2 | ToolNames | string[]? |
| 3 | WorkspacePath | string? |
| 4 | SubscribeToStreams | string[]? |

### ChatMessage (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | Role | string |
| 1 | Content | string |
| 2 | TimestampUtc | DateTimeOffset |

### StateEntry (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | Key | string |
| 1 | Value | object |

### TrackingItem (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | Id | string |
| 1 | Description | string |
| 2 | Interval | TimeSpan |
| 3 | CreatedAt | DateTimeOffset |
| 4 | LastCheckAt | DateTimeOffset? |
| 5 | LastResult | string? |

### ToolDescription (Core.V3)
| Id | Property | Type |
|----|----------|------|
| 0 | Name | string |
| 1 | Description | string |

## Communication Records

### BroadcastResult (Core.V3.Communication)
| Id | Property | Type |
|----|----------|------|
| 0 | TotalReceivers | int |
| 1 | Delivered | int |
| 2 | Failed | int |
| 3 | FailedReceiverIds | string[] |

### MessageReceipt (Core.V3.Communication)
| Id | Property | Type |
|----|----------|------|
| 0 | Accepted | bool |
| 1 | ReceiptId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | RejectionReason | string? |

## Context Records

### AIContext (Core.V3.Context)
| Id | Property | Type |
|----|----------|------|
| 0 | AdditionalMessages | IReadOnlyList<ChatMessage> |
| 1 | Metadata | IDictionary<string, string>? |

## Diagnostics Records

### DiagnosticReport (Core.V3.Diagnostics)
| Id | Property | Type |
|----|----------|------|
| 0 | AgentName | string |
| 1 | Timestamp | DateTimeOffset |
| 2 | IsHealthy | bool |
| 3 | EventCount | int |
| 4 | MessageCount | int |
| 5 | Uptime | TimeSpan |
| 6 | Issues | string[] |

## Registry Records

### AgentRegistration (Core.V3.Registry)
| Id | Property | Type |
|----|----------|------|
| 0 | AgentType | string |
| 1 | DisplayName | string |
| 2 | Description | string |
| 3 | Kind | AgentKind |
| 4 | Capabilities | string[] |
| 5 | Publishes | string[] |
| 6 | Subscribes | string[] |

### AgentQuery (Core.V3.Registry)
| Id | Property | Type |
|----|----------|------|
| 0 | Kind | AgentKind? |
| 1 | Capabilities | string[]? |
| 2 | Publishes | string[]? |
| 3 | Subscribes | string[]? |

## Message Records

### AgentActivatedEvent (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | AgentType | string |

### AlertNotification (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | Severity | string |
| 4 | Message | string |

### AssignTaskCommand (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | Description | string |
| 4 | WorkspacePath | string? |

### BuildCompletedEvent (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | Success | bool |
| 4 | CommitSha | string? |
| 5 | Output | string? |

### CodeChangedEvent (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | FilePaths | string[] |
| 4 | CommitSha | string? |

### DeployCompletedEvent (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | Success | bool |
| 4 | Environment | string |
| 5 | Version | string? |

### HealthCheckEvent (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | ServiceName | string |
| 4 | Healthy | bool |
| 5 | ResponseTimeMs | double? |

### ProgressNotification (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | Step | string |
| 4 | Status | string |
| 5 | Progress | float? |

### ReviewRequestNotification (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | FilePath | string |
| 4 | Description | string |

### StateChangedEvent (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | Key | string |
| 4 | OldValue | string |
| 5 | NewValue | string |

### TestResultEvent (Core.V3.Messages)
| Id | Property | Type |
|----|----------|------|
| 0 | SourceAgentId | string |
| 1 | CorrelationId | string |
| 2 | Timestamp | DateTimeOffset |
| 3 | Passed | bool |
| 4 | TotalTests | int |
| 5 | FailedTests | int |
| 6 | Summary | string? |

## Enums

### AgentKind (Core.V3) - `[GenerateSerializer]`
| Value | Name |
|-------|------|
| 0 | Static |
| 1 | Dynamic |

### AgentResponseKind (Core.V3) - NOT serialized
| Value | Name |
|-------|------|
| 0 | Text |
| 1 | ToolCall |
| 2 | ToolResult |
| 3 | Error |
| 4 | Final |

## Audit Result

All `[GenerateSerializer]` types have:
- Sequential `[Id(n)]` starting at 0
- No gaps in ID sequences
- No duplicate IDs
- Consistent use of `[property: Id(n)]` on positional record parameters

**Note:** `AgentResponseKind` enum lacks `[GenerateSerializer]` but is used in `AgentResponse` (which is serializable). Orleans will serialize it by value, which is fine for enums but adding `[GenerateSerializer]` would be more explicit.
