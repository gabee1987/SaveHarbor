namespace SaveHarbor.App.Services;

public interface IDeviceIdentity
{
    // Stable id of this installation; session locks use it to recognise their own holder.
    string DeviceId { get; }

    // This PC's name as friends see it.
    string DeviceLabel { get; }
}
