# Website Behavior Tabs Component Design

## Goal

Replace the homepage features grid with an auto-rotating vertical tab component showcasing all 8 IAgent behaviors with real code examples.

## Layout

Left vertical tab list (8 behaviors) + right code card. Active tab highlighted with brand indigo. Auto-rotates every 15 seconds with a thin progress bar. Clicking a tab resets the timer. On mobile (<768px), horizontal scrollable tabs above the code card.

## Component

Custom Vue SFC (`BehaviorTabs.vue`) registered in VitePress theme, used in `index.md` homepage. Pure CSS transitions for tab switching. `setInterval` for 15s auto-rotation cleared on manual click. Code blocks are pre-formatted HTML with CSS syntax highlighting (no runtime Shiki needed).

## Behaviors and Examples

1. **State** — Weather agent storing city + counting visits
2. **History** — Conversation turns with role tracking
3. **Events** — Audit trail with payloads
4. **Notifications** — Multi-agent pub/sub
5. **Tools** — AI function definition and LLM tool calling
6. **Streams** — Real-time Orleans streaming
7. **Tracking** — Periodic execution with auto-stop
8. **Metadata** — Agent identity and capabilities

## Files

- Create: `website/.vitepress/theme/BehaviorTabs.vue`
- Modify: `website/.vitepress/theme/index.ts` (register component)
- Modify: `website/.vitepress/theme/custom.css` (tab styles)
- Modify: `website/index.md` (replace features with component)
