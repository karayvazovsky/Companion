# Desktop Companion — Design Spec (v1)

**Date:** 2026-09-24
**Status:** Draft for review
**Codename:** Companion (the pet names itself on first run — the user chooses)

## 1. Summary

A tiny pixel-art creature that lives on the Windows desktop (on top of the taskbar), behaves like a low-maintenance pet, and answers any question via Claude. It must feel alive, respond fast, and cost almost nothing in RAM/CPU while idle.

**Architecture in one line:** a native C# / .NET 10 WPF app runs the pet's body and life locally (zero cost), and sends only real questions to the Claude API (Opus 5.5, streaming).

## 2. Goals & non-goals

**Goals (v1)**
- Living pixel pet: idle, walk, happy, bored, sleepy/sleep, thinking, talking animations.
- Light-care needs (attention, energy) that affect mood only — never punish.
- Chat bubble with streaming answers from Claude Opus 5.5.
- Memory of facts about the user, editable by the user.
- Web search for current information.
- Tray icon, settings window, first-run hatch flow, optional autostart.
- Footprint: < 60 MB RAM idle, < 1% CPU idle, first answer tokens visible in ~1–2 s.

**Non-goals (v1)** — planned for later stages, but the design leaves slots for them:
- v2: clipboard helper, reminders & timers.
- v3: screen understanding (screenshot on explicit request).
- v4: PC actions (open apps/folders/URLs, file search) with confirm-before-act.
- v5: voice input/output.
- Not planned: multi-monitor walking, evolution/growth, cross-platform.

## 3. Folder layout

Everything lives under one portable root, no AppData:

```
D:\Claude\Companion\
├─ src\          source code (git repo root is D:\Claude\Companion)
├─ app\          published Companion.exe + dependencies
├─ data\         settings.json, secrets.bin, memory.json, pet.json, logs\
├─ skins\        default\sheet.png + default\sheet.json (user-replaceable)
├─ workspace\    files the pet creates for the user
└─ docs\         specs & plans
```

The app resolves its root as the parent of the folder containing `Companion.exe` (i.e. `app\..`), so the whole folder can be moved as a unit.

## 4. Components

Each unit has one job and a narrow interface. Names are indicative.

| Unit | Responsibility | Depends on |
|---|---|---|
| `PetWindow` | Transparent, borderless, topmost, non-activating WPF window sized to the sprite. Hit-tests only opaque pixels. Handles click, double-click, drag, right-click menu. Positions itself on top of the taskbar of the primary monitor. | `SpriteAnimator`, `PetLife` |
| `SpriteAnimator` | Loads a skin (`sheet.png` + `sheet.json`), plays named animations at their frame rate using nearest-neighbour scaling (3× or 4×). Redraws only on frame change. | Skin files |
| `PetLife` | State machine + meters. Pure logic, driven by an injected `IClock` and `IUserActivity`. Emits the current `PetState` and optional short built-in lines. Ticks once per second. | `IClock`, `IUserActivity` |
| `UserActivity` | Reads idle time (`GetLastInputInfo`), detects fullscreen foreground app and session lock. | Win32 |
| `ChatBubble` | Popup window anchored above the pet: input box, streamed answer view with minimal markdown (bold, lists, code blocks with Copy), "🧠 Think harder" and "New chat" buttons. `Esc`/click-away closes. | `Brain` |
| `Brain` | Owns the conversation, builds requests, streams responses, runs the tool loop, reports usage. | `IClaudeClient`, `MemoryStore`, `ToolRegistry`, `SpendTracker` |
| `ClaudeClient` | Thin wrapper over the official `Anthropic` NuGet SDK behind `IClaudeClient` so tests can use a fake. | Anthropic SDK |
| `ToolRegistry` | Holds tool definitions + handlers. v1: `remember`, `forget`. Later stages register clipboard/reminder/screen/PC tools here. | `MemoryStore` |
| `MemoryStore` | Loads/saves `memory.json` (user facts) and `pet.json` (name, meters, position). Atomic writes (temp file + rename). | File system |
| `SettingsStore` | Loads/saves `settings.json`; API key encrypted with DPAPI (CurrentUser scope) into `secrets.bin`. | File system, DPAPI |
| `SpendTracker` | Accumulates estimated cost per month from response `usage`; enforces local monthly cap. | `IClock` |
| `TrayIcon` | Tray menu (Chat · Show/Hide · Sleep · Settings · Quit), tooltip with mood + month spend. | — |
| `SettingsWindow` | Edits settings and memory facts. | `SettingsStore`, `MemoryStore` |
| `HotkeyService` | Registers global hotkey (default `Ctrl+Alt+Space`) via `RegisterHotKey`. | Win32 |
| `App` | Composition root, single-instance mutex, first-run flow, autostart toggle (HKCU `Run` entry, only when the user enables it). | all |

## 5. Pet life

### 5.1 States

