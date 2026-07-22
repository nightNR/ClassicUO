// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Configuration
{
    internal enum FillOrder
    {
        ColumnMajor = 0,
        RowMajor = 1
    }

    internal sealed class PluginAnchorGroupDef
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public FillOrder Fill { get; set; } = FillOrder.ColumnMajor;
        public int X { get; set; }
        public int Y { get; set; }
        public bool Locked { get; set; }
    }
}
