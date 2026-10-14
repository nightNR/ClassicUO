// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Configuration
{
    /// <summary>
    /// Named profiles (Profiles/_named/&lt;name&gt;/) and the character -> profile assignment table
    /// (Profiles/assignments.json). All methods take the profiles root so they do not depend on <c>Settings</c>.
    /// </summary>
    internal static class ProfileCatalog
    {
        public const string NamedFolder = "_named";
        public const string AssignmentsFile = "assignments.json";
        public const string MarkerFile = "profiles_v2.marker";

        public const string GumpsFile = "gumps.xml";

        /// <summary>
        /// Files that make up a shared profile. gumps.xml here is the layout part only; serial-bound gumps and
        /// character_state.json stay with the character.
        /// </summary>
        public static readonly string[] ProfileFiles =
        {
            "profile.json", "macros.xml", "infobar.xml", "skillsgroups.xml", "statusbar_colors.xml", "ignore_list.xml", GumpsFile
        };

        public static string NamedRoot(string root) => Path.Combine(root, NamedFolder);

        public static string ProfileDir(string root, string name) => Path.Combine(NamedRoot(root), name);

        public static bool IsMigrated(string root) => File.Exists(Path.Combine(root, MarkerFile));

        public static string AssignmentKey(string user, string server, string character) => $"{user}|{server}|{character}";

        public static Dictionary<string, string> LoadAssignments(string root)
        {
            Dictionary<string, string> loaded = null;

            try
            {
                loaded = ConfigurationResolver.Load(Path.Combine(root, AssignmentsFile), ProfileJsonContext.DefaultToUse.DictionaryStringString);
            }
            catch (Exception e)
            {
                Log.Error($"Cannot read {AssignmentsFile}: {e.Message}");
            }

            return new Dictionary<string, string>(loaded ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static void SaveAssignments(string root, Dictionary<string, string> assignments)
        {
            string file = Path.Combine(root, AssignmentsFile);
            string tmp = file + ".tmp";

            ConfigurationResolver.Save(new Dictionary<string, string>(assignments), tmp, ProfileJsonContext.DefaultToUse.DictionaryStringString);
            File.Move(tmp, file, true);
        }

        public static IEnumerable<string> ListProfiles(string root)
        {
            string named = NamedRoot(root);

            return Directory.Exists(named)
                ? Directory.GetDirectories(named).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
        }

        public static bool Exists(string root, string name)
        {
            return !string.IsNullOrWhiteSpace(name) && Directory.Exists(ProfileDir(root, SanitizeName(name)));
        }

        public static int AssignedCount(string root, string name)
        {
            return LoadAssignments(root).Values.Count(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Characters (account|server|character keys) currently assigned to <paramref name="name"/>.</summary>
        public static List<string> AssignedCharacters(string root, string name)
        {
            return LoadAssignments(root).Where(kv => string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Copies the shared files of <paramref name="source"/> into a new profile. False if source is missing or target exists.</summary>
        public static bool Duplicate(string root, string source, string target)
        {
            target = SanitizeName(target);

            if (!Exists(root, source) || Exists(root, target))
            {
                return false;
            }

            string src = ProfileDir(root, SanitizeName(source));
            string dst = ProfileDir(root, target);
            Directory.CreateDirectory(dst);

            foreach (string file in ProfileFiles)
            {
                if (File.Exists(Path.Combine(src, file)))
                {
                    File.Copy(Path.Combine(src, file), Path.Combine(dst, file), true);
                }
            }

            return true;
        }

        /// <summary>Renames a profile and re-points its assignments. False if missing or the new name is taken.</summary>
        public static bool Rename(string root, string oldName, string newName)
        {
            newName = SanitizeName(newName);

            if (!Exists(root, oldName) || (Exists(root, newName) && !string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            oldName = SanitizeName(oldName);
            string from = ProfileDir(root, oldName);
            string to = ProfileDir(root, newName);

            if (!string.Equals(from, to, StringComparison.Ordinal))
            {
                // two-step move so a case-only rename also works on case-insensitive file systems
                string tmp = from + ".rename";
                Directory.Move(from, tmp);
                Directory.Move(tmp, to);
            }

            Dictionary<string, string> assignments = LoadAssignments(root);

            foreach (string key in assignments.Where(kv => string.Equals(kv.Value, oldName, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
            {
                assignments[key] = newName;
            }

            SaveAssignments(root, assignments);

            return true;
        }

        /// <summary>Deletes a profile that no character is assigned to.</summary>
        public static bool Delete(string root, string name)
        {
            if (!Exists(root, name) || AssignedCount(root, name) > 0)
            {
                return false;
            }

            Directory.Delete(ProfileDir(root, SanitizeName(name)), true);

            return true;
        }

        public static string SanitizeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string s = string.Concat((name ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c)).Trim('.', ' ');

            return s.Length == 0 ? "profile" : s;
        }

        /// <summary>Picks a profile name not in <paramref name="taken"/>, preferring the character name, then adding server/account.</summary>
        public static string UniqueName(ISet<string> taken, string user, string server, string character)
        {
            string baseName = SanitizeName(character);

            foreach (string candidate in new[] { baseName, SanitizeName($"{character} ({server})"), SanitizeName($"{character} ({server}, {user})") })
            {
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }

            for (int i = 2; ; i++)
            {
                string candidate = SanitizeName($"{character} ({server}, {user}) {i}");

                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>
        /// Resolves the profile folder for a character: explicit override, then the assignment table.
        /// Returns null when the character has no (valid) named profile.
        /// </summary>
        public static string Resolve(string root, IReadOnlyDictionary<string, string> assignments, string user, string server, string character, string overrideName, out string name)
        {
            foreach (string candidate in new[] { overrideName, assignments.TryGetValue(AssignmentKey(user, server, character), out string a) ? a : null })
            {
                if (Exists(root, candidate))
                {
                    name = SanitizeName(candidate);

                    return ProfileDir(root, name);
                }
            }

            name = null;

            return null;
        }
    }
}
