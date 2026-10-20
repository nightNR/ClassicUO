// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Configuration
{
    /// <summary>
    /// Data that belongs to one character and must not be shared between characters that use the same named
    /// <see cref="Profile"/> (chat history, serial-bound container state). Stored as character_state.json in the
    /// per-character folder (Profiles/&lt;account&gt;/&lt;server&gt;/&lt;char&gt;/).
    /// </summary>
    internal sealed class CharacterState
    {
        public List<ChatHistoryEntry> ChatHistory { get; set; } = new List<ChatHistoryEntry>();
        public List<uint> PinnedContainerSerials { get; set; } = new List<uint>();
        public Dictionary<uint, GridContainerSize> GridContainerSizes { get; set; } = new Dictionary<uint, GridContainerSize>();
        public Dictionary<uint, bool> ContainerGridStates { get; set; } = new Dictionary<uint, bool>();
        public uint GrabBagSerial { get; set; }

        public static CharacterState From(Profile p)
        {
            return new CharacterState
            {
                ChatHistory = p.ChatHistory ?? new List<ChatHistoryEntry>(),
                PinnedContainerSerials = p.PinnedContainerSerials ?? new List<uint>(),
                GridContainerSizes = p.GridContainerSizes ?? new Dictionary<uint, GridContainerSize>(),
                ContainerGridStates = p.ContainerGridStates ?? new Dictionary<uint, bool>(),
                GrabBagSerial = p.GrabBagSerial
            };
        }

        public void ApplyTo(Profile p)
        {
            p.ChatHistory = ChatHistory ?? new List<ChatHistoryEntry>();
            p.PinnedContainerSerials = PinnedContainerSerials ?? new List<uint>();
            p.GridContainerSizes = GridContainerSizes ?? new Dictionary<uint, GridContainerSize>();
            p.ContainerGridStates = ContainerGridStates ?? new Dictionary<uint, bool>();
            p.GrabBagSerial = GrabBagSerial;
        }
    }
}
