namespace Deliverance.Core;

public sealed class SaveDiagnostics
{
    public event Action<string>? Info;
    public event Action<string>? Warning;
    public event Action<string, Exception>? Error;

    internal void EmitInfo(string msg) => Info?.Invoke(msg);
    internal void EmitWarning(string msg) => Warning?.Invoke(msg);
    internal void EmitError(string msg, Exception ex) => Error?.Invoke(msg, ex);
}
