using Siemens.Collaboration.Net;
using System.IO;
using System.Threading.Tasks;

namespace TiaMcpServer.Siemens
{
    public static class Openness
    {
        public static int TiaMajorVersion => Engineering.TiaMajorVersion;

        public static void Initialize()
        {
            var probe = Engineering.ProbeOpennessAssemblies();
            if (!probe.Ok) throw new FileNotFoundException(probe.Problem, "Siemens.Engineering.Base.dll");

            Api.Global.Openness().Initialize(
                tiaPortalInstallationDirectory: new DirectoryInfo(probe.InstallPath!),
                tiaMajorVersion: TiaMajorVersion);
        }

        // Pure check — does NOT add the user or prompt UAC. Use for read-only diagnosis.
        public static bool IsUserInGroupNoFix()
        {
            return Api.Global.Openness().IsUserInGroup();
        }

        public static async Task<bool> IsUserInGroup()
        {
            if (Api.Global.Openness().IsUserInGroup())
            {
                // user is in group
                return true;
            }
            else
            {
                return await Api.Global.Openness().AddUserToGroupAsync();
            }
        }
    }
}
