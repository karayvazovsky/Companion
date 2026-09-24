# Companion v1.1 — Subscription brain + desktop movement (design addendum)

**Date:** 2026-09-25 · **Status:** Approved in chat · **Extends:** `2026-09-24-desktop-companion-design.md` (still binding where not changed here)

## A. Claude subscription brain

**Goal:** answers cost nothing extra — they run through the user's installed Claude Code and its subscription.

- **Brain setting:** `BrainKind { Subscription, ApiKey }`, default **Subscription**. The API path (spec §7) stays as is and is used only when `ApiKey` is selected.
- **Process per question:** `ClaudeCodeClient : IClaudeClient` starts `claude.exe` directly (never through `cmd.exe`) for each turn with:
  `-p --output-format stream-json --verbose --include-partial-messages --model <m> --effort <low|medium|high> --system-prompt <prompt> --tools WebSearch --allowedTools WebSearch mcp__companion__remember mcp__companion__forget --permission-mode dontAsk --settings {"disableAllHooks":true} --setting-sources "" --strict-mcp-config --mcp-config <json> --no-session-persistence`
  Working directory: `<root>\workspace`. The conversation (text-only history + the new turn) is written to stdin. `--bare` is NOT used (it disables subscription login).
- **Models:** everyday `claude-sonnet-5`, think-harder `claude-opus-5-5` (Settings, editable). Effort as before (everyday low, think-harder high).
- **Locating Claude Code:** Settings override → `claude.exe` on PATH → `claude.cmd` on PATH resolved to `<dir>\node_modules\@anthropic-ai\claude-code\bin\claude.exe` → `%USERPROFILE%\.local\bin\claude.exe` → `%APPDATA%\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe`. Not found → friendly message, no crash.
- **Stream parsing:** `stream_event` `text_delta` → streamed text (joined with a blank line between assistant messages); `content_block_start` `tool_use` named `WebSearch` → "searching the web…"; every line → activity (watchdog); `rate_limit_event` with status `rejected` → plan-limit error with reset time; `result` → finished, `is_error`, `stop_reason`.
- **Memory tools:** `Companion.Memory.exe` (new console project) is a minimal MCP stdio server exposing the existing `remember`/`forget` tools over `data\memory.json`. Claude Code launches it via `--mcp-config`. After each turn the app reloads `MemoryStore` from disk.
- **Errors:** not installed → `NotInstalled`; not logged in / 401 → `NotLoggedIn` ("run `claude` in a terminal and log in"); plan limit → `PlanLimit` ("resets at HH:mm"); others as before.
- **Spend:** the local cap and 80 % warning apply only to `ApiKey`. Tooltip shows "Claude subscription" instead of dollars.
- **Onboarding:** names only in Subscription mode (no key step); key step only in ApiKey mode.
- **Cost of a turn:** none in money; counts toward the user's plan limits. Latency: ~3–6 s to first words (process start).

## B. Desktop movement

**Perches** (where the pet lives), on any monitor:
| Perch | Position | Look |
|---|---|---|
| Floor | top of the taskbar / bottom of the work area | normal moods, walks |
| WindowTop | sitting on a window's top edge; follows the window; window gone/minimised → falls to the floor | normal moods, walks along the edge |
| WallLeft / WallRight | clinging to a monitor's side edge | hang art rotated ±90° |
| Ceiling | hanging from a monitor's top edge | hang art, idle swing ±6° |
| Free | exactly where dropped (e.g. over an icon) | normal moods, no walking |

- **Drag:** free 2-D drag. While held the pet dangles from the cursor (hang art, pivot at the top) and swings like a damped pendulum driven by cursor velocity (max ±35°).
- **Drop:** snap to the nearest of window top / floor / walls / ceiling within **24 DIP**; otherwise Free.
- **Teleport:** "poof" (3 frames) out, reappear at a random perch on a random monitor (floor 40 %, window top 25 % when available, ceiling 15 %, each wall 10 %), poof in. Menu item **Teleport**; automatic every 5–15 min when the user is present, the pet is idle/bored, and no chat is open (Settings: "Teleport on its own", default on).
- **Art:** new frames — hang (2: open eyes, blink) and poof (3). Sheet grows to 4×10 frames (64×160 px). Swing and wall rotation are render transforms, not frames.
- **Window:** the pet window is 2× sprite width and 1.5× sprite height (transparent padding so rotations aren't clipped; transparent pixels stay click-through).
- **Multi-monitor:** monitors and windows are read with Win32 (`Screen.AllScreens`, `EnumWindows` + DWM extended frame bounds), converted to DIPs. Bubbles clamp to the pet's own monitor and open below the pet when there is no room above.
- **Energy:** a 30 fps motion timer runs only while held, falling, or hanging from the ceiling; window-following polls every 250 ms only on a WindowTop perch. Footprint targets unchanged.
- **Persistence:** `pet.json` stores perch kind + X/Y; a saved WindowTop perch restarts on the floor.
