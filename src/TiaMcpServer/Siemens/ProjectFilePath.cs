using System;
using System.IO;

namespace TiaMcpServer.Siemens
{
    internal static class ProjectFilePath
    {
        public static bool IsProject(string? path) =>
            string.Equals(Path.GetExtension(path), ".ap21", StringComparison.OrdinalIgnoreCase);

        public static bool IsSession(string? path) =>
            string.Equals(Path.GetExtension(path), ".als21", StringComparison.OrdinalIgnoreCase);

        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Provide a TIA Portal V21 project (.ap21) or session (.als21) path.", nameof(path));

            var fullPath = Path.GetFullPath(path);
            if (!IsProject(fullPath) && !IsSession(fullPath))
                throw new ArgumentException("Use a TIA Portal V21 project (.ap21) or session (.als21) file.", nameof(path));

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Select an existing TIA Portal V21 project or session file.", fullPath);

            return fullPath;
        }
    }
}
