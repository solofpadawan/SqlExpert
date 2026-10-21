using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// The single "Server name" box of the Database Properties dialog: <c>host</c>, <c>host\INSTANCE</c>,
/// <c>host,port</c> or <c>host\INSTANCE,port</c>, optionally with a protocol prefix (tcp:, np:, lpc:).
/// </summary>
public static class ServerNameText
{
    public const int DefaultPort = 1433;

    public sealed record Parts(string Server, string? Instance, int Port, NetworkProtocol? Protocol);

    /// <summary>Data-source prefix of a protocol ("" for Default).</summary>
    public static string Prefix(NetworkProtocol protocol) => protocol switch
    {
        NetworkProtocol.Tcp => "tcp:",
        NetworkProtocol.NamedPipes => "np:",
        NetworkProtocol.SharedMemory => "lpc:",
        _ => ""
    };

    /// <summary>Removes a leading tcp:/np:/lpc: prefix; returns the protocol it named (null = none).</summary>
    public static string StripPrefix(string? text, out NetworkProtocol? protocol)
    {
        var t = (text ?? "").Trim();
        protocol = null;
        foreach (var (prefix, p) in new[] { ("tcp:", NetworkProtocol.Tcp), ("np:", NetworkProtocol.NamedPipes), ("lpc:", NetworkProtocol.SharedMemory) })
        {
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                protocol = p;
                return t[prefix.Length..].Trim();
            }
        }
        return t;
    }

    /// <summary>
    /// Splits the box text. Throws <see cref="FormatException"/> for an invalid port.
    /// An empty host becomes "localhost".
    /// </summary>
    public static Parts Parse(string? text)
    {
        var t = StripPrefix(text, out var protocol);
        var port = DefaultPort;
        var comma = t.LastIndexOf(',');
        if (comma >= 0)
        {
            var p = t[(comma + 1)..].Trim();
            if (!int.TryParse(p, out port) || port is < 1 or > 65535)
                throw new FormatException($"Porta inválida: '{p}' (use 1–65535).");
            t = t[..comma].Trim();
        }

        string? instance = null;
        var slash = t.IndexOf('\\');
        if (slash >= 0)
        {
            instance = t[(slash + 1)..].Trim();
            if (instance.Length == 0) instance = null;
            t = t[..slash].Trim();
        }

        if (t.Length == 0) t = "localhost";
        return new Parts(t, instance, port, protocol);
    }

    /// <summary>Text of the box for a registration (no protocol prefix: the Protocol combo holds it).</summary>
    public static string Format(string? server, string? instance, int port)
    {
        var s = string.IsNullOrWhiteSpace(server) ? "localhost" : server.Trim();
        if (!string.IsNullOrWhiteSpace(instance)) s += "\\" + instance.Trim();
        if (port > 0 && port != DefaultPort) s += "," + port;
        return s;
    }

    public static string Format(ConnectionProfile p) => Format(p.Server, p.Instance, p.Port);

    private static readonly string[] LocalHosts = { "localhost", ".", "(local)", "127.0.0.1", "::1", "(localdb)" };

    /// <summary>True for names that always mean this machine (localhost, ., (local), the machine name…).</summary>
    public static bool IsLocalHost(string? server)
    {
        var s = StripPrefix(server, out _);
        if (s.Length == 0) return true;
        if (LocalHosts.Any(h => s.Equals(h, StringComparison.OrdinalIgnoreCase))) return true;
        try { return s.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
