using Siemens.Engineering.HW;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Siemens
{
    // Unified HMI 标签绑定、PLC 通信连接与地址读回验证。
    public partial class Portal
    {
        public ResponseMessage EnsureUnifiedHmiTagTable(string hmiSoftwarePath, string tagTableName)
        {
            return RunHmiStepTool("EnsureUnifiedHmiTagTable", meta =>
            {
                var sw = ResolveHmiSoftwareOrThrow(hmiSoftwarePath);
                var tables = TryGetHmiTagTablesCollection(sw);
                if (tables == null) throw new InvalidOperationException($"HMI TagTables collection not found. hmiType={sw.GetType().FullName}; tagRootType={TryGetHmiTagRoot(sw).GetType().FullName}");

                var table = TryFindHmiTagTable(sw, tagTableName);
                var action = "exists";
                if (table == null)
                {
                    table = TryCreateNamedEngineeringObject(tables, tagTableName, out var createError);
                    if (table == null) throw new InvalidOperationException(createError ?? $"Create failed on {tables.GetType().FullName}.");
                    action = "created";
                }

                if (table == null) throw new InvalidOperationException("Tag table create/find returned null.");
                meta["action"] = action;
                meta["tagTableType"] = table.GetType().FullName;
                return $"HMI tag table '{tagTableName}' {action}.";
            });
        }

        /// <summary>
        /// Unified HMI tag → PLC symbolic binding (same rules as <see cref="EnsureUnifiedHmiTag"/>).
        /// </summary>
        private void BindUnifiedHmiTagToPlcSymbol(
            object tag,
            string connectionName,
            string plcName,
            string plcTagSymbol,
            string hmiDataType,
            JsonArray writeResults,
            string address = "")
        {
            bool Set(string label, object? value, params string[] names)
            {
                var ok = TrySetAnyPropertyOrAttribute(tag, value, names);
                writeResults.Add($"{label}={ok}");
                return ok;
            }

            var tagTypeCandidates = new[]
            {
                "External",
                "ExternalTag",
                "HmiExternal",
                "ConnectedExternal",
                "PLC",
                "Plc",
                "Process",
                "ConnectionTag",
                "HmiTag"
            };

            Set("DataType", hmiDataType, "DataType", "HmiDataType");
            TrySetEngineeringAttribute(tag, "DataType", hmiDataType);
            TrySetEngineeringAttribute(tag, "HmiDataType", hmiDataType);
            var tagTypeSet = TrySetAnyEnumCandidatePropertyOrAttribute(tag, tagTypeCandidates, "TagType", "Type", "Kind");
            writeResults.Add("TagTypeCandidate=" + tagTypeSet);
            if (!string.IsNullOrWhiteSpace(plcName))
                Set("PlcName", plcName, "PlcName", "ControllerName", "Station");
            if (!string.IsNullOrWhiteSpace(connectionName))
                Set("Connection", connectionName, "Connection", "ConnectionName");
            var targetPlcTag = string.IsNullOrWhiteSpace(plcTagSymbol) ? string.Empty : plcTagSymbol;
            var targetAddress = string.IsNullOrWhiteSpace(address) ? string.Empty : address.Trim();
            // Only true PLC absolute operands (e.g. %DB200.DBX0.0, DB200.DBX0.0). Do NOT treat symbolic
            // "DB_HMI_Interface.Member" as absolute — that wrongly flipped AddressAccessMode and broke PLC binding.
            var isAbsoluteAddress =
                !string.IsNullOrWhiteSpace(targetAddress)
                || targetPlcTag.StartsWith("%", StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(
                    targetPlcTag,
                    @"^(DB|IW|QW|ID|QD|IB|QB|MB|MW|MD)\d",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (isAbsoluteAddress)
            {
                if (!string.IsNullOrWhiteSpace(targetPlcTag) && !targetPlcTag.StartsWith("%", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizedTag = NormalizeControllerTagName(targetPlcTag);
                    TryBindUnifiedHmiTagPlcSymbolicPaths(tag, normalizedTag, writeResults);
                }

                TrySetUnifiedHmiTagAddressingModeEnum(tag, symbolic: false, writeResults);
                var runtimeAddress = string.IsNullOrWhiteSpace(targetAddress) ? targetPlcTag : targetAddress;
                TrySetUnifiedHmiTagRuntimeAddress(tag, runtimeAddress, writeResults);
            }
            else
            {
                Set("ClearAddress", string.Empty, "Address", "LogicalAddress");
                TrySetEngineeringAttribute(tag, "Address", string.Empty);
                TrySetEngineeringAttribute(tag, "LogicalAddress", string.Empty);
                TrySetUnifiedHmiTagAddressingModeEnum(tag, symbolic: true, writeResults);
                var normalizedTag = NormalizeControllerTagName(targetPlcTag);
                TryBindUnifiedHmiTagPlcSymbolicPaths(tag, normalizedTag, writeResults);
                if (TrySetUnifiedHmiTagAccessModeByEnumScan(tag, true))
                    writeResults.Add("AccessMode_repass_symbolic=true");
            }
        }

        private static void TrySetUnifiedHmiTagRuntimeAddress(object tag, string runtimeAddress, JsonArray writeResults)
        {
            var addressNames = new[]
            {
                "Address",
                "LogicalAddress",
                "ProcessValueAddress",
                "RuntimeAddress",
                "ControllerAddress",
                "ControllerTagAddress",
                "ExternalAddress",
                "PlcAddress",
                "PLCAddress",
                "TagAddress",
                "AbsoluteAddress"
            };

            var primaryOk = TrySetAnyPropertyOrAttribute(tag, runtimeAddress, "Address", "LogicalAddress");
            writeResults.Add("RuntimeAddressPrimary=" + primaryOk);

            var readback = TryReadUnifiedHmiTagRuntimeAddress(tag, addressNames);
            if (!string.Equals(readback, runtimeAddress, StringComparison.OrdinalIgnoreCase))
            {
                var extraOk = false;
                foreach (var name in addressNames.Skip(2))
                {
                    extraOk = TrySetProperty(tag, name, runtimeAddress) || TrySetEngineeringAttribute(tag, name, runtimeAddress) || extraOk;
                }

                writeResults.Add("RuntimeAddressExtra=" + extraOk);
                readback = TryReadUnifiedHmiTagRuntimeAddress(tag, addressNames);
            }

            writeResults.Add("RuntimeAddressReadback=" + (readback ?? string.Empty));
        }

        private static string TryReadUnifiedHmiTagRuntimeAddress(object tag, params string[] addressNames)
        {
            foreach (var name in addressNames)
            {
                try
                {
                    var prop = tag.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                    {
                        var value = prop.GetValue(tag)?.ToString();
                        if (!string.IsNullOrWhiteSpace(value)) return value!;
                    }
                }
                catch
                {
                }

                var attr = TryGetEngineeringAttribute(tag, name)?.ToString();
                if (!string.IsNullOrWhiteSpace(attr)) return attr!;
            }

            return string.Empty;
        }

        /// <summary>
        /// WinCC Unified HMI tags expose addressing mode as enums; writing display strings (e.g. "SymbolicAccess")
        /// via generic SetProperty fails silently and leaves the UI on default Absolute with empty Address/PLC tag.
        /// </summary>
        private static void TrySetUnifiedHmiTagAddressingModeEnum(object tag, bool symbolic, JsonArray writeResults)
        {
            var ok = TrySetUnifiedHmiTagAccessModeByEnumScan(tag, symbolic);
            if (!ok)
            {
                var candidates = symbolic
                    ? new[] { "Symbolic", "SymbolicAccess", "FromTag", "HmiSymbolic", "ExternalSymbolic", "TagSymbolic" }
                    : new[] { "Absolute", "AbsoluteAccess", "Direct", "HmiAbsolute", "ExternalAbsolute", "TagAbsolute" };
                ok = TrySetAnyEnumCandidatePropertyOrAttribute(tag, candidates, "AddressAccessMode", "AccessMode", "TagAddressingMode", "HmiTagAddressingMode");
            }

            writeResults.Add($"AddressingMode({(symbolic ? "symbolic" : "absolute")})={ok}");
        }

        /// <summary>
        /// Unified <see cref="HmiTag"/> access mode enum names differ by TIA version; scan all declared enum members.
        /// </summary>
        private static bool TrySetUnifiedHmiTagAccessModeByEnumScan(object tag, bool wantSymbolic)
        {
            foreach (var propName in new[] { "AccessMode", "AddressAccessMode", "TagAddressingMode", "HmiTagAddressingMode" })
            {
                try
                {
                    var prop = tag.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null || !prop.CanWrite || !prop.PropertyType.IsEnum) continue;
                    foreach (var enumName in Enum.GetNames(prop.PropertyType))
                    {
                        var u = enumName.ToUpperInvariant();
                        var match = wantSymbolic
                            ? u.Contains("SYMBOL") || u.Contains("NAMED")
                            : u.Contains("ABSOL") || u.Contains("DIRECT") || u.Contains("ADDRESS");
                        if (!match) continue;
                        var ev = Enum.Parse(prop.PropertyType, enumName);
                        prop.SetValue(tag, ev);
                        TrySetEngineeringAttribute(tag, propName, ev);
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        /// <summary>
        /// S7-1200/1500 PLC partner in HMI connections uses rack 0 and CPU slot 1 in almost all compact PLC projects.
        /// Missing slot shows as "?" in TIA and breaks tag resolution.
        /// </summary>
        private static void TryConfigureUnifiedHmiConnectionS7PartnerRackSlot(object connection, string plcFamily)
        {
            if (plcFamily != "S71200" && plcFamily != "S71500" && plcFamily != "UNKNOWN") return;

            foreach (var slotName in new[] { "PartnerSlot", "Slot", "PlcSlot", "PartnerExpansionSlot", "ExpansionSlot", "ControllerSlot" })
            {
                foreach (var slotVal in new object[] { 1, (short)1, (ushort)1, "1" })
                {
                    if (TrySetProperty(connection, slotName, slotVal) || TrySetEngineeringAttribute(connection, slotName, slotVal))
                    {
                        break;
                    }
                }
            }

            foreach (var rackName in new[] { "PartnerRack", "Rack", "PlcRack", "ControllerRack" })
            {
                foreach (var rackVal in new object[] { 0, (short)0, (ushort)0, "0" })
                {
                    if (TrySetProperty(connection, rackName, rackVal) || TrySetEngineeringAttribute(connection, rackName, rackVal))
                    {
                        break;
                    }
                }
            }
        }

        private static void TryBindUnifiedHmiTagPlcSymbolicPaths(object tag, string normalizedTag, JsonArray writeResults)
        {
            if (string.IsNullOrWhiteSpace(normalizedTag)) return;
            var names = new[]
            {
                "PlcTag", "ControllerTag", "ControllerTagName", "ProcessTag", "ExternalTag", "Tag", "TagName", "SymbolicAddress"
            };
            foreach (var n in names)
            {
                var ok = TrySetProperty(tag, n, normalizedTag) || TrySetEngineeringAttribute(tag, n, normalizedTag);
                writeResults.Add($"{n}={ok}");
            }
        }

        /// <summary>
        /// Unified HMI connection CommunicationDriver is often an engineering attribute whose runtime type is an enum.
        /// Passing a human-readable driver string into SetAttribute then fails Enum.Parse and leaves S7-300/400 default.
        /// </summary>
        private static bool TrySetUnifiedHmiCommunicationDriverEnum(object connection, string plcFamily)
        {
            try
            {
                var get = connection.GetType().GetMethod("GetAttribute", new[] { typeof(string) });
                var set = connection.GetType().GetMethod("SetAttribute", new[] { typeof(string), typeof(object) });
                if (get == null || set == null) return false;

                Type? enumType = null;
                var prop = connection.GetType().GetProperty("CommunicationDriver", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType.IsEnum) enumType = prop.PropertyType;
                if (enumType == null)
                {
                    try
                    {
                        var cur = get.Invoke(connection, new object[] { "CommunicationDriver" });
                        if (cur != null && cur.GetType().IsEnum) enumType = cur.GetType();
                    }
                    catch
                    {
                    }
                }

                if (enumType == null || !enumType.IsEnum) return false;

                var ev = SelectCommunicationDriverEnumValue(enumType, plcFamily);
                if (ev == null && (plcFamily == "UNKNOWN" || plcFamily == "S71200" || plcFamily == "S71500"))
                {
                    foreach (var name in Enum.GetNames(enumType))
                    {
                        var u = name.ToUpperInvariant();
                        if (u.Contains("1200") || u.Contains("1500") || u.Contains("S712") || u.Contains("S715") || u.Contains("PLUS"))
                        {
                            ev = Enum.Parse(enumType, name);
                            break;
                        }
                    }
                }

                if (ev == null) return false;

                set.Invoke(connection, new object[] { "CommunicationDriver", ev });
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        prop.SetValue(connection, ev);
                    }
                    catch
                    {
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public ResponseMessage EnsureUnifiedHmiTag(string hmiSoftwarePath, string tagTableName, string tagName, string hmiDataType = "Bool", string plcName = "PLC_1", string plcTag = "", string connectionName = "", string address = "", bool requireVerifiedBinding = true)
        {
            return RunHmiStepTool("EnsureUnifiedHmiTag", meta =>
            {
                var sw = ResolveHmiSoftwareOrThrow(hmiSoftwarePath);
                var tagTable = EnsureHmiTagTableObject(sw, tagTableName);
                var tags = TryGetPropertyValue(tagTable, "Tags");
                if (tags == null) throw new InvalidOperationException($"Tags collection not found on tag table '{tagTableName}'.");

                // 去掉 ?? TryFindByNameInCollection(tags, Array.Empty<string>(), ...)：空 hints 恒返回 null。
                var tag = FindExistingByName(tags, tagName);
                var action = "exists";
                if (tag == null)
                {
                    tag = TryCreateNamedEngineeringObject(tags, tagName, out var createError);
                    if (tag == null) throw new InvalidOperationException(createError ?? $"Create failed on {tags.GetType().FullName}.");
                    action = "created";
                }

                if (tag == null) throw new InvalidOperationException("Tag create/find returned null.");
                var writeResults = new JsonArray();
                var targetPlcTag = string.IsNullOrWhiteSpace(plcTag) ? tagName : plcTag;
                BindUnifiedHmiTagToPlcSymbol(tag, connectionName, plcName, targetPlcTag, hmiDataType, writeResults, address);

                meta["action"] = action;
                meta["tagType"] = tag.GetType().FullName;
                meta["tagEnumHints"] = DescribeWritableEnumProperties(tag, "TagType", "AccessMode", "AddressAccessMode");
                meta["requestedPlcTag"] = targetPlcTag;
                meta["requestedAddress"] = address ?? string.Empty;
                meta["writeResults"] = writeResults;
                var binding = ClassifyUnifiedHmiTagBinding(tag, connectionName, targetPlcTag, address ?? string.Empty);
                meta["readback"] = binding.Readback;
                meta["bindingStatus"] = binding.Status;
                meta["bindingVerified"] = binding.Verified;
                meta["bindingGuidance"] = binding.Guidance;
                meta["requireVerifiedBinding"] = requireVerifiedBinding;
                if (requireVerifiedBinding && !binding.Verified)
                {
                    throw new InvalidOperationException($"HMI tag '{tagName}' binding is not verified. Status={binding.Status}; {binding.Guidance}; Readback={binding.Readback}");
                }

                return $"HMI tag '{tagName}' {action}. Binding={binding.Status}.";
            });
        }

        public ModelContextProtocol.ResponseObjectDescribe EnsureUnifiedHmiConnection(string hmiSoftwarePath, string connectionName = "HMI_Connection_1", string plcName = "PLC_1")
        {
            var sw = ResolveHmiSoftwareOrThrow(hmiSoftwarePath);
            var connections = TryGetPropertyValue(sw, "Connections");
            if (connections == null) throw new InvalidOperationException($"Connections collection not found on HMI software '{hmiSoftwarePath}'.");

            // 去掉 ?? TryFindByNameInCollection(connections, Array.Empty<string>(), ...)：空 hints 恒返回 null。
            var connection = FindExistingByName(connections, connectionName);
            if (connection == null)
            {
                var create = connections.GetType().GetMethod("Create", new[] { typeof(string) });
                if (create == null) throw new InvalidOperationException($"Create(string) not found on {connections.GetType().FullName}.");
                connection = InvokeCreate(create, connections, new object[] { connectionName });
            }

            if (connection == null) throw new InvalidOperationException("Connection create/find returned null.");

            TrySetProperty(connection, "Name", connectionName);
            var partner = ResolveUnifiedHmiPlcPartner(plcName);
            TryConfigureUnifiedHmiConnectionPartner(connection, partner);
            TryConfigureUnifiedHmiConnectionS7PartnerRackSlot(connection, partner.Family);
            // Driver last so partner binding cannot clobber S7-1200/1500 selection.
            TryConfigureUnifiedHmiCommunicationDriver(connection, plcName);
            ValidateUnifiedHmiCommunicationDriver(connection, partner.Family);

            return new ModelContextProtocol.ResponseObjectDescribe
            {
                ObjectKind = "HmiConnection",
                ObjectPath = $"{hmiSoftwarePath}:{connectionName}",
                TypeName = connection.GetType().FullName,
                Members = DescribeMembers(connection, 220),
                Message = $"HMI connection '{connectionName}' ensured. PartnerResolved={partner.Summary}; {SummarizeHmiObjectReadback(connection, "Name", "CommunicationDriver", "Partner", "Station", "Node", "InitialAddress", "PlcName", "ControllerName", "PartnerName")}"
            };
        }

        private object EnsureHmiTagTableObject(object hmiSoftware, string tagTableName)
        {
            var tagRoot = TryGetHmiTagRoot(hmiSoftware);
            var table = TryFindHmiTagTable(hmiSoftware, tagTableName);
            if (table != null) return table;

            var tables = TryGetHmiTagTablesCollection(hmiSoftware);
            if (tables == null)
            {
                throw new InvalidOperationException($"HMI TagTables collection not found. hmiType={hmiSoftware.GetType().FullName}; tagRootType={tagRoot.GetType().FullName}; tagRootMembers={string.Join(" | ", DescribeMembers(tagRoot, 80).Select(m => $"{m.Kind}:{m.Name}:{m.Type}"))}");
            }

            table = TryCreateNamedEngineeringObject(tables, tagTableName, out var createError);
            if (table == null)
            {
                throw new InvalidOperationException(createError ?? $"Failed to create HMI tag table '{tagTableName}'.");
            }

            return table;
        }

        private sealed class UnifiedHmiTagBindingReadback
        {
            public string Status { get; set; } = "Failed";
            public bool Verified { get; set; }
            public string Readback { get; set; } = string.Empty;
            public string Guidance { get; set; } = string.Empty;
        }

        private static UnifiedHmiTagBindingReadback ClassifyUnifiedHmiTagBinding(object tag, string connectionName, string plcTag, string address)
        {
            string Attr(params string[] names)
            {
                foreach (var name in names)
                {
                    try
                    {
                        var prop = tag.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                        if (prop != null && prop.CanRead)
                        {
                            var v = prop.GetValue(tag)?.ToString();
                            if (!string.IsNullOrWhiteSpace(v)) return v!;
                        }
                    }
                    catch { }

                    try
                    {
                        var v = TryGetEngineeringAttribute(tag, name)?.ToString();
                        if (!string.IsNullOrWhiteSpace(v)) return v!;
                    }
                    catch { }
                }

                return string.Empty;
            }

            var readback = SummarizeHmiObjectReadback(tag, "Connection", "AccessMode", "AddressAccessMode", "TagType", "PlcName", "ControllerName", "Station", "PlcTag", "ControllerTag", "ControllerTagName", "Address", "LogicalAddress", "ProcessValueAddress", "RuntimeAddress", "ControllerAddress", "ControllerTagAddress", "ExternalAddress", "PlcAddress", "PLCAddress", "TagAddress", "AbsoluteAddress", "DataType", "HmiDataType");
            var connection = Attr("Connection", "ConnectionName");
            var accessMode = Attr("AccessMode", "AddressAccessMode");
            var symbol = Attr("PlcTag", "ControllerTag", "ControllerTagName");
            var runtimeAddress = Attr("Address", "LogicalAddress", "ProcessValueAddress", "RuntimeAddress", "ControllerAddress", "ControllerTagAddress", "ExternalAddress", "PlcAddress", "PLCAddress", "TagAddress", "AbsoluteAddress");
            var requestedAddress = (address ?? string.Empty).Trim();
            var requestedSymbol = NormalizeControllerTagName(plcTag ?? string.Empty);
            var expectedConnection = (connectionName ?? string.Empty).Trim();

            var connectionOk = string.IsNullOrWhiteSpace(expectedConnection) ||
                string.Equals(connection, expectedConnection, StringComparison.OrdinalIgnoreCase);
            var absoluteOk = !string.IsNullOrWhiteSpace(requestedAddress) &&
                connectionOk &&
                string.Equals(runtimeAddress, requestedAddress, StringComparison.OrdinalIgnoreCase);
            var symbolicOk = !string.IsNullOrWhiteSpace(requestedSymbol) &&
                connectionOk &&
                string.Equals(NormalizeControllerTagName(symbol), requestedSymbol, StringComparison.OrdinalIgnoreCase) &&
                accessMode.IndexOf("Symbol", StringComparison.OrdinalIgnoreCase) >= 0;

            if (symbolicOk)
            {
                return new UnifiedHmiTagBindingReadback
                {
                    Status = "SymbolicVerified",
                    Verified = true,
                    Readback = readback,
                    Guidance = "PLC symbolic HMI tag binding read back successfully."
                };
            }

            if (absoluteOk)
            {
                return new UnifiedHmiTagBindingReadback
                {
                    Status = "AbsoluteVerified",
                    Verified = true,
                    Readback = readback,
                    Guidance = "Absolute-address HMI tag binding read back successfully."
                };
            }

            var status = string.IsNullOrWhiteSpace(connection) || connection.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0 || connection.IndexOf("内部", StringComparison.OrdinalIgnoreCase) >= 0
                ? "InternalOnly"
                : "Unverified";
            return new UnifiedHmiTagBindingReadback
            {
                Status = status,
                Verified = false,
                Readback = readback,
                Guidance = "Pass connectionName plus a verified PLC symbol or absolute address, then read back Connection/AccessMode/PlcTag/Address. Internal HMI tags are not accepted by the stable project-generation path."
            };
        }

        private static string NormalizeControllerTagName(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName)) return string.Empty;
            return tagName.Replace("\"", string.Empty);
        }

        private sealed class UnifiedHmiPlcPartnerInfo
        {
            public string SoftwarePath { get; set; } = string.Empty;
            public string DeviceName { get; set; } = string.Empty;
            public string StationName { get; set; } = string.Empty;
            public string NodeName { get; set; } = string.Empty;
            public string InitialAddress { get; set; } = string.Empty;
            public string Family { get; set; } = "UNKNOWN";

            public string Summary =>
                $"SoftwarePath={SoftwarePath}; DeviceName={DeviceName}; StationName={StationName}; NodeName={NodeName}; InitialAddress={InitialAddress}; Family={Family}";
        }

        private UnifiedHmiPlcPartnerInfo ResolveUnifiedHmiPlcPartner(string plcSoftwarePath)
        {
            plcSoftwarePath ??= string.Empty;
            var info = new UnifiedHmiPlcPartnerInfo
            {
                SoftwarePath = plcSoftwarePath,
                DeviceName = FirstPathSegment(plcSoftwarePath),
                StationName = FirstPathSegment(plcSoftwarePath),
                Family = InferUnifiedPlcFamilyFromSoftwarePath(plcSoftwarePath)
            };

            try
            {
                var sc = GetSoftwareContainer(plcSoftwarePath);
                var di = sc?.Parent as DeviceItem;
                if (di != null)
                {
                    info.StationName = TryGetName(di) ?? di.Name ?? info.StationName;
                    var root = GetTopDeviceItem(di);
                    if (root != null)
                    {
                        info.DeviceName = TryGetName(root) ?? root.Name ?? info.DeviceName;
                        info.StationName = TryGetName(root) ?? root.Name ?? info.StationName;
                        FillUnifiedHmiPartnerNetworkInfo(root, info);
                    }
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(info.NodeName))
            {
                try
                {
                    var root = GetDeviceItemByPath(info.DeviceName);
                    if (root != null) FillUnifiedHmiPartnerNetworkInfo(root, info);
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(info.DeviceName)) info.DeviceName = FirstPathSegment(plcSoftwarePath);
            if (string.IsNullOrWhiteSpace(info.StationName)) info.StationName = info.DeviceName;
            return info;
        }

        private static string FirstPathSegment(string path)
        {
            return (path ?? string.Empty).Trim()
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
        }

        private static DeviceItem? GetTopDeviceItem(DeviceItem item)
        {
            var current = item;
            while (current.Parent is DeviceItem parent)
            {
                current = parent;
            }

            return current;
        }

        private static void FillUnifiedHmiPartnerNetworkInfo(DeviceItem root, UnifiedHmiPlcPartnerInfo info)
        {
            var plcNode = FindNetworkNodes(root).FirstOrDefault(n => IsIndustrialEthernetNode(n.Node));
            if (plcNode.Node == null) return;

            info.NodeName = TryGetName(plcNode.Node)
                ?? TryGetPropertyValue(plcNode.Node, "Name")?.ToString()
                ?? plcNode.Item.Name
                ?? string.Empty;

            var address = TryGetPropertyValue(plcNode.Node, "Address")?.ToString()
                ?? TryGetPropertyValue(plcNode.Node, "IpAddress")?.ToString()
                ?? TryGetPropertyValue(plcNode.Node, "IPAddress")?.ToString()
                ?? TryGetEngineeringAttribute(plcNode.Node, "Address")?.ToString()
                ?? TryGetEngineeringAttribute(plcNode.Node, "IpAddress")?.ToString()
                ?? string.Empty;
            info.InitialAddress = address;
        }

        private static void TryConfigureUnifiedHmiConnectionPartner(object connection, UnifiedHmiPlcPartnerInfo partner)
        {
            var deviceName = string.IsNullOrWhiteSpace(partner.DeviceName) ? partner.SoftwarePath : partner.DeviceName;
            var stationName = string.IsNullOrWhiteSpace(partner.StationName) ? deviceName : partner.StationName;

            TrySetAnyPropertyOrAttribute(connection, deviceName, "Partner", "PartnerName", "DeviceName", "PlcName", "ControllerName");
            TrySetAnyPropertyOrAttribute(connection, stationName, "Station", "StationName", "ControllerStation");
            TrySetAnyPropertyOrAttribute(connection, deviceName, "Controller", "Device", "Plc", "Target");

            if (!string.IsNullOrWhiteSpace(partner.NodeName))
            {
                TrySetAnyPropertyOrAttribute(connection, partner.NodeName, "Node", "PartnerNode", "Interface", "NetworkNode", "AccessPoint");
            }

            if (!string.IsNullOrWhiteSpace(partner.InitialAddress))
            {
                TrySetAnyPropertyOrAttribute(connection, partner.InitialAddress, "InitialAddress", "Address", "IpAddress", "IPAddress", "PartnerAddress");
            }
        }

        /// <summary>
        /// Infer PLC CPU family from the PLC software path (device TypeIdentifier / order number).
        /// </summary>
        private string InferUnifiedPlcFamilyFromSoftwarePath(string plcSoftwarePath)
        {
            try
            {
                var sc = GetSoftwareContainer(plcSoftwarePath);
                var di = sc?.Parent as DeviceItem;
                while (di != null)
                {
                    var tid = TryGetPropertyValue(di, "TypeIdentifier")?.ToString() ?? string.Empty;
                    var t = tid.ToUpperInvariant();
                    // Catalog MLFB often contains spaces (e.g. "OrderNumber:6ES7 211-1BE40-0XB0/...").
                    // Old checks used "6ES721" which fails after "6ES7 " + "211" — driver fell back to S7-300/400.
                    var tCompact = string.Concat(t.Where(ch => !char.IsWhiteSpace(ch)));
                    if (t.IndexOf("S7-1200", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S71200", StringComparison.OrdinalIgnoreCase) >= 0
                        || tCompact.IndexOf("6ES721", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES722", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "S71200";
                    if (t.IndexOf("S7-1500", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S71500", StringComparison.OrdinalIgnoreCase) >= 0
                        || tCompact.IndexOf("6ES751", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES752", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "S71500";
                    if (t.IndexOf("S7-300", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S7300", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES731", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "S7300";
                    if (t.IndexOf("S7-400", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S7400", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES741", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "S7400";
                    di = di.Parent as DeviceItem;
                }

                var fromDevices = TryInferPlcFamilyFromProjectDevices(plcSoftwarePath);
                if (!string.IsNullOrEmpty(fromDevices)) return fromDevices;
            }
            catch
            {
            }

            return "UNKNOWN";
        }

        /// <summary>
        /// When <see cref="SoftwareContainer.Parent"/> is not a <see cref="DeviceItem"/>, CPU TypeIdentifier may still
        /// exist on nested rack/CPU items under the PLC device — walk the device tree by PLC software path head name.
        /// </summary>
        private string TryInferPlcFamilyFromProjectDevices(string plcSoftwarePath)
        {
            try
            {
                if (_project?.Devices == null) return string.Empty;
                var head = (plcSoftwarePath ?? string.Empty).Trim()
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(head)) return string.Empty;

                foreach (var device in _project.Devices)
                {
                    if (!device.Name.Equals(head, StringComparison.OrdinalIgnoreCase)) continue;
                    var stack = new Stack<DeviceItem>(device.DeviceItems ?? Enumerable.Empty<DeviceItem>());
                    while (stack.Count > 0)
                    {
                        var di = stack.Pop();
                        if (di == null) continue;
                        if (di.DeviceItems != null)
                        {
                            foreach (var ch in di.DeviceItems) stack.Push(ch);
                        }

                        var tid = TryGetPropertyValue(di, "TypeIdentifier")?.ToString() ?? string.Empty;
                        var t = tid.ToUpperInvariant();
                        var tCompact = string.Concat(t.Where(ch => !char.IsWhiteSpace(ch)));
                        if (t.IndexOf("S7-1200", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S71200", StringComparison.OrdinalIgnoreCase) >= 0
                            || tCompact.IndexOf("6ES721", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES722", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "S71200";
                        if (t.IndexOf("S7-1500", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S71500", StringComparison.OrdinalIgnoreCase) >= 0
                            || tCompact.IndexOf("6ES751", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES752", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "S71500";
                        if (t.IndexOf("S7-300", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S7300", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES731", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "S7300";
                        if (t.IndexOf("S7-400", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("S7400", StringComparison.OrdinalIgnoreCase) >= 0 || tCompact.IndexOf("6ES741", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "S7400";
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static object? SelectCommunicationDriverEnumValue(Type enumType, string plcFamily)
        {
            object? best = null;
            var bestScore = -1;
            foreach (var name in Enum.GetNames(enumType))
            {
                var u = name.ToUpperInvariant();
                var score = 0;
                if (plcFamily == "S71200" || plcFamily == "S71500" || plcFamily == "UNKNOWN")
                {
                    if (u.Contains("300") && !u.Contains("1500")) continue;
                    if (u.Contains("400") && !u.Contains("1500")) continue;
                    if (u.Contains("318") || u.Contains("319")) continue;
                    if (u.Contains("1200") || u.Contains("1500") || u.Contains("S712") || u.Contains("S715") || u.Contains("PLUS"))
                        score += 10;
                    if (u.Contains("UNIFIED") || u.Contains("PLUS")) score += 2;
                }
                else if (plcFamily == "S7300")
                {
                    if (u.Contains("300") || u.Contains("318") || u.Contains("319")) score += 10;
                }
                else if (plcFamily == "S7400")
                {
                    if (u.Contains("400") || u.Contains("414") || u.Contains("416")) score += 10;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = Enum.Parse(enumType, name);
                }
            }

            return bestScore > 0 ? best : null;
        }

        private static void TryConfigureUnifiedDriverProperties(object connection, string plcFamily)
        {
            try
            {
                var dps = TryGetPropertyValue(connection, "DriverProperties");
                if (dps is not IEnumerable en) return;

                foreach (var dp in en)
                {
                    if (dp == null) continue;
                    var n = TryGetPropertyValue(dp, "Name")?.ToString() ?? TryGetName(dp) ?? string.Empty;
                    var nu = n.ToUpperInvariant();
                    if (nu.Contains("DRIVER") || nu.Contains("FAMILY") || nu.Contains("CPU") || nu.Contains("CONTROLLER"))
                    {
                        if (plcFamily == "S71200" || plcFamily == "S71500" || plcFamily == "UNKNOWN")
                        {
                            TrySetProperty(dp, "Value", "SIMATIC S7-1200/1500");
                            TrySetEngineeringAttribute(dp, "Value", "SIMATIC S7-1200/1500");
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// WinCC Unified HMI connection: pick CommunicationDriver enum / attribute that matches the PLC hardware.
        /// </summary>
        private void TryConfigureUnifiedHmiCommunicationDriver(object connection, string plcSoftwarePath)
        {
            var plcFamily = InferUnifiedPlcFamilyFromSoftwarePath(plcSoftwarePath);

            try
            {
                var prop = connection.GetType().GetProperty("CommunicationDriver", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite && prop.PropertyType.IsEnum)
                {
                    var ev = SelectCommunicationDriverEnumValue(prop.PropertyType, plcFamily);
                    if (ev != null)
                    {
                        prop.SetValue(connection, ev);
                        return;
                    }
                }
            }
            catch
            {
            }

            // CommunicationDriver is commonly exposed as an engineering attribute typed as an enum; string writes fail.
            if (TrySetUnifiedHmiCommunicationDriverEnum(connection, plcFamily))
            {
                return;
            }

            var driverCandidates = plcFamily switch
            {
                "S7300" => new[] { "SIMATIC S7 300/400", "SIMATIC S7-300/400", "SIMATIC S7 300", "SIMATIC S7-300" },
                "S7400" => new[] { "SIMATIC S7 400", "SIMATIC S7-400", "SIMATIC S7 300/400", "SIMATIC S7-300/400" },
                _ => new[]
                {
                    "SIMATIC S7-1200/1500",
                    "SIMATIC S7 1200/1500",
                    "SIMATIC S7-1200",
                    "SIMATIC S7 1200",
                    "SIMATIC S7-1500",
                    "SIMATIC S7 1500",
                    "S7-1200/1500",
                    "S7-1200",
                    "S7-1500",
                    "S71200",
                    "S71500"
                }
            };

            foreach (var driver in driverCandidates)
            {
                if (TrySetProperty(connection, "CommunicationDriver", driver) ||
                    TrySetEngineeringAttribute(connection, "CommunicationDriver", driver))
                {
                    return;
                }
            }

            TrySetCommunicationDriverFromAttributeInfos(connection, driverCandidates);

            TryConfigureUnifiedDriverProperties(connection, plcFamily);
        }

        private static void ValidateUnifiedHmiCommunicationDriver(object connection, string plcFamily)
        {
            var driver = ReadUnifiedHmiCommunicationDriver(connection);
            var normalized = (driver ?? string.Empty).ToUpperInvariant().Replace("-", "").Replace(" ", "");
            if (plcFamily == "S7300" || plcFamily == "S7400") return;

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException("HMI connection CommunicationDriver did not read back. S7-1200/S7-1500 projects must read back a 1200/1500 driver before HMI tags are created.");
            }

            if (normalized.Contains("300/400") || normalized.Contains("S7300") || normalized.Contains("S7400"))
            {
                throw new InvalidOperationException($"HMI connection CommunicationDriver read back as '{driver}', but the PLC family is {plcFamily}. Use SIMATIC S7-1200/1500 for S7-1200/S7-1500 projects.");
            }
        }

        private static string ReadUnifiedHmiCommunicationDriver(object connection)
        {
            foreach (var name in new[] { "CommunicationDriver", "Driver", "Protocol" })
            {
                try
                {
                    var prop = connection.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                    {
                        var value = prop.GetValue(connection)?.ToString();
                        if (!string.IsNullOrWhiteSpace(value)) return value!;
                    }
                }
                catch
                {
                }

                var attr = TryGetEngineeringAttribute(connection, name)?.ToString();
                if (!string.IsNullOrWhiteSpace(attr)) return attr!;
            }

            return string.Empty;
        }

        /// <summary>
        /// Some Unified builds expose the driver only under a localized or version-specific engineering attribute name.
        /// </summary>
        private static void TrySetCommunicationDriverFromAttributeInfos(object connection, string[] driverCandidates)
        {
            try
            {
                var getInfos = connection.GetType().GetMethod("GetAttributeInfos", Type.EmptyTypes);
                if (getInfos == null) return;
                var infos = getInfos.Invoke(connection, null) as System.Collections.IEnumerable;
                if (infos == null) return;
                foreach (var info in infos)
                {
                    if (info == null) continue;
                    var n = TryGetPropertyValue(info, "Name")?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    var nu = n.ToUpperInvariant();
                    if (!nu.Contains("COMMUNICATIONDRIVER") && !nu.Contains("DRIVER") && !n.Contains("通信")) continue;
                    foreach (var driver in driverCandidates)
                    {
                        try
                        {
                            if (TrySetEngineeringAttribute(connection, n, driver)) return;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
