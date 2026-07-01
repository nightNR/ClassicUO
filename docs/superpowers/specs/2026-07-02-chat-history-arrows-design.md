# Chat History Navigation via Up/Down Arrows

**Date:** 2026-07-02
**Status:** Approved design
**Area:** `src/ClassicUO.Client` — chat input

## Summary

Add Up/Down arrow navigation of the chat message history, plus an options-menu
toggle to choose arrows vs. the existing Ctrl+Q/W shortcuts, plus a configurable
history length that persists per profile across client restarts.

A message history already exists in `SystemChatControl.cs` (static
`_messageHistory` list, navigated by Ctrl+Q = previous / Ctrl+W = next, restoring
both the text and the `ChatMode`). It is currently **unbounded** and **shared
across all profiles** (static). This work rebinds it to arrow keys, caps its
length, and makes it per-profile persistent.

## Goals

- Up = previous message, Down = next message in history.
- Ctrl+Up / Ctrl+Down = move the text caret between wrapped lines.
- Options toggle: history navigation via arrows **or** Ctrl+Q/W (either/or).
- Configurable history length (default 20).
- History persists per profile across restarts.

## Non-goals

- No change to how messages are sent or to `ChatMode` behavior.
- No new keybinding UI beyond the single toggle + length field.
- No migration of the existing static shared history into profiles.

## Behavior

### Arrow navigation
- **Up** restores the previous history entry (text + `ChatMode`).
- **Down** restores the next history entry; past the newest entry, clears the box.
- Mirrors the current Ctrl+Q / Ctrl+W index logic exactly (same
  `_messageHistoryIndex` semantics).
- Active only while the chat is active (`IsActive`), matching current guards.

### Caret movement
- **Ctrl+Up / Ctrl+Down** move the caret between wrapped lines of a multi-line
  message. Plain arrows no longer move the caret vertically when the arrow toggle
  is on.

### Toggle interaction
- New setting `ChatUseArrowsForHistory` (default **true**).
  - When **true**: Up/Down navigate history; Ctrl+Q/W history handling disabled.
  - When **false**: Ctrl+Q/W behave as today; plain arrows fall through to the
    textbox for normal caret movement.
- Existing `DisableCtrlQWBtn` remains independent (fully disables Ctrl+Q/W).

## Implementation

### `Configuration/Profile.cs`
Add:
```csharp
public bool ChatUseArrowsForHistory { get; set; } = true;
public int ChatHistoryLength { get; set; } = 20;
public List<ChatHistoryEntry> ChatHistory { get; set; } = new();
```
`ChatHistoryEntry` is a small serializable class:
```csharp
public class ChatHistoryEntry
{
    public ChatMode Mode { get; set; }
    public string Text { get; set; }
}
```
(Place it where `Profile` can reference it and JSON can round-trip `ChatMode`.)

### `Game/UI/Gumps/SystemChatControl.cs`
- **Load on construct:** populate `_messageHistory` from
  `ProfileManager.CurrentProfile.ChatHistory`; set `_messageHistoryIndex` to the
  end.
- **Append + trim on send** (in `HandleMessageSend`): after adding the new entry,
  trim `_messageHistory` from the front to `ChatHistoryLength`, then write the
  list back into `profile.ChatHistory` so it is saved with the profile.
- **`OnKeyDown`:**
  - Add `SDLK_UP` case (when `ChatUseArrowsForHistory` && !Ctrl): previous-entry
    logic (same as the Ctrl+Q branch).
  - Add `SDLK_DOWN` case (when `ChatUseArrowsForHistory` && !Ctrl): next-entry
    logic (same as the Ctrl+W branch).
  - Ctrl+Up / Ctrl+Down: do **not** intercept — let the event reach `StbTextBox`
    for caret movement.
  - Existing Ctrl+Q / Ctrl+W cases: add `&& !ChatUseArrowsForHistory` to the
    guard.

**Routing risk (verify during build):** plain Up/Down currently reach
`StbTextBox` for caret movement. Confirm `SystemChatControl.OnKeyDown` fires and
can consume the key before the textbox does (the existing Ctrl+Q/W handling here
proves keydown reaches this control). Confirm `StbTextBox` moves the caret on
Ctrl+Up/Down (or that plain arrows are the only caret-vertical binding, in which
case Ctrl+arrows need explicit forwarding).

### `Game/UI/Gumps/OptionsGump.cs`
Near the existing "Disable message history" checkbox (~line 3245):
- Add a checkbox for `ChatUseArrowsForHistory` (field `_chatUseArrowsForHistory`).
- Add a numeric `InputField` for `ChatHistoryLength` (copy the `_maxJournalFiles`
  pattern), field `_chatHistoryLength`.
Save both in the corresponding apply method (~line 4322 alongside
`DisableCtrlQWBtn`). Parse/clamp the length to a sane minimum (>= 0; 0 = disabled
history is acceptable).

### Resources (`ResGumps`)
Add two strings:
- Checkbox label — e.g. "Use Up/Down arrows for chat history".
- Length field label — e.g. "Chat history length".

## Files touched
- `src/ClassicUO.Client/Configuration/Profile.cs`
- `src/ClassicUO.Client/Game/UI/Gumps/SystemChatControl.cs`
- `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`
- Resource string source for `ResGumps`

No new files (aside from the small `ChatHistoryEntry` class, which may live in
`Profile.cs` or a nearby configuration file).

## Testing
- Send several messages; press Up repeatedly → walks back through history with
  correct text and mode; Down walks forward and clears past the newest.
- Multi-line message: Ctrl+Up / Ctrl+Down move the caret; plain arrows navigate
  history.
- Toggle off: Ctrl+Q/W navigate; plain arrows move caret.
- Set length to N; send > N messages; confirm only the last N persist.
- Restart client; confirm history reloads from the profile.
- Length 0: history disabled cleanly (no crash).

## Open questions
None. Defaults confirmed: arrow toggle on by default; history is per-profile.