| State | Enter when | Animation |
|---|---|---|
| Idle | default | breathe, blink, look around |
| Walk | randomly from Idle (every ~20–60 s) | walks a few steps along the taskbar, stays in screen bounds |
| Happy | clicked, or a chat finishes | bounce + heart, ~3 s then back to Idle |
| Bored | Attention < 30 | sighs, sits, occasionally waves |
| Sleepy | Energy < 25, or user idle ≥ 3 min | yawns |
| Sleep | user idle ≥ 5 min, or local time 00:00–07:00 with user idle ≥ 1 min, or "Sleep" chosen from menu | curled up, "z z z" |
| Thinking | request sent, no tokens yet | head scratch, dots |
| Talking | tokens streaming | mouth moves |

Priority (highest first): Thinking/Talking > Happy > Sleep > Sleepy > Bored > Walk > Idle. Any user input while sleeping wakes the pet (Idle, brief yawn).

### 5.2 Meters (0–100, persisted in `pet.json`)

- **Attention:** −1 per 2 min while the user is active at the PC; +15 per click; +30 per chat; clamp 0–100.
- **Energy:** −1 per 3 min while awake; +2 per min while asleep.
- No penalties ever. Meters only select mood/animation and whether to show a nudge line.

### 5.3 Built-in lines (no API cost)

A small local list per mood (e.g. Bored: "psst… anything I can help with?"). Shown in a mini bubble for ~4 s, at most once every 20 min, never while the user is fullscreen or typing heavily (input within last 2 s).

### 5.4 Energy-saving rules

- Animation timer runs at the animation's fps (≈8 fps active, ≈2 fps sleeping); draws only when the frame index changes.
- `PetLife` ticks at 1 Hz.
- When a fullscreen app is foreground or the session is locked: stop animation timer and hide the window; `PetLife` keeps a 1 Hz tick for meters only.
- No network activity unless the user asks a question.

## 6. Skin format

`skins/default/sheet.png` — grid of square frames, 4 columns wide. The default skin uses 16×16 frames (like the reference creature); custom skins may use any `frameSize` (e.g. 32). `sheet.json`:

```json
{
  "frameSize": 16,
  "animations": {
    "idle":     { "fps": 6, "loop": true,  "frames": [0, 1, 2, 1] },
    "walk":     { "fps": 8, "loop": true,  "frames": [4, 5, 6, 7] },
    "happy":    { "fps": 8, "loop": false, "frames": [8, 9, 10, 9, 8] },
    "bored":    { "fps": 4, "loop": true,  "frames": [12, 13] },
    "sleepy":   { "fps": 4, "loop": true,  "frames": [16, 17] },
    "sleep":    { "fps": 2, "loop": true,  "frames": [20, 21] },
    "thinking": { "fps": 6, "loop": true,  "frames": [24, 25, 26] },
    "talking":  { "fps": 8, "loop": true,  "frames": [28, 29] },
    "egg":      { "fps": 2, "loop": true,  "frames": [30, 31] }
  }
}
```

On screen the frame is scaled with nearest-neighbour by the "pet size" setting: 3×, 4× (default) or 6×, i.e. 48 / 64 / 96 px for the default skin.

Frame index = row-major position in the grid. A skin missing an animation falls back to `idle`. The default skin is a placeholder creature drawn for this project; the user can replace the PNG freely.

## 7. Brain & Claude integration

- **SDK:** official `Anthropic` NuGet package (verified against 12.50.0); streaming via the beta Messages endpoint (`client.Beta.Messages.CreateStreaming` + `.Aggregate()`), because the thinking-binding control below is a beta.
- **Thinking-block safety:** every request sends beta `thinking-binding-controls-2026-08-01` with `thinking.block_binding.prefix_mismatch_behavior = "drop_block"`, so an imperfect echo degrades gracefully instead of returning 400.
- **History shape:** completed exchanges are stored and replayed as plain text only (user text + final assistant text). Thinking and tool blocks are replayed only inside the tool round that produced them, verbatim. This keeps the history append-only-safe even when the 10-exchange window slides.
- **Model:** `claude-opus-5-5` for all requests.
  - Everyday: `output_config.effort = "low"`.
  - "🧠 Think harder": appends a new user turn ("Think this through more carefully and give me your best answer.") and sends it with `effort = "high"`. History is never rewritten — Opus 5.5 binds thinking blocks to the conversation, so replacing the earlier answer would break them. The bubble replaces the displayed answer with the new one.
  - Thinking is always on for this model; omit the `thinking` parameter (disabling returns 400). Do not use forced `tool_choice` (`any`/`tool` return 400) — use `auto`.
  - `max_tokens`: 8000 (streaming).
