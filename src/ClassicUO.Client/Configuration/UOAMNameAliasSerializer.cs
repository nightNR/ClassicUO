// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.Text;

namespace ClassicUO.Configuration
{
    internal static class UOAMNameAliasSerializer
    {
        public static Dictionary<string, string> Parse(string serialized)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(serialized))
            {
                return result;
            }

            foreach (string pair in serialized.Split(';'))
            {
                int eq = pair.IndexOf('=');

                if (eq <= 0 || eq == pair.Length - 1)
                {
                    continue;
                }

                string key = pair.Substring(0, eq).Trim();
                string value = pair.Substring(eq + 1).Trim();

                if (key.Length == 0 || value.Length == 0)
                {
                    continue;
                }

                result[key] = value;
            }

            return result;
        }

        public static string Serialize(IEnumerable<KeyValuePair<string, string>> aliases)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var kvp in aliases)
            {
                if (!first)
                {
                    sb.Append(';');
                }

                sb.Append(kvp.Key).Append('=').Append(kvp.Value);
                first = false;
            }

            return sb.ToString();
        }
    }
}
