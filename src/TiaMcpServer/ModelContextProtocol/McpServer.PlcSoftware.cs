using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    // PLC software metadata, tags, technology objects, sources, OPC UA and alarms.
    public static partial class McpServer
    {
        #region plc software

        [McpServerTool(Name = "GetSoftwareInfo"), Description("[L1][PLC-Software] Get PLC software properties (language, version, block counts). Requires: Connect + OpenProject. softwarePath comes from GetProjectTree (e.g. 'PLC_1'). Use GetSoftwareTree for the full block hierarchy.")]
        public static ResponseSoftwareInfo GetSoftwareInfo(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath)
        {
            try
            {
                var software = Portal.GetPlcSoftware(softwarePath);
                if (software != null)
                {

                    var attributes = Helper.GetAttributeList(software);

                    return new ResponseSoftwareInfo
                    {
                        Message = $"Software info retrieved from '{softwarePath}'",
                        Name = software.Name,
                        Attributes = attributes,
                        Description = software.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Software not found at '{softwarePath}'", McpErrorCode.InternalError);
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving software info from '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DescribeObjectProperty"), Description("[L2][Reflection]Describe an object's nested property via reflection (members list). propertyPath supports dotted path.")]
        public static ResponseObjectDescribe DescribeObjectProperty(
            [Description("objectKind: Project|Portal|Device|DeviceItem|Software|Block|Type")] string objectKind,
            [Description("objectPath: object path")] string objectPath,
            [Description("propertyPath: dotted property path, e.g. 'Connections' or 'PressedStateTags'")] string propertyPath,
            [Description("softwarePath: required for Block/Type")] string softwarePath = "",
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeObjectProperty(objectKind, objectPath, propertyPath, softwarePath, maxMembers);
                // success 原来写的是「成员表非空」。那是把**空**当成了**失败**：
                // 一个真实存在、但确实没有成员的对象会被报成 success=false，
                // 调用方于是去"修"一个根本没坏的东西。走到这一行就说明对象已经解析到了
                // （解析不到在 Portal 层就抛了），这就是成功；空不空看 memberCount。
                res.Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = true,
                    ["memberCount"] = res.Members?.Count() ?? 0
                };
                return res;
            }
            catch (PortalException pex)
            {
                // 路径解析不到是调用方的参数问题，不是服务器内部意外错误。
                throw new McpException(pex.Message, pex,
                    pex.Code == PortalErrorCode.NotFound ? McpErrorCode.InvalidParams : McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error describing property '{propertyPath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetCrossReferences"), Description("[L2][PLC-Software]Get cross references for a Step7 block/type (best-effort). Requires applicable object and Openness support.")]
        public static ResponseCrossReferences GetCrossReferences(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("objectPath: blockPath or typePath inside the PLC software")] string objectPath,
            [Description("objectKind: Block or Type")] string objectKind = "Block",
            [Description("filter: CrossReferenceFilter enum name (e.g. AllObjects, ObjectsWithReferences, UnusedObjects)")] string filter = "AllObjects")
        {
            try
            {
                var items = Portal.GetCrossReferences(softwarePath, objectPath, objectKind, filter);
                if (items != null)
                {
                    return new ResponseCrossReferences
                    {
                        Message = $"Cross references retrieved for {objectKind} '{objectPath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"Cross reference service not available for {objectKind} '{objectPath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving cross references: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetPlcExternalSources"), Description("[L2][PLC-Software]List PLC external source names (best-effort)")]
        public static ResponseStringList GetPlcExternalSources(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetPlcExternalSources(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"PLC external sources listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"PLC software not found at '{softwarePath}'.{Portal.AvailablePlcPathsSuffix()}", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing PLC external sources: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetPlcTagTables"), Description("[L2][PLC-Software] List all PLC tag table names. Requires: Connect + OpenProject. softwarePath from GetProjectTree (e.g. 'PLC_1'). Use before ExportPlcTagTable to get exact table names, or before ImportPlcTagTable to check for conflicts.")]
        public static ResponseStringList GetPlcTagTables(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetPlcTagTables(softwarePath, out var walk);
                if (items != null)
                {
                    // 空清单有三种完全不同的成因：这个 PLC 确实没有表 / TagTables 属性
                    // 在这个版本上叫别的名字 / 读属性时抛了异常被吞掉。三者返回的东西
                    // 一模一样，用户报「枚举返回空但删除工具能找到同一张表」时我们手上
                    // 没有任何证据。所以空清单必须把「走过了什么」一并带回来。
                    var meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true };
                    if (items.Count == 0)
                    {
                        meta["walkedGroupType"] = walk.RootGroupType;
                        meta["tagTablesPropertyFound"] = walk.TagTablesPropertyFound;
                        meta["tagTablesPropertyError"] = walk.TagTablesPropertyError;
                        meta["groupsVisited"] = walk.GroupsVisited;
                        meta["notes"] = new JsonArray(
                            walk.Notes.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
                    }

                    return new ResponseStringList
                    {
                        Message = items.Count > 0
                            ? $"PLC tag tables listed for '{softwarePath}'"
                            : $"'{softwarePath}' 上没有枚举到任何变量表。这**不一定**表示它没有表 —— "
                              + "读属性失败也长这样，所以 Meta 里带了这次遍历的证据"
                              + "（walkedGroupType / tagTablesPropertyFound / tagTablesPropertyError / groupsVisited / notes）。"
                              + "若你确信有表，把这几项贴给维护者。",
                        Items = items,
                        Meta = meta
                    };
                }

                throw new McpException($"PLC software not found at '{softwarePath}'.{Portal.AvailablePlcPathsSuffix()}", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing PLC tag tables: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportPlcTagTable"), Description("[L2][PLC-Software]Export one PLC tag table (PlcTagTable) to XML file")]
        public static ResponseExportFile ExportPlcTagTable(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("tagTableName: PLC tag table name")] string tagTableName,
            [Description("exportPath: full file path to write to")] string exportPath)
        {
            try
            {
                var ok = Portal.ExportPlcTagTable(softwarePath, tagTableName, exportPath, out var reason);
                if (ok)
                {
                    return new ResponseExportFile
                    {
                        Message = $"PLC tag table '{tagTableName}' exported",
                        ExportPath = exportPath,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException(
                    $"Failed exporting PLC tag table '{tagTableName}' from '{softwarePath}'" +
                    (string.IsNullOrWhiteSpace(reason) ? string.Empty : ": " + reason),
                    McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting PLC tag table: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportPlcTagTable"), Description("[L1][PLC-Software]Import one PLC tag table XML file into PLC software (best-effort)")]
        public static ResponseMessage ImportPlcTagTable(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional tag table group path (use empty for root)")] string folderPath,
            [Description("importPath: full file path of PLC tag table XML")] string importPath)
        {
            try
            {
                Portal.ImportPlcTagTable(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"PLC tag table imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed importing PLC tag table from '{importPath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing PLC tag table: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportPlcTagTablesFromDirectory"), Description("[L2][PLC-Software]Batch import PLC tag table .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportPlcTagTablesFromDirectory(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional tag table group path (use empty for root)")] string folderPath,
            [Description("dir: directory containing PLC tag table XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportPlcTagTablesFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} PLC tag tables from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing PLC tag tables from '{dir}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetTechnologyObjects"), Description(
            "[L2][Category:PLC-TechnologyObjects][PreCondition:Connect+OpenProject]" +
            " List all Technology Objects (TOs) in the PLC software: axes, cams, measuring inputs, etc." +
            " Returns each TO's Name, type (OfSystemLibElement), and firmware version (OfSystemLibVersion)." +
            " Use this to discover TO names before ExportTechnologyObject." +
            " No tool returns axis/TO parameter values directly — export the TO with ExportTechnologyObject and read the XML file." +
            " TOs are stored as TechnologicalInstanceDB instances in the TechnologicalObjectGroup.")]
        public static ResponseTechnologyObjectList GetTechnologyObjects(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                var items = Portal.GetTechnologyObjects(softwarePath);
                var typed = items.Select(jo => new TechnologyObjectInfo
                {
                    Name = jo["Name"]?.GetValue<string>(),
                    OfSystemLibElement = jo["OfSystemLibElement"]?.GetValue<string>(),
                    OfSystemLibVersion = jo["OfSystemLibVersion"]?.GetValue<string>(),
                    TypeHint = jo["TypeHint"]?.GetValue<string>(),
                }).ToArray();

                return new ResponseTechnologyObjectList
                {
                    Ok = true,
                    SoftwarePath = softwarePath,
                    Count = typed.Length,
                    Items = typed,
                    Message = $"{typed.Length} technology object(s) found in '{softwarePath}'."
                };
            }
            catch (PortalException pex)
            {
                // 路径解析不到是调用方的参数问题，不是服务器内部意外错误。
                throw new McpException(pex.Message, pex,
                    pex.Code == PortalErrorCode.NotFound ? McpErrorCode.InvalidParams : McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing technology objects: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportTechnologyObject"), Description(
            "[L2][Category:PLC-TechnologyObjects][PreCondition:Connect+OpenProject]" +
            " Export a single Technology Object (axis, cam, measuring input, etc.) to an XML file." +
            " The XML can be inspected, modified offline, and re-imported with ImportTechnologyObject." +
            " Use GetTechnologyObjects first to confirm the exact TO name.")]
        public static ResponseMessage ExportTechnologyObject(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("toName: exact name of the technology object, e.g. 'Axis_1'")] string toName,
            [Description("exportPath: full file path for the XML output, e.g. 'C:\\Temp\\Axis_1.xml'")] string exportPath)
        {
            try { return Portal.ExportTechnologyObject(softwarePath, toName, exportPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error exporting technology object: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ExportTechnologyObjectsToDirectory"), Description(
            "[L2][Category:PLC-TechnologyObjects][PreCondition:Connect+OpenProject]" +
            " Batch-export all (or regex-filtered) Technology Objects to XML files in a directory." +
            " Each TO is saved as '<TOName>.xml'. Returns lists of exported names and any failures." +
            " Use regexName to filter by TO name, e.g. 'Axis_.*' for all axes.")]
        public static ResponseImportBatch ExportTechnologyObjectsToDirectory(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportDir: directory to write XML files to, e.g. 'C:\\Temp\\TOs'")] string exportDir,
            [Description("regexName: optional regex filter on TO name; empty = export all")] string regexName = "")
        {
            try { return Portal.ExportTechnologyObjectsToDirectory(softwarePath, exportDir, regexName); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error batch-exporting technology objects: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ImportTechnologyObject"), Description("[L2][PLC-Software]Import one PLC Technology Object XML file into PLC software (best-effort)")]
        public static ResponseMessage ImportTechnologyObject(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional technology object group path (use empty for root)")] string folderPath,
            [Description("importPath: full file path of Technology Object XML")] string importPath)
        {
            try
            {
                Portal.ImportTechnologyObject(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"Technology object imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed importing technology object from '{importPath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing technology object: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportTechnologyObjectsFromDirectory"), Description("[L2][PLC-Software]Batch import PLC technology object .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportTechnologyObjectsFromDirectory(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("folderPath: optional technology object group path (use empty for root)")] string folderPath,
            [Description("dir: directory containing technology object XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportTechnologyObjectsFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} technology objects from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing technology objects from '{dir}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportPlcExternalSource"), Description("[L2][PLC-Software]Import one PLC external source file into a group (best-effort)")]
        public static ResponseMessage ImportPlcExternalSource(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("groupPath: external source group path (use empty for root)")] string groupPath,
            [Description("filePath: path to external source file (.scl, etc.)")] string filePath)
        {
            try
            {
                Portal.ImportPlcExternalSource(softwarePath, groupPath, filePath);
                return new ResponseMessage
                {
                    Message = "PLC external source imported",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed importing PLC external source [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing PLC external source: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DeletePlcExternalSource"), Description("[L2][PLC-Software]Delete a PLC external source by name so ImportPlcExternalSource can replace it (idempotent). Name may include or omit .scl.")]
        public static ResponseMessage DeletePlcExternalSource(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("externalSourceName: name from GetPlcExternalSources (e.g. MCPVerify_FC_SCL_v3.scl)")] string externalSourceName)
        {
            try
            {
                Portal.DeletePlcExternalSource(softwarePath, externalSourceName);
                return new ResponseMessage
                {
                    Message = $"PLC external source '{externalSourceName}' deleted or was not present",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed deleting PLC external source '{externalSourceName}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting PLC external source: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GenerateBlocksFromExternalSource"), Description("[L2][PLC-Software]Generate blocks from a PLC external source by name (best-effort)")]
        public static ResponseMessage GenerateBlocksFromExternalSource(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("externalSourceName: name from GetPlcExternalSources")] string externalSourceName)
        {
            try
            {
                Portal.GenerateBlocksFromExternalSource(softwarePath, externalSourceName);
                return new ResponseMessage
                {
                    Message = $"Blocks generated from external source '{externalSourceName}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed generating blocks from external source '{externalSourceName}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error generating blocks from external source: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetOpcUaConfig"), Description(
            "[L2][Category:PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Read the full OPC UA server configuration for a PLC: server interfaces, SIMATIC interfaces, and reference namespaces — each with their Name, Enabled state, and key properties." +
            " Use this to audit what OPC UA interfaces exist before enabling or exporting them." +
            " Enabled=true means the interface is active and will be downloaded to the CPU.")]
        public static ResponseJsonReport GetOpcUaConfig(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try { return Portal.GetOpcUaConfig(softwarePath); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error reading OPC UA config: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "SetOpcUaInterfaceEnabled"), Description(
            "[L2][Category:PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Enable or disable an OPC UA server interface, SIMATIC interface, or reference namespace." +
            " Setting Enabled=true activates the interface — download to PLC is required for the change to take effect on the CPU." +
            " interfaceType options: 'ServerInterface' (default), 'SimaticInterface', 'ReferenceNamespace'." +
            " Workflow: GetOpcUaConfig → SetOpcUaInterfaceEnabled → DownloadToPlc.")]
        public static ResponseMessage SetOpcUaInterfaceEnabled(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("interfaceName: exact name of the interface as shown in GetOpcUaConfig")] string interfaceName,
            [Description("enabled: true to enable, false to disable")] bool enabled,
            [Description("interfaceType: 'ServerInterface' (default), 'SimaticInterface', or 'ReferenceNamespace'")] string interfaceType = "ServerInterface")
        {
            try { return Portal.SetOpcUaInterfaceEnabled(softwarePath, interfaceName, enabled, interfaceType); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error setting OPC UA interface enabled state: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ExportOpcUaInterface"), Description(
            "[L2][Category:PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Export an OPC UA server interface or reference namespace to an XML file." +
            " The exported XML can be inspected, modified, and re-imported." +
            " interfaceType: 'ServerInterface' (default), 'SimaticInterface', 'ReferenceNamespace'.")]
        public static ResponseMessage ExportOpcUaInterface(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("interfaceName: exact name of the interface to export")] string interfaceName,
            [Description("exportPath: full file path for the XML output, e.g. 'C:\\Temp\\OpcUa_Interface.xml'")] string exportPath,
            [Description("interfaceType: 'ServerInterface' (default), 'SimaticInterface', or 'ReferenceNamespace'")] string interfaceType = "ServerInterface")
        {
            try { return Portal.ExportOpcUaInterface(softwarePath, interfaceName, exportPath, interfaceType); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error exporting OPC UA interface: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ImportOpcUaInterface"), Description(
            "[L2][Category:PLC-OpcUA][PreCondition:Connect+OpenProject]" +
            " Import an OPC UA server interface or reference namespace from an XML file." +
            " If an interface with the same name (derived from the file name) already exists, it is updated in place." +
            " Otherwise a new interface is created." +
            " Download to PLC after import to apply changes to the CPU." +
            " interfaceType: 'ServerInterface' (default), 'ReferenceNamespace'.")]
        public static ResponseMessage ImportOpcUaInterface(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("importPath: full file path to the XML file")] string importPath,
            [Description("interfaceType: 'ServerInterface' (default) or 'ReferenceNamespace'")] string interfaceType = "ServerInterface")
        {
            try { return Portal.ImportOpcUaInterface(softwarePath, importPath, interfaceType); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error importing OPC UA interface: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ExportAlarmClasses"), Description(
            "[L2][Category:PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Export PLC alarm classes to a file. Alarm classes define severity, acknowledgment behavior, and display colors for alarms." +
            " The exported file can be edited and re-imported to update alarm class configurations." +
            " Use before bulk alarm class updates to create a backup.")]
        public static ResponseMessage ExportAlarmClasses(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportPath: full file path for the export, e.g. 'C:\\Temp\\AlarmClasses.xml'")] string exportPath)
        {
            try { return Portal.ExportAlarmClasses(softwarePath, exportPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error exporting alarm classes: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ImportAlarmClasses"), Description(
            "[L2][Category:PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Import PLC alarm classes from a previously exported file." +
            " Overwrites existing alarm class definitions. Run CompileSoftware after import.")]
        public static ResponseMessage ImportAlarmClasses(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("importPath: full file path to import from")] string importPath)
        {
            try { return Portal.ImportAlarmClasses(softwarePath, importPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error importing alarm classes: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ExportAlarmTextLists"), Description(
            "[L2][Category:PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Export all PLC alarm text lists to an XLSX (Excel) file." +
            " Text lists contain the text strings shown for each alarm condition." +
            " Supports multi-language projects — all configured languages are exported." +
            " Typical use: export → translate in Excel → ImportAlarmTextLists.")]
        public static ResponseMessage ExportAlarmTextLists(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportPath: full file path for the XLSX output, e.g. 'C:\\Temp\\AlarmTexts.xlsx'")] string exportPath)
        {
            try { return Portal.ExportAlarmTextLists(softwarePath, exportPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error exporting alarm text lists: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ImportAlarmTextLists"), Description(
            "[L2][Category:PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Import PLC alarm text lists from an XLSX file." +
            " The file must match the format exported by ExportAlarmTextLists." +
            " Run CompileSoftware after import to validate alarm configuration.")]
        public static ResponseMessage ImportAlarmTextLists(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("importPath: full file path to the XLSX file")] string importPath)
        {
            try { return Portal.ImportAlarmTextLists(softwarePath, importPath); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error importing alarm text lists: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "ExportAlarmInstanceTexts"), Description(
            "[L2][Category:PLC-Alarms][PreCondition:Connect+OpenProject]" +
            " Export PLC alarm instance texts to an XLSX file." +
            " Instance texts are the alarm messages tied to specific FB/FC instances (e.g. Motor_01.AlarmText)." +
            " Options control what additional columns are included in the export." +
            " Typical use: export → fill in alarm descriptions → ImportInstanceTexts (not yet exposed — edit via TIA Portal UI).")]
        public static ResponseMessage ExportAlarmInstanceTexts(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("exportPath: full file path for the XLSX output")] string exportPath,
            [Description("includeInfoText: include the Info Text column (default: true)")] bool includeInfoText = true,
            [Description("includeAdditionalTexts: include Additional Texts columns (default: true)")] bool includeAdditionalTexts = true,
            [Description("includeAlarmClass: include the Alarm Class column (default: true)")] bool includeAlarmClass = true)
        {
            try { return Portal.ExportAlarmInstanceTexts(softwarePath, exportPath, includeInfoText, includeAdditionalTexts, includeAlarmClass); }
            catch (Exception ex) when (ex is not McpException)
            { throw new McpException($"Unexpected error exporting alarm instance texts: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError); }
        }

        [McpServerTool(Name = "CompileSoftware"), Description("[L1][PLC-Software] Compile all blocks in the PLC software. Requires: Connect + OpenProject. Returns basic success/failure. For structured error/warning details use CompileAndDiagnosePlc instead. Must compile before ExportBlock if any blocks are inconsistent. After adding new blocks via import, always compile to catch type/interface mismatches.")]
        public static ResponseCompile CompileSoftware(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("password: the password to access adminsitration, default: no password")] string password = "")
        {
            try
            {
                var result = WithAutoOffline(() => Portal.CompileSoftware(softwarePath, password));
                var collected = CollectCompilerMessages(result.Messages);

                return new ResponseCompile
                {
                    Message = $"Software '{softwarePath}' compiled. State={result.State} Errors={result.ErrorCount} Warnings={result.WarningCount}",
                    State = result.State.ToString(),
                    ErrorCount = result.ErrorCount,
                    WarningCount = result.WarningCount,
                    Messages = collected.Raw,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = !result.State.ToString().Equals("Error", StringComparison.OrdinalIgnoreCase),
                        ["errorDetailCount"] = collected.Errors.Count,
                        ["warningDetailCount"] = collected.Warnings.Count
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed compiling software '{softwarePath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error compiling software '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }


        [McpServerTool(Name = "GetSoftwareTree"), Description("[L1][PLC-Software] Get the full PLC block/type/external-source hierarchy as ASCII tree. Requires: Connect + OpenProject. softwarePath from GetProjectTree (e.g. 'PLC_1'). ALWAYS call before ExportBlock/ImportBlock to get exact group paths (e.g. 'Program blocks/FBs/FB_Motor'). Returns OB/FB/FC/GlobalDB/UDT/ExternalSource blocks with group hierarchy.")]
        public static ResponseSoftwareTree GetSoftwareTree(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath)
        {
            try
            {
                var tree = Portal.GetSoftwareTree(softwarePath);

                if (!string.IsNullOrEmpty(tree))
                {
                    return new ResponseSoftwareTree
                    {
                        Message = $"Software tree retrieved from '{softwarePath}'",
                        Tree = "```\n" + tree + "\n```",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed retrieving software tree from '{softwarePath}'", McpErrorCode.InternalError);
                }
            }
            catch (PortalException pex)
            {
                // 路径解析不到是调用方的参数问题，不是服务器内部意外错误。
                throw new McpException(pex.Message, pex,
                    pex.Code == PortalErrorCode.NotFound ? McpErrorCode.InvalidParams : McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving software tree from '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        #endregion
    }
}
