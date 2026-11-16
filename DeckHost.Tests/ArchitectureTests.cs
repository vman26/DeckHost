using System.Reflection;
using System.Text;
using DeckHost.Contracts.Interfaces;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>
/// The dependency rules (docs/adr/0001) and the plugin-facing API, checked on the built assemblies.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Contracts = typeof(IDeckAction).Assembly;
    private static readonly Assembly Core = typeof(DeckHost.Core.ActionRouter).Assembly;
    private static readonly Assembly App = typeof(DeckHost.App.ViewModels.MainViewModel).Assembly;

    private static readonly Assembly[] DevicePlugins =
    [
        typeof(DeckHost.Device.MiraBox.MiraBoxDevicePlugin).Assembly,
        typeof(DeckHost.Device.Elgato.ElgatoDevicePlugin).Assembly,
        typeof(DeckHost.Device.Web.WebDeckPlugin).Assembly,
        typeof(DeckHost.Device.Hid.HidDevicePlugin).Assembly,
    ];

    private static string[] References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(static name => name.Name ?? string.Empty)];

    private static string[] DeckHostReferences(Assembly assembly) =>
        [.. References(assembly).Where(static name => name.StartsWith("DeckHost.", StringComparison.Ordinal)).Order()];

    [Test]
    public void Contracts_depends_on_nothing_of_DeckHost_and_on_no_UI_or_drawing_library()
    {
        Assert.That(DeckHostReferences(Contracts), Is.Empty);
        Assert.That(References(Contracts).Any(static name => name.StartsWith("Avalonia", StringComparison.Ordinal) || name.StartsWith("SkiaSharp", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void Core_depends_only_on_Contracts_and_never_on_a_device_plugin_or_the_UI()
    {
        Assert.That(DeckHostReferences(Core), Is.EqualTo(new[] { "DeckHost.Contracts" }));
        Assert.That(References(Core).Any(static name => name.StartsWith("Avalonia", StringComparison.Ordinal) || name.StartsWith("HidSharp", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void Device_plugins_depend_only_on_Contracts()
    {
        foreach (var plugin in DevicePlugins)
        {
            Assert.That(DeckHostReferences(plugin).SequenceEqual(["DeckHost.Contracts"]), Is.True, $"{plugin.GetName().Name} references {string.Join(", ", DeckHostReferences(plugin))}");
            Assert.That(References(plugin).Any(static name => name.StartsWith("Avalonia", StringComparison.Ordinal)), Is.False);
        }
    }

    [Test]
    public void Every_device_plugin_implements_IDevicePlugin()
    {
        foreach (var plugin in DevicePlugins)
        {
            Assert.That(plugin.GetExportedTypes().Any(static type => typeof(IDevicePlugin).IsAssignableFrom(type) && !type.IsAbstract), Is.True);
        }
    }

    [Test]
    public void The_app_is_the_only_assembly_that_knows_the_device_plugins()
    {
        var names = DevicePlugins.Select(static plugin => plugin.GetName().Name).ToHashSet();
        Assert.That(DeckHostReferences(App).Any(name => names.Contains(name)), Is.True);
        foreach (var assembly in DevicePlugins.Append(Core).Append(Contracts))
        {
            Assert.That(DeckHostReferences(assembly).Any(name => names.Contains(name)), Is.False);
        }
    }

    /// <summary>
    /// Installed plugins are compiled against Contracts. Removing or changing anything public there,
    /// or adding an interface member without a default body, breaks them: such a change must bump
    /// <see cref="PluginManifest.CurrentHostApiVersion"/>. This compares the public surface with
    /// <c>ContractsApi.txt</c>; additions only need the file updated (run the tests with
    /// <c>UPDATE_API_BASELINE=1</c>).
    /// </summary>
    [Test]
    public void The_plugin_API_only_grows_unless_the_host_API_version_is_bumped()
    {
        var current = DescribeApi(Contracts);
        var path = Path.Combine(SourceDirectory(), "ContractsApi.txt");
        if (Environment.GetEnvironmentVariable("UPDATE_API_BASELINE") == "1")
        {
            File.WriteAllText(path, current);
            return;
        }

        var baseline = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n');
        var now = current.Split('\n');
        var before = baseline.ToHashSet();
        var after = now.ToHashSet();
        var versionChanged = baseline[0] != now[0];

        // Breaking: anything gone or changed, and a member without a body on an interface plugins
        // already implement (a new interface may have them).
        var removed = baseline.Skip(1).Where(line => line.Length > 0 && !after.Contains(line));
        var newAbstract = now.Skip(1).Where(line => line.EndsWith(" abstract", StringComparison.Ordinal) && !before.Contains(line) &&
            before.Contains("interface " + line.Trim().Split(' ')[0][..line.Trim().Split(' ')[0].LastIndexOf('.')]));
        var breaking = removed.Concat(newAbstract).ToList();
        Assert.That(
            breaking.Count == 0 || versionChanged,
            Is.True,
            "Breaking change to the plugin API: bump PluginManifest.CurrentHostApiVersion, then run the tests with UPDATE_API_BASELINE=1.\n  " + string.Join("\n  ", breaking));

        var added = now.Skip(1).Where(line => line.Length > 0 && !before.Contains(line)).ToList();
        Assert.That(added.Count == 0 && !versionChanged, Is.True, "The plugin API changed: run the tests once with UPDATE_API_BASELINE=1 to update ContractsApi.txt.\n  " + string.Join("\n  ", added));
    }

    /// <summary>One line per public member; interface members without a body are marked "abstract".</summary>
    internal static string DescribeApi(Assembly assembly)
    {
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in assembly.GetExportedTypes())
        {
            var name = Name(type);
            lines.Add($"{Kind(type)} {name}");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var member in type.GetMembers(flags))
            {
                var text = member switch
                {
                    ConstructorInfo constructor => $"{name}({Parameters(constructor)})",
                    MethodInfo { IsSpecialName: true } => null,
                    MethodInfo method when type.IsEnum => null,
                    MethodInfo method => $"{name}.{method.Name}{Generic(method)}({Parameters(method)}) : {Name(method.ReturnType)}{(type.IsInterface && method.IsAbstract ? " abstract" : string.Empty)}",
                    PropertyInfo property => $"{name}.{property.Name} : {Name(property.PropertyType)}{(property.CanWrite && property.SetMethod?.IsPublic == true ? " {set}" : string.Empty)}{(type.IsInterface && property.GetMethod?.IsAbstract == true ? " abstract" : string.Empty)}",
                    FieldInfo field when type.IsEnum && field.IsLiteral => $"{name}.{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture)}",
                    FieldInfo field => $"{name}.{field.Name} : {Name(field.FieldType)}{(field.IsLiteral ? " const" : string.Empty)}",
                    EventInfo @event => $"{name}.{@event.Name} event {Name(@event.EventHandlerType!)}{(type.IsInterface && @event.AddMethod?.IsAbstract == true ? " abstract" : string.Empty)}",
                    _ => null,
                };
                if (text is not null)
                {
                    lines.Add("  " + text);
                }
            }
        }

        var output = new StringBuilder();
        output.Append("# host API ").Append(PluginManifest.CurrentHostApiVersion).Append('\n');
        foreach (var line in lines)
        {
            output.Append(line).Append('\n');
        }

        return output.ToString();
    }

    private static string Kind(Type type) =>
        type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class" : "class";

    private static string Generic(MethodInfo method) =>
        method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(static argument => argument.Name)) + ">" : string.Empty;

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(static parameter => $"{Name(parameter.ParameterType)} {parameter.Name}"));

    private static string Name(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[]";
        }

        if (type.IsByRef)
        {
            return Name(type.GetElementType()!) + "&";
        }

        var name = type.IsNested ? Name(type.DeclaringType!) + "." + type.Name : type.FullName ?? type.Name;
        if (type.IsGenericType)
        {
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            name = (tick >= 0 ? name[..tick] : name) + "<" + string.Join(",", type.GetGenericArguments().Select(Name)) + ">";
            if (type.IsNested)
            {
                name = name.Replace("`1", string.Empty, StringComparison.Ordinal);
            }
        }

        return name;
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
