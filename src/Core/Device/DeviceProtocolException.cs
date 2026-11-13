namespace GozcuStandalone.Device;

public sealed class DeviceProtocolException : Exception
{
    public DeviceProtocolException(string message) : base(message) { }
    public DeviceProtocolException(string message, Exception inner) : base(message, inner) { }
}
