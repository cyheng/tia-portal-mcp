using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        private static void ApplyProjectHmi(ProjectSpecification spec, ProjectStepRunner runner)
        {
            var path = "";
            if (!runner.Run("hmiResolve", () =>
            {
                path = ProjectWorkflow.SelectHmiPath(spec, ReadProjectHmiTargets());
                var info = GetHmiProgramInfo(path);
                if (!string.Equals(info.ProgramType, "Unified", StringComparison.OrdinalIgnoreCase))
                    return ProjectStepOutcome.Failed("The requested HMI is not WinCC Unified: " + path);
                return ProjectStepOutcome.FromResponse(info);
            }))
            {
                runner.Skip("hmi", "Resolve the requested HMI device before applying its connection, screens, and tags.");
                return;
            }
            if (!runner.Run("hmiConnection", () => ProjectStepOutcome.FromResponse(
                EnsureUnifiedHmiConnection(path, spec.ConnectionName, spec.PlcName))))
            {
                runner.Skip("hmi", "Complete the HMI connection before applying screens and tags.");
                return;
            }
            foreach (var screen in spec.HmiScreens)
                runner.RunSequence("hmiScreen", screen.Name,
                    () => ProjectStepOutcome.FromResponse(EnsureUnifiedHmiScreen(path, screen.Name, screen.Width, screen.Height)),
                    () => ProjectStepOutcome.FromResponse(ApplyUnifiedHmiScreenDesignJson(path, screen.Name, screen.DesignJson, true)));
            foreach (var tag in spec.HmiTags)
                runner.Run("hmiTag", () => ProjectStepOutcome.FromResponse(EnsureUnifiedHmiTag(path, tag.TableName, tag.Name,
                    tag.DataType, spec.PlcName, tag.PlcTag, spec.ConnectionName, tag.Address, true)), tag.TableName + "/" + tag.Name);
        }

        private static IEnumerable<ProjectHmiTarget> ReadProjectHmiTargets()
        {
            foreach (var device in Portal.GetDevices())
            {
                var names = new List<string> { device.Name };
                for (var parent = device.Parent; parent is DeviceUserGroup group; parent = group.Parent)
                    names.Insert(0, group.Name);
                var devicePath = string.Join("/", names);
                var pending = new Stack<DeviceItem>();
                foreach (var item in device.DeviceItems) pending.Push(item);
                while (pending.Count > 0)
                {
                    var item = pending.Pop();
                    var software = item.GetService<SoftwareContainer>()?.Software;
                    if (software is HmiSoftware || software is HmiTarget)
                        yield return new ProjectHmiTarget(device.Name, devicePath, software.Name, devicePath + "/" + item.Name);
                    foreach (var child in item.DeviceItems) pending.Push(child);
                }
            }
        }
    }
}
