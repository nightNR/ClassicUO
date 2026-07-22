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
    /// <paramref name="serial"/>. <paramref name="hue"/> tints the outline ring;
    /// <paramref name="backgroundHue"/> tints the bar background (0 leaves the
    /// state-driven background color). Both 0 clears the overlay. The overlay
    /// persists until the plugin clears it; it is NOT auto-cleared when the
    /// mobile is removed, and because serials can be recycled you should clear
    /// it when you no longer want it. Re-apply as needed. Auto-marshals to the
    /// game thread. Badge support is deferred (see spec).
    /// </summary>
    void SetOverlay(uint serial, ushort hue, ushort backgroundHue = 0);

    /// <summary>
    /// Sets the ordering priority for the status bar of <paramref name="serial"/>.
    /// Within its anchor group, bars are ordered by priority descending, then by
    /// the order they were opened; default is 0, and passing 0 resets. Priority
    /// only affects layout while the serial's bar belongs to a group. Auto-marshals
    /// to the game thread.
    /// </summary>
    void SetStatusBarPriority(uint serial, int priority);
}
