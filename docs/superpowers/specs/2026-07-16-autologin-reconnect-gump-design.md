# Autologin Reconnect-Only Login Gump — Design

Date: 2026-07-16
Branch target: TBD (feat)

## Goal

When the client is in autologin mode with saved credentials, the login screen
must NOT show account/password inputs. Instead it shows a minimal
**reconnect-only** gump (Reconnect button + "Change account" escape). If saved
credentials are rejected as fatal (bad password / blocked account / invalid
credentials), the client falls back to the full login gump with the error
message shown. Non-fatal rejections (account in-use, communication/network
errors) keep the reconnect-only gump and continue retrying.

Visual layout/art is designed separately — this spec covers behavior/logic only.

## Login scene facts (reference)

- Window: `640x480`, DPI-scaled. `AllowUserResizing = false`.
  `LoginScene.Load()` — `LoginScene.cs:94-97`.
- `LoginBackground` (640x480 tiled) + `LoginGump` (origin 0,0) added in
  `Load()` — `LoginScene.cs:74-75`.
- Two version branches by `Client.Game.UO.Version`:
  - Legacy (`< CV_706400`): bg `0x2329` + flag `0x15A0`; login panel ResizePic
    `0x13BE` @128,288 size 451x157.
  - New (`>= CV_706400`): bg `0x014E` (panel baked into art).
- Gump for current step chosen in `GetGumpForStep()` — `LoginScene.cs:177-223`.
  `LoginSteps.Main` → `new LoginGump`.
- Reconnect retry loop — `LoginScene.cs:139-162` (fires while `Reconnect &&
  step ∈ {Main, PopUpMessage} && !connected`).
- `CanAutologin => _autoLogin || Reconnect` — `LoginScene.cs:62`.
- Connect entry point: `LoginScene.Connect(account, password)` (public).
- Full-gump connect triggers: `OnButtonClick(Buttons.NextArrow)` —
  `LoginGump.cs:550-556`; `OnKeyboardReturn` — `LoginGump.cs:488-497`.

## Design

### 1. Gump selection

Add to `LoginScene`:

```csharp
private bool _forceFullLogin;   // set true by "Change account" and fatal auth reject

private bool UseReconnectGump =>
    Settings.GlobalSettings.AutoLogin
    && !string.IsNullOrEmpty(Settings.GlobalSettings.Username)
    && !string.IsNullOrEmpty(Settings.GlobalSettings.Password)
    && !_forceFullLogin;
```

- `Load()` (`LoginScene.cs:74-75`): choose `ReconnectGump` vs `LoginGump` via
  `UseReconnectGump`.
- `GetGumpForStep()` `case LoginSteps.Main` (`:194-197`): same choice.
- `LoginSteps.PopUpMessage` currently routes to `GetLoadingScreen()`. Leave that
  as-is for network/in-use waits (reconnect loop drives it). Fatal auth reject
  instead switches to `Main` with `_forceFullLogin = true` (see §3), so the full
  `LoginGump` renders.

This satisfies requirement C: reconnect-only only when AutoLogin AND both
credentials present; otherwise full login.

### 2. ReconnectGump (new file)

`src/ClassicUO.Client/Game/UI/Gumps/Login/ReconnectGump.cs`.

- Same 640x480 frame and same two version branches (bg `0x2329`/`0x014E`,
  legacy flag/panel) as `LoginGump`. Reuse the background/quit/credits/version-
  label construction; drop account/password backgrounds, textboxes, and the
  autologin/save-account checkboxes.
- **Reconnect button** — reuses `Buttons.NextArrow` action. `OnButtonClick`
  calls `Connect(Settings.GlobalSettings.Username,
  Crypter.Decrypt(Settings.GlobalSettings.Password))`. (Mirrors the existing
  arrow path in `LoginGump.cs:550-556`.)
- **"Change account" link/button** (escape route, requirement A): sets
  `scene._forceFullLogin = true`, `scene.CurrentLoginStep = LoginSteps.Main`
  (and `Reconnect = false` to stop the retry loop). Scene redraws the full
  `LoginGump` on next `Update()` tick (`LoginScene.cs:127-137`). Needs an
  internal setter/method on `LoginScene` to flip `_forceFullLogin`.
- **Status label**: shows the username being used and, when reconnecting, the
  attempt count / `PopupMessage`.
- Keep Quit, Credits, version labels, music controls optional (music can be
  dropped for minimalism — TBD low-priority).

