using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using TiaMcpServer.Siemens;


namespace TiaMcpServer.ModelContextProtocol
{
    // Partial: project/session. Extracted from McpServer.cs (god-file split); behavior unchanged.
    public static partial class McpServer
    {
        #region project/session

        [McpServerTool(Name = "GetProject"), Description("[L1][Project] List all open local projects and multi-user sessions with their attributes. Requires: Connect. Use this to confirm which project is active, or to find the project name for AttachToOpenProject.")]
        public static ResponseGetProjects GetProjects()
        {
            try
            {
                var list = Portal.GetProjects();

                list.AddRange(Portal.GetSessions());

                var responseList = new List<ResponseProjectInfo>();
                foreach (var project in list)
                {
                    var attributes = Helper.GetAttributeList(project);

                    if (project != null)
                    {
                        responseList.Add(new ResponseProjectInfo
                        {
                            Name = project.Name,
                            Attributes = attributes
                        });
                    }
                }

                return new ResponseGetProjects
                {
                    Message = "Open projects and sessions retrieved",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving open projects: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "OpenProject"), Description("[L1][Project] Open a TIA Portal V21 project (.ap21) or multi-user session (.als21). Validates the target file before switching from the current project. Requires: Connect. After success, call GetProjectTree to explore its structure.")]
        public static ResponseOpenProject OpenProject(
            [Description("path: defines the path where to the project/session")] string path,
            [Description("closeForeignProject: DEFAULT false. If TIA already has a project open that this session did not open, the call is REFUSED rather than closing the user's work. Only pass true after the user has agreed to close it.")] bool closeForeignProject = false)
        {
            try
            {
                try { path = ProjectFilePath.Resolve(path); }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new McpException(ex.Message, ex, McpErrorCode.InvalidParams);
                }

                var foreign = Portal.ForeignOpenProjectName();
                if (foreign != null && !closeForeignProject)
                    throw new McpException(
                        "OpenProject refused: TIA Portal already has the project '" + foreign + "' open and this " +
                        "session did not open it - it belongs to the user. OpenProject closes the current project " +
                        "first, which would discard any unsaved edits. To work on that project call " +
                        "AttachToOpenProject(projectName=\"" + foreign + "\"). To close it anyway pass " +
                        "closeForeignProject=true - ask the user before you do.",
                        McpErrorCode.InvalidRequest);

                if (Portal.ProjectIsValid)
                {
                    Portal.CloseProject();
                }

                bool success = ProjectFilePath.IsSession(path)
                    ? Portal.OpenSession(path)
                    : Portal.OpenProject(path, closeForeignProject);

                if (success)
                {
                    return new ResponseOpenProject
                    {
                        Message = $"Project '{path}' opened",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    var detail = Portal.LastConnectError;
                    throw new McpException(
                        string.IsNullOrWhiteSpace(detail)
                            ? $"Failed to open project '{path}'"
                            : $"Failed to open project '{path}': {detail}",
                        McpErrorCode.InternalError);
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error opening project '{path}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "AttachToOpenProject"), Description("[L1][Project]Attach MCP to an already-open TIA Portal project by name (avoids disposed project handles).")]
        public static ResponseMessage AttachToOpenProject(
            [Description("projectName: name shown in TIA (e.g. '项目1')")] string projectName)
        {
            try
            {
                var ok = Portal.AttachToOpenProject(projectName);
                if (ok)
                {
                    return new ResponseMessage
                    {
                        Message = $"Attached to open project '{projectName}'",
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"Failed to attach to open project '{projectName}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error attaching to open project '{projectName}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "CreateProject"), Description("[L1][Project] Create a new empty TIA Portal project. Requires: Connect. After creation, call AddDevice to add PLCs/HMIs, then GetProjectTree to verify. The project is automatically opened after creation — no separate OpenProject call needed.")]
        public static ResponseMessage CreateProject(
            [Description("directoryPath: folder where project will be created")] string directoryPath,
            [Description("projectName: project name")] string projectName,
            [Description("closeForeignProject: DEFAULT false. If TIA already has a project open that this session did not open, the call is REFUSED rather than closing the user's work. Only pass true after the user has agreed to close it.")] bool closeForeignProject = false)
        {
            try
            {
                var foreign = Portal.ForeignOpenProjectName();
                if (foreign != null && !closeForeignProject)
                    throw new McpException(
                        "CreateProject refused: TIA Portal already has the project '" + foreign + "' open and this " +
                        "session did not open it - it belongs to the user. CreateProject closes the current project " +
                        "first, which would discard any unsaved edits. To work on that project call " +
                        "AttachToOpenProject(projectName=\"" + foreign + "\"). To close it anyway pass " +
                        "closeForeignProject=true - ask the user before you do.",
                        McpErrorCode.InvalidRequest);

                if (Portal.ProjectIsValid)
                {
                    Portal.CloseProject();
                }
                var ok = Portal.CreateProject(directoryPath, projectName, closeForeignProject);
                if (!ok)
                    throw new McpException($"Failed to create project '{projectName}' in '{directoryPath}'", McpErrorCode.InternalError);

                return new ResponseMessage
                {
                    Message = $"Project '{projectName}' created in '{directoryPath}'",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating project: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        // NOTE: Deprecated demo tool removed.
        // In V21, prefer importing block XML via ImportBlock/ImportBlocksFromDirectory, then CompileSoftware.

        [McpServerTool(Name = "ScaffoldProject"), Description("[L1][Project] Generate a TIA Portal V21 project from one JSON spec: add PLC and optional Unified HMI hardware, import PLC artifacts and SCL/LAD sources, compile, apply HMI connections/screens/tags, and save. The complete specification is validated offline before any TIA connection or project change. dryRun defaults to true and returns only that preflight report. Execution collects per-element failures; automatic saving runs only after all requested steps succeed. On failure the current in-memory project remains available for inspection. Spec keys: projectName(required); directoryPath?; plcName?(PLC_1); plcFamily?(S7-1500); plcMlfb?; hmiName?; hmiFamily?(WinCCUnifiedPC); hmiSoftwarePath?(within hmiName); connectionName?(HMI_Connection_1); udt?/globalDb?/tagTable? = arrays accepted by PlcBuildAndImport; sclSourceFiles? = source paths; ladDocs? = {importPath,name} arrays; hmiScreens? = {screenName,width?,height?,designJson} arrays; hmiTags? = {tagTableName?,tagName,hmiDataType?,plcTag?,address?} arrays; compile?(true); save?(true). Invalid field types are reported with their JSON paths.")]
        public static ResponseScaffold ScaffoldProject(
            [Description("spec: JSON object describing the project to generate. See tool description for keys.")] string spec,
            [Description("dryRun: DEFAULT true; validate the complete specification offline. Pass false to execute after successful preflight.")] bool dryRun = true)
            => RunProjectWorkflow(spec, ProjectWorkflowMode.Create, dryRun);

        [McpServerTool(Name = "SaveProject"), Description("[L1][Project] Save the currently open project or session to disk. Requires: Connect + OpenProject. Call after any significant change (device add, block import, HMI edit). Compile first if there are pending changes to ensure consistency.")]
        public static ResponseSaveProject SaveProject()
        {
            try
            {
                if (Portal.IsLocalSession)
                {
                    if (Portal.SaveSession())
                    {
                        return new ResponseSaveProject
                        {
                            Message = "Local session saved",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed to save local session", McpErrorCode.InternalError);
                    }
                }
                else
                {
                    if (Portal.SaveProject())
                    {
                        return new ResponseSaveProject
                        {
                            Message = "Local project saved",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed to save project", McpErrorCode.InternalError);
                    }
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error saving local project/session: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "SaveAsProject"), Description("[L2][Project]Save current TIA-Portal project/session with a new name")]
        public static ResponseSaveAsProject SaveAsProject(
            [Description("newProjectPath: defines the new path where to save the project")] string newProjectPath)
        {
            try
            {
                if (Portal.IsLocalSession)
                {
                    throw new McpException($"Cannot save local session as '{newProjectPath}'", McpErrorCode.InvalidParams);
                }
                else
                {
                    if (Portal.SaveAsProject(newProjectPath))
                    {
                        return new ResponseSaveAsProject
                        {
                            Message = $"Local project saved as '{newProjectPath}'",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException($"Failed saving local project as '{newProjectPath}'", McpErrorCode.InternalError);
                    }
                }

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error saving local project/session as '{newProjectPath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "CloseProject"), Description("[L1][Project] Close the currently open project or multi-user session. Requires: Connect + OpenProject. Any unsaved changes are lost — call SaveProject first. After closing, the connection remains active but no project is open.")]
        public static ResponseCloseProject CloseProject()
        {
            try
            {
                bool success;

                if (Portal.IsLocalSession)
                {
                    success = Portal.CloseSession();
                    if (success)
                    {
                        return new ResponseCloseProject
                        {
                            Message = "Local session closed",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed closing local session", McpErrorCode.InternalError);
                    }
                }
                else
                {
                    success = Portal.CloseProject();
                    if (success)
                    {
                        return new ResponseCloseProject
                        {
                            Message = "Local project closed",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed closing project", McpErrorCode.InternalError);
                    }
                }

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error closing local project/session: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        #endregion
    }
}
