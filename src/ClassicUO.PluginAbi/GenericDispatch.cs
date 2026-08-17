// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.PluginAbi;

/// <summary>
/// Opcodes for <see cref="ClientBindings.GenericInvokeFn"/> — the plugin/host
/// calls into cuo, cuo returns a byte count written to the caller's output
/// buffer (or a negative error code: -1 unknown opcode, -2 output buffer too
/// small). Add new capabilities as new opcodes here instead of new
/// <see cref="ClientBindings"/> fields.
/// </summary>
public static class GenericOpcodes
{
    /// <summary>
    /// Capability negotiation. Args: none. Result: a packed <c>int[]</c> of
    /// every opcode this cuo build supports (including this one). Callers
    /// should probe this once after connect/init and only invoke opcodes the
    /// result actually lists — an older cuo build simply won't list newer ones.
    /// </summary>
    public const int GetSupportedOpcodes = 0;
}

/// <summary>
/// Event ids for <see cref="HostBindings.GenericEventFn"/> — cuo pushes an
/// event to the host, which fans it out to plugins. Add new event kinds as
/// new ids here instead of new <see cref="HostBindings"/> fields.
/// </summary>
public static class GenericEventIds
{
}
