namespace SaveHarbor.App.Domain;

// Current is the lock that holds the world after the attempt: the requested one when acquired, otherwise the holder's.
public sealed record CloudLockAttempt(bool Acquired, CloudSessionLock Current);
