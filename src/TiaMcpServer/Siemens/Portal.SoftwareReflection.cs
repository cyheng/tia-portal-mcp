using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace TiaMcpServer.Siemens
{
    // 工程对象的反射访问、集合遍历、值转换与诊断格式化。
    public partial class Portal
    {
        private static object? TryInvokeMethodByName(object target, string methodName, params object?[] args)
        {
            try
            {
                var method = target.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
                return method?.Invoke(target, args);
            }
            catch { return null; }
        }

        private static void SetEnumPropertyByName(object target, string propertyName, string valueName)
        {
            try
            {
                var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.PropertyType.IsEnum) return;
                var enumValue = Enum.Parse(prop.PropertyType, valueName, ignoreCase: true);
                prop.SetValue(target, enumValue);
            }
            catch { }
        }

        private static bool TrySetProperty(object target, string propName, object? value)
        {
            try
            {
                var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return false;

                object? v = CoerceReflectionValue(value, p.PropertyType);

                p.SetValue(target, v);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object? TryCreateNamedEngineeringObject(object collection, string name, out string? error)
        {
            error = null;
            var attempts = new List<string>();

            foreach (var method in collection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => string.Equals(m.Name, "Create", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(m => m.GetParameters().Length))
            {
                var ps = method.GetParameters();
                var sig = $"{method.Name}({string.Join(", ", ps.Select(p => p.ParameterType.FullName + " " + p.Name))})";

                object?[]? args = null;
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    args = new object?[] { name };
                }
                else if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string))
                {
                    args = new object?[] { name, name };
                }
                else if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsEnum)
                {
                    args = new object?[] { name, Enum.ToObject(ps[1].ParameterType, 0) };
                }
                else if (ps.Length == 2 && ps[0].ParameterType.IsEnum && ps[1].ParameterType == typeof(string))
                {
                    args = new object?[] { Enum.ToObject(ps[0].ParameterType, 0), name };
                }
                else
                {
                    attempts.Add($"SKIP {sig}");
                    continue;
                }

                try
                {
                    var created = method.Invoke(collection, args);
                    if (created != null) return created;
                    attempts.Add($"NULL {sig}");
                }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    var msg = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
                    attempts.Add($"ERR {sig}: {msg}");
                    if (msg.IndexOf("ValueIsNotUnique", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var existing = FindExistingByName(collection, name);
                        if (existing != null) return existing;
                    }
                }
                catch (Exception ex)
                {
                    attempts.Add($"ERR {sig}: {ex.Message}");
                }
            }

            error = $"No supported Create overload succeeded on {collection.GetType().FullName}. Attempts: {string.Join(" | ", attempts)}";
            return null;
        }

        private static object? FindExistingByName(object compositionOrEnumerable, string name)
        {
            try
            {
                if (compositionOrEnumerable is IEnumerable en)
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

        private static object? InvokeCreate(MethodInfo method, object target, object[] args)
        {
            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                var msg = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
                throw new InvalidOperationException(msg, tie.InnerException);
            }
        }

        private static bool TrySetAnyProperty(object target, string value, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                if (TrySetProperty(target, propName, value)) return true;
            }

            return false;
        }

        private static bool TrySetAnyPropertyOrAttribute(object target, object? value, params string[] propertyNames)
        {
            var any = false;
            foreach (var propName in propertyNames)
            {
                any = TrySetProperty(target, propName, value) || any;
                any = TrySetEngineeringAttribute(target, propName, value) || any;
            }

            return any;
        }

        private static bool TrySetAnyEnumCandidatePropertyOrAttribute(object target, IEnumerable<string> valueCandidates, params string[] propertyNames)
        {
            var any = false;
            foreach (var propName in propertyNames)
            {
                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite && prop.PropertyType.IsEnum)
                {
                    foreach (var candidate in valueCandidates)
                    {
                        try
                        {
                            var enumValue = Enum.Parse(prop.PropertyType, candidate, ignoreCase: true);
                            prop.SetValue(target, enumValue);
                            any = true;
                            break;
                        }
                        catch { }
                    }
                }

                var oldValue = TryGetEngineeringAttribute(target, propName);
                if (oldValue != null && oldValue.GetType().IsEnum)
                {
                    foreach (var candidate in valueCandidates)
                    {
                        try
                        {
                            var enumValue = Enum.Parse(oldValue.GetType(), candidate, ignoreCase: true);
                            if (TrySetEngineeringAttribute(target, propName, enumValue))
                            {
                                any = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }

            return any;
        }

        private static string DescribeWritableEnumProperties(object target, params string[] propertyNames)
        {
            var parts = new List<string>();
            foreach (var name in propertyNames)
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType.IsEnum)
                {
                    parts.Add($"{name}:{prop.PropertyType.FullName}=[{string.Join(",", Enum.GetNames(prop.PropertyType))}]");
                    continue;
                }

                var oldValue = TryGetEngineeringAttribute(target, name);
                if (oldValue != null && oldValue.GetType().IsEnum)
                {
                    parts.Add($"{name}:attr:{oldValue.GetType().FullName}=[{string.Join(",", Enum.GetNames(oldValue.GetType()))}]");
                }
            }

            return string.Join(" | ", parts);
        }

        private static object? TryGetEngineeringAttribute(object target, string attributeName)
        {
            try
            {
                var get = target.GetType().GetMethod("GetAttribute", new[] { typeof(string) });
                return get?.Invoke(target, new object[] { attributeName });
            }
            catch
            {
                return null;
            }
        }

        private static string SummarizeHmiObjectReadback(object target, params string[] names)
        {
            var parts = new List<string>();
            foreach (var name in names)
            {
                object? value = null;
                var got = false;
                try
                {
                    var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                    {
                        value = prop.GetValue(target);
                        got = true;
                    }
                }
                catch { }

                if (!got)
                {
                    value = TryGetEngineeringAttribute(target, name);
                    got = value != null;
                }

                if (got)
                    parts.Add($"{name}={value ?? ""}");
            }

            return string.Join("; ", parts);
        }

        private static bool TrySetEngineeringAttribute(object target, string attributeName, object? value)
        {
            try
            {
                var get = target.GetType().GetMethod("GetAttribute", new[] { typeof(string) });
                var set = target.GetType().GetMethod("SetAttribute", new[] { typeof(string), typeof(object) });
                if (set == null) return false;

                object? oldValue = null;
                try { oldValue = get?.Invoke(target, new object[] { attributeName }); } catch { }
                var typed = oldValue == null ? value : CoerceReflectionValue(value, oldValue.GetType());
                set.Invoke(target, new[] { attributeName, typed });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool? IsAttributeWritable(object attributeInfo)
        {
            var names = new[] { "AccessMode", "Access", "Mode" };
            foreach (var name in names)
            {
                var value = TryGetPropertyValue(attributeInfo, name)?.ToString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (value!.IndexOf("ReadWrite", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (value.IndexOf("Write", StringComparison.OrdinalIgnoreCase) >= 0 && value.IndexOf("ReadOnly", StringComparison.OrdinalIgnoreCase) < 0) return true;
                if (value.IndexOf("ReadOnly", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }

            return null;
        }

        private static object CoerceAttributeValue(string value, object? oldValue, object attributeInfo)
        {
            if (oldValue != null)
            {
                var oldType = oldValue.GetType();
                if (oldType == typeof(string)) return value;
                if (oldType == typeof(bool)) return bool.Parse(value);
                if (oldType == typeof(int)) return int.Parse(value);
                if (oldType == typeof(uint)) return uint.Parse(value);
                if (oldType == typeof(short)) return short.Parse(value);
                if (oldType == typeof(ushort)) return ushort.Parse(value);
                if (oldType == typeof(long)) return long.Parse(value);
                if (oldType == typeof(ulong)) return ulong.Parse(value);
                if (oldType == typeof(float)) return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                if (oldType == typeof(double)) return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                if (oldType.IsEnum) return Enum.Parse(oldType, value, ignoreCase: true);
            }

            var dataType = TryGetPropertyValue(attributeInfo, "DataType", "Type")?.ToString() ?? string.Empty;
            if (dataType.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0 || dataType.Equals("Bool", StringComparison.OrdinalIgnoreCase)) return bool.Parse(value);
            if (dataType.IndexOf("Int32", StringComparison.OrdinalIgnoreCase) >= 0 || dataType.Equals("Int", StringComparison.OrdinalIgnoreCase)) return int.Parse(value);
            if (dataType.IndexOf("UInt32", StringComparison.OrdinalIgnoreCase) >= 0 || dataType.Equals("UInt", StringComparison.OrdinalIgnoreCase)) return uint.Parse(value);
            if (dataType.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0 || dataType.Equals("Real", StringComparison.OrdinalIgnoreCase)) return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

            return value;
        }

        private static object? CoerceReflectionValue(object? value, Type targetType)
        {
            if (value == null) return null;

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null) targetType = nullableType;
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType == typeof(string)) return value.ToString();
            if (targetType.IsEnum) return value is string enumText
                ? Enum.Parse(targetType, enumText, ignoreCase: true)
                : Enum.ToObject(targetType, value);
            if (targetType == typeof(bool)) return value is string boolText ? bool.Parse(boolText) : Convert.ToBoolean(value);
            if (targetType == typeof(byte)) return value is string byteText ? byte.Parse(byteText) : Convert.ToByte(value);
            if (targetType == typeof(short)) return value is string shortText ? short.Parse(shortText) : Convert.ToInt16(value);
            if (targetType == typeof(ushort)) return value is string ushortText ? ushort.Parse(ushortText) : Convert.ToUInt16(value);
            if (targetType == typeof(int)) return value is string intText ? int.Parse(intText) : Convert.ToInt32(value);
            if (targetType == typeof(uint)) return value is string uintText ? uint.Parse(uintText) : Convert.ToUInt32(value);
            if (targetType == typeof(long)) return value is string longText ? long.Parse(longText) : Convert.ToInt64(value);
            if (targetType == typeof(ulong)) return value is string ulongText ? ulong.Parse(ulongText) : Convert.ToUInt64(value);
            if (targetType == typeof(float)) return value is string floatText ? float.Parse(floatText, System.Globalization.CultureInfo.InvariantCulture) : Convert.ToSingle(value);
            if (targetType == typeof(double)) return value is string doubleText ? double.Parse(doubleText, System.Globalization.CultureInfo.InvariantCulture) : Convert.ToDouble(value);
            if (targetType == typeof(Color)) return CoerceColor(value);

            return value;
        }

        private static Color CoerceColor(object value)
        {
            if (value is Color c) return c;

            if (value is string s)
            {
                var text = s.Trim();
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return Color.FromArgb(unchecked((int)Convert.ToUInt32(text.Substring(2), 16)));
                }
                if (text.StartsWith("#", StringComparison.Ordinal))
                {
                    return ColorTranslator.FromHtml(text);
                }
                if (Regex.IsMatch(text, "^[0-9A-Fa-f]{8}$"))
                {
                    return Color.FromArgb(unchecked((int)Convert.ToUInt32(text, 16)));
                }
                return ColorTranslator.FromHtml(text);
            }

            if (value is long l) return Color.FromArgb(unchecked((int)l));
            if (value is int i) return Color.FromArgb(i);
            if (value is uint ui) return Color.FromArgb(unchecked((int)ui));

            return Color.FromArgb(Convert.ToInt32(value));
        }

        private static IEnumerable<string> TryGetEnumerableStrings(object target, string propertyName)
        {
            try
            {
                var value = TryGetPropertyValue(target, propertyName);
                if (value is IEnumerable en)
                {
                    foreach (var item in en)
                    {
                        if (item != null) yield return item.ToString() ?? string.Empty;
                    }
                }
            }
            finally
            {
            }
        }

        private static JsonArray ToJsonArray(IEnumerable<string> values)
        {
            var arr = new JsonArray();
            foreach (var value in values)
            {
                arr.Add(value);
            }
            return arr;
        }

        private static string FormatExceptionDetail(Exception ex)
        {
            if (ex is TargetInvocationException tie && tie.InnerException != null)
            {
                return $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}\n{tie.InnerException}";
            }

            if (ex.InnerException != null)
            {
                return $"{ex.GetType().FullName}: {ex.Message}\nInner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex}";
            }

            return $"{ex.GetType().FullName}: {ex.Message}\n{ex}";
        }

        private static List<string> TryListNamesFromCollection(object root, string[] propertyHints, string finalCollectionNameHint)
        {
            var result = new List<string>();
            try
            {
                object? collection = null;
                var rootType = root.GetType();

                if (propertyHints.Length == 0 && root is System.Collections.IEnumerable)
                {
                    collection = root;
                }

                // try direct property matches
                foreach (var propName in propertyHints)
                {
                    var prop = rootType.GetProperty(propName);
                    if (prop == null) continue;

                    var v = prop.GetValue(root);
                    if (v == null) continue;

                    // tag tables can be under a folder object
                    if (propName.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
                    {
                        collection = v.GetType().GetProperty(finalCollectionNameHint)?.GetValue(v);
                    }
                    else
                    {
                        collection = v;
                    }

                    if (collection != null) break;
                }

                if (collection is System.Collections.IEnumerable enumerable)
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

        private static object? TryFindByNameInCollection(object root, string[] propertyHints, string wantedName)
        {
            try
            {
                var rootType = root.GetType();
                foreach (var propName in propertyHints)
                {
                    object? collection = null;

                    var prop = rootType.GetProperty(propName);
                    if (prop != null)
                    {
                        collection = prop.GetValue(root);
                    }
                    else if (propName.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
                    {
                        var folder = rootType.GetProperty(propName)?.GetValue(root);
                        if (folder != null)
                        {
                            collection = folder.GetType().GetProperty(propName.Replace("Folder", "s"))?.GetValue(folder);
                        }
                    }

                    if (collection is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item == null) continue;
                            var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
                            if (string.Equals(name, wantedName, StringComparison.OrdinalIgnoreCase))
                            {
                                return item;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static object? TryGetPropertyValue(object obj, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                try
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) continue;
                    var v = p.GetValue(obj);
                    if (v != null) return v;
                }
                catch { }
            }
            return null;
        }

        private static IEnumerable<string> DescribeTypeMembers(Type type, bool includeNonPublic)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;

            var result = new List<string>();
            foreach (var p in type.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                result.Add($"Property:{p.Name}:{p.PropertyType.FullName ?? p.PropertyType.Name}");
            }

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName) continue;
                var ps = string.Join(", ", m.GetParameters().Select(x => $"{x.ParameterType.Name} {x.Name}"));
                result.Add($"Method:{m.Name}({ps}) -> {m.ReturnType.FullName ?? m.ReturnType.Name}");
            }

            return result
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal);
        }

        private static IEnumerable<string> FormatEnumerableObjects(object? value, int limit)
        {
            var lines = new List<string>();
            if (value == null)
            {
                lines.Add("<null>");
                return lines;
            }

            if (value is not IEnumerable enumerable || value is string)
            {
                lines.Add(value.ToString() ?? "<null>");
                return lines;
            }

            var count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= Math.Max(1, limit)) break;
                if (item == null)
                {
                    lines.Add("<null>");
                    continue;
                }

                var type = item.GetType();
                var parts = new List<string> { type.FullName ?? type.Name };
                foreach (var propertyName in new[] { "Name", "CompositionName", "Type", "Description" })
                {
                    try
                    {
                        var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        var propValue = prop?.GetValue(item);
                        if (propValue != null)
                        {
                            parts.Add($"{propertyName}={propValue}");
                        }
                    }
                    catch { }
                }
                lines.Add(string.Join(" | ", parts));
            }

            if (lines.Count == 0) lines.Add("<empty>");
            return lines;
        }

        private static object? TryInvokeExplicitEngineeringMethod(object target, string methodShortName, object?[] args, out string? error)
        {
            error = null;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var methods = target.GetType().GetMethods(flags)
                    .Where(m =>
                        !m.IsSpecialName &&
                        (string.Equals(m.Name, methodShortName, StringComparison.OrdinalIgnoreCase) ||
                         m.Name.EndsWith("." + methodShortName, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(m => m.GetParameters().Length)
                    .ToList();

                if (methods.Count == 0)
                {
                    error = "Method not found: " + methodShortName;
                    return null;
                }

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != args.Length) continue;

                    try
                    {
                        var converted = new object?[parameters.Length];
                        for (var i = 0; i < parameters.Length; i++)
                        {
                            converted[i] = ConvertReflectionArgument(args[i], parameters[i].ParameterType);
                        }
                        return method.Invoke(target, converted);
                    }
                    catch (Exception ex)
                    {
                        error = FormatExceptionDetail(ex);
                    }
                }

                error ??= "No matching overload succeeded for " + methodShortName;
                return null;
            }
            catch (Exception ex)
            {
                error = FormatExceptionDetail(ex);
                return null;
            }
        }

        private static object? ConvertReflectionArgument(object? value, Type targetType)
        {
            if (value == null) return null;

            var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (nonNullable.IsInstanceOfType(value)) return value;

            if (typeof(System.Collections.IDictionary).IsAssignableFrom(nonNullable))
            {
                var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(object));
                var dict = Activator.CreateInstance(dictType);
                var addMethod = dictType.GetMethod("Add", new[] { typeof(string), typeof(object) });
                if (value is IEnumerable<KeyValuePair<string, object?>> kvps)
                {
                    foreach (var kv in kvps)
                    {
                        addMethod?.Invoke(dict, new object?[] { kv.Key, kv.Value });
                    }
                    return dict;
                }
            }

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(nonNullable) && nonNullable != typeof(string))
            {
                var enumerableInterface = nonNullable.IsInterface && nonNullable.IsGenericType
                    ? nonNullable
                    : nonNullable.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                if (enumerableInterface != null)
                {
                    var elementType = enumerableInterface.GetGenericArguments()[0];
                    if (elementType.IsGenericType &&
                        elementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) &&
                        elementType.GenericTypeArguments[0] == typeof(string))
                    {
                        var valueType = elementType.GenericTypeArguments[1];
                        var listType = typeof(List<>).MakeGenericType(elementType);
                        var list = (System.Collections.IList)Activator.CreateInstance(listType);
                        if (value is IEnumerable<KeyValuePair<string, object?>> kvps)
                        {
                            foreach (var kv in kvps)
                            {
                                var kvValue = kv.Value;
                                if (kvValue != null && valueType != typeof(object) && !valueType.IsInstanceOfType(kvValue))
                                {
                                    kvValue = Convert.ChangeType(kvValue, valueType);
                                }
                                var pair = Activator.CreateInstance(elementType, kv.Key, kvValue);
                                list.Add(pair);
                            }
                            return list;
                        }
                    }
                }
            }

            if (nonNullable.IsEnum)
            {
                return value is string s
                    ? Enum.Parse(nonNullable, s, ignoreCase: true)
                    : Enum.ToObject(nonNullable, value);
            }

            return Convert.ChangeType(value, nonNullable);
        }

        private static object? TryResolveChildGroupByPath(object rootGroup, string groupPath)
        {
            if (string.IsNullOrWhiteSpace(groupPath)) return rootGroup;

            var parts = groupPath.Trim().Trim('/').Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            object? current = rootGroup;
            foreach (var part in parts)
            {
                if (current == null) return null;

                // common group collections used by HMI objects
                var next = TryFindByNameInCollection(current, new[] { "Groups", "ScreenGroups", "TagTableGroups", "Folders" }, part);
                if (next == null)
                {
                    // Some shapes: current.ScreenGroups or current.Groups are nested under another property
                    var groupContainer = TryGetPropertyValue(current, "Groups", "ScreenGroups", "TagTableGroups");
                    if (groupContainer != null)
                    {
                        next = TryFindByNameInCollection(groupContainer, new[] { "Groups", "ScreenGroups", "TagTableGroups", "Folders" }, part);
                    }
                }

                current = next;
            }

            return current;
        }

        private static object? TryGetServiceByTypeSuffix(object target, string serviceTypeNameSuffix)
        {
            try
            {
                var getService = target.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                if (getService == null) return null;

                var serviceType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                    })
                    .FirstOrDefault(t => t.Name.Equals(serviceTypeNameSuffix, StringComparison.OrdinalIgnoreCase) ||
                                         t.FullName?.EndsWith("." + serviceTypeNameSuffix, StringComparison.OrdinalIgnoreCase) == true);
                if (serviceType == null) return null;

                return getService.MakeGenericMethod(serviceType).Invoke(target, Array.Empty<object>());
            }
            catch
            {
                return null;
            }
        }
    }
}
