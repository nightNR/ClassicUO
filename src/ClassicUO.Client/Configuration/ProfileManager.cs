// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Configuration
{
    internal static class ProfileManager
    {
        public static GlobalProfile GlobalProfile { get; private set; }
        public static Profile CurrentProfile { get; private set; }
        public static string ProfilePath { get; private set; }

        private static string _rootPath;
        private static string RootPath
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

            string path = FileSystemHelper.CreateFolderIfNotExists(RootPath, username, servername, charactername);
            string fileToLoad = Path.Combine(path, "profile.json");

            ProfilePath = path;
            CurrentProfile = ConfigurationResolver.Load<Profile>(fileToLoad, ProfileJsonContext.DefaultToUse.Profile) ?? NewFromDefault();

            CurrentProfile.Username = username;
            CurrentProfile.ServerName = servername;
            CurrentProfile.CharacterName = charactername;

            ValidateFields(CurrentProfile);

            // New per-profile atlas state, then migrate legacy ChatFont/TooltipFont
            // slot bytes into LocationFonts (once — only if not already present).
            ClassicUO.Resources.AtlasFontRegistry.Reset();
            MigrateLegacyFont(CurrentProfile, FontLocation.Chat, CurrentProfile.ChatFont);
            MigrateLegacyFont(CurrentProfile, FontLocation.Tooltip, CurrentProfile.TooltipFont);

            // Reset() just freed every dynamic slot, so any legacy byte pointing at a
            // dynamic TTF slot from a previous session is now stale/unregistered.
            // Re-resolve through LocationFonts (which re-registers the slot via
            // TryResolve) and re-sync the legacy bytes so renderers that still read
            // ChatFont/TooltipFont directly see a live slot.
            CurrentProfile.ChatFont = CurrentProfile.ResolveFont(FontLocation.Chat).slot;
            CurrentProfile.TooltipFont = CurrentProfile.ResolveFont(FontLocation.Tooltip).slot;
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
            return ConfigurationResolver.Load<Profile>(Path.Combine(RootPath, "default.json"), ProfileJsonContext.DefaultToUse.Profile) ?? new Profile();
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
