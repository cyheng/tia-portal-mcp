using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Siemens
{
    // Unified HMI 画面、画面项、事件脚本与动态属性。
    public partial class Portal
    {
        public ResponseMessage EnsureStartStopUnifiedHmi(
            string hmiSoftwarePath,
            string screenName = "Main",
            string tagTableName = "默认变量表",
            string plcName = "PLC_1",
            string connectionName = "HMI_Connection_1")
        {
            var meta = new JsonObject
            {
                ["timestamp"] = DateTime.Now,
                ["success"] = false
            };

            var steps = new JsonArray();
            meta["steps"] = steps;

            void Step(string name, bool ok, string? detail = null)
            {
                var o = new JsonObject
                {
                    ["step"] = name,
                    ["ok"] = ok
                };
                if (!string.IsNullOrWhiteSpace(detail)) o["detail"] = detail;
                steps.Add(o);
            }

            object? FindExistingByName(object compositionOrEnumerable, string name)
            {
                try
                {
                    if (compositionOrEnumerable is System.Collections.IEnumerable en)
                    {
                        foreach (var it in en)
                        {
                            var n = TryGetName(it);
                            if (!string.IsNullOrWhiteSpace(n) &&
                                string.Equals(n!.Trim(), name, StringComparison.OrdinalIgnoreCase))
                            {
                                return it;
                            }
                        }
                    }
                }
                catch { }
                return null;
            }

            try
            {
                var totalDeadline = DateTime.UtcNow.AddSeconds(25); // hard timeout for this tool
                bool TimedOut() => DateTime.UtcNow > totalDeadline;

                if (IsProjectNull())
                {
                    Step("precheck", false, "Project is null");
                    return new ResponseMessage { Message = "Project is null", Meta = meta };
                }

                var sc = GetSoftwareContainer(hmiSoftwarePath);
                if (sc?.Software == null)
                {
                    Step("resolveSoftware", false, $"SoftwareContainer not found at '{hmiSoftwarePath}'");
                    return new ResponseMessage { Message = "HMI software not found", Meta = meta };
                }

                var sw = sc.Software;
                Step("resolveSoftware", true, sw.GetType().FullName);

                // Resolve screen + tag table
                var screen = TryFindByNameInCollection(sw, new[] { "Screens", "ScreenFolder" }, screenName);
                if (screen == null)
                {
                    Step("findScreen", false, $"Screen '{screenName}' not found");
                    return new ResponseMessage { Message = "Screen not found", Meta = meta };
                }
                Step("findScreen", true, screen.GetType().FullName);

                var tagTable = TryFindByNameInCollection(sw, new[] { "TagTables" }, tagTableName);
                if (tagTable == null)
                {
                    Step("findTagTable", false, $"TagTable '{tagTableName}' not found");
                    return new ResponseMessage { Message = "Tag table not found", Meta = meta };
                }
                Step("findTagTable", true, tagTable.GetType().FullName);

                // Ensure PLC↔HMI connection with correct driver for the actual PLC CPU (1200/1500 vs 300/400).
                try
                {
                    var connDesc = EnsureUnifiedHmiConnection(hmiSoftwarePath, connectionName, plcName);
                    Step("ensureUnifiedHmiConnection", true, connDesc.Message ?? "ok");
                }
                catch (Exception ex)
                {
                    Step("ensureUnifiedHmiConnection", false, ex.InnerException?.Message ?? ex.Message);
                }

                var connName = string.IsNullOrWhiteSpace(connectionName) ? "HMI_Connection_1" : connectionName.Trim();
                Step("resolveConnection", true, connName);

                // Ensure tags in HMI tag table
                var tagsComp = tagTable.GetType().GetProperty("Tags")?.GetValue(tagTable);
                if (tagsComp == null)
                {
                    Step("resolveTagComposition", false, "tagTable.Tags not found");
                    return new ResponseMessage { Message = "Tag composition not found", Meta = meta };
                }
                Step("resolveTagComposition", true, tagsComp.GetType().FullName);

                string[] tagNames = new[] { "StartPB", "StopPB", "EStop", "RunOut" };
                foreach (var tn in tagNames)
                {
                    if (TimedOut())
                    {
                        Step("timeout", false, "Timeout during tag ensure");
                        return new ResponseMessage { Message = "Timeout", Meta = meta };
                    }
                    try
                    {
                        // find existing
                        // 去掉 ?? TryFindByNameInCollection(tagsComp, Array.Empty<string>(), ...)：空 hints 恒返回 null。
                        var exists = FindExistingByName(tagsComp, tn);
                        if (exists != null)
                        {
                            var wr = new JsonArray();
                            BindUnifiedHmiTagToPlcSymbol(exists, connName, plcName, tn, "Bool", wr);
                            Step($"tag:{tn}", true, "exists");
                            continue;
                        }

                        // create by reflection: Create(string)
                        var mCreate = tagsComp.GetType().GetMethod("Create", new[] { typeof(string) });
                        if (mCreate == null)
                        {
                            Step($"tag:{tn}", false, $"No Create(string) on {tagsComp.GetType().FullName}");
                            continue;
                        }

                        object? tagObj = null;
                        try
                        {
                            tagObj = mCreate.Invoke(tagsComp, new object[] { tn });
                        }
                        catch (TargetInvocationException tie) when (tie.InnerException != null)
                        {
                            // If name already exists, treat as idempotent and return the existing object.
                            var msg = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
                            if (msg.IndexOf("ValueIsNotUnique", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var existing = FindExistingByName(tagsComp, tn);
                                if (existing != null)
                                {
                                    var wrU = new JsonArray();
                                    BindUnifiedHmiTagToPlcSymbol(existing, connName, plcName, tn, "Bool", wrU);
                                    Step($"tag:{tn}", true, "exists");
                                    continue;
                                }
                            }

                            Step($"tag:{tn}", false, msg);
                            continue;
                        }
                        if (tagObj == null)
                        {
                            Step($"tag:{tn}", false, "Create returned null");
                            continue;
                        }

                        TrySetProperty(tagObj, "Name", tn);
                        var wrNew = new JsonArray();
                        BindUnifiedHmiTagToPlcSymbol(tagObj, connName, plcName, tn, "Bool", wrNew);

                        Step($"tag:{tn}", true, "created");
                    }
                    catch (Exception ex)
                    {
                        Step($"tag:{tn}", false, ex.InnerException?.Message ?? ex.Message);
                    }
                }

                // Create minimal screen items (best-effort): two buttons + one lamp
                var itemsComp = screen.GetType().GetProperty("ScreenItems")?.GetValue(screen);
                if (itemsComp == null)
                {
                    Step("resolveScreenItems", false, "screen.ScreenItems not found");
                    return new ResponseMessage { Message = "ScreenItems not found", Meta = meta };
                }
                Step("resolveScreenItems", true, itemsComp.GetType().FullName);

                // Dump all Create* method signatures on ScreenItems composition so we see what's really there.
                var itemsType = itemsComp.GetType();
                var createSigs = itemsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("Create", StringComparison.OrdinalIgnoreCase))
                    .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))}) -> {m.ReturnType.FullName}")
                    .ToArray();
                Step("screenItems.CreateSignatures", true, string.Join(" | ", createSigs));

                // Resolve candidate HMI widget types by FullName (from Siemens.Engineering.HmiUnified).
                Type? ResolveHmiType(params string[] fullNames)
                {
                    foreach (var fn in fullNames)
                    {
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                var t = asm.GetType(fn, throwOnError: false, ignoreCase: false);
                                if (t != null) return t;
                            }
                            catch { }
                        }
                    }
                    return null;
                }

                var tButton = ResolveHmiType(
                    "Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton",
                    "Siemens.Engineering.HmiUnified.UI.Controls.HmiButton");
                var tIOField = ResolveHmiType(
                    "Siemens.Engineering.HmiUnified.UI.Widgets.HmiIOField",
                    "Siemens.Engineering.HmiUnified.UI.Controls.HmiIOField");
                var tRectangle = ResolveHmiType(
                    "Siemens.Engineering.HmiUnified.UI.Widgets.HmiRectangle",
                    "Siemens.Engineering.HmiUnified.UI.Shapes.HmiRectangle");
                var tLabel = ResolveHmiType(
                    "Siemens.Engineering.HmiUnified.UI.Widgets.HmiLabel",
                    "Siemens.Engineering.HmiUnified.UI.Controls.HmiLabel");
                Step("hmiTypeResolve", true,
                    $"Button={tButton?.AssemblyQualifiedName ?? "null"}; IOField={tIOField?.AssemblyQualifiedName ?? "null"}; Rectangle={tRectangle?.AssemblyQualifiedName ?? "null"}; Label={tLabel?.AssemblyQualifiedName ?? "null"}");

                // Try all Create overloads and candidate types; record per-attempt outcome.
                object? CreateItem(string name, Type?[] preferTypes, string[] stringTypeHints)
                {
                    var allAttempts = new List<string>();

                    // idempotent: return existing item if already present
                    var existingByName = FindExistingByName(itemsComp, name);
                    if (existingByName != null)
                    {
                        allAttempts.Add("EXISTS");
                        Step($"ui-detail:{name}", true, "exists");
                        return existingByName;
                    }

                    foreach (var m in itemsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                               .Where(x => x.Name.StartsWith("Create", StringComparison.OrdinalIgnoreCase)))
                    {
                        var ps = m.GetParameters();

                        // Generic Create<T>(string name)
                        if (m.IsGenericMethodDefinition && ps.Length == 1 && ps[0].ParameterType == typeof(string))
                        {
                            foreach (var t in preferTypes.Where(x => x != null))
                            {
                                try
                                {
                                    var gm = m.MakeGenericMethod(t!);
                                    var obj = gm.Invoke(itemsComp, new object[] { name });
                                    if (obj != null)
                                    {
                                        allAttempts.Add($"OK {m.Name}<{t!.Name}>(name)");
                                        return obj;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    allAttempts.Add($"ERR {m.Name}<{t!.Name}>(name): {(ex.InnerException?.Message ?? ex.Message)}");
                                    var innerMsg = ex.InnerException?.Message ?? ex.Message;
                                    if (innerMsg.IndexOf("ValueIsNotUnique", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var ex2 = FindExistingByName(itemsComp, name);
                                        if (ex2 != null) return ex2;
                                    }
                                }
                            }
                        }

                        // Create(string name, Type type)
                        if (!m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(Type))
                        {
                            foreach (var t in preferTypes.Where(x => x != null))
                            {
                                try
                                {
                                    var obj = m.Invoke(itemsComp, new object[] { name, t! });
                                    if (obj != null)
                                    {
                                        allAttempts.Add($"OK {m.Name}(name, typeof({t!.Name}))");
                                        return obj;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    allAttempts.Add($"ERR {m.Name}(name, typeof({t!.Name})): {(ex.InnerException?.Message ?? ex.Message)}");
                                    var innerMsg = ex.InnerException?.Message ?? ex.Message;
                                    if (innerMsg.IndexOf("ValueIsNotUnique", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var ex2 = FindExistingByName(itemsComp, name);
                                        if (ex2 != null) return ex2;
                                    }
                                }
                            }
                        }

                        // Create(Type type, string name)
                        if (!m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(Type) && ps[1].ParameterType == typeof(string))
                        {
                            foreach (var t in preferTypes.Where(x => x != null))
                            {
                                try
                                {
                                    var obj = m.Invoke(itemsComp, new object[] { t!, name });
                                    if (obj != null)
                                    {
                                        allAttempts.Add($"OK {m.Name}(typeof({t!.Name}), name)");
                                        return obj;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    allAttempts.Add($"ERR {m.Name}(typeof({t!.Name}), name): {(ex.InnerException?.Message ?? ex.Message)}");
                                    var innerMsg = ex.InnerException?.Message ?? ex.Message;
                                    if (innerMsg.IndexOf("ValueIsNotUnique", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var ex2 = FindExistingByName(itemsComp, name);
                                        if (ex2 != null) return ex2;
                                    }
                                }
                            }
                        }

                        // Create(string name, string typeId) / Create(string typeId, string name)
                        if (!m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string))
                        {
                            foreach (var th in stringTypeHints)
                            {
                                foreach (var order in new[] { new object[] { name, th }, new object[] { th, name } })
                                {
                                    try
                                    {
                                        var obj = m.Invoke(itemsComp, order);
                                        if (obj != null)
                                        {
                                            allAttempts.Add($"OK {m.Name}({order[0]}, {order[1]})");
                                            return obj;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        allAttempts.Add($"ERR {m.Name}({order[0]}, {order[1]}): {(ex.InnerException?.Message ?? ex.Message)}");
                                        var innerMsg = ex.InnerException?.Message ?? ex.Message;
                                        if (innerMsg.IndexOf("ValueIsNotUnique", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            var ex2 = FindExistingByName(itemsComp, name);
                                            if (ex2 != null) return ex2;
                                        }
                                    }
                                }
                            }
                        }

                        // Create(string name)
                        if (!m.IsGenericMethodDefinition && ps.Length == 1 && ps[0].ParameterType == typeof(string))
                        {
                            try
                            {
                                var obj = m.Invoke(itemsComp, new object[] { name });
                                if (obj != null)
                                {
                                    allAttempts.Add($"OK {m.Name}(name)");
                                    return obj;
                                }
                            }
                            catch (Exception ex)
                            {
                                allAttempts.Add($"ERR {m.Name}(name): {(ex.InnerException?.Message ?? ex.Message)}");
                                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                                if (innerMsg.IndexOf("ValueIsNotUnique", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    var ex2 = FindExistingByName(itemsComp, name);
                                    if (ex2 != null) return ex2;
                                }
                            }
                        }
                    }

                    Step($"ui-detail:{name}", false, string.Join(" || ", allAttempts));
                    return null;
                }

                var hdrBar = CreateItem("HDR_Bar", new[] { tRectangle }, new[] { "HmiRectangle", "Rectangle" });
                Step("ui:HDR_Bar", hdrBar != null, hdrBar?.GetType().FullName);
                var hdrTitle = CreateItem("HDR_Title", new[] { tLabel, tIOField }, new[] { "HmiLabel", "Label", "HmiText", "Text" });
                Step("ui:HDR_Title", hdrTitle != null, hdrTitle?.GetType().FullName);

                var btnStart = CreateItem("BTN_Start", new[] { tButton }, new[] { "HmiButton", "Button" });
                Step("ui:BTN_Start", btnStart != null, btnStart?.GetType().FullName);
                var btnStop = CreateItem("BTN_Stop", new[] { tButton }, new[] { "HmiButton", "Button" });
                Step("ui:BTN_Stop", btnStop != null, btnStop?.GetType().FullName);
                var lampRun = CreateItem("LAMP_Run", new[] { tRectangle, tIOField }, new[] { "HmiRectangle", "HmiIOField", "Lamp", "HmiLamp" });
                Step("ui:LAMP_Run", lampRun != null, lampRun?.GetType().FullName);

                // Layout + styling (Unified RT): header strip + grouped controls
                try
                {
                    if (hdrBar != null)
                    {
                        TrySetProperty(hdrBar, "Left", 0);
                        TrySetProperty(hdrBar, "Top", 0);
                        TrySetProperty(hdrBar, "Width", (uint)1280);
                        TrySetProperty(hdrBar, "Height", (uint)72);
                        TrySetProperty(hdrBar, "BackColor", ColorTranslator.FromHtml("#1E3A5F"));
                        TrySetProperty(hdrBar, "BorderWidth", (uint)0);
                    }

                    if (hdrTitle != null)
                    {
                        TrySetProperty(hdrTitle, "Left", 24);
                        TrySetProperty(hdrTitle, "Top", 12);
                        TrySetProperty(hdrTitle, "Width", (uint)900);
                        TrySetProperty(hdrTitle, "Height", (uint)48);
                        var txtH = hdrTitle.GetType().GetProperty("Text")?.GetValue(hdrTitle);
                        if (txtH != null)
                        {
                            TrySetProperty(txtH, "Item", "MCP 验证 · 起停与状态");
                            TrySetProperty(txtH, "HorizontalAlignment", "Left");
                        }

                        TrySetProperty(hdrTitle, "ForeColor", Color.White);
                    }

                    if (btnStart != null)
                    {
                        TrySetProperty(btnStart, "Left", 48);
                        TrySetProperty(btnStart, "Top", 110);
                        TrySetProperty(btnStart, "Width", (uint)200);
                        TrySetProperty(btnStart, "Height", (uint)72);
                        TrySetProperty(btnStart, "BackColor", ColorTranslator.FromHtml("#2E7D32"));
                        var txt = btnStart.GetType().GetProperty("Text")?.GetValue(btnStart);
                        if (txt != null)
                        {
                            TrySetProperty(txt, "Item", "启动 (Start)");
                            TrySetProperty(txt, "HorizontalAlignment", "Center");
                        }
                    }

                    if (btnStop != null)
                    {
                        TrySetProperty(btnStop, "Left", 48);
                        TrySetProperty(btnStop, "Top", 200);
                        TrySetProperty(btnStop, "Width", (uint)200);
                        TrySetProperty(btnStop, "Height", (uint)72);
                        TrySetProperty(btnStop, "BackColor", ColorTranslator.FromHtml("#C62828"));
                        var txt = btnStop.GetType().GetProperty("Text")?.GetValue(btnStop);
                        if (txt != null)
                        {
                            TrySetProperty(txt, "Item", "停止 (Stop)");
                            TrySetProperty(txt, "HorizontalAlignment", "Center");
                        }
                    }

                    if (lampRun != null)
                    {
                        TrySetProperty(lampRun, "Left", 300);
                        TrySetProperty(lampRun, "Top", 110);
                        TrySetProperty(lampRun, "Width", (uint)120);
                        TrySetProperty(lampRun, "Height", (uint)120);
                        TrySetProperty(lampRun, "BackColor", ColorTranslator.FromHtml("#B0BEC5"));
                        TrySetProperty(lampRun, "BorderWidth", (uint)2);
                    }

                    Step("ui:layout", true);
                }
                catch (Exception ex)
                {
                    Step("ui:layout", false, ex.InnerException?.Message ?? ex.Message);
                }

                // Attempt: map button pressed state to HMI tag (momentary) via PressedStateTags composition (best-effort)
                void TryBindPressedTag(object? button, string table, string tagName)
                {
                    if (button == null) return;
                    try
                    {
                        var pst = button.GetType().GetProperty("PressedStateTags")?.GetValue(button);
                        if (pst == null)
                        {
                            Step($"bind:{TryGetName(button)}.PressedStateTags", false, "PressedStateTags missing");
                            return;
                        }

                        // dump create signatures once per button
                        var sigs = pst.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => m.Name.StartsWith("Create", StringComparison.OrdinalIgnoreCase))
                            .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName))})")
                            .ToArray();
                        Step($"bind:{TryGetName(button)}.PressedStateTags.CreateSignatures", true, string.Join(" | ", sigs));

                        // idempotent check
                        var existing = FindExistingByName(pst, tagName);
                        if (existing != null)
                        {
                            Step($"bind:{TryGetName(button)}:{tagName}", true, "exists");
                            return;
                        }

                        // Try Create() then bind HMI tag path (table/tag) for Unified RT
                        var m0 = pst.GetType().GetMethod("Create", Type.EmptyTypes);
                        if (m0 != null)
                        {
                            try
                            {
                                var o = m0.Invoke(pst, Array.Empty<object>());
                                if (o != null)
                                {
                                    var path = string.IsNullOrWhiteSpace(table) ? tagName : $"{table}/{tagName}";
                                    var bound = TrySetAnyProperty(o, path, "Tag", "TagName", "HmiTag", "HmiTagName", "Path", "HmiTagPath", "FullName")
                                                || TrySetEngineeringAttribute(o, "Tag", path)
                                                || TrySetEngineeringAttribute(o, "HmiTag", path);
                                    Step($"bind:{TryGetName(button)}:{tagName}", bound, o.GetType().FullName);
                                    return;
                                }
                            }
                            catch (TargetInvocationException tie) when (tie.InnerException != null)
                            {
                                var msg = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
                                Step($"bind:{TryGetName(button)}:{tagName}", false, msg);
                                return;
                            }
                        }

                        // Try Create(string)
                        var m1 = pst.GetType().GetMethod("Create", new[] { typeof(string) });
                        if (m1 != null)
                        {
                            try
                            {
                                var path2 = string.IsNullOrWhiteSpace(table) ? tagName : $"{table}/{tagName}";
                                var o = m1.Invoke(pst, new object[] { path2 });
                                Step($"bind:{TryGetName(button)}:{tagName}", o != null, o?.GetType().FullName);
                                return;
                            }
                            catch (TargetInvocationException tie) when (tie.InnerException != null)
                            {
                                var msg = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
                                Step($"bind:{TryGetName(button)}:{tagName}", false, msg);
                                return;
                            }
                        }

                        // Fallback: some parts may expose a property like TagName/Tag
                        Step($"bind:{TryGetName(button)}:{tagName}", false, "No suitable Create on PressedStateTags");
                    }
                    catch (Exception ex)
                    {
                        Step($"bind:{TryGetName(button)}:{tagName}", false, ex.InnerException?.Message ?? ex.Message);
                    }
                }

                TryBindPressedTag(btnStart, tagTableName, "StartPB");
                TryBindPressedTag(btnStop, tagTableName, "StopPB");

                // Attempt: lamp BackColor dynamization based on RunOut (best-effort; may need richer APIs)
                try
                {
                    if (lampRun != null)
                    {
                        var dyn = lampRun.GetType().GetProperty("Dynamizations")?.GetValue(lampRun);
                        if (dyn == null)
                        {
                            Step("dyn:LAMP_Run", false, "Dynamizations missing");
                        }
                        else
                        {
                            var sigs = dyn.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .Where(m => m.Name.StartsWith("Create", StringComparison.OrdinalIgnoreCase))
                                .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName))})")
                                .ToArray();
                            Step("dyn:LAMP_Run.CreateSignatures", true, string.Join(" | ", sigs));

                            // No universal way here without knowing specific dynamization classes;
                            // return signatures so next iteration can target correct Create overload.
                            Step("dyn:LAMP_Run", false, "Not implemented yet (see CreateSignatures)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Step("dyn:LAMP_Run", false, ex.InnerException?.Message ?? ex.Message);
                }

                meta["success"] = true;
                return new ResponseMessage
                {
                    Message = "Unified HMI start/stop skeleton created (best-effort).",
                    Meta = meta
                };
            }
            catch (Exception ex)
            {
                Step("exception", false, ex.ToString());
                return new ResponseMessage { Message = "Failed creating HMI skeleton", Meta = meta };
            }
        }

        public ResponseMessage EnsureUnifiedHmiScreen(string hmiSoftwarePath, string screenName, uint width = 0, uint height = 0)
        {
            return RunHmiStepTool("EnsureUnifiedHmiScreen", meta =>
            {
                var sw = ResolveHmiSoftwareOrThrow(hmiSoftwarePath);
                var screens = TryGetPropertyValue(sw, "Screens");
                if (screens == null) throw new InvalidOperationException("HMI Screens collection not found.");

                var screen = TryFindByNameInCollection(sw, new[] { "Screens", "ScreenFolder" }, screenName);
                var action = "exists";
                if (screen == null)
                {
                    var mCreate = screens.GetType().GetMethod("Create", new[] { typeof(string) });
                    if (mCreate == null) throw new InvalidOperationException($"Create(string) not found on {screens.GetType().FullName}.");
                    screen = InvokeCreate(mCreate, screens, new object[] { screenName });
                    action = "created";
                }

                if (screen == null) throw new InvalidOperationException("Screen create/find returned null.");
                if (width > 0) TrySetProperty(screen, "Width", width);
                if (height > 0) TrySetProperty(screen, "Height", height);

                meta["action"] = action;
                meta["screenType"] = screen.GetType().FullName;
                return $"HMI screen '{screenName}' {action}.";
            });
        }

        public ResponseMessage EnsureUnifiedHmiScreenItem(string hmiSoftwarePath, string screenName, string itemName, string itemType = "Button", int left = 0, int top = 0, uint width = 120, uint height = 40, string text = "")
        {
            return RunHmiStepTool("EnsureUnifiedHmiScreenItem", meta =>
            {
                var screen = ResolveHmiScreenOrThrow(hmiSoftwarePath, screenName);
                var items = TryGetPropertyValue(screen, "ScreenItems");
                if (items == null) throw new InvalidOperationException($"ScreenItems collection not found on screen '{screenName}'.");

                var item = FindExistingByName(items, itemName);
                var action = "exists";
                if (item == null)
                {
                    var itemClrType = ResolveUnifiedScreenItemType(itemType);
                    item = CreateUnifiedScreenItem(items, itemName, itemClrType, itemType);
                    action = "created";
                }

                if (item == null) throw new InvalidOperationException("Screen item create/find returned null.");
                TrySetProperty(item, "Left", left);
                TrySetProperty(item, "Top", top);
                TrySetProperty(item, "Width", width);
                TrySetProperty(item, "Height", height);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var textPart = TryGetPropertyValue(item, "Text", "DisplayName");
                    if (textPart != null) TrySetProperty(textPart, "Item", text);
                }

                meta["action"] = action;
                meta["itemType"] = item.GetType().FullName;
                return $"HMI screen item '{itemName}' {action}.";
            });
        }

        public ResponseMessage ApplyUnifiedHmiScreenDesignJson(string hmiSoftwarePath, string screenName, string designJson, bool strict = true)
        {
            return RunHmiStepTool("ApplyUnifiedHmiScreenDesignJson", meta =>
            {
                if (string.IsNullOrWhiteSpace(designJson))
                {
                    throw new InvalidOperationException("designJson is empty.");
                }

                var root = JsonNode.Parse(designJson) as JsonObject
                    ?? throw new InvalidOperationException("designJson root must be a JSON object.");

                var screen = ResolveHmiScreenOrThrow(hmiSoftwarePath, screenName);
                var items = TryGetPropertyValue(screen, "ScreenItems")
                    ?? throw new InvalidOperationException($"ScreenItems collection not found on screen '{screenName}'.");

                var changed = new JsonArray();
                var failed = new JsonArray();

                if (root["screen"] is JsonObject screenProps)
                {
                    ApplyJsonProperties(screen, screenProps, failed, "screen");
                }

                var itemArray = root["items"] as JsonArray
                    ?? throw new InvalidOperationException("designJson.items must be an array.");

                foreach (var itemNode in itemArray.OfType<JsonObject>())
                {
                    var name = JsonString(itemNode, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        failed.Add("item without name skipped");
                        continue;
                    }

                    try
                    {
                        var typeHint = JsonString(itemNode, "type");
                        if (string.IsNullOrWhiteSpace(typeHint)) typeHint = "Rectangle";

                        var item = FindExistingByName(items, name!);
                        var action = "updated";
                        if (item == null)
                        {
                            var itemClrType = ResolveUnifiedScreenItemType(typeHint!);
                            item = CreateUnifiedScreenItem(items, name!, itemClrType, typeHint!);
                            action = "created";
                        }

                        if (item == null) throw new InvalidOperationException($"Create/find returned null for '{name}'.");

                        if (itemNode["left"] != null) TrySetProperty(item, "Left", JsonObjectValue(itemNode["left"]));
                        if (itemNode["top"] != null) TrySetProperty(item, "Top", JsonObjectValue(itemNode["top"]));
                        if (itemNode["width"] != null) TrySetProperty(item, "Width", JsonObjectValue(itemNode["width"]));
                        if (itemNode["height"] != null) TrySetProperty(item, "Height", JsonObjectValue(itemNode["height"]));

                        if (itemNode["properties"] is JsonObject props)
                        {
                            ApplyJsonProperties(item, props, failed, name!, typeHint ?? string.Empty);
                        }

                        var text = JsonString(itemNode, "text");
                        if (!string.IsNullOrEmpty(text))
                        {
                            var textTarget = JsonString(itemNode, "textProperty");
                            if (string.IsNullOrWhiteSpace(textTarget)) textTarget = "Text";
                            if (!TrySetMultilingualText(item, textTarget!, text!, JsonString(itemNode, "culture") ?? "zh-CN"))
                            {
                                failed.Add($"{name}.{textTarget}: text write failed");
                            }
                        }

                        if (itemNode["font"] is JsonObject font)
                        {
                            var fontPart = TryGetPropertyValue(item, "Font");
                            if (fontPart != null) ApplyJsonProperties(fontPart, font, failed, name + ".Font");
                            else failed.Add($"{name}.Font: part not found");
                        }

                        if (itemNode["content"] is JsonObject content)
                        {
                            var contentPart = TryGetPropertyValue(item, "Content");
                            if (contentPart != null) ApplyJsonProperties(contentPart, content, failed, name + ".Content");
                            else failed.Add($"{name}.Content: part not found");
                        }

                        if (itemNode["padding"] is JsonObject padding)
                        {
                            var paddingPart = TryGetPropertyValue(item, "Padding");
                            if (paddingPart != null) ApplyJsonProperties(paddingPart, padding, failed, name + ".Padding");
                            else failed.Add($"{name}.Padding: part not found");
                        }

                        changed.Add($"{action}:{name}:{item.GetType().Name}");
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                meta["changed"] = changed;
                meta["failed"] = failed;
                meta["strict"] = strict;
                if (strict && failed.Count > 0)
                {
                    throw new InvalidOperationException($"Unified HMI design apply had {failed.Count} failed writes: {string.Join(" | ", failed.Select(x => x?.ToString() ?? string.Empty))}");
                }
                return $"Applied Unified HMI design to '{screenName}'. changed={changed.Count}, failed={failed.Count}.";
            });
        }

        public ResponseMessage BindUnifiedHmiButtonPressedTag(string hmiSoftwarePath, string screenName, string buttonName, string tagName)
        {
            return RunHmiStepTool("BindUnifiedHmiButtonPressedTag", meta =>
            {
                var screen = ResolveHmiScreenOrThrow(hmiSoftwarePath, screenName);
                var items = TryGetPropertyValue(screen, "ScreenItems");
                if (items == null) throw new InvalidOperationException($"ScreenItems collection not found on screen '{screenName}'.");

                var button = FindExistingByName(items, buttonName);
                if (button == null) throw new InvalidOperationException($"Screen item '{buttonName}' not found.");

                var pressedStateTags = TryGetPropertyValue(button, "PressedStateTags");
                if (pressedStateTags == null) throw new InvalidOperationException($"PressedStateTags not found on '{buttonName}'.");

                var existing = FindPressedStateTag(pressedStateTags, tagName);
                var action = "exists";
                if (existing == null)
                {
                    var mCreate = pressedStateTags.GetType().GetMethod("Create", Type.EmptyTypes);
                    if (mCreate == null) throw new InvalidOperationException($"Create() not found on {pressedStateTags.GetType().FullName}.");
                    existing = InvokeCreate(mCreate, pressedStateTags, Array.Empty<object>());
                    action = "created";
                }

                if (existing == null) throw new InvalidOperationException("Pressed-state part create/find returned null.");
                var bound = TrySetAnyProperty(existing, tagName, "Tag", "TagName", "HmiTag", "HmiTagName", "Name", "TagPath");

                meta["action"] = action;
                meta["pressedStateTagPartType"] = existing.GetType().FullName;
                meta["propertyBound"] = bound;
                if (!bound)
                {
                    meta["availableProperties"] = string.Join(", ", existing.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{p.Name}:{p.PropertyType.Name}"));
                }

                return bound
                    ? $"Button '{buttonName}' pressed-state tag bound to '{tagName}'."
                    : $"Pressed-state part created for '{buttonName}', but no writable tag-name property was found.";
            });
        }

        public List<string> ListUnifiedHmiApiTypes(string nameContains = "", int limit = 500)
        {
            var filter = nameContains?.Trim() ?? string.Empty;
            var result = new List<string>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t.FullName == null || !t.FullName.StartsWith("Siemens.Engineering.HmiUnified.", StringComparison.Ordinal)) continue;
                    if (!string.IsNullOrWhiteSpace(filter) && t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var kind = t.IsEnum ? "enum" : t.IsClass ? "class" : t.IsInterface ? "interface" : t.IsValueType ? "value" : "type";
                    var line = $"{kind}: {t.FullName}";
                    if (t.BaseType != null && t.BaseType != typeof(object))
                    {
                        line += $" : {t.BaseType.FullName}";
                    }
                    if (t.IsEnum)
                    {
                        line += $" values=[{string.Join(",", Enum.GetNames(t).Take(50))}]";
                    }

                    result.Add(line);
                    if (result.Count >= Math.Max(1, limit)) return result;
                }
            }

            return result;
        }

        public ResponseMessage EnsureUnifiedHmiButtonEventHandler(string hmiSoftwarePath, string screenName, string buttonName, string eventType)
        {
            return RunHmiStepTool("EnsureUnifiedHmiButtonEventHandler", meta =>
            {
                var button = ResolveHmiScreenItemOrThrow(hmiSoftwarePath, screenName, buttonName);
                var eventHandlers = TryGetPropertyValue(button, "EventHandlers");
                if (eventHandlers == null) throw new InvalidOperationException($"EventHandlers not found on '{buttonName}'.");

                var create = eventHandlers.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
                if (create == null) throw new InvalidOperationException($"Create(enum) not found on {eventHandlers.GetType().FullName}.");

                var enumType = create.GetParameters()[0].ParameterType;
                var enumValue = Enum.Parse(enumType, eventType, ignoreCase: true);

                object? handler = null;
                var find = eventHandlers.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Find" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == enumType);
                if (find != null)
                {
                    handler = find.Invoke(eventHandlers, new[] { enumValue });
                }

                var action = "exists";
                if (handler == null)
                {
                    handler = InvokeCreate(create, eventHandlers, new[] { enumValue });
                    action = "created";
                }

                if (handler == null) throw new InvalidOperationException("Event handler create/find returned null.");
                meta["action"] = action;
                meta["eventEnumType"] = enumType.FullName;
                meta["eventType"] = enumValue.ToString();
                meta["handlerType"] = handler.GetType().FullName;
                meta["handlerMembers"] = string.Join(" | ", DescribeMembers(handler, 80).Select(m => $"{m.Kind}:{m.Name}:{m.Type}"));
                return $"Button event handler '{eventValueToText(enumValue)}' on '{buttonName}' {action}.";
            });

            static string eventValueToText(object value) => value.ToString() ?? string.Empty;
        }

        public ModelContextProtocol.ResponseObjectDescribe DescribeUnifiedHmiButtonEventScript(string hmiSoftwarePath, string screenName, string buttonName, string eventType, int maxMembers = 200)
        {
            try
            {
                if (IsProjectNull())
                {
                    return new ModelContextProtocol.ResponseObjectDescribe
                    {
                        Message = "Project is null",
                        ObjectKind = "HmiButtonEventScript",
                        ObjectPath = $"{hmiSoftwarePath}:{screenName}:{buttonName}:{eventType}",
                        Members = Array.Empty<ModelContextProtocol.ObjectMember>()
                    };
                }

                var handler = ResolveHmiButtonEventHandlerOrThrow(hmiSoftwarePath, screenName, buttonName, eventType);
                var scriptProp = handler.GetType().GetProperty("Script", BindingFlags.Public | BindingFlags.Instance);
                var script = scriptProp?.GetValue(handler);

                var members = new List<ModelContextProtocol.ObjectMember>
                {
                    new ModelContextProtocol.ObjectMember
                    {
                        Name = "HandlerType",
                        Kind = "Info",
                        Type = handler.GetType().FullName ?? handler.GetType().Name,
                        Signature = null
                    },
                    new ModelContextProtocol.ObjectMember
                    {
                        Name = "ScriptProperty",
                        Kind = "Info",
                        Type = scriptProp == null ? "missing" : $"{scriptProp.PropertyType.FullName}; CanRead={scriptProp.CanRead}; CanWrite={scriptProp.CanWrite}",
                        Signature = null
                    },
                    new ModelContextProtocol.ObjectMember
                    {
                        Name = "ScriptValue",
                        Kind = "Info",
                        Type = script == null ? "null" : (script.GetType().FullName ?? script.GetType().Name),
                        Signature = null
                    }
                };

                if (script != null)
                {
                    members.AddRange(DescribeMembers(script, Math.Max(10, Math.Min(2000, maxMembers))));
                    try
                    {
                        var infos = script.GetType().GetMethod("GetAttributeInfos", Type.EmptyTypes)?.Invoke(script, Array.Empty<object>());
                        if (infos is IEnumerable en)
                        {
                            foreach (var info in en.Cast<object>().Take(100))
                            {
                                members.Add(new ModelContextProtocol.ObjectMember
                                {
                                    Name = $"AttributeInfo:{TryGetPropertyValue(info, "Name") ?? info}",
                                    Kind = "AttributeInfo",
                                    Type = TryGetPropertyValue(info, "DataType", "Type")?.ToString(),
                                    Signature = info.ToString()
                                });
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    members.AddRange(DescribeMembers(handler, Math.Max(10, Math.Min(2000, maxMembers))));
                    try
                    {
                        var infos = handler.GetType().GetMethod("GetAttributeInfos", Type.EmptyTypes)?.Invoke(handler, Array.Empty<object>());
                        if (infos is IEnumerable en)
                        {
                            foreach (var info in en.Cast<object>().Take(100))
                            {
                                members.Add(new ModelContextProtocol.ObjectMember
                                {
                                    Name = $"HandlerAttributeInfo:{TryGetPropertyValue(info, "Name") ?? info}",
                                    Kind = "AttributeInfo",
                                    Type = TryGetPropertyValue(info, "DataType", "Type")?.ToString(),
                                    Signature = info.ToString()
                                });
                            }
                        }
                    }
                    catch { }
                }

                return new ModelContextProtocol.ResponseObjectDescribe
                {
                    Message = "OK",
                    ObjectKind = "HmiButtonEventScript",
                    ObjectPath = $"{hmiSoftwarePath}:{screenName}:{buttonName}:{eventType}.Script",
                    TypeName = script == null ? scriptProp?.PropertyType.FullName : script.GetType().FullName,
                    Members = members
                };
            }
            catch (Exception ex)
            {
                return new ModelContextProtocol.ResponseObjectDescribe
                {
                    Message = ex.ToString(),
                    ObjectKind = "HmiButtonEventScript",
                    ObjectPath = $"{hmiSoftwarePath}:{screenName}:{buttonName}:{eventType}.Script",
                    Members = Array.Empty<ModelContextProtocol.ObjectMember>()
                };
            }
        }

        public ResponseMessage SetUnifiedHmiButtonEventScriptCode(string hmiSoftwarePath, string screenName, string buttonName, string eventType, string scriptCode, string globalDefinitionAreaScriptCode = "", bool async = false, bool syntaxCheck = false)
        {
            return RunHmiStepTool("SetUnifiedHmiButtonEventScriptCode", meta =>
            {
                var handler = ResolveHmiButtonEventHandlerOrThrow(hmiSoftwarePath, screenName, buttonName, eventType);
                var script = TryGetPropertyValue(handler, "Script");
                if (script == null)
                {
                    throw new InvalidOperationException($"Script object is null on '{buttonName}.{eventType}'. Ensure the event handler exists first.");
                }

                var setScriptCode = TrySetProperty(script, "ScriptCode", scriptCode ?? string.Empty);
                var setGlobalCode = TrySetProperty(script, "GlobalDefinitionAreaScriptCode", globalDefinitionAreaScriptCode ?? string.Empty);
                var setAsync = TrySetProperty(script, "Async", async);

                meta["scriptType"] = script.GetType().FullName;
                meta["setScriptCode"] = setScriptCode;
                meta["setGlobalDefinitionAreaScriptCode"] = setGlobalCode;
                meta["setAsync"] = setAsync;

                // 写不进去就到此为止：ScriptCode 都没落下，再去跑 SyntaxCheck 只是拿一个
                // 已知会弄崩 V21 的调用，去检查一份根本不存在的脚本。这个判断以前排在
                // SyntaxCheck 之后，等于先冒一次崩溃风险，才发现这一步本来就该失败。
                if (!setScriptCode)
                {
                    throw new InvalidOperationException($"ScriptCode property could not be written on {script.GetType().FullName}.");
                }

                // SyntaxCheck 默认不跑（issue #36）：TIA V21 上对 Unified 的 Script 对象调
                // SyntaxCheck() 会偶发抛 NonRecoverableException 并带走整个 Portal 进程，
                // 脚本已写进内存却随进程一起丢掉。检查是可选的增值动作，不该让「写脚本」
                // 这件必须成功的事去赌它。需要证据的调用方显式传 syntaxCheck: true。
                meta["syntaxCheckRequested"] = syntaxCheck;
                if (!syntaxCheck)
                {
                    // 不发 syntaxErrorCount：缺席必须读成「没查」，而不是「查了 0 个错」。
                    meta["syntaxCheckStatus"] = "skipped";
                    meta["syntaxCheckSkippedReason"] =
                        "SyntaxCheck was not run (default). On TIA V21 it can crash the Portal process " +
                        "(NonRecoverableException) and take the just-written ScriptCode with it. " +
                        "Pass syntaxCheck=true only when you need the evidence and can afford the risk.";
                }
                else
                {
                    object? syntaxResult = null;
                    try
                    {
                        syntaxResult = script.GetType().GetMethod("SyntaxCheck", Type.EmptyTypes)?.Invoke(script, Array.Empty<object>());
                        if (syntaxResult != null)
                        {
                            var syntaxErrors = TryGetEnumerableStrings(syntaxResult, "Errors").ToList();
                            var syntaxWarnings = TryGetEnumerableStrings(syntaxResult, "Warnings").ToList();
                            meta["syntaxCheckStatus"] = "ran";
                            meta["syntaxResultType"] = syntaxResult.GetType().FullName;
                            meta["syntaxResult"] = syntaxResult.ToString();
                            meta["syntaxErrors"] = ToJsonArray(syntaxErrors);
                            meta["syntaxWarnings"] = ToJsonArray(syntaxWarnings);
                            meta["syntaxErrorCount"] = syntaxErrors.Count;
                            meta["syntaxWarningCount"] = syntaxWarnings.Count;
                            meta["syntaxPropertyName"] = TryGetPropertyValue(syntaxResult, "PropertyName")?.ToString() ?? string.Empty;
                            meta["syntaxMembers"] = string.Join(" | ", DescribeMembers(syntaxResult, 80).Select(m => $"{m.Kind}:{m.Name}:{m.Type}"));
                        }
                        else
                        {
                            // 这个 Script 类型上根本没有 SyntaxCheck 方法，同样不是「0 个错」。
                            meta["syntaxCheckStatus"] = "unavailable";
                            meta["syntaxCheckSkippedReason"] = $"No SyntaxCheck() method on {script.GetType().FullName}.";
                        }
                    }
                    catch (Exception ex)
                    {
                        var real = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                        meta["syntaxCheckStatus"] = "faulted";
                        meta["syntaxError"] = $"{real.GetType().FullName}: {real.Message}";

                        // NonRecoverableException 不是「这一步没做成」，是 Portal 进程已经没了。
                        // 刚写进去的 ScriptCode 没保存就随进程消失，这时候再返回 Success 是在撒谎。
                        if (ModelContextProtocol.PortalFailureClassifier.IsPortalProcessLost(real))
                        {
                            throw new InvalidOperationException(
                                "SyntaxCheck killed the TIA Portal process (" + real.GetType().Name + "). " +
                                "The ScriptCode was written in memory but is NOT saved - the whole session is gone. " +
                                "Reconnect, re-apply the script with syntaxCheck=false, and save. This is issue #36.", real);
                        }
                    }
                }

                return syntaxCheck
                    ? $"ScriptCode set for '{buttonName}.{eventType}'."
                    : $"ScriptCode set for '{buttonName}.{eventType}' (SyntaxCheck skipped by default; see syntaxCheckSkippedReason).";
            });
        }

        public ResponseMessage EnsureUnifiedHmiDynamization(string hmiSoftwarePath, string screenName, string itemName, string propertyName, string dynamizationType = "")
        {
            return RunHmiStepTool("EnsureUnifiedHmiDynamization", meta =>
            {
                var item = ResolveHmiScreenItemOrThrow(hmiSoftwarePath, screenName, itemName);
                var dynamizations = TryGetPropertyValue(item, "Dynamizations");
                if (dynamizations == null) throw new InvalidOperationException($"Dynamizations not found on '{itemName}'.");

                var find = dynamizations.GetType().GetMethod("Find", new[] { typeof(string) });
                var existing = find?.Invoke(dynamizations, new object[] { propertyName });
                if (existing != null)
                {
                    meta["action"] = "exists";
                    meta["dynamizationType"] = existing.GetType().FullName;
                    meta["members"] = string.Join(" | ", DescribeMembers(existing, 100).Select(m => $"{m.Kind}:{m.Name}:{m.Type}"));
                    return $"Dynamization for '{itemName}.{propertyName}' exists.";
                }

                var createMethods = dynamizations.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string))
                    .ToList();
                if (!createMethods.Any())
                {
                    throw new InvalidOperationException($"Generic Create<T>(string) not found on {dynamizations.GetType().FullName}.");
                }

                var candidates = ResolveUnifiedHmiDynamizationTypes(dynamizationType).ToList();
                meta["candidateTypes"] = string.Join(" | ", candidates.Select(t => t.FullName));
                if (!candidates.Any())
                {
                    throw new InvalidOperationException($"No dynamization type matched '{dynamizationType}'. Use ListUnifiedHmiApiTypes with nameContains='Dynamization'.");
                }

                var errors = new List<string>();
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var created = createMethods[0].MakeGenericMethod(candidate).Invoke(dynamizations, new object[] { propertyName });
                        if (created == null) continue;

                        meta["action"] = "created";
                        meta["dynamizationType"] = created.GetType().FullName;
                        meta["members"] = string.Join(" | ", DescribeMembers(created, 120).Select(m => $"{m.Kind}:{m.Name}:{m.Type}"));
                        return $"Dynamization for '{itemName}.{propertyName}' created as '{created.GetType().Name}'.";
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{candidate.FullName}: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }

                meta["attemptErrors"] = string.Join(" || ", errors);
                throw new InvalidOperationException($"Unable to create dynamization for '{itemName}.{propertyName}'.");
            });
        }

        /// <summary>
        /// Unified TagDynamization: PLC address may live on property <c>Address</c>, <c>LogicalAddress</c>,
        /// or only as an engineering attribute — plain <see cref="TrySetProperty"/> often misses it.
        /// </summary>
        private static bool TrySetTagDynamizationAddress(object dyn, string address)
        {
            if (string.IsNullOrWhiteSpace(address) || dyn == null) return false;
            foreach (var attr in new[] { "Address", "LogicalAddress", "ControllerTagAddress", "PlcAddress" })
            {
                if (TrySetProperty(dyn, attr, address)) return true;
                if (TrySetEngineeringAttribute(dyn, attr, address)) return true;
            }

            return false;
        }

        public ResponseMessage BindUnifiedHmiTagDynamization(string hmiSoftwarePath, string screenName, string itemName, string propertyName, string tagName, string dataType = "Bool", string plcTag = "", string address = "")
        {
            return RunHmiStepTool("BindUnifiedHmiTagDynamization", meta =>
            {
                var item = ResolveHmiScreenItemOrThrow(hmiSoftwarePath, screenName, itemName);
                var dynamizations = TryGetPropertyValue(item, "Dynamizations");
                if (dynamizations == null) throw new InvalidOperationException($"Dynamizations not found on '{itemName}'.");

                var find = dynamizations.GetType().GetMethod("Find", new[] { typeof(string) });
                var dyn = find?.Invoke(dynamizations, new object[] { propertyName });
                var action = "exists";
                if (dyn == null)
                {
                    var tagDynType = ResolveUnifiedHmiDynamizationTypes("TagDynamization").FirstOrDefault(t => t.Name == "TagDynamization");
                    if (tagDynType == null) throw new InvalidOperationException("TagDynamization type not found.");

                    var create = dynamizations.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                    if (create == null) throw new InvalidOperationException($"Create<T>(string) not found on {dynamizations.GetType().FullName}.");

                    dyn = create.MakeGenericMethod(tagDynType).Invoke(dynamizations, new object[] { propertyName });
                    action = "created";
                }

                if (dyn == null) throw new InvalidOperationException("Dynamization create/find returned null.");
                var setTag = TrySetProperty(dyn, "Tag", tagName);
                var setDataType = TrySetProperty(dyn, "DataType", dataType);
                var setPlcTag = !string.IsNullOrWhiteSpace(plcTag) && TrySetProperty(dyn, "PlcTag", plcTag);
                var setAddress = false;
                if (!string.IsNullOrWhiteSpace(address))
                {
                    setAddress = TrySetTagDynamizationAddress(dyn, address);
                }

                meta["action"] = action;
                meta["dynamizationType"] = dyn.GetType().FullName;
                meta["setTag"] = setTag;
                meta["setDataType"] = setDataType;
                meta["setPlcTag"] = string.IsNullOrWhiteSpace(plcTag) ? "skipped" : setPlcTag;
                meta["setAddress"] = string.IsNullOrWhiteSpace(address) ? "skipped" : setAddress;
                meta["members"] = string.Join(" | ", DescribeMembers(dyn, 120).Select(m => $"{m.Kind}:{m.Name}:{m.Type}"));

                if (!setTag)
                {
                    throw new InvalidOperationException($"Tag property could not be written on {dyn.GetType().FullName}.");
                }

                return $"Tag dynamization for '{itemName}.{propertyName}' bound to '{tagName}'.";
            });
        }

        private object ResolveHmiButtonEventHandlerOrThrow(string hmiSoftwarePath, string screenName, string buttonName, string eventType)
        {
            var button = ResolveHmiScreenItemOrThrow(hmiSoftwarePath, screenName, buttonName);
            var eventHandlers = TryGetPropertyValue(button, "EventHandlers");
            if (eventHandlers == null)
            {
                throw new InvalidOperationException($"EventHandlers not found on '{buttonName}'.");
            }

            var create = eventHandlers.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
            if (create == null)
            {
                throw new InvalidOperationException($"Create(enum) not found on {eventHandlers.GetType().FullName}.");
            }

            var enumType = create.GetParameters()[0].ParameterType;
            var enumValue = Enum.Parse(enumType, eventType, ignoreCase: true);
            var find = eventHandlers.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Find" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == enumType);

            var handler = find?.Invoke(eventHandlers, new[] { enumValue });
            if (handler != null) return handler;

            handler = InvokeCreate(create, eventHandlers, new[] { enumValue });
            if (handler == null)
            {
                throw new InvalidOperationException($"Button event handler '{eventType}' create/find returned null.");
            }

            return handler;
        }

        private static Type? ResolveUnifiedScreenItemType(string itemType)
        {
            var key = (itemType ?? string.Empty).Trim();
            var candidates = key.Equals("Button", StringComparison.OrdinalIgnoreCase) || key.Equals("HmiButton", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton" }
                : key.Equals("Text", StringComparison.OrdinalIgnoreCase) || key.Equals("HmiText", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Siemens.Engineering.HmiUnified.UI.Shapes.HmiText" }
                : key.Equals("Rectangle", StringComparison.OrdinalIgnoreCase) || key.Equals("Lamp", StringComparison.OrdinalIgnoreCase) || key.Equals("HmiRectangle", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Siemens.Engineering.HmiUnified.UI.Shapes.HmiRectangle", "Siemens.Engineering.HmiUnified.UI.Widgets.HmiRectangle" }
                    : key.Equals("IOField", StringComparison.OrdinalIgnoreCase) || key.Equals("HmiIOField", StringComparison.OrdinalIgnoreCase)
                        ? new[] { "Siemens.Engineering.HmiUnified.UI.Widgets.HmiIOField" }
                        : new[] { key };

            foreach (var name in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType(name, throwOnError: false, ignoreCase: false);
                        if (t != null) return t;
                    }
                    catch { }
                }
            }

            return null;
        }

        private static IEnumerable<Type> ResolveUnifiedHmiDynamizationTypes(string dynamizationType)
        {
            var filter = (dynamizationType ?? string.Empty).Trim();
            var preferredNames = string.IsNullOrWhiteSpace(filter)
                ? new[]
                {
                    "Siemens.Engineering.HmiUnified.UI.Dynamization.TagDynamization",
                    "Siemens.Engineering.HmiUnified.UI.Dynamization.DiscreteDynamization",
                    "Siemens.Engineering.HmiUnified.UI.Dynamization.RangeDynamization",
                    "Siemens.Engineering.HmiUnified.UI.Dynamization.ScriptDynamization"
                }
                : filter.Contains(".")
                    ? new[] { filter }
                    : new[]
                    {
                        $"Siemens.Engineering.HmiUnified.UI.Dynamization.{filter}",
                        $"Siemens.Engineering.HmiUnified.UI.Dynamization.{filter}Dynamization",
                        filter
                    };

            foreach (var name in preferredNames)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type? t = null;
                    try { t = asm.GetType(name, throwOnError: false, ignoreCase: false); } catch { }
                    if (t != null) yield return t;
                }
            }

            if (!string.IsNullOrWhiteSpace(filter) && !filter.Contains("."))
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t.FullName == null) continue;
                        if (!t.FullName.StartsWith("Siemens.Engineering.HmiUnified.UI.Dynamization.", StringComparison.Ordinal)) continue;
                        if (t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) yield return t;
                    }
                }
            }
        }

        private static object? CreateUnifiedScreenItem(object items, string itemName, Type? itemClrType, string itemTypeHint)
        {
            var methods = items.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "Create", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (itemClrType != null && m.IsGenericMethodDefinition && ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    var created = m.MakeGenericMethod(itemClrType).Invoke(items, new object[] { itemName });
                    if (created != null) return created;
                }

                if (!m.IsGenericMethodDefinition && ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string))
                {
                    foreach (var args in new[] { new object[] { itemName, itemTypeHint }, new object[] { itemTypeHint, itemName } })
                    {
                        try
                        {
                            var created = m.Invoke(items, args);
                            if (created != null) return created;
                        }
                        catch { }
                    }
                }
            }

            throw new InvalidOperationException($"Unable to create screen item '{itemName}' as '{itemTypeHint}'. ResolvedType={itemClrType?.FullName ?? "null"}.");
        }

        private static object? FindPressedStateTag(object pressedStateTags, string tagName)
        {
            try
            {
                if (pressedStateTags is IEnumerable en)
                {
                    foreach (var it in en)
                    {
                        foreach (var propName in new[] { "Tag", "TagName", "HmiTag", "HmiTagName", "Name", "TagPath" })
                        {
                            var value = TryGetPropertyValue(it, propName)?.ToString();
                            if (!string.IsNullOrWhiteSpace(value) &&
                                string.Equals(value!.Trim(), tagName, StringComparison.OrdinalIgnoreCase))
                            {
                                return it;
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static void ApplyJsonProperties(object target, JsonObject props, JsonArray failed, string path, string typeHint = "")
        {
            foreach (var kv in props)
            {
                var schemaError = ValidateUnifiedHmiDesignProperty(typeHint, kv.Key);
                if (!string.IsNullOrEmpty(schemaError))
                {
                    failed.Add($"{path}.{kv.Key}: {schemaError}");
                    continue;
                }

                var value = JsonObjectValue(kv.Value);
                if (TrySetProperty(target, kv.Key, value)) continue;
                if (TrySetEngineeringAttribute(target, kv.Key, value)) continue;
                failed.Add($"{path}.{kv.Key}: property/attribute write failed");
            }
        }

        private static string ValidateUnifiedHmiDesignProperty(string typeHint, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName)) return "property name is empty";
            var type = (typeHint ?? string.Empty).Trim();
            var prop = propertyName.Trim();

            if (type.Equals("Rectangle", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Lamp", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("HmiRectangle", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Equals("ForeColor", StringComparison.OrdinalIgnoreCase) ||
                    prop.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                    prop.Equals("Font", StringComparison.OrdinalIgnoreCase) ||
                    prop.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
                    prop.Equals("Padding", StringComparison.OrdinalIgnoreCase))
                {
                    return "unsupported on Rectangle. Use a separate HmiText item for text/foreground/font, and keep Rectangle for BackColor/BorderColor/BorderWidth.";
                }
            }

            if (type.Equals("IOField", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("HmiIOField", StringComparison.OrdinalIgnoreCase))
            {
                var stable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "BackColor", "ForeColor", "BorderColor", "BorderWidth", "Visible", "Enabled", "Name"
                };
                if (!stable.Contains(prop))
                    return "not in the stable IOField property set for generated screens. Bind runtime values with BindUnifiedHmiTagDynamization instead of ad-hoc ProcessValue properties.";
            }

            return string.Empty;
        }

        private static bool TrySetMultilingualText(object item, string propertyName, string text, string culture)
        {
            try
            {
                var multilingualText = TryGetPropertyValue(item, propertyName);
                if (multilingualText == null) return false;

                var html = text.TrimStart().StartsWith("<body", StringComparison.OrdinalIgnoreCase)
                    ? text
                    : $"<body><p>{SecurityElement.Escape(text) ?? string.Empty}</p></body>";

                var items = TryGetPropertyValue(multilingualText, "Items");
                if (items is IEnumerable en)
                {
                    object? first = null;
                    object? cultureMatch = null;
                    foreach (var it in en)
                    {
                        if (it == null) continue;
                        first ??= it;
                        var itemCulture = TryGetPropertyValue(it, "Culture")?.ToString();
                        if (!string.IsNullOrWhiteSpace(itemCulture) &&
                            itemCulture!.Equals(culture, StringComparison.OrdinalIgnoreCase))
                        {
                            cultureMatch = it;
                            break;
                        }
                    }

                    var target = cultureMatch ?? first;
                    if (target != null)
                    {
                        if (TrySetProperty(target, "Text", html)) return true;
                        if (TrySetEngineeringAttribute(target, "Text", html)) return true;
                    }
                }

                if (TrySetProperty(multilingualText, "Item", html)) return true;
                return TrySetEngineeringAttribute(multilingualText, "Text", html);
            }
            catch
            {
                return false;
            }
        }

        private static string? JsonString(JsonObject obj, string propertyName)
        {
            var node = obj[propertyName];
            if (node == null) return null;
            if (node is JsonValue v && v.TryGetValue<string>(out var s)) return s;
            return node.ToJsonString();
        }

        private static object? JsonObjectValue(JsonNode? node)
        {
            if (node == null) return null;
            if (node is JsonValue value)
            {
                if (value.TryGetValue<string>(out var s)) return s;
                if (value.TryGetValue<bool>(out var b)) return b;
                if (value.TryGetValue<int>(out var i)) return i;
                if (value.TryGetValue<long>(out var l)) return l;
                if (value.TryGetValue<double>(out var d)) return d;
                return value.ToJsonString();
            }

            return node.ToJsonString();
        }
    }
}
