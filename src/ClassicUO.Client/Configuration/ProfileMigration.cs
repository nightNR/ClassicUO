// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Configuration
{
    /// <summary>
    /// One-time migration from the legacy layout (Profiles/&lt;account&gt;/&lt;server&gt;/&lt;char&gt;/) to named profiles.
    /// Every legacy character becomes its own named profile + an assignment, so behaviour stays identical.
    /// Non-destructive: legacy folders are kept (and backed up first); the marker is written last, so an interrupted
    /// run is simply repeated. Until the marker exists the client keeps resolving the legacy folders.
    /// </summary>
    internal static class ProfileMigration
    {
        public static bool RunIfNeeded(string root)
        {
            if (ProfileCatalog.IsMigrated(root))
            {
                return true;
            }

            try
            {
                Directory.CreateDirectory(root);
                int count = Migrate(root);
                File.WriteAllText(Path.Combine(root, ProfileCatalog.MarkerFile), "2");
                Log.Info($"Profile migration done: {count} character profile(s) migrated to named profiles.");

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Profile migration failed, keeping legacy per-character profiles: {e}");

                return false;
            }
        }

        private static int Migrate(string root)
        {
            List<(string user, string server, string character, string dir)> found = FindLegacyCharacters(root);

            string tmp = Path.Combine(root, ProfileCatalog.NamedFolder + ".tmp");
            string named = ProfileCatalog.NamedRoot(root);

            if (Directory.Exists(tmp))
            {
                Directory.Delete(tmp, true);
            }

            if (found.Count == 0)
            {
                return 0;
            }

            BackupRoot(root);

            Directory.CreateDirectory(tmp);

            var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((string user, string server, string character, string dir) in found)
            {
                Profile profile;

                try
                {
                    profile = ConfigurationResolver.Load(Path.Combine(dir, "profile.json"), ProfileJsonContext.DefaultToUse.Profile);
                }
                catch (Exception e)
                {
                    Log.Warn($"Skipping unreadable profile of {character} ({dir}): {e.Message}");

                    continue;
                }

                if (profile == null)
                {
                    continue;
                }

                string name = ProfileCatalog.UniqueName(taken, user, server, character);
                taken.Add(name);

                string target = Path.Combine(tmp, name);
                Directory.CreateDirectory(target);

                // character-bound data goes to the character folder (never overwrite an existing state file)
                string statePath = Path.Combine(dir, "character_state.json");

                if (!File.Exists(statePath))
                {
                    ConfigurationResolver.Save(CharacterState.From(profile), statePath, ProfileJsonContext.DefaultToUse.CharacterState);
                }

                // the shared profile no longer carries per-character fields (they are JsonIgnore'd)
                ConfigurationResolver.Save(profile, Path.Combine(target, "profile.json"), ProfileJsonContext.DefaultToUse.Profile);

                foreach (string file in ProfileCatalog.ProfileFiles.Skip(1))
                {
                    string src = Path.Combine(dir, file);

                    if (File.Exists(src))
                    {
                        File.Copy(src, Path.Combine(target, file), true);
                    }
                }

                assignments[ProfileCatalog.AssignmentKey(user, server, character)] = name;
            }

            // publish: named folder, then assignments; marker (written by the caller) is the commit point
            if (Directory.Exists(named))
            {
                Directory.Delete(named, true);
            }

            Directory.Move(tmp, named);
            ProfileCatalog.SaveAssignments(root, assignments);

            return assignments.Count;
        }

        internal static List<(string user, string server, string character, string dir)> FindLegacyCharacters(string root)
        {
            var result = new List<(string, string, string, string)>();

            foreach (string userDir in SortedDirs(root))
            {
                string user = Path.GetFileName(userDir);

                if (user.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (string serverDir in SortedDirs(userDir))
                {
                    foreach (string charDir in SortedDirs(serverDir))
                    {
                        if (File.Exists(Path.Combine(charDir, "profile.json")))
                        {
                            result.Add((user, Path.GetFileName(serverDir), Path.GetFileName(charDir), charDir));
                        }
                    }
                }
            }

            return result;
        }

        private static IEnumerable<string> SortedDirs(string path)
        {
            return Directory.GetDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
        }

        private static void BackupRoot(string root)
        {
            string trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string backup = $"{trimmed}_backup_{DateTime.Now:yyyyMMdd_HHmmss}";

            CopyDirectory(trimmed, backup);
            Log.Info($"Profiles backed up to {backup}");
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);

            foreach (string file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            }

            foreach (string dir in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(dir);

                if (name.StartsWith(ProfileCatalog.NamedFolder, StringComparison.Ordinal))
                {
                    continue;
                }

                CopyDirectory(dir, Path.Combine(dst, name));
            }
        }
    }
}
