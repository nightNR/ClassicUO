// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Configuration
{
    internal static class ProfileManager
    {
        public static GlobalProfile GlobalProfile { get; private set; }
        public static Profile CurrentProfile { get; private set; }
        /// <summary>Folder of the active (possibly shared) profile: profile.json, macros, infobar, skills groups, ...</summary>
        public static string ProfilePath { get; private set; }

        /// <summary>Per-character folder: gumps.xml and character_state.json (serial-bound data that is never shared).</summary>
        public static string CharacterPath { get; private set; }

        /// <summary>Name of the active named profile, or null while the legacy per-character folder is in use.</summary>
        public static string ActiveProfileName { get; private set; }

        /// <summary>Set by a plugin (e.g. Phoenix) to force a named profile for the next load; wins over the assignment table.</summary>
        public static string OverrideProfileName { get; set; }

        private static string _rootPath;

        internal static void ResetRootCache() => _rootPath = null;
        internal static string RootPath
        {
            get
            {
                if (string.IsNullOrEmpty(_rootPath))
                {
                    if (string.IsNullOrWhiteSpace(Settings.GlobalSettings.ProfilesPath))
                    {
                        _rootPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles");
                    }
                    else
                    {
                        _rootPath = Settings.GlobalSettings.ProfilesPath;
                    }
                }

                return _rootPath;
            }
        }

        public static void Load(string servername, string username, string charactername)
        {
            GlobalProfile = ConfigurationResolver.Load<GlobalProfile>(Path.Combine(RootPath, "globalprofile.json"), ProfileJsonContext.DefaultToUse.GlobalProfile) ?? new GlobalProfile();

            string charPath = FileSystemHelper.CreateFolderIfNotExists(RootPath, username, servername, charactername);
            string path = ResolveProfileDir(username, servername, charactername, charPath, out string profileName);
            string fileToLoad = Path.Combine(path, "profile.json");

            // Build the new profile completely before publishing it, so a broken file leaves the active profile untouched.
            Profile loaded = ConfigurationResolver.Load<Profile>(fileToLoad, ProfileJsonContext.DefaultToUse.Profile) ?? NewFromDefault();

            // per-character data; when the file is missing the values read from a legacy profile.json stay in place
            CharacterState state = ConfigurationResolver.Load<CharacterState>(Path.Combine(charPath, "character_state.json"), ProfileJsonContext.DefaultToUse.CharacterState);
            state?.ApplyTo(loaded);

            loaded.Username = username;
            loaded.ServerName = servername;
            loaded.CharacterName = charactername;

            ValidateFields(loaded);

            // New per-profile atlas state, then migrate legacy ChatFont/TooltipFont
            // slot bytes into LocationFonts (once — only if not already present).
            ClassicUO.Resources.AtlasFontRegistry.Reset();
            MigrateLegacyFont(loaded, FontLocation.Chat, loaded.ChatFont);
            MigrateLegacyFont(loaded, FontLocation.Tooltip, loaded.TooltipFont);

            // Reset() just freed every dynamic slot, so any legacy byte pointing at a
            // dynamic TTF slot from a previous session is now stale/unregistered.
            // Re-resolve through LocationFonts (which re-registers the slot via
            // TryResolve) and re-sync the legacy bytes so renderers that still read
            // ChatFont/TooltipFont directly see a live slot.
            loaded.ChatFont = loaded.ResolveFont(FontLocation.Chat).slot;
            loaded.TooltipFont = loaded.ResolveFont(FontLocation.Tooltip).slot;

            CharacterPath = charPath;
            ProfilePath = path;
            ActiveProfileName = profileName;
            CurrentProfile = loaded;
        }

        /// <summary>
        /// Folder holding the shared profile for a character: plugin override, then the assignment table. A character
        /// without a profile gets a new named one (from default.json) unless it still has a legacy profile.json, in
        /// which case the legacy folder is used (also when the migration could not run).
        /// </summary>
        private static string ResolveProfileDir(string username, string servername, string charactername, string charPath, out string profileName)
        {
            profileName = null;

            if (!ProfileMigration.RunIfNeeded(RootPath))
            {
                return charPath;
            }

            Dictionary<string, string> assignments = ProfileCatalog.LoadAssignments(RootPath);
            string dir = ProfileCatalog.Resolve(RootPath, assignments, username, servername, charactername, OverrideProfileName, out profileName);

            if (dir != null)
            {
                return dir;
            }

            if (File.Exists(Path.Combine(charPath, "profile.json")))
            {
                return charPath;
            }

            var taken = new HashSet<string>(ProfileCatalog.ListProfiles(RootPath), StringComparer.OrdinalIgnoreCase);
            profileName = ProfileCatalog.UniqueName(taken, username, servername, charactername);
            assignments[ProfileCatalog.AssignmentKey(username, servername, charactername)] = profileName;
            ProfileCatalog.SaveAssignments(RootPath, assignments);

            return Directory.CreateDirectory(ProfileCatalog.ProfileDir(RootPath, profileName)).FullName;
        }

        internal static void SaveCharacterState(Profile profile)
        {
            if (!string.IsNullOrEmpty(CharacterPath))
            {
                ConfigurationResolver.Save(CharacterState.From(profile), Path.Combine(CharacterPath, "character_state.json"), ProfileJsonContext.DefaultToUse.CharacterState);
            }
        }

        private static void MigrateLegacyFont(Profile p, string location, byte legacySlot)
        {
            p.LocationFonts ??= new System.Collections.Generic.Dictionary<string, FontChoice>();
            if (p.LocationFonts.ContainsKey(location))
            {
                return; // already migrated / user-set
            }

            (string face, byte size)? mapped = ClassicUO.Resources.AtlasFontRegistry.LegacySlotToFaceSize(legacySlot);
            if (mapped.HasValue)
            {
                p.LocationFonts[location] = new FontChoice { Face = mapped.Value.face, Size = mapped.Value.size };
            }
            // else: legacy classic slot (< 20) -> leave absent => Classic fallback.
        }

        public static void SetProfileAsDefault(Profile profile)
        {
            Save(profile, RootPath, "default.json");
        }

        public static Profile NewFromDefault()
        {
            Profile profile = ConfigurationResolver.Load<Profile>(Path.Combine(RootPath, "default.json"), ProfileJsonContext.DefaultToUse.Profile) ?? new Profile();

            // an old default.json may still carry another character's data
            new CharacterState().ApplyTo(profile);

            return profile;
        }

        private static void ValidateFields(Profile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(profile.ServerName))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.Username))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.CharacterName))
            {
                throw new InvalidDataException();
            }

            if (profile.WindowClientBounds.X < 600)
            {
                profile.WindowClientBounds = new Point(600, profile.WindowClientBounds.Y);
            }

            if (profile.WindowClientBounds.Y < 480)
            {
                profile.WindowClientBounds = new Point(profile.WindowClientBounds.X, 480);
            }
        }

        public static void UnLoadProfile()
        {
            GlobalProfile = null;
            CurrentProfile = null;
        }

        internal static void Save(Profile profile, string path, string filename = "profile.json")
        {
            ConfigurationResolver.Save(profile, Path.Combine(path, filename), ProfileJsonContext.DefaultToUse.Profile);
            if (GlobalProfile != null)
            {
                ConfigurationResolver.Save(GlobalProfile, Path.Combine(RootPath, "globalprofile.json"), ProfileJsonContext.DefaultToUse.GlobalProfile);
            }
        }
    }
}
