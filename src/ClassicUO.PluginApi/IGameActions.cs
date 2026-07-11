// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.PluginApi;

/// <summary>
/// Player-driven game actions that synthesize the same packets the client
/// sends for user input. Per-method threading rules are documented below.
/// </summary>
public interface IGameActions
{
    /// <summary>
    /// Casts a spell by spellbook index. Auto-marshals to the game thread
    /// when called from elsewhere.
    /// </summary>
    void CastSpell(int spellIndex);

    /// <summary>
    /// Requests a single tile of movement in the given direction (0..7).
    /// Returns <c>false</c> if the player can't move right now (paralyzed,
    /// blocked, etc.). Must be called on the game thread; throws otherwise.
    /// Use <see cref="IPluginContext.Game"/>.<c>Post</c> to marshal.
    /// </summary>
    bool RequestMove(int direction, bool run);

    /// <summary>
    /// Reads the player's tile coordinates. Returns <c>false</c> if the
    /// player object is not initialized (pre-login or mid-disconnect).
    /// </summary>
    bool TryGetPlayerPosition(out int x, out int y, out int z);

    /// <summary>
    /// Pathfinds to tile (x, y, z) and starts auto-walking toward it, stopping
    /// within <paramref name="distance"/> tiles of the goal. Pass
    /// <paramref name="run"/> = true to run instead of walk. Returns
    /// <c>false</c> if no path can be found right now. Must be called on the
    /// game thread; throws otherwise. Use <see cref="IPluginContext.Game"/>.
    /// <c>Post</c> to marshal.
    /// </summary>
    bool WalkTo(int x, int y, int z, int distance, bool run);

    /// <summary>
    /// Cancels any active auto-walk. Safe to call at any time and from any
    /// thread (auto-marshals to the game thread).
    /// </summary>
    void StopWalk();

    /// <summary>
    /// Raised on auto-walk state transitions. Fired on the game thread.
    /// </summary>
    event Action<WalkState>? WalkProgress;
}

/// <summary>State of an auto-walk requested via <see cref="IGameActions.WalkTo"/>.</summary>
public enum WalkState
{
    /// <summary>A path was found and the player started moving.</summary>
    Walking,
    /// <summary>The player reached within <c>distance</c> tiles of the goal.</summary>
    Arrived,
    /// <summary>A step failed (a dynamic mobile/item blocks the path). The
    /// plugin may re-issue WalkTo to re-route.</summary>
    Blocked,
    /// <summary>The walk was cancelled, no path existed, or the player object
    /// went away.</summary>
    Stopped,
}
