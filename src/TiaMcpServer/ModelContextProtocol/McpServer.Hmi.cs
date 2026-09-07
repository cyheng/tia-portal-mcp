using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    // HMI software inspection, screens, tags, connections and actions.
    public static partial class McpServer
    {

        [McpServerTool(Name = "GetHmiProgramInfo"), Description("[L2][HMI] Get HMI software type (Classic/Basic/Unified), version, and list of all screen names. Requires: Connect + OpenProject. softwarePath from GetProjectTree (e.g. 'HMI_RT_1'). Use to confirm HMI type before choosing Classic vs Unified tool variants.")]
        public static ResponseHmiProgramInfo GetHmiProgramInfo(
            [Description("softwarePath: path in the project structure to the HMI software (see GetProjectTree)")] string softwarePath)
        {
            try
            {
                var info = Portal.GetHmiProgramInfo(softwarePath);
                if (info != null)
                {
                    return new ResponseHmiProgramInfo
                    {
                        Message = $"HMI program info retrieved from '{softwarePath}'",
                        Name = info.Value.Name,
                        ProgramType = info.Value.ProgramType,
                        Screens = info.Value.Screens,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }

                throw new McpException($"HMI program not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI program info from '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DescribeHmiSoftware"), Description("[L2][HMI]Describe the HMI software object (members/methods) via reflection. Useful to discover Export/Import/Create APIs.")]
        public static ResponseObjectDescribe DescribeHmiSoftware(
            [Description("softwarePath: path in the project structure to the HMI software (e.g. 'HMI_RT_1')")] string softwarePath,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiSoftware(softwarePath, maxMembers);
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
                throw new McpException($"Unexpected error describing HMI software '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DescribeHmiScreen"), Description("[L2][HMI]Describe one HMI screen object (members/methods) by name under an HMI software.")]
        public static ResponseObjectDescribe DescribeHmiScreen(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("screenName: screen name, e.g. 'Main'")] string screenName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiScreen(softwarePath, screenName, maxMembers);
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
                throw new McpException($"Unexpected error describing HMI screen '{screenName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DescribeHmiTagTable"), Description("[L2][HMI]Describe one HMI tag table object (members/methods) by name under an HMI software.")]
        public static ResponseObjectDescribe DescribeHmiTagTable(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("tagTableName: tag table name")] string tagTableName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiTagTable(softwarePath, tagTableName, maxMembers);
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
                throw new McpException($"Unexpected error describing HMI tag table '{tagTableName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DescribeHmiTag"), Description("[L2][HMI]Describe one HMI tag object (members/methods) by name under an HMI tag table.")]
        public static ResponseObjectDescribe DescribeHmiTag(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("tagTableName: tag table name")] string tagTableName,
            [Description("tagName: tag name")] string tagName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiTag(softwarePath, tagTableName, tagName, maxMembers);
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
                throw new McpException($"Unexpected error describing HMI tag '{tagName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "CompileAndDiagnoseHmi"), Description("[L1][HMI] Compile an HMI and return structured errors/warnings, the HMI counterpart of CompileAndDiagnosePlc. Use it after generating screens/tags so you can read the diagnostics and fix them yourself instead of asking the engineer to compile in the TIA UI. WinCC Unified: HmiSoftware is not compilable on its own, so the owning device is compiled (same as the TIA UI does) and hardware diagnostics may appear alongside screen ones. Classic (Comfort/KTP): the HMI software itself is compiled. Requires: Connect + OpenProject. softwarePath from GetProjectTree, e.g. 'HMI_RT_1'.")]
        public static ResponseCompileDiagnose CompileAndDiagnoseHmi(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath)
            => CompileAndDiagnoseCore(softwarePath, "");

        [McpServerTool(Name = "DescribeHmiScreenItem"), Description("[L2][HMI]Describe one HMI screen item (widget) by name under an HMI screen.")]
        public static ResponseObjectDescribe DescribeHmiScreenItem(
            [Description("softwarePath: HMI software path, e.g. 'HMI_RT_1'")] string softwarePath,
            [Description("screenName: screen name, e.g. 'Main'")] string screenName,
            [Description("itemName: widget name, e.g. 'BTN_Start'")] string itemName,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeHmiScreenItem(softwarePath, screenName, itemName, maxMembers);
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
                throw new McpException($"Unexpected error describing HMI screen item '{itemName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureStartStopUnifiedHmi"), Description("[L2][HMI-Unified] SHORTCUT for motor start/stop HMI. Ensures HMI_Connection_1 uses the correct PLC driver (1200/1500 vs 300/400 from CPU TypeIdentifier), 4 HMI tags (StartPB/StopPB/EStop/RunOut) with symbolic PLC binding, and a simple styled Main screen. Requires: Connect + OpenProject + PLC + Unified HMI. Call after EnsureUnifiedHmiScreen if you need a fixed screen size. Idempotent.")]
        public static ResponseMessage EnsureStartStopUnifiedHmi(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name (default 'Main')")] string screenName = "Main",
            [Description("tagTableName: target HMI tag table name (default '默认变量表')")] string tagTableName = "默认变量表",
            [Description("plcName: PLC software path / device name for connection + tag mapping (default 'PLC_1')")] string plcName = "PLC_1",
            [Description("connectionName: Unified HMI connection object name (default 'HMI_Connection_1')")] string connectionName = "HMI_Connection_1")
        {
            try
            {
                var res = Portal.EnsureStartStopUnifiedHmi(hmiSoftwarePath, screenName, tagTableName, plcName, connectionName);
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring unified HMI start/stop: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiScreen"), Description("[L2][HMI-Unified] Create or verify a WinCC Unified HMI screen exists. Requires: Connect + OpenProject + Unified HMI. Idempotent. After creating a screen, add tags with EnsureUnifiedHmiTag, add controls with EnsureUnifiedHmiScreenItem, or apply a complete layout with ApplyUnifiedHmiScreenDesignJson.")]
        public static ResponseMessage EnsureUnifiedHmiScreen(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("width: optional screen width, 0 means keep current")] uint width = 0,
            [Description("height: optional screen height, 0 means keep current")] uint height = 0)
        {
            try
            {
                return Portal.EnsureUnifiedHmiScreen(hmiSoftwarePath, screenName, width, height);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring HMI screen '{screenName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiTagTable"), Description("[L2][HMI-Unified] Create or verify a Unified HMI tag table exists. Requires: Connect + OpenProject + Unified HMI. Idempotent. Create tag tables before adding tags with EnsureUnifiedHmiTag. Default tag table name is '默认变量表'.")]
        public static ResponseMessage EnsureUnifiedHmiTagTable(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("tagTableName: target HMI tag table name")] string tagTableName)
        {
            try
            {
                return Portal.EnsureUnifiedHmiTagTable(hmiSoftwarePath, tagTableName);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring HMI tag table '{tagTableName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiTag"), Description("[L2][HMI-Unified] Create or verify a Unified HMI external tag. For PLC-backed tags pass plcTag and address in the same call; the address must read back in Address/LogicalAddress, e.g. %DB200.DBX0.0. Requires: Connect + OpenProject + EnsureUnifiedHmiConnection + EnsureUnifiedHmiTagTable.")]
        public static ResponseMessage EnsureUnifiedHmiTag(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("tagTableName: target HMI tag table name")] string tagTableName,
            [Description("tagName: HMI tag name")] string tagName,
            [Description("hmiDataType: HMI data type, e.g. Bool, Int, Real, String")] string hmiDataType = "Bool",
            [Description("plcName: PLC name for symbolic binding")] string plcName = "PLC_1",
            [Description("plcTag: PLC tag name/path; empty means same as tagName")] string plcTag = "",
            [Description("connectionName: HMI connection name; empty keeps current/auto")] string connectionName = "",
            [Description("address: optional absolute PLC address, e.g. %DB200.DBX0.0. When supplied it is written as the verified HMI runtime address while plcTag remains available as the symbolic reference.")] string address = "",
            [Description("requireVerifiedBinding: stable public generation should keep this true. Set false only for intentional internal HMI-only tags used by local validation/probes.")] bool requireVerifiedBinding = true)
        {
            try
            {
                return Portal.EnsureUnifiedHmiTag(hmiSoftwarePath, tagTableName, tagName, hmiDataType, plcName, plcTag, connectionName, address, requireVerifiedBinding);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring HMI tag '{tagName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiConnection"), Description("[L2][HMI-Unified] Create or verify the PLC↔HMI communication connection (HMI_Connection_1 by default). Requires: Connect + OpenProject + both PLC and Unified HMI devices. Must exist before PLC-backed HMI tags can exchange data. Call before EnsureUnifiedHmiTag with plcTag binding. UNIFIED PANELS ONLY: on Classic/Comfort/Basic panels (KTP Basic, TP/KTP Comfort) this connection cannot be created via Openness (CommunicationConnections service is not exposed); if the project needs end-to-end HMI automation, use a WinCC Unified panel instead of a classic one.")]
        public static ResponseObjectDescribe EnsureUnifiedHmiConnection(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("connectionName: HMI connection name")] string connectionName = "HMI_Connection_1",
            [Description("plcName: PLC software/device symbolic name")] string plcName = "PLC_1")
        {
            try
            {
                var res = Portal.EnsureUnifiedHmiConnection(hmiSoftwarePath, connectionName, plcName);
                res.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true };
                return res;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring HMI connection '{connectionName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiScreenItem"), Description("[L2][HMI-Unified] Create or verify a single Unified HMI control (button, lamp, IO field, etc.) on a screen. Requires: Connect + OpenProject + EnsureUnifiedHmiScreen. itemType: Button, Rectangle (lamp/indicator/background — has NO text), Text (static text label = HmiText), IOField (value display/entry), or full CLR type name. For a text caption/label ALWAYS use Text, never Rectangle (a Rectangle has no Text property and renders blank if given text). For a complete screen layout use ApplyUnifiedHmiScreenDesignJson instead.")]
        public static ResponseMessage EnsureUnifiedHmiScreenItem(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("itemName: screen item name")] string itemName,
            [Description("itemType: Button, Rectangle/Lamp, IOField, or full CLR type name")] string itemType = "Button",
            [Description("left: X position")] int left = 0,
            [Description("top: Y position")] int top = 0,
            [Description("width: item width")] uint width = 120,
            [Description("height: item height")] uint height = 40,
            [Description("text: optional button/display text")] string text = "")
        {
            try
            {
                return Portal.EnsureUnifiedHmiScreenItem(hmiSoftwarePath, screenName, itemName, itemType, left, top, width, height, text);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring HMI screen item '{itemName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ApplyUnifiedHmiScreenDesignJson"), Description("[L2][HMI-Unified] PREFERRED for natural-language HMI design. Apply a complete JSON layout spec to a screen in one call: screen size + multiple controls (Button/Rectangle/IOField/Text) with positions, text, and properties. Use item type \"Text\" (HmiText) for static text labels/titles/field captions — a Rectangle has no Text property and renders blank if given text. Requires: Connect + OpenProject + EnsureUnifiedHmiScreen. Better than calling EnsureUnifiedHmiScreenItem multiple times. Use BuildUnifiedHmiLayoutDesignJson to generate the JSON from a grid description.")]
        public static ResponseMessage ApplyUnifiedHmiScreenDesignJson(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("designJson: JSON object with optional screen properties and items array")] string designJson,
            [Description("strict: true fails the tool when any property/text write fails; false keeps legacy best-effort behavior.")] bool strict = true)
        {
            try
            {
                return Portal.ApplyUnifiedHmiScreenDesignJson(hmiSoftwarePath, screenName, designJson, strict);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error applying Unified HMI design to '{screenName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "BuildUnifiedHmiThemeDesignJson"), Description("[L2][HMI-Unified][Offline] Build ApplyUnifiedHmiScreenDesignJson-compatible JSON from a theme/palette. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildUnifiedHmiThemeDesignJson(
            [Description("themeJson: JSON {name?, palette:{Page?,Surface?,Text?,Border?,...}} with TIA ARGB colors like 0xFFF4F6F8.")] string themeJson)
        {
            try
            {
                var root = JsonNode.Parse(themeJson) as JsonObject
                    ?? throw new ArgumentException("themeJson root must be an object.");
                var design = HmiUnifiedThemeLayoutBuilder.BuildThemeDesign(root);
                return new ResponseJsonReport
                {
                    Ok = true,
                    Message = "Unified HMI theme design JSON built offline",
                    Data = design,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["offlineOnly"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid Unified HMI theme JSON: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "BuildUnifiedHmiLayoutDesignJson"), Description("[L2][HMI-Unified][Offline] Build ApplyUnifiedHmiScreenDesignJson-compatible JSON from a grid layout. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildUnifiedHmiLayoutDesignJson(
            [Description("layoutJson: JSON {grid?,left?,top?,gap?,columns?,cellWidth?,cellHeight?,items:[{name,type?,row?,col?,rowSpan?,colSpan?,text?,properties?}]}.")] string layoutJson)
        {
            try
            {
                var root = JsonNode.Parse(layoutJson) as JsonObject
                    ?? throw new ArgumentException("layoutJson root must be an object.");
                var design = HmiUnifiedThemeLayoutBuilder.BuildLayoutDesign(root);
                return new ResponseJsonReport
                {
                    Ok = true,
                    Message = "Unified HMI layout design JSON built offline",
                    Data = design,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["offlineOnly"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Invalid Unified HMI layout JSON: {ex.Message}", ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "ApplyUnifiedHmiTheme"), Description("[L2][HMI-Unified] Apply a theme/palette to a real Unified HMI screen through ApplyUnifiedHmiScreenDesignJson. Requires a connected TIA project; verify with DescribeHmiScreenItem/readback before saving.")]
        public static ResponseMessage ApplyUnifiedHmiTheme(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("themeJson: JSON accepted by BuildUnifiedHmiThemeDesignJson.")] string themeJson)
        {
            try
            {
                var design = BuildUnifiedHmiThemeDesignJson(themeJson).Data?.ToJsonString() ?? "{}";
                return Portal.ApplyUnifiedHmiScreenDesignJson(hmiSoftwarePath, screenName, design);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error applying Unified HMI theme: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ApplyUnifiedHmiLayout"), Description("[L2][HMI-Unified] Apply a grid layout to a real Unified HMI screen through ApplyUnifiedHmiScreenDesignJson. Requires a connected TIA project; verify changed items with DescribeHmiScreenItem/readback before saving.")]
        public static ResponseMessage ApplyUnifiedHmiLayout(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("layoutJson: JSON accepted by BuildUnifiedHmiLayoutDesignJson.")] string layoutJson)
        {
            try
            {
                var design = BuildUnifiedHmiLayoutDesignJson(layoutJson).Data?.ToJsonString() ?? "{}";
                return Portal.ApplyUnifiedHmiScreenDesignJson(hmiSoftwarePath, screenName, design);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error applying Unified HMI layout: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "BindUnifiedHmiButtonPressedTag"), Description("[L2][HMI-Unified]Bind a Unified HMI button PressedStateTags entry to an HMI tag (momentary press behavior, best-effort).")]
        public static ResponseMessage BindUnifiedHmiButtonPressedTag(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("tagName: HMI tag name to write while pressed")] string tagName)
        {
            try
            {
                return Portal.BindUnifiedHmiButtonPressedTag(hmiSoftwarePath, screenName, buttonName, tagName);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error binding button '{buttonName}' to tag '{tagName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ListUnifiedHmiApiTypes"), Description("[L2][HMI-Unified]List loaded WinCC Unified HMI API types/enums by name filter, useful for discovering event and dynamization types.")]
        public static ResponseStringList ListUnifiedHmiApiTypes(
            [Description("nameContains: case-insensitive substring filter, e.g. Dynamization or EventType")] string nameContains = "",
            [Description("limit: max returned type lines")] int limit = 500)
        {
            try
            {
                var items = Portal.ListUnifiedHmiApiTypes(nameContains, limit);
                return new ResponseStringList
                {
                    Message = $"Unified HMI API types listed (filter='{nameContains}')",
                    Items = items,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing Unified HMI API types: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiButtonEventHandler"), Description("[L2][HMI-Unified]Ensure a Unified HMI button event handler exists and return its API shape. eventType must match HmiButtonEventType.")]
        public static ResponseMessage EnsureUnifiedHmiButtonEventHandler(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: HmiButtonEventType value, e.g. Tapped, Down, Up; use ListUnifiedHmiApiTypes('HmiButtonEventType') to inspect")] string eventType)
        {
            try
            {
                return Portal.EnsureUnifiedHmiButtonEventHandler(hmiSoftwarePath, screenName, buttonName, eventType);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring button event handler '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DescribeUnifiedHmiButtonEventScript"), Description("[L2][HMI-Unified]Describe a Unified HMI button event handler Script property and its current object members/attributes.")]
        public static ResponseObjectDescribe DescribeUnifiedHmiButtonEventScript(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: enum value, e.g. Tapped, Down, Up")] string eventType,
            [Description("maxMembers: max member count")] int maxMembers = 200)
        {
            try
            {
                var res = Portal.DescribeUnifiedHmiButtonEventScript(hmiSoftwarePath, screenName, buttonName, eventType, maxMembers);
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
                throw new McpException($"Unexpected error describing button event script '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "SetUnifiedHmiButtonEventScriptCode"), Description("[L2][HMI-Unified]Set ScriptCode on a Unified HMI button event ScriptDynamization. SyntaxCheck is OFF by default because on TIA V21 it can crash the Portal process and lose the script (issue #36); pass syntaxCheck=true only when you need that evidence.")]
        public static ResponseMessage SetUnifiedHmiButtonEventScriptCode(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: enum value, e.g. Tapped, Down, Up")] string eventType,
            [Description("scriptCode: JavaScript code for the event")] string scriptCode,
            [Description("globalDefinitionAreaScriptCode: optional global definitions for the script")] string globalDefinitionAreaScriptCode = "",
            [Description("async: whether the script is async")] bool async = false,
            [Description("syntaxCheck: run TIA SyntaxCheck after writing. Default false - on TIA V21 this call can crash the Portal process (NonRecoverableException) and lose the just-written script. When false, no syntaxErrorCount is reported: absence means NOT CHECKED, never zero errors.")] bool syntaxCheck = false)
        {
            try
            {
                return Portal.SetUnifiedHmiButtonEventScriptCode(hmiSoftwarePath, screenName, buttonName, eventType, scriptCode, globalDefinitionAreaScriptCode, async, syntaxCheck);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error setting button event script '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "BuildUnifiedHmiButtonActionScript"), Description("[L2][HMI-Unified]Build a safe Unified HMI button action script from a high-level action recipe without connecting to TIA.")]
        public static ResponseMessage BuildUnifiedHmiButtonActionScript(
            [Description("actionKind: set-bit, reset-bit, toggle-bit, open-popup, goto-screen, confirm-write")] string actionKind,
            [Description("eventType: HmiButtonEventType value, e.g. Down (press), Up (release), Tapped — NOT Pressed/Released")] string eventType,
            [Description("targetTag: target HMI tag for set/reset/toggle actions")] string targetTag = "",
            [Description("targetScreen: target screen for goto-screen actions")] string targetScreen = "",
            [Description("targetPopup: target popup for open-popup actions")] string targetPopup = "")
        {
            try
            {
                var tags = string.IsNullOrWhiteSpace(targetTag)
                    ? Array.Empty<string>()
                    : new[] { targetTag };
                var recipe = HmiActionScriptRecipeBuilder.Build(actionKind, eventType, tags, targetScreen, targetPopup);
                return new ResponseMessage
                {
                    Message = recipe["ok"]?.GetValue<bool>() == true
                        ? "Unified HMI button action script recipe built."
                        : "Unified HMI button action script recipe has validation errors.",
                    Meta = recipe
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error building Unified HMI button action script: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "RunHmiActionScriptRecipeSafetySelfTest"), Description("[L2][Diagnostics]Offline-only helper: prove deterministic HMI button action scripts are allowed only for safe set/reset/toggle bit recipes, while high-risk writes and unverified navigation/popup recipes are blocked.")]
        public static ResponseJsonReport RunHmiActionScriptRecipeSafetySelfTest()
        {
            try
            {
                var data = HmiActionScriptRecipeBuilder.RunSafetySelfTest();
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "HMI action script recipe safety self-test passed" : "HMI action script recipe safety self-test failed",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error running HMI action script recipe safety self-test: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiButtonAction"), Description("[L2][HMI-Unified]Generate and apply a deterministic Unified HMI button action. Only set-bit/reset-bit/toggle-bit are applied; high-risk or TODO recipes are rejected. SyntaxCheck is OFF by default (issue #36: it can crash TIA V21).")]
        public static ResponseMessage EnsureUnifiedHmiButtonAction(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("buttonName: HMI button item name")] string buttonName,
            [Description("eventType: HmiButtonEventType value, e.g. Down (press), Up (release), Tapped — NOT Pressed/Released")] string eventType,
            [Description("actionKind: set-bit, reset-bit, or toggle-bit")] string actionKind,
            [Description("targetTag: verified target HMI tag")] string targetTag,
            [Description("syntaxCheck: run TIA SyntaxCheck after writing the script. Default false - see SetUnifiedHmiButtonEventScriptCode. The applied recipes are generated by this server and already linted offline, so the check adds little and risks a V21 crash.")] bool syntaxCheck = false)
        {
            try
            {
                var recipe = HmiActionScriptRecipeBuilder.Build(actionKind, eventType, new[] { targetTag });
                var kind = recipe["recipeKind"]?.ToString() ?? "";
                var script = recipe["script"]?.ToString() ?? "";
                var allowed = new[] { "set-bit", "reset-bit", "toggle-bit" };
                if (!allowed.Contains(kind, StringComparer.OrdinalIgnoreCase))
                {
                    recipe["applyStatus"] = "rejected";
                    recipe["applyReason"] = "Only set-bit/reset-bit/toggle-bit recipes can be applied by this safe high-level tool.";
                    return new ResponseMessage { Message = "Unified HMI button action rejected by safety policy.", Meta = recipe };
                }
                if (recipe["ok"]?.GetValue<bool>() != true || string.IsNullOrWhiteSpace(script) || script.IndexOf("TODO", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    recipe["applyStatus"] = "rejected";
                    recipe["applyReason"] = "Recipe has errors, empty script, or TODO placeholder.";
                    return new ResponseMessage { Message = "Unified HMI button action rejected because the generated script is not directly applicable.", Meta = recipe };
                }

                var ensure = Portal.EnsureUnifiedHmiButtonEventHandler(hmiSoftwarePath, screenName, buttonName, eventType);
                var set = Portal.SetUnifiedHmiButtonEventScriptCode(hmiSoftwarePath, screenName, buttonName, eventType, script, "", false, syntaxCheck);
                recipe["applyStatus"] = set.Meta?["success"]?.GetValue<bool>() == true ? "applied" : "apply-failed";
                recipe["ensureMessage"] = ensure.Message ?? "";
                recipe["setMessage"] = set.Message ?? "";
                recipe["setMeta"] = set.Meta?.DeepClone();
                return new ResponseMessage
                {
                    Message = "Unified HMI button action applied via generated recipe.",
                    Meta = recipe
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error applying Unified HMI button action '{buttonName}.{eventType}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "EnsureUnifiedHmiDynamization"), Description("[L2][HMI-Unified]Ensure a Unified HMI item property dynamization exists using a concrete dynamization type and return its API shape.")]
        public static ResponseMessage EnsureUnifiedHmiDynamization(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("itemName: HMI screen item name")] string itemName,
            [Description("propertyName: target property name, e.g. BackColor or Visible")] string propertyName,
            [Description("dynamizationType: type short name/full name; empty tries common candidates and returns errors if unsupported")] string dynamizationType = "")
        {
            try
            {
                return Portal.EnsureUnifiedHmiDynamization(hmiSoftwarePath, screenName, itemName, propertyName, dynamizationType);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error ensuring dynamization '{itemName}.{propertyName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "BindUnifiedHmiTagDynamization"), Description("[L2][HMI-Unified]Ensure a Unified HMI TagDynamization exists for an item property and bind it to an HMI tag.")]
        public static ResponseMessage BindUnifiedHmiTagDynamization(
            [Description("hmiSoftwarePath: path to HMI software (e.g. 'HMI_RT_1')")] string hmiSoftwarePath,
            [Description("screenName: target screen name")] string screenName,
            [Description("itemName: HMI screen item name")] string itemName,
            [Description("propertyName: target property name, e.g. BackColor or Visible")] string propertyName,
            [Description("tagName: HMI tag name used as the dynamic source")] string tagName,
            [Description("dataType: tag data type, e.g. Bool, Int, Real")] string dataType = "Bool",
            [Description("plcTag: optional PLC tag/path")] string plcTag = "",
            [Description("address: optional absolute address")] string address = "")
        {
            try
            {
                return Portal.BindUnifiedHmiTagDynamization(hmiSoftwarePath, screenName, itemName, propertyName, tagName, dataType, plcTag, address);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error binding tag dynamization '{itemName}.{propertyName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetHmiScreens"), Description("[L2][HMI] List all screen names in an HMI (Classic or Unified). Requires: Connect + OpenProject. softwarePath from GetProjectTree. Use before EnsureUnifiedHmiScreen/ExportHmiScreen to confirm which screens exist.")]
        public static ResponseStringList GetHmiScreens(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetHmiScreens(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI screens listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing HMI screens for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetHmiTagTables"), Description("[L2][HMI]List HMI tag table names (Classic/Unified, best-effort)")]
        public static ResponseStringList GetHmiTagTables(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetHmiTagTables(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI tag tables listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing HMI tag tables for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetHmiTags"), Description("[L2][HMI]List HMI tag names (best-effort). If tagTableName empty, returns tags found at root collection if available.")]
        public static ResponseStringList GetHmiTags(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("tagTableName: optional tag table name to list tags from")] string tagTableName = "")
        {
            try
            {
                var items = Portal.GetHmiTags(softwarePath, tagTableName);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI tags listed for '{softwarePath}' (table='{tagTableName}')",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing HMI tags for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetHmiConnections"), Description("[L2][HMI]List HMI connection names (Classic/Unified, best-effort)")]
        public static ResponseStringList GetHmiConnections(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetHmiConnections(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"HMI connections listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing HMI connections for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportHmiScreen"), Description("[L2][HMI]Export one HMI screen to a file (best-effort; requires Openness export support)")]
        public static ResponseExportFile ExportHmiScreen(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: the screen name to export")] string screenName,
            [Description("exportPath: full file path to write to (e.g. C:\\\\temp\\\\screen.xml)")] string exportPath)
        {
            try
            {
                Portal.ExportHmiScreen(softwarePath, screenName, exportPath);
                return new ResponseExportFile
                {
                    Message = $"HMI screen '{screenName}' exported",
                    ExportPath = exportPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed exporting HMI screen '{screenName}' from '{softwarePath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting HMI screen '{screenName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportHmiTagTable"), Description("[L2][HMI]Export one HMI tag table to a file (best-effort; requires Openness export support)")]
        public static ResponseExportFile ExportHmiTagTable(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("tagTableName: the tag table name to export")] string tagTableName,
            [Description("exportPath: full file path to write to (e.g. C:\\\\temp\\\\tagtable.xml)")] string exportPath)
        {
            try
            {
                Portal.ExportHmiTagTable(softwarePath, tagTableName, exportPath);
                return new ResponseExportFile
                {
                    Message = $"HMI tag table '{tagTableName}' exported",
                    ExportPath = exportPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed exporting HMI tag table '{tagTableName}' from '{softwarePath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting HMI tag table '{tagTableName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportHmiConnection"), Description("[L2][HMI]Export one HMI connection to a file (best-effort; Classic/Unified via reflection)")]
        public static ResponseExportFile ExportHmiConnection(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("connectionName: the HMI connection name to export")] string connectionName,
            [Description("exportPath: full file path to write to (e.g. C:\\\\temp\\\\connection.xml)")] string exportPath)
        {
            try
            {
                Portal.ExportHmiConnection(softwarePath, connectionName, exportPath);
                return new ResponseExportFile
                {
                    Message = $"HMI connection '{connectionName}' exported",
                    ExportPath = exportPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed exporting HMI connection '{connectionName}' from '{softwarePath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting HMI connection '{connectionName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportHmiProgram"), Description("[L2][HMI]Batch export HMI screens/tagtables into a directory (best-effort)")]
        public static ResponseBatchExport ExportHmiProgram(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("exportDir: directory to write exported files into")] string exportDir,
            [Description("exportScreens: default true")] bool exportScreens = true,
            [Description("exportTagTables: default true")] bool exportTagTables = true)
        {
            try
            {
                var res = Portal.ExportHmiProgram(softwarePath, exportDir, exportScreens, exportTagTables);
                if (res != null)
                {
                    return new ResponseBatchExport
                    {
                        Message = $"HMI program exported to '{exportDir}'",
                        Exported = res.Value.Exported,
                        Failed = res.Value.Failed,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }
                throw new McpException($"HMI software not found at '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting HMI program: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportHmiScreen"), Description("[L2][HMI]Import one HMI screen XML file into an HMI program (best-effort; Classic/Unified via reflection)")]
        public static ResponseMessage ImportHmiScreen(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional screen group path inside HMI (use empty for root)")] string folderPath,
            [Description("importPath: full file path of exported screen XML")] string importPath)
        {
            try
            {
                Portal.ImportHmiScreen(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"HMI screen imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed importing HMI screen from '{importPath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing HMI screen: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportHmiTagTable"), Description("[L2][HMI]Import one HMI tag table XML file into an HMI program (best-effort; Classic/Unified via reflection)")]
        public static ResponseMessage ImportHmiTagTable(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional tag table group path inside HMI (use empty for root)")] string folderPath,
            [Description("importPath: full file path of exported tag table XML")] string importPath)
        {
            try
            {
                Portal.ImportHmiTagTable(softwarePath, folderPath, importPath);
                return new ResponseMessage
                {
                    Message = $"HMI tag table imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed importing HMI tag table from '{importPath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing HMI tag table: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportHmiConnection"), Description("[L2][HMI]Import one HMI connection XML file into an HMI program (best-effort; Classic/Unified via reflection)")]
        public static ResponseMessage ImportHmiConnection(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("importPath: full file path of exported HMI connection XML")] string importPath)
        {
            try
            {
                Portal.ImportHmiConnection(softwarePath, importPath);
                return new ResponseMessage
                {
                    Message = $"HMI connection imported from '{importPath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException($"Failed importing HMI connection from '{importPath}' [{pex.Code}]: {pex.Message}", pex, McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing HMI connection: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportHmiScreensFromDirectory"), Description("[L2][HMI]Batch import HMI screen .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportHmiScreensFromDirectory(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional screen group path inside HMI (use empty for root)")] string folderPath,
            [Description("dir: directory containing exported screen XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportHmiScreensFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} HMI screens from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing HMI screens from '{dir}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ImportHmiTagTablesFromDirectory"), Description("[L2][HMI]Batch import HMI tag table .xml files from a directory (best-effort)")]
        public static ResponseImportBatch ImportHmiTagTablesFromDirectory(
            [Description("softwarePath: path in the project structure to the HMI software")] string softwarePath,
            [Description("folderPath: optional tag table group path inside HMI (use empty for root)")] string folderPath,
            [Description("dir: directory containing exported tag table XML files")] string dir,
            [Description("regexName: optional regex filter applied to filename without extension")] string regexName = "",
            [Description("overwrite: true=Override (default)")] bool overwrite = true)
        {
            try
            {
                var result = Portal.ImportHmiTagTablesFromDirectory(softwarePath, folderPath, dir, regexName, overwrite);
                return new ResponseImportBatch
                {
                    Message = $"Imported {result.Imported?.Count() ?? 0} HMI tag tables from '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing HMI tag tables from '{dir}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }
    }
}