- **System prompt (stable, cached):** personality (friendly, loyal, dog-like helper; short direct answers by default, detail when asked), pet name, user name, then the user-facts block. Prompt caching is applied at the end of the system block so personality + facts are cached; facts change rarely. Caching only kicks in above the model's minimum cacheable prefix, so a short prompt may simply not cache — acceptable; verify with `usage.cache_read_input_tokens` during the smoke test. Nothing volatile (current time, etc.) goes in the system prompt; the current local date/time is sent at the start of each user turn instead.
- **Conversation window:** last 10 exchanges; conversation resets after 30 min without a message or on "New chat". Conversations are append-only (no editing earlier turns).
- **Tools (v1):**
  - `web_search` (server tool, `web_search_20260209`, `max_uses: 3`).
  - `remember(fact: string)` — appends a fact to `memory.json`.
  - `forget(fact_id: string)` — removes a fact.
  - Client tool inputs are validated locally before running (the C# SDK's `InputSchema` does not expose `additionalProperties`, so `strict` is not used).
- **Tool loop:** a manual loop (max 5 rounds) continues on `tool_use` and `pause_turn` until `end_turn`. `refusal` stop reason → pet shows a neutral "can't help with that one" line. `max_tokens` → append "…(cut off)" to the bubble.
- **Streaming UI:** first `text` delta switches pet from Thinking to Talking; bubble appends deltas as they arrive.

## 8. Memory

`memory.json`:

```json
{ "facts": [ { "id": "f_01", "text": "Works in Cinema 4D", "created": "2026-09-24T12:00:00Z" } ] }
```

- Soft limit 100 facts; beyond that, `remember` returns an error asking Claude to merge or forget older facts.
- Facts are sent with every request (inside the cached system prompt).
- Chat logs are written to `data/logs/chat-YYYY-MM.jsonl` locally, never uploaded except as part of the live conversation window.

## 9. Spend tracking

- Per response, compute cost from `usage` (input, cache read/write, output) with a price table in code for `claude-opus-5-5` ($4 / $20 per MTok input/output, cache reads $0.20) plus web-search charges.
- Stored per calendar month in `settings.json`.
- Local cap default **$30/month**. At the cap the pet refuses new questions with a clear message and a link to Settings. At 80% it shows one warning.
- The price table is an estimate; the Anthropic console remains the source of truth.

## 10. Error handling

| Situation | Behaviour |
|---|---|
| No network / connection error | Pet shows "no signal" cloud; bubble: "I can't reach the internet right now." |
| 401 (bad key) | Bubble explains and offers "Open Settings". |
| Credit exhausted / billing error | Bubble explains and links to console.anthropic.com. |
| 429 / 5xx | SDK retries (default 2) internally; if no answer text has arrived after 8 s the bubble shows "hmm, one sec…"; final failure shown plainly. |
| Local cap reached | See §9. |
| Corrupt data file | Rename to `*.bad-<timestamp>`, start with defaults, log it. |
| Skin fails to load | Fall back to a built-in 1-frame placeholder, log it. |

Errors are logged to `data/logs/app-YYYY-MM-DD.log` (rotating, keep 7 days).

## 11. First run

1. Egg animation on the taskbar; click to hatch.
2. Bubble asks the pet's name, then the user's name.
3. Bubble asks for the API key (link to console.anthropic.com), validates it with a minimal request.
4. Pet says hello; normal life begins.

## 12. Settings

API key · hotkey · everyday effort / think-harder effort · monthly cap · pet size (3×/4×/6×) · start with Windows (off by default) · memory editor (list, edit, delete facts) · "Reset pet".

## 13. Platform & build

- .NET 10 (LTS), WPF, `net10.0-windows`. **Prerequisite:** .NET 10 SDK installed (not currently present on this machine; .NET 8 runtime only).
- Framework-dependent publish to `app\` (small on disk; uses the shared .NET Desktop Runtime).
- Git repository at `D:\Claude\Companion`.
- Projects: `src/Companion` (WPF app), `src/Companion.Core` (logic: PetLife, meters, MemoryStore, SpendTracker, skin parsing, Brain, Claude client), `src/Companion.Tests` (xUnit), `tools/SkinGen` (renders the placeholder sprite sheet), `tools/Smoke` (real-API smoke test console).

## 14. Testing

- **Unit tests (xUnit, no network, fake clock):** PetLife transitions and priorities; meter drain/refill; built-in line throttling; skin JSON parsing and fallbacks; MemoryStore round-trip and corrupt-file recovery; SpendTracker math and cap; Brain conversation window, reset timing, tool loop with a fake `IClaudeClient`.
- **Smoke test (manual trigger, real API, a few cents):** `tools/Smoke` asks three questions: a `remember` round-trip plus a joke, recall of the remembered fact, and one web search.
- **Footprint check:** script samples the process's working set and CPU for 2 minutes idle; pass if < 60 MB and < 1% average.
- **Manual checklist:** animations look right, drag and position persistence, bubble open/close, hotkey, fullscreen pause, sleep/wake on AFK, tray menu, first-run hatch.

## 15. Later stages (outline only)

- **v2 Clipboard & reminders:** `read_clipboard` / `write_clipboard` tools; `set_reminder(time, text)` persisted in `data/reminders.json`, fired by a local scheduler; pet walks to screen center and taps.
- **v3 Screen:** `look_at_screen` tool — captures the active window or full screen only after the user asks; image sent as base64 in the next request.
- **v4 PC actions:** `open_path`, `open_url`, `launch_app`, `search_files` (scoped to allowed roots, default `D:\Claude`); every action shows a confirm prompt in the bubble before running.
- **v5 Voice:** push-to-talk speech-to-text and optional text-to-speech.
