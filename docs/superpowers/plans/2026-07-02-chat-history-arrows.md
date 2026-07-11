# Chat History Up/Down Arrow Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Navigate the chat message history with Up/Down arrows (Ctrl+arrows move the caret), toggle arrows vs. the existing Ctrl+Q/W in options, and persist a length-capped history per profile.

**Architecture:** Extract the history list + index math into a pure, unit-tested helper (`ChatMessageHistory`) mirroring the existing `CounterBarGridMath` precedent. Persist entries on the `Profile` (System.Text.Json source-gen). Wire the helper into `SystemChatControl`, add key handling, suppress caret-on-plain-arrow in `StbTextBox` for the chat box, and add the two options controls.

**Tech Stack:** C# / .NET 10, FNA, SDL3 key handling, System.Text.Json source generation, xUnit.

## Global Constraints

- Every new source file starts with `// SPDX-License-Identifier: BSD-2-Clause` (license headers enforced).
- Target `net10.0`, NativeAOT-safe: no reflection-based JSON — new persisted types must be reachable from `ProfileJsonContext`.
- Match surrounding style (fields `_camelCase`, `internal` types, brace-on-new-line as in the touched files).
- Profile JSON uses snake_case naming policy (property `ChatHistoryLength` serializes as `chat_history_length`).
- Defaults (confirmed): `ChatUseArrowsForHistory = true`, `ChatHistoryLength = 20`, history is per-profile.

---

### Task 1: Pure history logic + entry type (TDD)

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/ChatMessageHistory.cs`
- Test: `tests/ClassicUO.UnitTests/Game/UI/ChatMessageHistoryTests.cs`

**Interfaces:**
- Produces:
  - `class ChatHistoryEntry { ChatMode Mode {get;set;}; string Text {get;set;} }` (namespace `ClassicUO.Game.UI.Gumps`)
  - `class ChatMessageHistory` with: `int MaxLength {get;set;}`, `IReadOnlyList<ChatHistoryEntry> Entries {get;}`, `int Count {get;}`, `void Load(IReadOnlyList<ChatHistoryEntry>)`, `void Add(ChatHistoryEntry)`, `ChatHistoryEntry MovePrevious()`, `bool MoveNext(out ChatHistoryEntry)`.
- `ChatMode` is the existing `internal enum` in `SystemChatControl.cs` (same namespace, same assembly).

**Semantics (mirror current Ctrl+Q/W behavior):** index sits at `Count` after `Load`/`Add`. `MovePrevious` decrements toward 0 and returns that entry (null if empty). `MoveNext` increments while below the newest and returns it; at/past the newest it returns `false` (caller clears the box). `Add`/`Load` trim from the front to `MaxLength` (`MaxLength < 0` treated as 0 → history disabled).

- [ ] **Step 1: Write the failing test**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class ChatMessageHistoryTests
    {
        private static ChatHistoryEntry E(string text, ChatMode mode = ChatMode.Default)
            => new ChatHistoryEntry { Mode = mode, Text = text };

        [Fact]
        public void MovePrevious_WalksBackFromNewest()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            h.Add(E("one"));
            h.Add(E("two"));
            h.Add(E("three"));

            Assert.Equal("three", h.MovePrevious().Text);
            Assert.Equal("two", h.MovePrevious().Text);
            Assert.Equal("one", h.MovePrevious().Text);
            // does not walk past the oldest
            Assert.Equal("one", h.MovePrevious().Text);
        }

        [Fact]
        public void MovePrevious_ReturnsNullWhenEmpty()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            Assert.Null(h.MovePrevious());
        }

        [Fact]
        public void MoveNext_WalksForwardThenSignalsClearAtNewest()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            h.Add(E("one"));
            h.Add(E("two"));
            h.Add(E("three"));

            h.MovePrevious(); // three
            h.MovePrevious(); // two
            h.MovePrevious(); // one

            Assert.True(h.MoveNext(out var e1));
            Assert.Equal("two", e1.Text);
            Assert.True(h.MoveNext(out var e2));
            Assert.Equal("three", e2.Text);
            // at newest -> clear
            Assert.False(h.MoveNext(out var e3));
            Assert.Null(e3);
        }

        [Fact]
        public void Add_TrimsFromFrontToMaxLength()
        {
            var h = new ChatMessageHistory { MaxLength = 2 };
            h.Add(E("one"));
            h.Add(E("two"));
            h.Add(E("three"));

            Assert.Equal(2, h.Count);
            Assert.Equal("two", h.Entries[0].Text);
            Assert.Equal("three", h.Entries[1].Text);
        }

        [Fact]
        public void Load_TrimsAndResetsIndexToNewest()
        {
            var h = new ChatMessageHistory { MaxLength = 2 };
            h.Load(new[] { E("a"), E("b"), E("c") });

            Assert.Equal(2, h.Count);
            Assert.Equal("c", h.MovePrevious().Text); // index started at newest
        }

        [Fact]
        public void MaxLengthZero_DisablesHistory()
        {
            var h = new ChatMessageHistory { MaxLength = 0 };
            h.Add(E("one"));

            Assert.Equal(0, h.Count);
            Assert.Null(h.MovePrevious());
            Assert.False(h.MoveNext(out _));
        }

        [Fact]
        public void MovePrevious_PreservesMode()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            h.Add(E("guildmsg", ChatMode.Guild));

            var e = h.MovePrevious();
            Assert.Equal(ChatMode.Guild, e.Mode);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ChatMessageHistoryTests"`
