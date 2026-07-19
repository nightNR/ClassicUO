# Autologin Reconnect-Only Login Gump Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In autologin mode with saved credentials, show a minimal reconnect-only login gump instead of account/password inputs, with an escape to the full login and automatic fallback on fatal auth rejection.

**Architecture:** Extract the two decision points (which gump to show; whether an auth rejection is fatal) into a pure static `LoginReconnectPolicy` class that is unit-tested without graphics. `LoginScene` consumes the policy to pick between the existing `LoginGump` and a new `ReconnectGump`. `HandleErrorCode` uses the policy to route fatal auth errors to the full login. `LoginGump` gains an error label that renders `LoginScene.PopupMessage`.

**Tech Stack:** C# `net10.0`, xUnit, FNA-XNA gump UI. Follows the existing pure-resolver test pattern (`ContainerViewModeResolver`).

## Global Constraints

- Target framework `net10.0`, `LangVersion=Preview`, `AllowUnsafeBlocks=true`.
- New source files carry the BSD-2 license header: `// SPDX-License-Identifier: BSD-2-Clause` as line 1.
- Login window is `640x480` DPI-scaled, `AllowUserResizing = false` — do not change.
- Gump construction touches `Client.Game.UO.*` graphics and is NOT unit-testable; only pure logic is unit-tested. Gump behavior is verified manually.
- Reconnect-only condition (requirement C): `AutoLogin && Username != "" && Password != "" && !forceFullLogin`.
- Fatal auth codes for packet `0x82`: `0` (bad name/password), `2` (blocked), `3` (invalid credentials). Non-fatal: `1` (in-use), `4`–`8` (network/IGR). Packets `0x53`/`0x85` are never fatal here.
- Visual layout / exact coordinates / art are OUT OF SCOPE (designed separately). Where this plan places controls, use the placeholder coordinates given and mark them `// layout: placeholder`.

---

## File Structure

- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs` — pure static decision logic (gump selection + fatal-auth classification).
- Create: `tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs` — unit tests for the policy.
- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/ReconnectGump.cs` — the reconnect-only gump.
- Modify: `src/ClassicUO.Client/Game/Scenes/LoginScene.cs` — `_forceFullLogin` field, gump selection in `Load()` and `GetGumpForStep()`, `HandleErrorCode` routing, keep `PopupMessage` on fatal fallback.
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginGump.cs` — error label rendering `scene.PopupMessage`.

---

## Task 1: LoginReconnectPolicy (pure decision logic + tests)

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs`
- Test: `tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs`

**Interfaces:**
- Consumes: nothing (leaf).
- Produces:
  - `static bool LoginReconnectPolicy.UseReconnectGump(bool autoLogin, string username, string password, bool forceFullLogin)`
  - `static bool LoginReconnectPolicy.IsFatalAuthRejection(byte packetID, byte code)`

- [ ] **Step 1: Write the failing test**

Create `tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps.Login;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI.Gumps
{
    public class LoginReconnectPolicyTests
    {
        [Theory]
        [InlineData(true, "user", "pass", false, true)]   // all present -> reconnect-only
        [InlineData(false, "user", "pass", false, false)]  // autologin off
        [InlineData(true, "", "pass", false, false)]       // no username
        [InlineData(true, "user", "", false, false)]       // no password
        [InlineData(true, null, "pass", false, false)]     // null username
        [InlineData(true, "user", null, false, false)]     // null password
        [InlineData(true, "user", "pass", true, false)]    // forced full login
        public void UseReconnectGump_TruthTable(bool autoLogin, string user, string pass, bool force, bool expected)
        {
            Assert.Equal(expected, LoginReconnectPolicy.UseReconnectGump(autoLogin, user, pass, force));
        }

        [Theory]
        [InlineData(0x82, 0, true)]   // bad name/password
        [InlineData(0x82, 2, true)]   // blocked
        [InlineData(0x82, 3, true)]   // invalid credentials
        [InlineData(0x82, 1, false)]  // in-use
        [InlineData(0x82, 4, false)]  // communication problem
        [InlineData(0x82, 8, false)]  // could not connect
        [InlineData(0x53, 0, false)]  // login-error packet, never fatal here
        [InlineData(0x85, 0, false)]  // character-list-error packet, never fatal here
        public void IsFatalAuthRejection_Classification(byte packetID, byte code, bool expected)
        {
            Assert.Equal(expected, LoginReconnectPolicy.IsFatalAuthRejection(packetID, code));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~LoginReconnectPolicyTests"`
