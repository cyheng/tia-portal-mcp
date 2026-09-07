using Microsoft.Extensions.Logging;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Siemens
{
    // HMI 软件对象查询、路径解析与单项导入导出。
    public partial class Portal
    {
        public (string? Name, string ProgramType, List<string> Screens)? GetHmiProgramInfo(string softwarePath)
        {
            _logger?.LogInformation($"Getting HMI program info by path: {softwarePath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null)
            {
                return null;
            }

            var sw = softwareContainer.Software;

            // Classic WinCC (HmiTarget)
            if (sw is HmiTarget classic)
            {
                return (classic.Name, "Classic", TryListScreens(classic));
            }

            // Unified (HmiSoftware)
            if (sw is HmiSoftware unified)
            {
                return (unified.Name, "Unified", TryListScreens(unified));
            }

            return (sw.ToString(), "Unknown", new List<string>());
        }

        public ModelContextProtocol.ResponseObjectDescribe DescribeHmiSoftware(string softwarePath, int maxMembers = 200)
        {
            if (IsProjectNull())
            {
                return new ModelContextProtocol.ResponseObjectDescribe
                {
                    Message = "Project is null",
                    ObjectKind = "Software",
                    ObjectPath = softwarePath,
                    TypeName = null,
                    Members = Array.Empty<ModelContextProtocol.ObjectMember>()
                };
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"HMI software not found: {softwarePath}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            var sw = softwareContainer.Software;
            return new ModelContextProtocol.ResponseObjectDescribe
            {
                Message = "OK",
                ObjectKind = "Software",
                ObjectPath = softwarePath,
                TypeName = sw.GetType().FullName ?? sw.GetType().Name,
                Members = DescribeMembers(sw, Math.Max(10, Math.Min(2000, maxMembers))).ToList()
            };
        }

        public ModelContextProtocol.ResponseObjectDescribe DescribeHmiScreen(string softwarePath, string screenName, int maxMembers = 200)
        {
            if (IsProjectNull())
            {
                return new ModelContextProtocol.ResponseObjectDescribe
                {
                    Message = "Project is null",
                    ObjectKind = "HmiScreen",
                    ObjectPath = $"{softwarePath}:{screenName}",
                    TypeName = null,
                    Members = Array.Empty<ModelContextProtocol.ObjectMember>()
                };
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"HMI software not found: {$"{softwarePath}:{screenName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            var sw = softwareContainer.Software;
            var screen = TryFindByNameInCollection(sw, new[] { "Screens", "ScreenFolder" }, screenName);
            if (screen == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen not found: {$"{softwarePath}:{screenName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            return new ModelContextProtocol.ResponseObjectDescribe
            {
                Message = "OK",
                ObjectKind = "HmiScreen",
                ObjectPath = $"{softwarePath}:{screenName}",
                TypeName = screen.GetType().FullName ?? screen.GetType().Name,
                Members = DescribeMembers(screen, Math.Max(10, Math.Min(2000, maxMembers))).ToList()
            };
        }

        public ModelContextProtocol.ResponseObjectDescribe DescribeHmiTagTable(string softwarePath, string tagTableName, int maxMembers = 200)
        {
            if (IsProjectNull())
            {
                return new ModelContextProtocol.ResponseObjectDescribe
                {
                    Message = "Project is null",
                    ObjectKind = "HmiTagTable",
                    ObjectPath = $"{softwarePath}:{tagTableName}",
                    TypeName = null,
                    Members = Array.Empty<ModelContextProtocol.ObjectMember>()
                };
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"HMI software not found: {$"{softwarePath}:{tagTableName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            var sw = softwareContainer.Software;
            var table = TryFindHmiTagTable(sw, tagTableName);
            if (table == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Tag table not found: {$"{softwarePath}:{tagTableName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            return new ModelContextProtocol.ResponseObjectDescribe
            {
                Message = "OK",
                ObjectKind = "HmiTagTable",
                ObjectPath = $"{softwarePath}:{tagTableName}",
                TypeName = table.GetType().FullName ?? table.GetType().Name,
                Members = DescribeMembers(table, Math.Max(10, Math.Min(2000, maxMembers))).ToList()
            };
        }

        public ModelContextProtocol.ResponseObjectDescribe DescribeHmiTag(string softwarePath, string tagTableName, string tagName, int maxMembers = 200)
        {
            if (IsProjectNull())
            {
                return new ModelContextProtocol.ResponseObjectDescribe
                {
                    Message = "Project is null",
                    ObjectKind = "HmiTag",
                    ObjectPath = $"{softwarePath}:{tagTableName}:{tagName}",
                    TypeName = null,
                    Members = Array.Empty<ModelContextProtocol.ObjectMember>()
                };
            }

            var sc = GetSoftwareContainer(softwarePath);
            if (sc?.Software == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"HMI software not found: {$"{softwarePath}:{tagTableName}:{tagName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            var sw = sc.Software;
            var table = TryFindHmiTagTable(sw, tagTableName);
            if (table == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Tag table not found: {$"{softwarePath}:{tagTableName}:{tagName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            var tagsComp = table.GetType().GetProperty("Tags")?.GetValue(table);
            if (tagsComp == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"tagTable.Tags not found: {$"{softwarePath}:{tagTableName}:{tagName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            object? tagObj = null;
            try
            {
                if (tagsComp is System.Collections.IEnumerable en)
                {
                    foreach (var it in en)
                    {
                        var n = TryGetName(it);
                        if (!string.IsNullOrWhiteSpace(n) && string.Equals(n!.Trim(), tagName, StringComparison.OrdinalIgnoreCase))
                        {
                            tagObj = it;
                            break;
                        }
                    }
                }
            }
            catch { }

            // 原来这里还有一次 TryFindByNameInCollection(tagsComp, Array.Empty<string>(), tagName) 的"兜底"，
            // 该方法只遍历 propertyHints，空数组＝循环体一次不进＝恒返回 null，纯死代码，已删。

            if (tagObj == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Tag not found: {$"{softwarePath}:{tagTableName}:{tagName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            return new ModelContextProtocol.ResponseObjectDescribe
            {
                Message = "OK",
                ObjectKind = "HmiTag",
                ObjectPath = $"{softwarePath}:{tagTableName}:{tagName}",
                TypeName = tagObj.GetType().FullName ?? tagObj.GetType().Name,
                Members = DescribeMembers(tagObj, Math.Max(10, Math.Min(2000, maxMembers))).ToList()
            };
        }

        public ModelContextProtocol.ResponseObjectDescribe DescribeHmiScreenItem(string softwarePath, string screenName, string itemName, int maxMembers = 200)
        {
            if (IsProjectNull())
            {
                return new ModelContextProtocol.ResponseObjectDescribe
                {
                    Message = "Project is null",
                    ObjectKind = "HmiScreenItem",
                    ObjectPath = $"{softwarePath}:{screenName}:{itemName}",
                    TypeName = null,
                    Members = Array.Empty<ModelContextProtocol.ObjectMember>()
                };
            }

            var sc = GetSoftwareContainer(softwarePath);
            if (sc?.Software == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"HMI software not found: {$"{softwarePath}:{screenName}:{itemName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            var sw = sc.Software;
            var screen = TryFindByNameInCollection(sw, new[] { "Screens", "ScreenFolder" }, screenName);
            if (screen == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen not found: {$"{softwarePath}:{screenName}:{itemName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            var itemsComp = screen.GetType().GetProperty("ScreenItems")?.GetValue(screen);
            if (itemsComp == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"screen.ScreenItems not found: {$"{softwarePath}:{screenName}:{itemName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            object? itemObj = null;
            try
            {
                if (itemsComp is System.Collections.IEnumerable en)
                {
                    foreach (var it in en)
                    {
                        var n = TryGetName(it);
                        if (!string.IsNullOrWhiteSpace(n) && string.Equals(n!.Trim(), itemName, StringComparison.OrdinalIgnoreCase))
                        {
                            itemObj = it;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (itemObj == null)
            {
                // 「找不到」返回一条正常响应 + 空成员表，调用方看到的是 isError=false，
                // 会把「我路径写错了」记成「这个对象确实没有任何成员」。Describe 系工具正是
                // 用来摸索路径的，摸错必须响。
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen item not found: {$"{softwarePath}:{screenName}:{itemName}"}. Resolve the exact path first "
                    + "(GetProjectTree / GetDevices / GetHmiScreens / GetHmiTagTables).");
            }

            return new ModelContextProtocol.ResponseObjectDescribe
            {
                Message = "OK",
                ObjectKind = "HmiScreenItem",
                ObjectPath = $"{softwarePath}:{screenName}:{itemName}",
                TypeName = itemObj.GetType().FullName ?? itemObj.GetType().Name,
                Members = DescribeMembers(itemObj, Math.Max(10, Math.Min(2000, maxMembers))).ToList()
            };
        }

        private ResponseMessage RunHmiStepTool(string toolName, Func<JsonObject, string> action)
        {
            var meta = new JsonObject
            {
                ["timestamp"] = DateTime.Now,
                ["tool"] = toolName,
                ["success"] = false
            };

            try
            {
                if (IsProjectNull())
                {
                    meta["error"] = "Project is null";
                    return new ResponseMessage { Message = "Project is null", Meta = meta };
                }

                var message = action(meta);
                meta["success"] = true;
                return new ResponseMessage { Message = message, Meta = meta };
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                meta["error"] = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
                return new ResponseMessage { Message = $"{toolName} failed", Meta = meta };
            }
            catch (Exception ex)
            {
                meta["error"] = ex.ToString();
                return new ResponseMessage { Message = $"{toolName} failed", Meta = meta };
            }
        }

        private object ResolveHmiSoftwareOrThrow(string hmiSoftwarePath)
        {
            var sc = GetSoftwareContainer(hmiSoftwarePath);
            if (sc?.Software == null)
            {
                throw new InvalidOperationException($"HMI software not found at '{hmiSoftwarePath}'.");
            }

            return sc.Software;
        }

        private object ResolveHmiScreenOrThrow(string hmiSoftwarePath, string screenName)
        {
            var sw = ResolveHmiSoftwareOrThrow(hmiSoftwarePath);
            var screen = TryFindByNameInCollection(sw, new[] { "Screens", "ScreenFolder" }, screenName);
            if (screen == null)
            {
                throw new InvalidOperationException($"HMI screen '{screenName}' not found.");
            }

            return screen;
        }

        private object ResolveHmiScreenItemOrThrow(string hmiSoftwarePath, string screenName, string itemName)
        {
            var screen = ResolveHmiScreenOrThrow(hmiSoftwarePath, screenName);
            var items = TryGetPropertyValue(screen, "ScreenItems");
            if (items == null)
            {
                throw new InvalidOperationException($"ScreenItems collection not found on screen '{screenName}'.");
            }

            var item = FindExistingByName(items, itemName);
            if (item == null)
            {
                throw new InvalidOperationException($"Screen item '{itemName}' not found on screen '{screenName}'.");
            }

            return item;
        }

        private static object TryGetHmiTagRoot(object hmiSoftware)
        {
            return TryGetPropertyValue(hmiSoftware,
                       "TagTableFolder",
                       "TagFolder",
                       "HmiTagTableFolder",
                       "HmiTagFolder",
                       "TagTableGroup",
                       "HmiTagTableGroup")
                   ?? hmiSoftware;
        }

        private static object? TryGetHmiTagTablesCollection(object hmiSoftware)
        {
            var root = TryGetHmiTagRoot(hmiSoftware);
            return TryGetPropertyValue(root,
                       "TagTables",
                       "HmiTagTables",
                       "Tables")
                   ?? TryGetPropertyValue(hmiSoftware,
                       "TagTables",
                       "HmiTagTables",
                       "Tables");
        }

        private static object? TryFindHmiTagTable(object hmiSoftware, string tagTableName)
        {
            var root = TryGetHmiTagRoot(hmiSoftware);
            return TryFindByNameInCollection(root, new[] { "TagTables", "HmiTagTables", "Tables" }, tagTableName)
                   ?? TryFindByNameInCollection(hmiSoftware, new[] { "TagTables", "HmiTagTables", "Tables" }, tagTableName)
                   ?? FindExistingByName(TryGetHmiTagTablesCollection(hmiSoftware) ?? root, tagTableName);
        }

        public List<string>? GetHmiScreens(string softwarePath)
        {
            if (IsProjectNull()) return null;
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) return null;
            return TryListScreens(softwareContainer.Software);
        }

        public List<string>? GetHmiTagTables(string softwarePath)
        {
            if (IsProjectNull()) return null;
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) return null;
            var sw = softwareContainer.Software;
            var tables = TryGetHmiTagTablesCollection(sw);
            if (tables == null) return new List<string>();
            return TryListNamesFromCollection(tables, Array.Empty<string>(), "TagTables");
        }

        public List<string>? GetHmiTags(string softwarePath, string tagTableName = "")
        {
            if (IsProjectNull()) return null;
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) return null;

            var sw = softwareContainer.Software;
            var tagRoot = TryGetHmiTagRoot(sw);
            object? tagTable = string.IsNullOrWhiteSpace(tagTableName)
                ? null
                : TryFindHmiTagTable(sw, tagTableName);

            var root = tagTable ?? tagRoot;
            return TryListNamesFromCollection(root, new[] { "Tags" }, "Tags");
        }

        public List<string>? GetHmiConnections(string softwarePath)
        {
            if (IsProjectNull()) return null;
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) return null;
            var sw = softwareContainer.Software;
            var connections = TryGetPropertyValue(sw, "Connections");
            if (connections == null) return new List<string>();
            return TryListNamesFromCollection(connections, Array.Empty<string>(), "Connections");
        }

        public void ExportHmiScreen(string softwarePath, string screenName, string exportPath)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "Project is null");
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI software not found: {softwarePath}");

            var screen = TryFindByNameInCollection(softwareContainer.Software, new[] { "Screens", "ScreenFolder" }, screenName);
            if (screen == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI screen not found: {screenName}");

            if (!TryExportEngineeringObject(screen, exportPath, out var err))
                throw new PortalException(PortalErrorCode.ExportFailed, err ?? "HMI screen export failed");
        }

        public void ExportHmiTagTable(string softwarePath, string tagTableName, string exportPath)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "Project is null");
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI software not found: {softwarePath}");

            var sw = softwareContainer.Software;
            var table = TryFindHmiTagTable(sw, tagTableName);
            if (table == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI tag table not found: {tagTableName}");

            if (!TryExportEngineeringObject(table, exportPath, out var err))
                throw new PortalException(PortalErrorCode.ExportFailed, err ?? "HMI tag table export failed");
        }

        public void ExportHmiConnection(string softwarePath, string connectionName, string exportPath)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "Project is null");
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI software not found: {softwarePath}");

            var sw = softwareContainer.Software;
            var connections = TryGetPropertyValue(sw, "Connections");
            if (connections == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI Connections collection not found on '{softwarePath}'");

            // 去掉 ?? TryFindByNameInCollection(connections, Array.Empty<string>(), ...)：空 hints 恒返回 null。
            var connection = FindExistingByName(connections, connectionName);
            if (connection == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI connection not found: {connectionName}");

            if (!TryExportEngineeringObject(connection, exportPath, out var err))
                throw new PortalException(PortalErrorCode.ExportFailed, err ?? "HMI connection export failed");
        }

        public string ProbeClassicHmiConnectionCreation(string softwarePath, string connectionName, string exportPath)
        {
            var sb = new StringBuilder();
            if (IsProjectNull()) return "Project is null";

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) return "HMI software not found: " + softwarePath;

            var sw = softwareContainer.Software;
            var connections = TryGetPropertyValue(sw, "Connections");
            if (connections == null) return "Connections collection not found. swType=" + sw.GetType().FullName;

            sb.AppendLine("SoftwareType=" + (sw.GetType().FullName ?? sw.GetType().Name));
            sb.AppendLine("ConnectionsType=" + (connections.GetType().FullName ?? connections.GetType().Name));
            sb.AppendLine("ConnectionsPublicMembers:");
            foreach (var line in DescribeTypeMembers(connections.GetType(), false).Take(120))
            {
                sb.AppendLine("  " + line);
            }
            sb.AppendLine("ConnectionsExplicitMembers:");
            foreach (var line in DescribeTypeMembers(connections.GetType(), true).Take(160))
            {
                sb.AppendLine("  " + line);
            }

            var connectionType = FindTypeBySuffix("Siemens.Engineering.Hmi.Communication.Connection")
                ?? FindTypeBySuffix("Hmi.Communication.Connection")
                ?? FindTypeBySuffix("Communication.Connection");
            sb.AppendLine("ConnectionType=" + (connectionType?.FullName ?? "<not found>"));

            // 去掉 ?? TryFindByNameInCollection(connections, Array.Empty<string>(), ...)：空 hints 恒返回 null。
            var existing = FindExistingByName(connections, connectionName);
            if (existing != null)
            {
                sb.AppendLine("ExistingConnection=" + connectionName);
                if (TryExportEngineeringObject(existing, exportPath, out var existingExportErr))
                {
                    sb.AppendLine("ExportExisting=OK :: " + exportPath);
                }
                else
                {
                    sb.AppendLine("ExportExisting=FAIL :: " + existingExportErr);
                }
                return sb.ToString();
            }

            if (connectionType == null)
            {
                sb.AppendLine("Create=SKIP :: connection type not found");
                return sb.ToString();
            }

            sb.AppendLine("CreationInfos:");
            var creationInfos = TryInvokeExplicitEngineeringMethod(connections, "GetCreationInfos", Array.Empty<object?>(), out var creationInfoErr);
            if (creationInfos == null && !string.IsNullOrWhiteSpace(creationInfoErr))
            {
                sb.AppendLine("  GetCreationInfos(\"\") failed: " + creationInfoErr);
            }
            else
            {
                foreach (var line in FormatEnumerableObjects(creationInfos, 80))
                {
                    sb.AppendLine("  " + line);
                }
            }

            object? created = null;
            string? createErr = null;
            var attempts = new[]
            {
                new { Description = "Name only", Parameters = new Dictionary<string, object?> { ["Name"] = connectionName } }
            };

            foreach (var attempt in attempts)
            {
                try
                {
                    sb.AppendLine($"CreateAttempt {attempt.Description}");
                    created = TryInvokeExplicitEngineeringMethod(
                        connections,
                        "Create",
                        new object?[] { connectionType, attempt.Parameters },
                        out createErr);
                    if (created != null)
                    {
                        sb.AppendLine("Create=OK :: type=" + (created.GetType().FullName ?? created.GetType().Name));
                        break;
                    }
                    sb.AppendLine("Create=FAIL :: " + (createErr ?? "<null result>"));
                }
                catch (Exception ex)
                {
                    createErr = FormatExceptionDetail(ex);
                    sb.AppendLine("Create=ERR :: " + createErr);
                }
            }

            // 去掉 ?? TryFindByNameInCollection(connections, Array.Empty<string>(), ...)：空 hints 恒返回 null。
            created ??= FindExistingByName(connections, connectionName);
            if (created == null)
            {
                sb.AppendLine("Readback=FAIL :: connection not found after create attempts");
                return sb.ToString();
            }

            sb.AppendLine("Readback=OK :: " + (TryGetName(created) ?? connectionName));
            if (TryExportEngineeringObject(created, exportPath, out var exportErr))
            {
                sb.AppendLine("ExportCreated=OK :: " + exportPath);
            }
            else
            {
                sb.AppendLine("ExportCreated=FAIL :: " + exportErr);
            }

            return sb.ToString();
        }

        public void ImportHmiScreen(string softwarePath, string folderPath, string importPath)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "No project is open. If a project is already open in the TIA Portal UI, call AttachToOpenProject(projectName); otherwise call OpenProject(path) for a local .ap21 project, or CreateProject to start a new one. (Connect is attempted automatically.)");

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI software not found: {softwarePath}");

            try
            {
                var sw = softwareContainer.Software;

                // Resolve screen folder then groups by folderPath
                object rootGroup = TryGetPropertyValue(sw, "ScreenFolder") ?? sw;
                var group = TryResolveChildGroupByPath(rootGroup, folderPath) ?? rootGroup;

                // Locate screens collection: group.Screens OR group.ScreenFolder.Screens
                var screens = TryGetPropertyValue(group, "Screens");
                if (screens == null)
                {
                    var nestedFolder = TryGetPropertyValue(group, "ScreenFolder");
                    if (nestedFolder != null)
                    {
                        screens = TryGetPropertyValue(nestedFolder, "Screens");
                    }
                }

                if (screens == null)
                    throw new PortalException(PortalErrorCode.NotFound, $"Screens collection not found. swType={sw.GetType().FullName} groupType={group.GetType().FullName}");

                if (TryImportEngineeringObjectIntoCollection(screens, importPath, out _, out var err))
                    return;

                throw new PortalException(PortalErrorCode.ImportFailed, err ?? "ImportHmiScreen failed");
            }
            catch (PortalException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.ImportFailed, ex.Message, null, ex);
            }
        }

        public void ImportHmiTagTable(string softwarePath, string folderPath, string importPath)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "No project is open. If a project is already open in the TIA Portal UI, call AttachToOpenProject(projectName); otherwise call OpenProject(path) for a local .ap21 project, or CreateProject to start a new one. (Connect is attempted automatically.)");

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI software not found: {softwarePath}");

            try
            {
                var sw = softwareContainer.Software;

                var tagRoot = TryGetHmiTagRoot(sw);

                var group = TryResolveChildGroupByPath(tagRoot, folderPath) ?? tagRoot;

                var tables = TryGetPropertyValue(group, "TagTables");
                if (tables == null)
                {
                    tables = TryGetHmiTagTablesCollection(sw);
                }

                if (tables == null)
                    throw new PortalException(PortalErrorCode.NotFound, $"TagTables collection not found. swType={sw.GetType().FullName} groupType={group.GetType().FullName}");

                if (TryImportEngineeringObjectIntoCollection(tables, importPath, out _, out var err))
                    return;

                throw new PortalException(PortalErrorCode.ImportFailed, err ?? "ImportHmiTagTable failed");
            }
            catch (PortalException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.ImportFailed, ex.Message, null, ex);
            }
        }

        public void ImportHmiConnection(string softwarePath, string importPath)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "No project is open. If a project is already open in the TIA Portal UI, call AttachToOpenProject(projectName); otherwise call OpenProject(path) for a local .ap21 project, or CreateProject to start a new one. (Connect is attempted automatically.)");

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null) throw new PortalException(PortalErrorCode.NotFound, $"HMI software not found: {softwarePath}");

            try
            {
                var sw = softwareContainer.Software;
                var connections = TryGetPropertyValue(sw, "Connections");
                if (connections == null)
                    throw new PortalException(PortalErrorCode.NotFound, $"Connections collection not found. swType={sw.GetType().FullName}");

                if (TryImportEngineeringObjectIntoCollection(connections, importPath, out _, out var err))
                    return;

                throw new PortalException(PortalErrorCode.ImportFailed, err ?? "ImportHmiConnection failed");
            }
            catch (PortalException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.ImportFailed, ex.Message, null, ex);
            }
        }

        private static List<string> TryListScreens(object hmiRoot)
        {
            var result = new List<string>();

            try
            {
                // Try common shapes: root.Screens OR root.ScreenFolder.Screens
                var rootType = hmiRoot.GetType();
                var screens = rootType.GetProperty("Screens")?.GetValue(hmiRoot);
                if (screens == null)
                {
                    var folder = rootType.GetProperty("ScreenFolder")?.GetValue(hmiRoot);
                    if (folder != null)
                    {
                        screens = folder.GetType().GetProperty("Screens")?.GetValue(folder);
                    }
                }

                if (screens is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null) continue;
                        var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(name!);
                        }
                    }
                }
            }
            catch
            {
                // best-effort only
            }

            return result;
        }
    }
}
