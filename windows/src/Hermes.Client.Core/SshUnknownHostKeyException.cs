namespace Hermes.Client.Core;

/// <summary>
/// The server presented an SSH host key that is not pinned for this host.
/// The UI catches this, shows the fingerprint to the user, and — on approval —
/// pins it via <see cref="ISshHostKeyTrust"/> and retries the connection.
/// <see cref="KeyChanged"/> distinguishes a first-seen key from a changed one
/// (possible man-in-the-middle): the UI must warn louder in the latter case.
/// </summary>
public sealed class SshUnknownHostKeyException : Exception
{
    public string Host { get; }
    public int Port { get; }
    public string Fingerprint { get; }
    public bool KeyChanged { get; }

    public SshUnknownHostKeyException(
        string host, int port, string fingerprint, bool keyChanged, Exception? inner = null)
        : base(keyChanged
            ? $"SSH host key for {host}:{port} CHANGED (possible man-in-the-middle). " +
              $"Presented fingerprint: {fingerprint}."
            : $"Unknown SSH host key for {host}:{port}. Presented fingerprint: {fingerprint}.",
            inner)
    {
        Host = host;
        Port = port;
        Fingerprint = fingerprint;
        KeyChanged = keyChanged;
    }
}
