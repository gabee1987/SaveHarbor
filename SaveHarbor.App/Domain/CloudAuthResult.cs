namespace SaveHarbor.App.Domain;

// Outcome of a sign-in step. NeedsCode: a code was sent and the next step is entering it.
public sealed record CloudAuthResult(bool IsSuccess, string Message, bool NeedsCode = false);
