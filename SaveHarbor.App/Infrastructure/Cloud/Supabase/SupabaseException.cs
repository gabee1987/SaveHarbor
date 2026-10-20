using System.Net;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// A failed SaveHarbor Cloud request. Code is an error code from the database functions (see the migration header), an
// Auth error code, or one of SupabaseErrorCodes derived from the HTTP status. The message never contains tokens,
// codes or request bodies.
public sealed class SupabaseException(
    string code,
    HttpStatusCode? status = null,
    TimeSpan? retryAfter = null,
    bool hostedProject = false,
    Exception? innerException = null)
    : Exception(status is null ? $"SaveHarbor Cloud request failed: {code}" : $"SaveHarbor Cloud request failed: {code} (HTTP {(int)status})", innerException)
{
    public string Code { get; } = code;

    public HttpStatusCode? Status { get; } = status;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    // The project is hosted by Supabase, where an unused free project is paused after a week.
    public bool HostedProject { get; } = hostedProject;
}

public static class SupabaseErrorCodes
{
    public const string NotConfigured = "not_configured";
    public const string NotSignedIn = "not_signed_in";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string PayloadTooLarge = "payload_too_large";
    public const string RateLimited = "rate_limited";
    public const string ServerError = "server_error";
    public const string Unreachable = "unreachable";
    public const string BadRequest = "bad_request";
    public const string InvalidData = "invalid_data";

    // Raised by the database functions.
    public const string NotMember = "not_member";
    public const string NotOwner = "not_owner";
    public const string InvalidInput = "invalid_input";
    public const string InviteInvalid = "invite_invalid";
    public const string GroupFull = "group_full";
    public const string GroupLimit = "group_limit";
    public const string OwnerMustStay = "owner_must_stay";
    public const string GroupNotEmpty = "group_not_empty";
    public const string VersionConflict = "version_conflict";
    public const string ArchiveMissing = "archive_missing";
    public const string WorldMissing = "world_missing";
    public const string LockHeld = "lock_held";
    public const string LockLost = "lock_lost";

    // Raised by Supabase Auth.
    public const string OtpExpired = "otp_expired";
    public const string EmailRateLimit = "over_email_send_rate_limit";
    public const string RequestRateLimit = "over_request_rate_limit";
}
