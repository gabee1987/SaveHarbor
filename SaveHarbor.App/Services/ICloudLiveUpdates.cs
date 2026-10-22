namespace SaveHarbor.App.Services;

public enum CloudLiveState
{
    // Not connected on purpose: another provider, signed out or no group.
    Off,
    Connecting,
    // Friends' changes arrive within a second or two.
    Live,
    // The live connection dropped; the window checks the cloud every minute until it is back.
    Polling
}

// What this app tells the group while it runs. Only what the online dots and the companion page need.
public sealed record CloudPresence(string DisplayName, string Game, string? PlayingWorldId);

// A member who has SaveHarbor open right now. Everything but UserId was sent by that member's app and is only displayed.
public sealed record CloudPresenceEntry(string UserId, string DisplayName, string Game, string? PlayingWorldId);

// A row of the group changed. The row itself is not passed on: the window reads the cloud again instead of trusting it.
public sealed record CloudLiveChange(string Table, string EventType);

// Pushes the group's changes to the window instead of the window polling for them. Events are raised on a background
// thread.
public interface ICloudLiveUpdates
{
    CloudLiveState State { get; }

    IReadOnlyList<CloudPresenceEntry> Online { get; }

    event EventHandler<CloudLiveChange>? ChangeReceived;

    event EventHandler? PresenceChanged;

    event EventHandler? StateChanged;

    // Connects to the group, or only updates the presence when already connected to it. A null presence receives the
    // group's changes and who is online without showing this player as online.
    Task StartAsync(string groupId, CloudPresence? presence);

    Task StopAsync();
}