Expected: FAIL — build error, `LoginReconnectPolicy` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Game.UI.Gumps.Login
{
    internal static class LoginReconnectPolicy
    {
        public static bool UseReconnectGump(bool autoLogin, string username, string password, bool forceFullLogin)
        {
            return autoLogin
                && !string.IsNullOrEmpty(username)
                && !string.IsNullOrEmpty(password)
                && !forceFullLogin;
        }

        public static bool IsFatalAuthRejection(byte packetID, byte code)
        {
            // Only the account-login rejection packet (0x82) carries fatal auth codes.
            // 0 = bad name/password, 2 = account blocked, 3 = invalid credentials.
            // 1 = account in-use, 4..8 = network/IGR -> not fatal (keep retrying).
            return packetID == 0x82 && (code == 0 || code == 2 || code == 3);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ClassicUO.UnitTests --filter "FullyQualifiedName~LoginReconnectPolicyTests"`
Expected: PASS (15 cases).

- [ ] **Step 5: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/Login/LoginReconnectPolicy.cs tests/ClassicUO.UnitTests/Game/UI/Gumps/LoginReconnectPolicyTests.cs
git commit -m "feat(login): add LoginReconnectPolicy decision logic"
```

---

## Task 2: Wire policy into LoginScene (selection + error routing)

**Files:**
- Modify: `src/ClassicUO.Client/Game/Scenes/LoginScene.cs`

**Interfaces:**
- Consumes: `LoginReconnectPolicy.UseReconnectGump(...)`, `LoginReconnectPolicy.IsFatalAuthRejection(...)` from Task 1; `ReconnectGump` from Task 3 (compiles only after Task 3 exists — see note).
- Produces:
  - `internal bool LoginScene.ForceFullLogin { get; set; }` (settable by `ReconnectGump`).
  - Gump-selection helper `private Gump CreateMainGump()`.

> **Note on ordering:** Task 2 references `ReconnectGump` (Task 3). Implement the `ReconnectGump` type stub (Task 3 Step 3) before compiling Task 2, or do Task 3 first. The two commits can land in either order but the solution only builds once both exist. If executing strictly in sequence, create an empty `ReconnectGump` shell first (Task 3), then complete Task 2, then finish Task 3 wiring.

This task has no unit test (gump construction needs graphics). Verification is a successful build + manual smoke test at the end.

- [ ] **Step 1: Add the `ForceFullLogin` field/property**

In `src/ClassicUO.Client/Game/Scenes/LoginScene.cs`, near the other private fields (after line 47 `private bool _autoLogin;`), add:

```csharp
        private bool _forceFullLogin;
```

And near the public properties (after line 62 `public bool CanAutologin => _autoLogin || Reconnect;`), add:

```csharp
        internal bool ForceFullLogin
        {
            get => _forceFullLogin;
            set => _forceFullLogin = value;
        }
```

- [ ] **Step 2: Add a gump-selection helper**

In `LoginScene`, add a private helper (place it just above `GetGumpForStep()` near line 177):

```csharp
        private Gump CreateMainGump()
        {
            if (LoginReconnectPolicy.UseReconnectGump(
                    Settings.GlobalSettings.AutoLogin,
                    Settings.GlobalSettings.Username,
                    Settings.GlobalSettings.Password,
                    _forceFullLogin))
            {
                return new ReconnectGump(_world, this);
            }

            return new LoginGump(_world, this);
        }
```

Add the using if not present at top of file:

```csharp
using ClassicUO.Game.UI.Gumps.Login;
```

(Check existing usings first — `LoginGump` is already referenced, so the namespace is likely already imported. If `LoginGump` resolves without a using, skip adding it.)

- [ ] **Step 3: Use the helper in `Load()`**

In `Load()` replace line 75:

```csharp
            UIManager.Add(_currentGump = new LoginGump(_world, this));
```

with:

```csharp
            UIManager.Add(_currentGump = CreateMainGump());
```

- [ ] **Step 4: Use the helper in `GetGumpForStep()`**

In `GetGumpForStep()`, `case LoginSteps.Main` (lines 194-197), replace:

```csharp
                case LoginSteps.Main:
                    PopupMessage = null;

                    return new LoginGump(_world,this);
```

with:

```csharp
                case LoginSteps.Main:
                    if (!_keepPopupOnMain)
                    {
                        PopupMessage = null;
                    }

                    _keepPopupOnMain = false;

                    return CreateMainGump();
```

Add the backing field near the other private fields (after `_forceFullLogin`):

```csharp
        private bool _keepPopupOnMain;
```

(`_keepPopupOnMain` lets a fatal-auth fallback carry its error message into the full `LoginGump` for one render; normal Main transitions still clear it.)

- [ ] **Step 5: Route fatal auth errors in `HandleErrorCode`**

Replace `HandleErrorCode` (lines 643-650):

```csharp
        public void HandleErrorCode(ref StackDataReader p)
        {
            byte code = p.ReadUInt8();

            PopupMessage = ServerErrorMessages.GetError(p[0], code, LoginDelay);
            CurrentLoginStep = LoginSteps.PopUpMessage;
            LoginDelay = default;
        }
```

with:

```csharp
        public void HandleErrorCode(ref StackDataReader p)
        {
            byte packetID = p[0];
            byte code = p.ReadUInt8();

            PopupMessage = ServerErrorMessages.GetError(packetID, code, LoginDelay);
            LoginDelay = default;

            if (LoginReconnectPolicy.IsFatalAuthRejection(packetID, code))
            {
                // Saved credentials are rejected for good: stop auto-retry and
                // fall back to the full login screen, keeping the error message.
                _forceFullLogin = true;
                Reconnect = false;
                _keepPopupOnMain = true;
                CurrentLoginStep = LoginSteps.Main;
            }
            else
            {
                CurrentLoginStep = LoginSteps.PopUpMessage;
            }
        }
```

- [ ] **Step 6: Build to verify it compiles (after Task 3 shell exists)**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds (requires the `ReconnectGump` type to exist — see Task 3).

- [ ] **Step 7: Commit**

```bash
git add src/ClassicUO.Client/Game/Scenes/LoginScene.cs
git commit -m "feat(login): select reconnect gump and route fatal auth to full login"
```

---

## Task 3: ReconnectGump

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/Gumps/Login/ReconnectGump.cs`

**Interfaces:**
- Consumes: `LoginScene.Connect(string, string)`, `LoginScene.ForceFullLogin` (Task 2), `Settings.GlobalSettings.Username/Password`, `Crypter.Decrypt`.
- Produces: `internal class ReconnectGump : Gump` with `ReconnectGump(World world, LoginScene scene)`.

No unit test (graphics). Verified by build + manual smoke test.

- [ ] **Step 1: Create the gump shell (unblocks Task 2 build)**

Create `src/ClassicUO.Client/Game/UI/Gumps/Login/ReconnectGump.cs`:

```csharp
// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Utility;
using ClassicUO.Renderer;

namespace ClassicUO.Game.UI.Gumps.Login
{
    internal class ReconnectGump : Gump
    {
        private readonly LoginScene _scene;

        public ReconnectGump(World world, LoginScene scene) : base(world, 0, 0)
        {
            _scene = scene;
            CanCloseWithRightClick = false;
            AcceptKeyboardInput = false;
        }

        private enum Buttons
        {
            Reconnect,
            ChangeAccount,
            Quit,
            Credits
        }
    }
}
```

- [ ] **Step 2: Build to confirm the type resolves**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds (Task 2 now compiles against this type).

- [ ] **Step 3: Add background + controls**

Fill in the constructor body after `AcceptKeyboardInput = false;`. Mirror `LoginGump`'s version branch for the background only, then add the reconnect/change-account controls. Coordinates are placeholders (visual pass will finalize them).

```csharp
            ushort buttonNormal, buttonOver, buttonPressed;

            if (Client.Game.UO.Version < ClientVersion.CV_706400)
            {
                if (Client.Game.UO.Version >= ClientVersion.CV_500A)
                {
                    Add(new GumpPic(0, 0, 0x2329, 0));
                }

                Add(new GumpPic(0, 4, 0x15A0, 0) { AcceptKeyboardInput = false });

                buttonNormal = 0x15A4;
                buttonOver = 0x15A6;
                buttonPressed = 0x15A5;

                // Quit
                Add(new Button((int)Buttons.Quit, 0x1589, 0x158B, 0x158A)
                {
                    X = 555,
                    Y = 4,
                    ButtonAction = ButtonAction.Activate
                });
            }
            else
            {
                Add(new GumpPic(0, 0, 0x014E, 0));

                buttonNormal = 0x5CD;
                buttonOver = 0x5CC;
                buttonPressed = 0x5CB;

                // Quit
                Add(new Button((int)Buttons.Quit, 0x05CA, 0x05C9, 0x05C8)
                {
                    X = 25,
                    Y = 240,
                    ButtonAction = ButtonAction.Activate
                });
            }

            // Status label: which account is reconnecting + any pending message. layout: placeholder
            string status = string.IsNullOrEmpty(_scene.PopupMessage)
                ? $"Reconnecting as {Settings.GlobalSettings.Username}"
                : _scene.PopupMessage;

            Add(new Label(status, false, 0x0386, font: 2, maxwidth: 400)
            {
                X = 120,
                Y = 300  // layout: placeholder
            });

            // Reconnect button. layout: placeholder
            Add(new Button((int)Buttons.Reconnect, buttonNormal, buttonPressed, buttonOver)
            {
                X = 300,
                Y = 340,
                ButtonAction = ButtonAction.Activate
            });

            // Change account escape link. layout: placeholder
            Add(new HtmlControl(
                200,
                380,
                200,
                20,
                false,
                false,
                false,
                "<body link=\"#FF00FF00\" vlink=\"#FF00FF00\" ><a href=\"changeaccount\">Change account",
                0x32,
                true,
                isunicode: true,
                style: FontStyle.BlackBorder));
```

Add `using Microsoft.Xna.Framework;` and any control usings the compiler flags. `HtmlControl` link clicks are handled via `OnHtmlLinkClicked`/link href — verify the actual mechanism in `LoginGump` (its links open URLs). If HtmlControl href routing to an in-app action is not supported, replace the "Change account" link with a `Button((int)Buttons.ChangeAccount, ...)` using an available gump art id and handle it in `OnButtonClick` (Step 4). Prefer the Button form if in doubt — it uses the same `OnButtonClick` path as Reconnect.

- [ ] **Step 4: Handle button clicks**

Add to `ReconnectGump`:

```csharp
        public override void OnButtonClick(int buttonID)
        {
            switch ((Buttons)buttonID)
            {
                case Buttons.Reconnect:
                    _scene.Connect(
                        Settings.GlobalSettings.Username,
                        Crypter.Decrypt(Settings.GlobalSettings.Password));

                    break;

                case Buttons.ChangeAccount:
                    _scene.ForceFullLogin = true;
                    _scene.Reconnect = false;
                    _scene.CurrentLoginStep = LoginSteps.Main;

                    break;

                case Buttons.Quit:
                    Client.Game.Exit();

                    break;

                case Buttons.Credits:
                    UIManager.Add(new CreditsGump(World));

                    break;
            }
        }
```

If the "Change account" control is an `HtmlControl` link (not a Button), instead route its href: find how `LoginGump` handles link clicks and mirror it, calling the same three `_scene.*` assignments as the `ChangeAccount` case above.

- [ ] **Step 5: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/Login/ReconnectGump.cs
git commit -m "feat(login): add reconnect-only gump"
```

---

## Task 4: LoginGump error label

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/Login/LoginGump.cs`

**Interfaces:**
- Consumes: `LoginScene.PopupMessage` (existing public property).
- Produces: nothing new (additive rendering).

No unit test (graphics). Verified by build + manual smoke test.

- [ ] **Step 1: Read the scene's PopupMessage in the constructor**

In `LoginGump` constructor (`LoginGump.cs`), after the controls are added and before the focus block near line 478, add an error label when the scene has a pending message:

```csharp
            if (!string.IsNullOrEmpty(scene.PopupMessage))
            {
                Add(new Label(scene.PopupMessage, false, 0x0020, font: 2, maxwidth: 400)
                {
                    X = 130,
                    Y = 250  // layout: placeholder (above the account/password panel)
                });
            }
```

Confirm the constructor parameter name for the scene is `scene` (it is: `public LoginGump(World world, LoginScene scene)`). Use that reference; do not call `Client.Game.GetScene<LoginScene>()`.

- [ ] **Step 2: Build**

Run: `dotnet build ClassicUO.sln -c Debug`
Expected: build succeeds.

- [ ] **Step 3: Manual smoke test**

Run the client (`cd scripts && bash build-naot.sh` or a Debug run) against a test shard with these settings, verifying each:

1. `autologin=true`, valid saved `Username`+`Password`, `SaveAccount=true` → login screen shows **ReconnectGump** (no account/password fields), connects on Reconnect.
2. Set a wrong saved password → server sends `0x82` code 0 → screen falls back to **full LoginGump** showing "Incorrect password" (or localized equivalent), no retry loop.
3. Account-in-use (`0x82` code 1) → stays on **ReconnectGump**, keeps retrying per `ReconnectTime`.
4. On ReconnectGump, click **Change account** → switches to full LoginGump; account/password fields editable.
5. `autologin=true` but empty saved password → full LoginGump (no lockout).

- [ ] **Step 4: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/Gumps/Login/LoginGump.cs
git commit -m "feat(login): show pending error message on full login gump"
```

---

## Self-Review Notes

- **Spec coverage:** §1 gump selection → Task 1 (`UseReconnectGump`) + Task 2 (`CreateMainGump`, `Load`, `GetGumpForStep`). §2 ReconnectGump → Task 3. §3 error routing → Task 1 (`IsFatalAuthRejection`) + Task 2 (`HandleErrorCode`). §4 full gump error → Task 4. Requirement C → Task 1 truth table. Requirement A escape → Task 3 ChangeAccount. Requirement B fatal fallback → Task 2 Step 5. Requirement A error display → Task 4.
- **Type consistency:** `LoginReconnectPolicy.UseReconnectGump`/`IsFatalAuthRejection`, `LoginScene.ForceFullLogin`, `ReconnectGump(World, LoginScene)`, `Buttons.{Reconnect,ChangeAccount,Quit,Credits}` used consistently across Tasks 1–4.
- **Known verification points for the implementer:** (a) whether `ClassicUO.Game.UI.Gumps.Login` is already imported in `LoginScene.cs`; (b) the exact `Label`/`Button`/`HtmlControl` constructor signatures (copy from existing `LoginGump.cs` usage); (c) whether `HtmlControl` href can trigger an in-app action or the "Change account" control must be a `Button`. Each is called out inline in the relevant task.
