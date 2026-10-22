namespace SaveHarbor.App.Domain;

public sealed record CloudConnectionResult(
    bool IsSuccess,
    CloudConnectionStatus Status,
    string Message);
