// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.PluginApi;

/// <summary>
/// Plugin-driven override of a single entity's nameplate (the Object
/// Handles name+HP box). An override for a serial replaces any previous
/// override for that serial and persists until cleared; it wins over the
/// player's own name-overhead filters, the global name-overhead toggle,
/// and a manual close.
/// </summary>
public interface INameplates
{
    /// <summary>
    /// Forces serial's nameplate visible or hidden. When
    /// <paramref name="visible"/> is true, <paramref name="opacityPercent"/>
    /// (0-100, clamped) sets the box's opacity; ignored when false.
    /// Auto-marshals to the game thread.
    /// </summary>
    void SetOverride(uint serial, bool visible, byte opacityPercent = 100);

    /// <summary>
    /// Removes the override for serial, reverting to normal (player-driven)
    /// behavior. Auto-marshals to the game thread.
    /// </summary>
    void ClearOverride(uint serial);

    /// <summary>
    /// Removes every nameplate override, regardless of which plugin set it.
    /// Auto-marshals to the game thread.
    /// </summary>
    void ClearAllOverrides();
}
