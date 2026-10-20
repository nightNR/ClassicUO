// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.PluginApi;

/// <summary>Outcome of <see cref="IProfiles.Switch"/>.</summary>
public enum ProfileSwitchResult
{
    /// <summary>The profile was switched immediately (in game).</summary>
    Applied,

    /// <summary>Not in game yet: stored as an override for the next login.</summary>
    QueuedForLogin,

    /// <summary>No profile with that name exists in the client.</summary>
    UnknownProfile,

    /// <summary>The client could not switch (profile failed to load, not logged in, or the client does not support profiles).</summary>
    Failed,
}

/// <summary>Account/server/character the player picked on the login screen.</summary>
public readonly record struct CharacterSelection(string Account, string Server, string Character);

/// <summary>
/// The client's named profiles. The client owns the character -> profile assignments; a plugin (e.g. a macro/assistant
/// with its own profiles) can only ask for a profile, either as a session override or by also assigning it to the
/// current character.
/// </summary>
public interface IProfiles
{
    /// <summary>Name of the active profile, or null when none/unknown (legacy per-character profile, older client).</summary>
    string? Active { get; }

    /// <summary>Names of all client profiles. Game thread only.</summary>
    IReadOnlyList<string> List();

    /// <summary>
    /// Activates a client profile. In game it is applied immediately (UI is rebuilt); before login it is queued for
    /// the next login. <paramref name="assignToCharacter"/> also stores it as the current character's profile
    /// (ignored before login); otherwise it is a session override cleared on logout. Game thread only.
    /// </summary>
    ProfileSwitchResult Switch(string name, bool assignToCharacter = false);

    /// <summary>Drops a queued/session override so the character's assigned profile applies again. Game thread only.</summary>
    void ClearOverride();

    /// <summary>The active profile changed (login load or live switch). Argument: new profile name.</summary>
    event Action<string>? Changed;

    /// <summary>
    /// A character was picked on the login screen, before its profile is resolved: the moment to call
    /// <see cref="Switch"/> if the plugin wants a specific profile for this login.
    /// </summary>
    event Action<CharacterSelection>? CharacterSelected;

    /// <summary>A named profile was created, renamed or deleted; re-read <see cref="List"/>.</summary>
    event Action? ListChanged;
}
