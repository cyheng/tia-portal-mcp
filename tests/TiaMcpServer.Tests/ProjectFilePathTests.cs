using System;
using System.IO;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.Tests
{
    internal static class ProjectFilePathTests
    {
        public static void Run(Action<bool, string> check)
        {
            check(ProjectFilePath.IsProject("Demo.AP21"), "Project paths accept the V21 extension case-insensitively");
            check(ProjectFilePath.IsSession("Demo.ALS21"), "Session paths accept the V21 extension case-insensitively");
            check(!ProjectFilePath.IsProject("Demo.ap20"), "Project paths require V21");
            check(!ProjectFilePath.IsSession("Demo.als20"), "Session paths require V21");
            check(!ProjectFilePath.IsProject(null) && !ProjectFilePath.IsSession(null), "Empty paths have no project type");
            check(!ProjectFilePath.IsProject("Demo.ap21.txt"), "Project extension must be the final extension");

            var directory = Path.Combine(Path.GetTempPath(), "tia-project-path-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var project = Path.Combine(directory, "Demo.ap21");
                var session = Path.Combine(directory, "Demo.als21");
                var oldProject = Path.Combine(directory, "Demo.ap20");
                File.WriteAllText(project, "");
                File.WriteAllText(session, "");
                File.WriteAllText(oldProject, "");
                check(ProjectFilePath.Resolve(project) == project, "Existing V21 project resolves to its full path");
                check(ProjectFilePath.Resolve(session) == session, "Existing V21 session resolves to its full path");
                check(Throws<ArgumentException>(() => ProjectFilePath.Resolve(oldProject)), "Existing older project is rejected before opening");
                check(Throws<ArgumentException>(() => ProjectFilePath.Resolve(" ")), "Blank project path reports an argument error");
                check(Throws<FileNotFoundException>(() => ProjectFilePath.Resolve(Path.Combine(directory, "Missing.ap21"))), "Missing target reports a file error before opening");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); return false; }
            catch (T) { return true; }
        }
    }
}
