using System.Text;

/// <summary>
/// File exchange between a test and the child processes it launches.
/// Windows rejects opening a file that another process is writing unless the share modes
/// are compatible, and any platform can observe a write that is only partly complete.
/// </summary>
internal static class ConcurrentFile
{
    /// <summary>
    /// Writes a one-shot marker or report so that it appears only once it is complete and closed.
    /// Readers never see partial content, never collide with the writer's handle, and a directory
    /// holding the published file can be deleted as soon as the file is observed. A later
    /// publication replaces an earlier one at the same path.
    /// </summary>
    public static void Publish(string path, string content)
    {
        var temporary = path + ".tmp-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Reads the newline-terminated lines of a log that other processes may still be appending to.
    /// The share mode coexists with an appending writer, and an unterminated last line is ignored
    /// because its writer has not finished it. A missing file has no lines.
    /// </summary>
    public static string[] ReadCompleteLines(string path)
    {
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        var complete = text.LastIndexOf('\n');
        return complete < 0 ? [] : text[..complete].Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
    }
}
