using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// What a failed SaveHarbor Cloud request means for the player.
public static class SupabaseErrorMapper
{
    public const string PausedProjectHint =
        " If nobody has used the group's cloud for a week, the project may be paused: the group owner can restore it in the Supabase dashboard.";

    public static (AppErrorCode Code, string Message) Describe(SupabaseException exception) => exception.Code switch
    {
        SupabaseErrorCodes.NotSignedIn or SupabaseErrorCodes.Unauthorized =>
            (AppErrorCode.CloudAuth, "Your sign-in expired. Sign in again to continue."),
        SupabaseErrorCodes.OtpExpired =>
            (AppErrorCode.CloudAuth, "This code is wrong or has expired. Request a new code."),
        SupabaseErrorCodes.NotMember =>
            (AppErrorCode.CloudAuth, "You are no longer a member of this group. Choose another group."),
        SupabaseErrorCodes.NotOwner =>
            (AppErrorCode.InvalidOperation, "Only the group owner can do this."),
        SupabaseErrorCodes.InviteInvalid =>
            (AppErrorCode.InvalidOperation, "No group has this invite code. Check it with the group owner."),
        SupabaseErrorCodes.GroupFull =>
            (AppErrorCode.InvalidOperation, "This group is full."),
        SupabaseErrorCodes.GroupLimit =>
            (AppErrorCode.InvalidOperation, "You have created the maximum number of groups."),
        SupabaseErrorCodes.OwnerMustStay =>
            (AppErrorCode.InvalidOperation, "The owner cannot leave while others are in the group."),
        SupabaseErrorCodes.GroupNotEmpty =>
            (AppErrorCode.InvalidOperation, "Remove the group's shared worlds before leaving it."),
        SupabaseErrorCodes.VersionConflict =>
            (AppErrorCode.CloudConflict, "Someone shared a newer version while you were uploading. Download it first."),
        SupabaseErrorCodes.LockLost =>
            (AppErrorCode.CloudConflict, "Your session lock expired and another player took over."),
        SupabaseErrorCodes.LockHeld =>
            (AppErrorCode.CloudConflict, "Someone is playing this world right now. Try again when they finish."),
        SupabaseErrorCodes.ArchiveMissing or SupabaseErrorCodes.NotFound =>
            (AppErrorCode.CloudArchiveMissing, "The world's archive is missing from the group's cloud."),
        SupabaseErrorCodes.WorldMissing =>
            (AppErrorCode.CloudArchiveMissing, "This world is no longer in the group's cloud."),
        SupabaseErrorCodes.PayloadTooLarge =>
            (AppErrorCode.CloudProviderFailure, "This world's archive is larger than the group's cloud allows (45 MB)."),
        SupabaseErrorCodes.RateLimited or SupabaseErrorCodes.EmailRateLimit or SupabaseErrorCodes.RequestRateLimit =>
            (AppErrorCode.CloudProviderFailure, exception.RetryAfter is { TotalSeconds: > 0 } wait
                ? $"Too many attempts. Try again in {Math.Ceiling(wait.TotalSeconds):0} seconds."
                : "Too many attempts. Wait a minute and try again."),
        SupabaseErrorCodes.Unreachable or SupabaseErrorCodes.ServerError =>
            (AppErrorCode.CloudProviderFailure, "SaveHarbor cannot reach the group's cloud. Check your connection."
                + (exception.HostedProject ? PausedProjectHint : string.Empty)),
        SupabaseErrorCodes.NotConfigured =>
            (AppErrorCode.CloudNotConnected, "SaveHarbor Cloud is not set up in this version of the app."),
        SupabaseErrorCodes.InvalidInput =>
            (AppErrorCode.InvalidData, "The group's cloud did not accept this name or value. Check it and try again."),
        SupabaseErrorCodes.InvalidData =>
            (AppErrorCode.InvalidData, "The group's cloud sent data SaveHarbor cannot use. Nothing was changed."),
        _ =>
            (AppErrorCode.CloudProviderFailure, "The group's cloud refused the request. Try again; if it keeps failing, check the log.")
    };

    public static bool IsRetryable(SupabaseException exception) =>
        exception.Code is SupabaseErrorCodes.Unreachable or SupabaseErrorCodes.ServerError
            or SupabaseErrorCodes.RateLimited or SupabaseErrorCodes.EmailRateLimit or SupabaseErrorCodes.RequestRateLimit;
}
