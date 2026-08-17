// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.PluginApi;

/// <summary>
/// Escape valve for plugin/host &lt;-&gt; cuo capabilities that don't (yet)
/// warrant a dedicated typed service. Requests are opcode + binary payload;
/// events are pushed the same way. Capabilities that see real use should
/// graduate to a proper typed interface (like <see cref="IHighlight"/> or
/// <see cref="IScreenTimers"/> did) rather than growing this indefinitely.
/// </summary>
public interface IRawChannel
{
    /// <summary>
    /// Calls into cuo with an opcode-identified request. Returns the number
    /// of bytes written to <paramref name="result"/>, or a negative error
    /// code (-1 unknown opcode, -2 <paramref name="result"/> too small).
    /// Must be called from the game thread — use <see cref="IDispatcher"/> to
    /// marshal first.
    /// </summary>
    int Invoke(int opcode, ReadOnlySpan<byte> args, Span<byte> result);

    /// <summary>
    /// Raised when cuo pushes an event not covered by a dedicated
    /// <see cref="IPluginContext"/> event. Fired on the game thread.
    /// </summary>
    event Action<int, byte[]>? Event;
}
