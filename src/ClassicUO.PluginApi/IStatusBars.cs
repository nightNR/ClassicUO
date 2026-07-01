// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.PluginApi;

/// <summary>
/// Thin, generic status-bar primitives keyed by mobile serial. Priority/
/// highlight policy lives in the plugin; the client only renders. All methods
/// auto-marshal to the game thread, so they are safe to call from any thread.
/// </summary>
public interface IStatusBars
{
    /// <summary>
    /// Opens a status (health) bar for <paramref name="serial"/> at screen
    /// (<paramref name="x"/>, <paramref name="y"/>). If a bar already exists for
    /// that serial: when <paramref name="moveIfExists"/> is true it is moved to
    /// (x, y); otherwise the call is a no-op. When <paramref name="groupId"/> is
    /// non-zero the bar is anchored into the shared group with that id.
    /// Auto-marshals to the game thread.
    /// </summary>
    void OpenStatusBar(uint serial, int x, int y, bool moveIfExists = true, int groupId = 0);

    /// <summary>Closes the status bar for <paramref name="serial"/> if present.
    /// Auto-marshals to the game thread.</summary>
    void CloseStatusBar(uint serial);

    /// <summary>
    /// Sets a priority-highlight hue on the status bar for
    /// <paramref name="serial"/>. <paramref name="hue"/> = 0 clears the
    /// highlight. The highlight persists until the plugin clears it (hue 0);
    /// it is NOT auto-cleared when the mobile is removed, and because serials
    /// can be recycled you should clear it when you no longer want it. Re-apply
    /// as needed. Auto-marshals to the game thread.
    /// Badge support is deferred (see spec).
    /// </summary>
    void SetOverlay(uint serial, ushort hue);
}