Expected: FAIL — `ChatMessageHistory` / `ChatHistoryEntry` do not exist (compile error).

- [ ] **Step 3: Create the implementation**

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.UI.Gumps
{
    internal sealed class ChatHistoryEntry
    {
        public ChatMode Mode { get; set; }
        public string Text { get; set; }
    }

    internal sealed class ChatMessageHistory
    {
        private readonly List<ChatHistoryEntry> _entries = new List<ChatHistoryEntry>();
        private int _index;

        public int MaxLength { get; set; } = 20;

        public IReadOnlyList<ChatHistoryEntry> Entries => _entries;

        public int Count => _entries.Count;

        public void Load(IReadOnlyList<ChatHistoryEntry> entries)
        {
            _entries.Clear();

            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null)
                    {
                        _entries.Add(entries[i]);
                    }
                }
            }

            Trim();
            _index = _entries.Count;
        }

        public void Add(ChatHistoryEntry entry)
        {
            if (entry != null)
            {
                _entries.Add(entry);
                Trim();
            }

            _index = _entries.Count;
        }

        public ChatHistoryEntry MovePrevious()
        {
            if (_entries.Count == 0)
            {
                return null;
            }

            if (_index > 0)
            {
                _index--;
            }

            if (_index >= _entries.Count)
            {
                _index = _entries.Count - 1;
            }

            return _entries[_index];
        }

        public bool MoveNext(out ChatHistoryEntry entry)
        {
            if (_index < _entries.Count - 1)
            {
                _index++;
                entry = _entries[_index];
                return true;
            }

            _index = _entries.Count;
            entry = null;
            return false;
        }

        private void Trim()
        {
            if (MaxLength < 0)
            {
                MaxLength = 0;
            }

            while (_entries.Count > MaxLength)
            {
                _entries.RemoveAt(0);
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ChatMessageHistoryTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/ChatMessageHistory.cs tests/ClassicUO.UnitTests/Game/UI/ChatMessageHistoryTests.cs
git commit -m "feat(chat): pure chat-history helper with tests"
```

---

### Task 2: Profile settings + JSON registration

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs` (add properties near line 198; add `[JsonSerializable]` near lines 23-24)
- Test: `tests/ClassicUO.UnitTests/Game/UI/ChatMessageHistoryTests.cs` (add default-value test — same test class file, or a new `ProfileChatHistoryDefaultsTests.cs`)

**Interfaces:**
- Consumes: `ChatHistoryEntry` (Task 1).
- Produces on `Profile`: `bool ChatUseArrowsForHistory` (default `true`), `int ChatHistoryLength` (default `20`), `List<ChatHistoryEntry> ChatHistory` (default `new()`).

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/ProfileChatHistoryDefaultsTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class ProfileChatHistoryDefaultsTests
    {
        [Fact]
        public void NewProfile_HasChatHistoryDefaults()
        {
            var p = new Profile();

            Assert.True(p.ChatUseArrowsForHistory);
            Assert.Equal(20, p.ChatHistoryLength);
            Assert.NotNull(p.ChatHistory);
            Assert.Empty(p.ChatHistory);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ProfileChatHistoryDefaultsTests"`
Expected: FAIL — properties `ChatUseArrowsForHistory` / `ChatHistoryLength` / `ChatHistory` do not exist.

- [ ] **Step 3: Add the properties**

In `src/ClassicUO.Client/Configuration/Profile.cs`, immediately after line 198 (`public bool DisableCtrlQWBtn { get; set; }`), add:

```csharp
        public bool ChatUseArrowsForHistory { get; set; } = true;
        public int ChatHistoryLength { get; set; } = 20;
        public List<ChatHistoryEntry> ChatHistory { get; set; } = new List<ChatHistoryEntry>();
```

(`using ClassicUO.Game.UI.Gumps;` is already imported at line 16; `System.Collections.Generic` at line 4.)

- [ ] **Step 4: Register the type for source-gen JSON**

In `src/ClassicUO.Client/Configuration/Profile.cs`, after the existing `[JsonSerializable(typeof(Profile), ...)]` line (line 24), add:

```csharp
    [JsonSerializable(typeof(ClassicUO.Game.UI.Gumps.ChatHistoryEntry), GenerationMode = JsonSourceGenerationMode.Metadata)]
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~ProfileChatHistoryDefaultsTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs tests/ClassicUO.UnitTests/Game/UI/ProfileChatHistoryDefaultsTests.cs
git commit -m "feat(chat): persist chat history settings on profile"
```

---

### Task 3: Wire history into SystemChatControl

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/SystemChatControl.cs`

**Interfaces:**
- Consumes: `ChatMessageHistory`, `ChatHistoryEntry` (Task 1); `Profile.ChatHistory/ChatHistoryLength/ChatUseArrowsForHistory` (Task 2).

No unit test (UI-bound wiring); verified by build + manual test. Follow steps exactly.

- [ ] **Step 1: Replace the static history fields**

Replace lines 41-42:

```csharp
        private static readonly List<Tuple<ChatMode, string>> _messageHistory = new List<Tuple<ChatMode, string>>();
        private static int _messageHistoryIndex = -1;
```

with:

```csharp
        private readonly ChatMessageHistory _history = new ChatMessageHistory();
```

- [ ] **Step 2: Load history in the constructor**

In the constructor, immediately before `SetFocus();` (currently line 117), add:

```csharp
            _history.MaxLength = ProfileManager.CurrentProfile.ChatHistoryLength;
            _history.Load(ProfileManager.CurrentProfile.ChatHistory);

```

- [ ] **Step 3: Add the two navigation helper methods**

Add these methods to the class (e.g. directly above `public string ExtractSendableTextSubstring`, currently line 723):

```csharp
        private void HistoryPrevious()
        {
            if (!IsActive)
            {
                return;
            }

            ChatHistoryEntry entry = _history.MovePrevious();

            if (entry != null)
            {
                Mode = entry.Mode;
                TextBoxControl.SetText(entry.Text);
            }
        }

        private void HistoryNext()
        {
            if (!IsActive)
            {
                return;
            }

            if (_history.MoveNext(out ChatHistoryEntry entry))
            {
                Mode = entry.Mode;
                TextBoxControl.SetText(entry.Text);
            }
            else
            {
                TextBoxControl.ClearText();
            }
        }
```

- [ ] **Step 4: Rewrite the Ctrl+Q case**

Replace the entire `SDLK_Q` case (currently lines 605-633) with:

```csharp
                case SDL.SDL_Keycode.SDLK_Q when Keyboard.Ctrl && !ProfileManager.CurrentProfile.ChatUseArrowsForHistory && !ProfileManager.CurrentProfile.DisableCtrlQWBtn:

                    if (Client.Game.GetScene<GameScene>() == null)
                    {
                        return;
                    }

                    if (_gump.World.Macros.FindMacro(key, false, true, false) != null)
                    {
                        return;
                    }

                    HistoryPrevious();

                    break;
```

- [ ] **Step 5: Rewrite the Ctrl+W case**

Replace the entire `SDLK_W` case (currently lines 635-667) with:

```csharp
                case SDL.SDL_Keycode.SDLK_W when Keyboard.Ctrl && !ProfileManager.CurrentProfile.ChatUseArrowsForHistory && !ProfileManager.CurrentProfile.DisableCtrlQWBtn:

                    if (Client.Game.GetScene<GameScene>() == null)
                    {
                        return;
                    }

                    if (_gump.World.Macros.FindMacro(key, false, true, false) != null)
                    {
                        return;
                    }

                    HistoryNext();

                    break;
```

- [ ] **Step 6: Add the Up/Down arrow cases**

Immediately after the Ctrl+W case (before the `SDLK_BACKSPACE when Keyboard.Ctrl` case), add:

```csharp
                case SDL.SDL_Keycode.SDLK_UP when ProfileManager.CurrentProfile.ChatUseArrowsForHistory && !Keyboard.Ctrl && IsActive:
                    HistoryPrevious();

                    break;

                case SDL.SDL_Keycode.SDLK_DOWN when ProfileManager.CurrentProfile.ChatUseArrowsForHistory && !Keyboard.Ctrl && IsActive:
                    HistoryNext();

                    break;
```

- [ ] **Step 7: Append + persist on send**

In `HandleMessageSend`, replace lines 821-822:

```csharp
            _messageHistory.Add(new Tuple<ChatMode, string>(Mode, text));
            _messageHistoryIndex = _messageHistory.Count;
```

with:

```csharp
            _history.MaxLength = ProfileManager.CurrentProfile.ChatHistoryLength;
            _history.Add(new ChatHistoryEntry { Mode = Mode, Text = text });
            ProfileManager.CurrentProfile.ChatHistory = new List<ChatHistoryEntry>(_history.Entries);
```

- [ ] **Step 8: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds, no references to `_messageHistory` / `_messageHistoryIndex` remain (no CS0103).

- [ ] **Step 9: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/SystemChatControl.cs
git commit -m "feat(chat): navigate history with arrows, persist per profile"
```

---

### Task 4: Suppress caret-on-plain-arrow in the chat textbox

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Controls/StbTextBox.cs`

**Why:** `StbTextBox.OnKeyDown` moves the caret on Up/Down (lines 623-633) and then bubbles to the parent via `base.OnKeyDown` (line 746). Without this change, a plain Up/Down would move the caret *and* trigger history nav. Requirement: plain arrows = history only; Ctrl+arrows = caret. This suppresses the caret move for the system-chat textbox on plain arrows when the arrow toggle is on; the key still bubbles to `SystemChatControl` for history.

**Interfaces:**
- Consumes: `Profile.ChatUseArrowsForHistory` (Task 2); `UIManager.SystemChat` (existing).

No unit test (UI-bound); verified by build + manual test.

- [ ] **Step 1: Ensure the ProfileManager using is present**

At the top of `src/ClassicUO.Client/Game/UI/Controls/StbTextBox.cs`, confirm `using ClassicUO.Configuration;` exists; if not, add it with the other usings.

- [ ] **Step 2: Add the guard helper**

Add this private method to the `StbTextBox` class (e.g. directly above `protected override void OnKeyDown`, line 489):

```csharp
        private bool IsChatHistoryArrow()
        {
            return !Keyboard.Ctrl
                && ProfileManager.CurrentProfile != null
                && ProfileManager.CurrentProfile.ChatUseArrowsForHistory
                && UIManager.SystemChat != null
                && UIManager.SystemChat.TextBoxControl == this;
        }
```

- [ ] **Step 3: Skip caret move on plain Up in the chat box**

Replace the `SDLK_UP` case (lines 623-627):

```csharp
                case SDL.SDL_Keycode.SDLK_UP:
                    stb_key = ApplyShiftIfNecessary(ControlKeys.Up);
                    update_caret = true;

                    break;
```

with:

```csharp
                case SDL.SDL_Keycode.SDLK_UP:
                    if (IsChatHistoryArrow())
                    {
                        break; // history handled by SystemChatControl; do not move caret
                    }

                    stb_key = ApplyShiftIfNecessary(ControlKeys.Up);
                    update_caret = true;

                    break;
```

- [ ] **Step 4: Skip caret move on plain Down in the chat box**

Replace the `SDLK_DOWN` case (lines 629-633):

```csharp
                case SDL.SDL_Keycode.SDLK_DOWN:
                    stb_key = ApplyShiftIfNecessary(ControlKeys.Down);
                    update_caret = true;

                    break;
```

with:

```csharp
                case SDL.SDL_Keycode.SDLK_DOWN:
                    if (IsChatHistoryArrow())
                    {
                        break; // history handled by SystemChatControl; do not move caret
                    }

                    stb_key = ApplyShiftIfNecessary(ControlKeys.Down);
                    update_caret = true;

                    break;
```

- [ ] **Step 5: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Controls/StbTextBox.cs
git commit -m "feat(chat): plain arrows skip caret move in chat box when history nav on"
```

---

### Task 5: Options UI + resource strings

**Files:**
- Modify: `src/ClassicUO.Client/Resources/ResGumps.resx` (add near line 678)
- Modify: `src/ClassicUO.Client/Resources/ResGumps.Designer.cs` (add near line 1300)
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs` (field decls ~line 66; build block ~line 3254; save block ~line 4322)

**Interfaces:**
- Consumes: `ResGumps.ChatHistoryUseArrows`, `ResGumps.ChatHistoryLength`; `Profile.ChatUseArrowsForHistory`, `Profile.ChatHistoryLength` (Task 2).

No unit test (UI-bound); verified by build + manual test.

- [ ] **Step 1: Add resx entries**

In `src/ClassicUO.Client/Resources/ResGumps.resx`, directly before the `DisableMessageHistory` data node (line 678), add:

```xml
  <data name="ChatHistoryUseArrows" xml:space="preserve">
    <value>Use Up/Down arrows for chat history</value>
  </data>
  <data name="ChatHistoryLength" xml:space="preserve">
    <value>Chat history length</value>
  </data>
```

- [ ] **Step 2: Add Designer.cs properties**

In `src/ClassicUO.Client/Resources/ResGumps.Designer.cs`, directly before the `public static string DisableMessageHistory {` property (line 1300), add:

```csharp
        /// <summary>
        ///   Looks up a localized string similar to Use Up/Down arrows for chat history.
        /// </summary>
        public static string ChatHistoryUseArrows {
            get {
                return ResourceManager.GetString("ChatHistoryUseArrows", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Chat history length.
        /// </summary>
        public static string ChatHistoryLength {
            get {
                return ResourceManager.GetString("ChatHistoryLength", resourceCulture);
            }
        }
        
```

- [ ] **Step 3: Add field declarations**

In `src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs`, near the other option field declarations (after line 67, `private InputField _maxJournalFiles;`), add:

```csharp
        private Checkbox _chatUseArrowsForHistory;
        private InputField _chatHistoryLength;
```

- [ ] **Step 4: Add the controls to the build block**

In `OptionsGump.cs`, directly after `startY += _disableCtrlQWBtn.Height + 2;` (line 3254) and before `_disableAutoMove = AddCheckBox`, add:

```csharp
            _chatUseArrowsForHistory = AddCheckBox
            (
                rightArea,
                ResGumps.ChatHistoryUseArrows,
                _currentProfile.ChatUseArrowsForHistory,
                startX,
                startY
            );

            startY += _chatUseArrowsForHistory.Height + 2;

            _chatHistoryLength = AddInputField
            (
                rightArea,
                startX,
                startY,
                50,
                TEXTBOX_HEIGHT,
                ResGumps.ChatHistoryLength,
                50,
                false,
                true,
                4
            );

            _chatHistoryLength.SetText(_currentProfile.ChatHistoryLength.ToString());

            startY += _chatHistoryLength.Height + 2;

```

- [ ] **Step 5: Persist the values in the save block**

In `OptionsGump.cs`, directly after `_currentProfile.DisableCtrlQWBtn = _disableCtrlQWBtn.IsChecked;` (line 4322), add:

```csharp
            _currentProfile.ChatUseArrowsForHistory = _chatUseArrowsForHistory.IsChecked;

            if (int.TryParse(_chatHistoryLength.Text, out int chatHistoryLength) && chatHistoryLength >= 0)
            {
                _currentProfile.ChatHistoryLength = chatHistoryLength;
            }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug`
Expected: Build succeeds; `ResGumps.ChatHistoryUseArrows` and `ResGumps.ChatHistoryLength` resolve.

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Resources/ResGumps.resx src/ClassicUO.Client/Resources/ResGumps.Designer.cs src/ClassicUO.Client/Game/UI/Gumps/OptionsGump.cs
git commit -m "feat(chat): options for chat-history arrow toggle and length"
```

---

### Task 6: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/ClassicUO.UnitTests`
Expected: PASS (including the new `ChatMessageHistoryTests` and `ProfileChatHistoryDefaultsTests`).

- [ ] **Step 2: Full solution build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: Build succeeds.

- [ ] **Step 3: Manual smoke test (requires a UO data dir; document results)**

Verify in a running client:
- Send several messages; Up walks back through them (text + mode restored); Down walks forward and clears past the newest.
- Multi-line message: Ctrl+Up / Ctrl+Down move the caret; plain arrows navigate history.
- Options → uncheck "Use Up/Down arrows for chat history": Ctrl+Q/W navigate; plain arrows move caret.
- Set "Chat history length" to a small N; send > N messages; only the last N are recalled.
- Restart client; history reloads from the profile.
- Set length to 0: history recall is disabled, no crash.

- [ ] **Step 4: Commit any doc/notes updates (if applicable)**

```bash
git commit --allow-empty -m "chore(chat): chat history feature verified"
```

---

## Self-Review

**Spec coverage:**
- Up/Down navigation → Task 3 (Steps 6) + Task 4.
- Ctrl+Up/Down caret movement → Task 4 (plain arrows suppressed; Ctrl passes through to existing caret logic; `SystemChatControl` Up/Down cases guarded `!Keyboard.Ctrl`).
- Options toggle (arrows vs Ctrl+Q/W) → Task 5 checkbox + Task 3 guards.
- Configurable length → Task 5 input + Task 1/3 trim.
- Per-profile persistence across restart → Task 2 + Task 3 (load in ctor, write on send).
- Resource strings → Task 5.

**Placeholder scan:** none — every code step contains full code.

**Type consistency:** `ChatMessageHistory` / `ChatHistoryEntry` / `MovePrevious` / `MoveNext(out)` / `Load` / `Add` / `MaxLength` / `Entries` used identically across Tasks 1-3. Profile props `ChatUseArrowsForHistory` / `ChatHistoryLength` / `ChatHistory` consistent across Tasks 2, 3, 5. `ResGumps.ChatHistoryUseArrows` / `ResGumps.ChatHistoryLength` consistent across Task 5 resx/Designer/gump.
