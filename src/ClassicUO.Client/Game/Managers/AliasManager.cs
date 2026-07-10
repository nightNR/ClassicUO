// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal sealed class AliasEntry
    {
        public uint Serial { get; set; }
        public string Alias { get; set; }
        public bool Global { get; set; }
    }
}
