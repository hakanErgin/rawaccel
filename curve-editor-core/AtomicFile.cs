using System.IO;

namespace CurveEditor.Core
{
    /// <summary>
    /// Writes a file so that a failure part-way (full disk, crash, power loss) leaves the previous
    /// contents in place instead of a truncated file.
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            string temp = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch (IOException) { } catch (System.UnauthorizedAccessException) { }
                }
            }
        }
    }
}
