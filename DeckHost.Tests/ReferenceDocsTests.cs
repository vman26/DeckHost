using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using DeckHost.App.SystemPlugins;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>
/// The reference pages are generated from the code, so they can't drift: <c>docs/reference/actions.md</c>
/// (every built-in action and its settings) and <c>docs/reference/api.md</c> (the plugin API with its
/// XML docs). A change that alters them fails here until the pages are regenerated:
/// <c>UPDATE_DOCS=1 dotnet test DeckHost.Tests --filter ReferenceDocsTests</c>.
/// </summary>
public class ReferenceDocsTests
{
    [Fact]
    public void The_built_in_actions_reference_is_up_to_date() => Check("actions.md", ActionsReference());

    [Fact]
    public void The_plugin_API_reference_is_up_to_date() => Check("api.md", ApiReference());

    private static void Check(string name, string generated)
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "reference", name);
        if (Environment.GetEnvironmentVariable("UPDATE_DOCS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated);
            return;
        }

        Assert.True(File.Exists(path), $"docs/reference/{name} is missing: run the tests once with UPDATE_DOCS=1.");
        Assert.True(File.ReadAllText(path).ReplaceLineEndings("\n") == generated,
            $"docs/reference/{name} is out of date: run the tests once with UPDATE_DOCS=1.");
    }

    // ---------------------------------------------------------------- actions

    internal static string ActionsReference()
    {
        var text = new StringBuilder();
        text.Append("# Built-in actions\n\n");
        text.Append("The actions that come with DeckHost, by plugin, with their settings. Plugins add more (**Plugins → Store**).\n\n");
        text.Append("*Generated from the code (`ReferenceDocsTests`); don't edit by hand.*\n");
        foreach (var plugin in SystemPluginSet.Create())
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## {plugin.Name}\n");
            foreach (var action in plugin.Actions.Where(static action => action.ShowInPalette).OrderBy(static action => action.Name, StringComparer.Ordinal))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n### {action.Name}\n\n");
                if (!string.IsNullOrWhiteSpace(action.Description))
                {
                    text.Append(Clean(action.Description)).Append("\n\n");
                }

                text.Append(CultureInfo.InvariantCulture, $"Goes on: {Fits(action)}.");
                if (action.RefreshInterval is { } refresh)
                {
                    text.Append(CultureInfo.InvariantCulture, $" Redraws every {Duration(refresh)}.");
                }

                if (!action.CanBeMultiActionStep)
                {
                    text.Append(" Not available as a Multi Action step.");
                }

                text.Append('\n');
                if (action.Settings.Count > 0)
                {
                    text.Append("\n| Setting | Kind | |\n|---|---|---|\n");
                    foreach (var setting in action.Settings)
                    {
                        text.Append(CultureInfo.InvariantCulture, $"| {Cell(setting.Label)} | {Kind(setting)} | {Cell(Describe(setting))} |\n");
                    }
                }
            }
        }

        return text.ToString();
    }

    private static string Fits(IDeckAction action)
    {
        var places = new List<string>();
        if (action.RequiredInputs == InputCapabilities.None || action.RequiredInputs.HasFlag(InputCapabilities.Press))
        {
            places.Add("keys");
        }

        if (action.RequiredInputs.HasFlag(InputCapabilities.Rotate) || action.RequiredInputs == InputCapabilities.None)
        {
            places.Add("knobs");
        }

        if (action.RequiredInputs.HasFlag(InputCapabilities.Swipe))
        {
            places.Add("touch strips (swipe)");
        }

        var where = places.Count == 0 ? "controls with " + action.RequiredInputs : string.Join(", ", places);
        return action.RequiredOutputs.HasFlag(OutputCapabilities.ColorImage) ? where + " with a screen" : where;
    }

    private static string Kind(ActionSettingDefinition setting) => setting.Kind switch
    {
        ActionSettingKind.Number when setting.Range is { } range => $"number {Number(range.Minimum)}–{Number(range.Maximum)}{(range.Unit is { } unit ? " " + unit : string.Empty)}",
        ActionSettingKind.Choice => "choice",
        ActionSettingKind.FilePath => "file",
        _ => setting.Kind.ToString().ToLowerInvariant(),
    };

    private static string Describe(ActionSettingDefinition setting)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(setting.Description))
        {
            parts.Add(Clean(setting.Description));
        }

        if (setting.Options is { Count: > 0 } options)
        {
            parts.Add("Options: " + string.Join(", ", options.Select(static option => option.Label)) + ".");
        }

        if (!string.IsNullOrWhiteSpace(setting.DefaultValue) && setting.Kind != ActionSettingKind.Secret)
        {
            var label = setting.Options?.FirstOrDefault(option => option.Value == setting.DefaultValue)?.Label ?? setting.DefaultValue;
            parts.Add($"Default: {label}.");
        }

        if (setting.PerInput)
        {
            parts.Add("Can differ per input.");
        }

        return string.Join(" ", parts);
    }

    // ---------------------------------------------------------------- API

    internal static string ApiReference()
    {
        var assembly = typeof(IDeckAction).Assembly;
        var docs = LoadDocs(assembly);
        var text = new StringBuilder();
        text.Append("# Plugin API reference\n\n");
        text.Append("Everything in `DeckHost.Contracts` (MIT), the library plugins are built against. Start with [Writing a plugin](../maintainers/plugins.md) or [Device plugins](../maintainers/device-plugins.md).\n\n");
        text.Append("*Generated from the code and its XML docs (`ReferenceDocsTests`); don't edit by hand.*\n");
        foreach (var group in assembly.GetExportedTypes().Where(static type => !type.IsNested).GroupBy(static type => type.Namespace ?? string.Empty).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## {group.Key}\n");
            foreach (var type in group.OrderBy(static type => type.Name, StringComparer.Ordinal))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n### {TypeName(type)}\n\n");
                text.Append(CultureInfo.InvariantCulture, $"*{TypeKind(type)}*");
                if (Summary(docs, "T:" + DocId(type)) is { } summary)
                {
                    text.Append(" — ").Append(summary);
                }

                text.Append('\n');
                var members = Members(type, docs).ToList();
                if (members.Count > 0)
                {
                    text.Append("\n| Member | |\n|---|---|\n");
                    foreach (var (signature, description) in members)
                    {
                        text.Append(CultureInfo.InvariantCulture, $"| `{signature}` | {Cell(description ?? string.Empty)} |\n");
                    }
                }
            }
        }

        return text.ToString();
    }

    private static IEnumerable<(string Signature, string? Description)> Members(Type type, Dictionary<string, string> docs)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                yield return (field.Name, Summary(docs, $"F:{DocId(type)}.{field.Name}"));
            }

            yield break;
        }

        var isRecord = type.GetMethod("<Clone>$") is not null;
        foreach (var constructor in type.GetConstructors().Where(static constructor => !constructor.GetParameters().Any(parameter => parameter.ParameterType == constructor.DeclaringType)))
        {
            yield return ($"{TypeName(type).Split('<')[0]}({Parameters(constructor)})", Summary(docs, $"M:{DocId(type)}.#ctor{DocParameters(constructor)}") ?? (isRecord ? "Creates it with the positional properties." : null));
        }

        foreach (var property in type.GetProperties(Flags).Where(static property => property.Name != "EqualityContract").OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            var access = property.SetMethod is { IsPublic: true } ? (property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(static modifier => modifier.Name == "IsExternalInit") ? " { get; init; }" : " { get; set; }") : string.Empty;
            yield return ($"{Short(property.PropertyType)} {property.Name}{access}", Summary(docs, $"P:{DocId(type)}.{property.Name}"));
        }

        foreach (var field in type.GetFields(Flags).OrderBy(static field => field.Name, StringComparer.Ordinal))
        {
            yield return ($"{(field.IsLiteral ? "const " : "static ")}{Short(field.FieldType)} {field.Name}", Summary(docs, $"F:{DocId(type)}.{field.Name}"));
        }

        foreach (var @event in type.GetEvents(Flags).OrderBy(static @event => @event.Name, StringComparer.Ordinal))
        {
            yield return ($"event {Short(@event.EventHandlerType!)} {@event.Name}", Summary(docs, $"E:{DocId(type)}.{@event.Name}"));
        }

        var hidden = new HashSet<string>(StringComparer.Ordinal) { "Equals", "GetHashCode", "ToString", "Deconstruct", "<Clone>$", "PrintMembers" };
        foreach (var method in type.GetMethods(Flags).Where(method => !method.IsSpecialName && !hidden.Contains(method.Name)).OrderBy(static method => method.Name, StringComparer.Ordinal).ThenBy(static method => method.GetParameters().Length))
        {
            var prefix = method.IsStatic ? "static " : string.Empty;
            yield return ($"{prefix}{Short(method.ReturnType)} {method.Name}({Parameters(method)})", Summary(docs, $"M:{DocId(type)}.{method.Name}{DocParameters(method)}"));
        }
    }

    private static Dictionary<string, string> LoadDocs(Assembly assembly)
    {
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        if (!File.Exists(path))
        {
            return [];
        }

        return XDocument.Load(path).Descendants("member")
            .Where(static member => member.Attribute("name") is not null && member.Element("summary") is not null)
            .GroupBy(static member => member.Attribute("name")!.Value)
            .ToDictionary(static group => group.Key, static group => Flatten(group.First().Element("summary")!), StringComparer.Ordinal);
    }

    /// <summary>A summary as one line of Markdown: references become code, tags become text.</summary>
    private static string Flatten(XElement element)
    {
        var text = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText plain:
                    text.Append(plain.Value);
                    break;
                case XElement { Name.LocalName: "see" or "seealso" } reference:
                    var target = reference.Attribute("cref")?.Value ?? reference.Attribute("langword")?.Value ?? reference.Attribute("href")?.Value ?? string.Empty;
                    text.Append('`').Append(target.Contains(':', StringComparison.Ordinal) ? target[(target.IndexOf(':', StringComparison.Ordinal) + 1)..].Split('(')[0].Split('.').Last() : target).Append('`');
                    break;
                case XElement { Name.LocalName: "paramref" or "typeparamref" } parameter:
                    text.Append('`').Append(parameter.Attribute("name")?.Value).Append('`');
                    break;
                case XElement { Name.LocalName: "c" } code:
                    text.Append('`').Append(code.Value).Append('`');
                    break;
                case XElement other:
                    text.Append(' ').Append(Flatten(other)).Append(' ');
                    break;
            }
        }

        return Clean(text.ToString());
    }

    private static string? Summary(Dictionary<string, string> docs, string id) => docs.TryGetValue(id, out var summary) && summary.Length > 0 ? summary : null;

    private static string DocId(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string DocParameters(MethodBase method)
    {
        var parameters = method.GetParameters();
        return parameters.Length == 0 ? string.Empty : "(" + string.Join(",", parameters.Select(static parameter => DocTypeName(parameter.ParameterType))) + ")";
    }

    private static string DocTypeName(Type type)
    {
        if (type.IsByRef)
        {
            return DocTypeName(type.GetElementType()!) + "@";
        }

        if (type.IsArray)
        {
            return DocTypeName(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericParameter)
        {
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition.ToString(CultureInfo.InvariantCulture);
        }

        if (!type.IsGenericType)
        {
            return DocId(type);
        }

        var name = DocId(type.GetGenericTypeDefinition());
        return name[..name.IndexOf('`', StringComparison.Ordinal)] + "{" + string.Join(",", type.GetGenericArguments().Select(DocTypeName)) + "}";
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(static parameter =>
            $"{Short(parameter.ParameterType)} {parameter.Name}{(parameter.HasDefaultValue ? " = " + Literal(parameter.DefaultValue) : string.Empty)}"));

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        bool flag => flag ? "true" : "false",
        uint number => "0x" + number.ToString("X8", CultureInfo.InvariantCulture),
        Enum item => item.GetType().Name + "." + item,
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    private static string TypeName(Type type) =>
        type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] + "<" + string.Join(", ", type.GetGenericArguments().Select(static argument => argument.Name)) + ">" : type.Name;

    private static string TypeKind(Type type) =>
        type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class" : type.GetMethod("<Clone>$") is not null ? "record" : "class";

    private static string Short(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return Short(inner) + "?";
        }

        if (type.IsArray)
        {
            return Short(type.GetElementType()!) + "[]";
        }

        if (type.IsByRef)
        {
            return Short(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            return type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] + "<" + string.Join(", ", type.GetGenericArguments().Select(Short)) + ">";
        }

        return type.FullName switch
        {
            "System.String" => "string",
            "System.Boolean" => "bool",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Byte" => "byte",
            "System.UInt32" => "uint",
            "System.Void" => "void",
            "System.Object" => "object",
            _ => type.Name,
        };
    }

    private static string Clean(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace(" .", ".", StringComparison.Ordinal).Replace(" ,", ",", StringComparison.Ordinal);

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan span) =>
        span.TotalSeconds >= 1 ? $"{span.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s" : $"{span.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms";

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(path))!;
}
