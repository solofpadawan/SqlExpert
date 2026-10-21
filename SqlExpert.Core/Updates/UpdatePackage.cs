using System.IO.Compression;

namespace SqlExpert.Core.Updates;

/// <summary>Extraction and validation of a downloaded release package.</summary>
public static class UpdatePackage
{
    public const string ExecutableName = "SqlExpert.exe";

    /// <summary>
    /// Extracts <paramref name="zipPath"/> into an empty <paramref name="stagingDir"/> and returns the folder that holds
    /// SqlExpert.exe (the zip root, or its single top-level folder). Entries that would land outside the staging folder are refused.
    /// </summary>
    public static string ExtractAndValidate(string zipPath, string stagingDir)
    {
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);
        var root = Path.GetFullPath(stagingDir);
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var e in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(root, e.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"O pacote contém um caminho inválido: {e.FullName}");
                if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, overwrite: true);
            }
        }

        var content = FindContentRoot(stagingDir)
            ?? throw new InvalidDataException($"O pacote não contém {ExecutableName}.");
        var exe = new FileInfo(Path.Combine(content, ExecutableName));
        if (exe.Length < 1024)
            throw new InvalidDataException($"{ExecutableName} do pacote está vazio ou corrompido.");
        return content;
    }

    /// <summary>Folder with SqlExpert.exe: the root or its only subfolder; null when absent.</summary>
    public static string? FindContentRoot(string dir)
    {
        if (File.Exists(Path.Combine(dir, ExecutableName))) return dir;
        var subs = Directory.GetDirectories(dir);
        var files = Directory.GetFiles(dir);
        if (subs.Length == 1 && files.Length == 0 && File.Exists(Path.Combine(subs[0], ExecutableName)))
            return subs[0];
        return null;
    }

    /// <summary>True when the program folder accepts new files (a probe file is created and removed).</summary>
    public static bool IsDirectoryWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".sqlexpert-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
