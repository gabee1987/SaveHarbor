using SaveHarbor.App.Infrastructure.Cloud.Supabase;

namespace SaveHarbor.App.Infrastructure;

// saveharbor://join/<CODE>: opens the Sharing page with the group's invite code filled in. Any web page can start such
// a link, so only a well-formed invite code is taken from it, and joining still needs the player's click.
public static class InviteLink
{
    public const string Scheme = "saveharbor";

    public static string Format(string inviteCode) => $"{Scheme}://join/{SupabaseGroupService.NormalizeInviteCode(inviteCode)}";

    public static bool TryParse(string? argument, out string inviteCode)
    {
        inviteCode = string.Empty;
        if (argument is not { Length: > 0 and <= 200 }
            || !Uri.TryCreate(argument.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "join", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length > 0)
        {
            return false;
        }

        var segment = uri.AbsolutePath.Trim('/');
        var code = SupabaseGroupService.NormalizeInviteCode(segment);
        if (code.Length != SupabaseGroupService.InviteCodeLength || !string.Equals(code, segment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        inviteCode = code;
        return true;
    }

    public static string? Find(IReadOnlyList<string> args) =>
        args.Select(argument => TryParse(argument, out var code) ? code : null).FirstOrDefault(code => code is not null);
}