No account/password textboxes, no autologin/save checkboxes.

### 3. Error routing (auto fallback, requirement B)

Auth errors arrive via packet `0x82` → `ReceiveLoginRejection`
(`PacketHandlers.cs:6026-6039`) → `LoginScene.HandleErrorCode`
(`LoginScene.cs:643-650`).

`0x82` code map (`ServerErrorMessages.cs:93-101`, `_generalErrors`):

| code | meaning | action |
|---|---|---|
| 0 | IncorrectNamePassword | **fatal → full login** |
| 1 | SomeoneIsAlreadyUsingThisAccount | keep reconnect-only |
| 2 | YourAccountHasBeenBlocked | **fatal → full login** |
| 3 | YourAccountCredentialsAreInvalid | **fatal → full login** |
| 4 | CommunicationProblem | keep reconnect-only (network) |
| 5 | IGR concurrency limit | keep reconnect-only |
| 6 | IGR time limit | keep reconnect-only |
| 7 | General IGR auth failure | keep reconnect-only |
| 8 | CouldntConnectToUO | keep reconnect-only (network) |

Modify `HandleErrorCode`:

```csharp
public void HandleErrorCode(ref StackDataReader p)
{
    byte packetID = p[0];
    byte code = p.ReadUInt8();

    PopupMessage = ServerErrorMessages.GetError(packetID, code, LoginDelay);
    LoginDelay = default;

    if (packetID == 0x82 && (code == 0 || code == 2 || code == 3))
    {
        // fatal auth: bad password / blocked / invalid credentials
        _forceFullLogin = true;
        Reconnect = false;                 // stop retry loop
        CurrentLoginStep = LoginSteps.Main; // redraw full LoginGump (carries PopupMessage)
    }
    else
    {
        CurrentLoginStep = LoginSteps.PopUpMessage; // existing behavior
    }
}
```

Non-`0x82` packets (`0x53`, `0x85`) are unaffected — they keep the existing
`PopUpMessage` path.

### 4. Full LoginGump shows the error (requirement A)

On fatal auth fallback, the full `LoginGump` must display `PopupMessage`
(e.g. "Incorrect password"). Today `LoginGump` does not render errors — the
`LoadingGump` does.

- `LoginScene.GetGumpForStep()` `case Main` sets `PopupMessage = null`
  currently (`:195`). Change: only null it when NOT arriving from a fatal auth
  fallback, so the message survives into the gump.
- `LoginGump` gains an error label (positioned in the free panel band; exact
  coords deferred to the visual design). It reads `scene.PopupMessage` on
  construction and renders it if non-empty.
- Once the user edits a field or submits, clearing is handled by the normal
  Main-step reset on the next successful/failed cycle.

## Units & boundaries

- `ReconnectGump` — new, self-contained gump. Depends on `LoginScene`
  (`Connect`, `_forceFullLogin` toggle, `Username`/`Password` settings). Testable
  by constructing with a scene and asserting no textboxes present + button
  wiring.
- `LoginScene.UseReconnectGump` / `_forceFullLogin` — pure selection logic.
- `LoginScene.HandleErrorCode` — classification logic; unit-testable by feeding
  packet id + code and asserting resulting `CurrentLoginStep` / `_forceFullLogin`
  / `Reconnect`.
- `LoginGump` error label — additive, no behavior change when `PopupMessage`
  empty.

## Error handling

- Missing saved password but AutoLogin on → `UseReconnectGump` false → full
  login (no lockout).
- Fatal auth → immediate full login with message; `Reconnect` cleared so no
  silent retry loop.
- Non-fatal → reconnect-only persists; existing retry loop and
  `ReconnectTime` cadence unchanged.
- "Change account" always available on reconnect-only gump as manual escape.

## Testing

- Unit: `HandleErrorCode` classification (0x82 codes 0/2/3 → Main +
  `_forceFullLogin`; codes 1/4/8 → PopUpMessage; 0x53/0x85 → PopUpMessage).
- Unit: `UseReconnectGump` truth table (AutoLogin × username × password ×
  `_forceFullLogin`).
- Manual: autologin with good creds → reconnect-only; wrong password → full
  login with "Incorrect password"; account-in-use → reconnect-only retries;
  "Change account" → full login.

## Out of scope

- Visual layout, art, exact coordinates, styling (designed separately).
- Legacy v1 plugin path.
- Changes to reconnect timing / `ReconnectTime`.
