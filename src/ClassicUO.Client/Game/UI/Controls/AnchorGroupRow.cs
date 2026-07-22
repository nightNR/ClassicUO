// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    /// <summary>
    /// One editable row for a <see cref="PluginAnchorGroupDef"/> inside the
    /// Options "Anchor groups" editor. Mirrors <see cref="AliasEntryControl"/>
    /// / <see cref="StatusbarColorControl"/>: raw child controls, edits are
    /// committed onto the def explicitly (via <see cref="Commit"/>) rather
    /// than live, because Id/Columns/Rows need parse + clamp + de-dup
    /// validation that happens once on Options Apply.
    /// </summary>
    internal sealed class AnchorGroupRow : Control
    {
        private readonly StbTextBox _idBox;
        private readonly StbTextBox _labelBox;
        private readonly StbTextBox _columnsBox;
        private readonly StbTextBox _rowsBox;
        private readonly Combobox _fillBox;
        private readonly PluginAnchorGroupDef _def;

        public AnchorGroupRow(Gump gump, PluginAnchorGroupDef def)
        {
            _def = def;

            _idBox = new StbTextBox(0xFF, 6, 45) { X = 2, Y = 0, Width = 41, Height = 26, NumbersOnly = true };
            _idBox.SetText(def.Id == 0 ? "" : def.Id.ToString());

            _labelBox = new StbTextBox(0xFF, 24, 130) { X = 52, Y = 0, Width = 126, Height = 26 };
            _labelBox.SetText(def.Label ?? "");

            _columnsBox = new StbTextBox(0xFF, 3, 36) { X = 187, Y = 0, Width = 36, Height = 26, NumbersOnly = true };
            _columnsBox.SetText(def.Columns.ToString());

            _rowsBox = new StbTextBox(0xFF, 3, 36) { X = 228, Y = 0, Width = 36, Height = 26, NumbersOnly = true };
            _rowsBox.SetText(def.Rows.ToString());

            _fillBox = new Combobox(270, 0, 85, new[] { "Column", "Row" }, (int) def.Fill);

            NiceButton deleteButton = new NiceButton(360, 0, 60, 25, ButtonAction.Activate, ResGumps.Delete) { ButtonParameter = 999 };
            deleteButton.MouseUp += (sender, e) =>
            {
                Dispose();
                ((DataBox) Parent)?.ReArrangeChildren();
            };

            Add(new ResizePic(0x0BB8) { X = 0, Y = 0, Width = 45, Height = 26 });
            Add(new ResizePic(0x0BB8) { X = 50, Y = 0, Width = 130, Height = 26 });
            Add(new ResizePic(0x0BB8) { X = 185, Y = 0, Width = 80, Height = 26 });
            Add(_idBox);
            Add(_labelBox);
            Add(_columnsBox);
            Add(_rowsBox);
            Add(_fillBox);
            Add(deleteButton);

            Width = 420;
            Height = 26;
        }

        public PluginAnchorGroupDef Def => _def;

        /// <summary>
        /// Parses/clamps the row's controls onto <see cref="Def"/>. Called
        /// from OptionsGump on Apply; validation (drop Id==0, drop later
        /// duplicate Ids) happens at the call site across all live rows.
        /// </summary>
        public void Commit()
        {
            _def.Id = int.TryParse(_idBox.Text, out int id) ? id : 0;
            _def.Label = _labelBox.Text ?? "";
            _def.Columns = int.TryParse(_columnsBox.Text, out int cols) && cols >= 1 ? cols : 1;
            _def.Rows = int.TryParse(_rowsBox.Text, out int rows) && rows >= 1 ? rows : 1;
            _def.Fill = (FillOrder) _fillBox.SelectedIndex;
        }
    }
}
