using System.Globalization;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public sealed record ImportVerdict(string Message, bool IsWarning);

// Explains, before anything is changed, what importing a world will do to the copy already on this computer.
public static class ImportComparison
{
    public const string SafetyNote = "Your current copy is backed up first, and the imported file is kept as a backup too, so both can be restored later.";

    public static ImportVerdict Describe(ImportCandidate incoming, ImportCandidate? local, bool localExists)
    {
        if (!localExists)
        {
            return new ImportVerdict("This adds a new world to this computer.", false);
        }

        if (local is null)
        {
            return new ImportVerdict("A world with this name already exists here, but SaveHarbor could not read it. It will be backed up, then replaced.", true);
        }

        if (incoming.Fingerprint is not null && local.Fingerprint is not null
            && !string.Equals(incoming.Fingerprint, local.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new ImportVerdict($"WARNING: \"{local.World.WorldName}\" on this computer is a DIFFERENT world that has the same name. It will be backed up, then replaced.", true);
        }

        var order = Compare(incoming, local);
        return order switch
        {
            > 0 => new ImportVerdict($"Same world. The imported copy is NEWER than yours{Detail(incoming, local)}.", false),
            < 0 => new ImportVerdict($"WARNING: Same world, but the imported copy is OLDER than yours{Detail(incoming, local)}. Your progress since then would be replaced.", true),
            _ => new ImportVerdict($"Same world, same save point as yours{Detail(incoming, local)}.", false)
        };
    }

    private static int Compare(ImportCandidate incoming, ImportCandidate local)
    {
        if (incoming.Revision is { } incomingRevision && local.Revision is { } localRevision)
        {
            return incomingRevision.CompareTo(localRevision);
        }

        return Nullable.Compare(incoming.SavedAtUtc, local.SavedAtUtc);
    }

    private static string Detail(ImportCandidate incoming, ImportCandidate local) =>
        incoming.Revision is { } incomingRevision && local.Revision is { } localRevision
            ? $" (save revision {incomingRevision.ToString(CultureInfo.InvariantCulture)} vs. yours {localRevision.ToString(CultureInfo.InvariantCulture)})"
            : incoming.SavedAtUtc is { } incomingSaved && local.SavedAtUtc is { } localSaved
                ? $" (saved {Format(incomingSaved)} vs. yours {Format(localSaved)})"
                : string.Empty;

    private static string Format(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
