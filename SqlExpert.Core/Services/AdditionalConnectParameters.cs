using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace SqlExpert.Core.Services;

/// <summary>
/// "Additional connect parameters" box: connection-string keywords, one or more <c>key=value</c> per line
/// (lines may also hold several pairs separated by ';'). Blank lines and lines starting with '#' or '--' are ignored.
/// Keywords that have their own field in the dialog (server, database, user, password, auth, encrypt,
/// trust certificate, timeout) are rejected, so the password never ends up in connections.json.
/// </summary>
public static class AdditionalConnectParameters
{
    /// <summary>Canonical SqlConnectionStringBuilder keywords owned by dedicated fields.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data Source", "Initial Catalog", "User ID", "Password", "Integrated Security",
        "Encrypt", "Trust Server Certificate", "TrustServerCertificate", "Connect Timeout",
        "Attach DB Filename", "AttachDbFilename"
    };

    /// <summary>Parses and validates. Returns the pairs; <paramref name="errors"/> lists problems (empty = valid).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? text, out List<string> errors)
    {
        errors = new List<string>();
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("--", StringComparison.Ordinal)) continue;
            var lineNo = i + 1;

            DbConnectionStringBuilder parsed;
            try
            {
                parsed = new DbConnectionStringBuilder { ConnectionString = line };
            }
            catch (ArgumentException)
            {
                errors.Add($"Linha {lineNo}: formato inválido (use chave=valor): {line}");
                continue;
            }

            if (parsed.Count == 0)
            {
                errors.Add($"Linha {lineNo}: formato inválido (use chave=valor): {line}");
                continue;
            }

            foreach (string key in parsed.Keys)
            {
                var value = Convert.ToString(parsed[key]) ?? "";
                var canonical = CanonicalKey(key, value, out var error);
                if (canonical is null)
                {
                    errors.Add($"Linha {lineNo}: {error}");
                    continue;
                }
                if (Reserved.Contains(canonical))
                {
                    errors.Add($"Linha {lineNo}: '{canonical}' tem campo próprio no diálogo e não pode ir em Additional connect parameters.");
                    continue;
                }
                result.Add(new KeyValuePair<string, string>(canonical, value));
            }
        }

        return result;
    }

    /// <summary>Validation messages (empty = valid).</summary>
    public static IReadOnlyList<string> Validate(string? text)
    {
        Parse(text, out var errors);
        return errors;
    }

    /// <summary>Merges the parameters into <paramref name="builder"/>. Throws <see cref="ArgumentException"/> if invalid.</summary>
    public static void ApplyTo(SqlConnectionStringBuilder builder, string? text)
    {
        var pairs = Parse(text, out var errors);
        if (errors.Count > 0)
            throw new ArgumentException("Additional connect parameters inválidos:\n" + string.Join("\n", errors));
        foreach (var (key, value) in pairs)
            builder[key] = value;
    }

    /// <summary>SqlClient's canonical keyword for <paramref name="key"/> (null + error when unknown/invalid value).</summary>
    private static string? CanonicalKey(string key, string value, out string? error)
    {
        error = null;
        try
        {
            var b = new SqlConnectionStringBuilder();
            b[key] = value;
            var cs = b.ConnectionString;
            var eq = cs.IndexOf('=');
            return eq > 0 ? cs[..eq].Trim() : key;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException or KeyNotFoundException or NotSupportedException)
        {
            error = $"'{key}={value}' não é aceito pelo SQL Server ({ex.Message.Split('\n')[0].Trim()})";
            return null;
        }
    }
}
