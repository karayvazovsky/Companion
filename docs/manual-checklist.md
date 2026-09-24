# Companion manual checklist

Run after any change to the app project. Start `app\Companion.exe` (or `dotnet run --project src/Companion`).

## Pet
- [ ] Egg on first run; click hatches; bubble asks pet name, user name, API key (masked).
- [ ] Idle breathing and blinking; walks every 20–60 s and turns at screen edges.
- [ ] Click → heart; drag moves it horizontally; position survives restart.
- [ ] Right-click menu: Chat, Sleep (Zs, click wakes), Hide for 1 hour, Settings, Quit.
- [ ] Sleeps after 5 idle minutes (1 minute at night); yawns when you come back.
- [ ] Hidden while a fullscreen app runs or the PC is locked; returns afterwards.
- [ ] Bored line appears in a mini bubble at most every 20 minutes.

## Chat
- [ ] Double-click, tray Chat and the hotkey all open the bubble above the pet.
- [ ] Answers stream; pet shows thinking dots, then talking, then a happy bounce.
- [ ] Lists, bold, inline code and code blocks render; Copy works.
- [ ] 🧠 Think harder gives a new, deeper answer.
- [ ] "searching the web…" appears for current-events questions.
- [ ] "Remember that …" adds a fact visible in Settings; "forget …" removes it.
- [ ] Esc / click-away hides; New chat resets; after 30 quiet minutes a new chat starts.
- [ ] No network: bubble says it can't reach the internet.

## Settings
- [ ] Key test & save; hotkey, effort, cap, size, autostart all apply on Save.
- [ ] Memory list update/delete works.
- [ ] Reset pet turns it back into an egg and keeps the key.

## Footprint
- [ ] `tools\measure-footprint.ps1` after 1 idle minute: PASS.
