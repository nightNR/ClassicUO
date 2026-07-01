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
