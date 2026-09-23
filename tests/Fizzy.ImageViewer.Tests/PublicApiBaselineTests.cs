using System.Globalization;
using System.IO;
using System.Reflection;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class PublicApiBaselineTests
{
    [Fact]
    public void ExportedApiMatchesReviewedSignatures()
    {
        var actual = Describe(typeof(Viewer).Assembly).Order(StringComparer.Ordinal).ToArray();
        var baseline = Path.Combine(AppContext.BaseDirectory, "PublicApi.txt");
        var output = Path.Combine(AppContext.BaseDirectory, "PublicApi.actual.txt");
        File.WriteAllLines(output, actual);
        Assert.True(File.Exists(baseline), $"Missing API baseline. Review {output} before updating PublicApi.txt.");
        Assert.True(File.ReadAllLines(baseline).SequenceEqual(actual),
            $"Public API changed. Review {output} against PublicApi.txt and update callers/docs before accepting the baseline.");
    }

    private static IEnumerable<string> Describe(Assembly assembly)
    {
        var nullability = new NullabilityInfoContext();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                   BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetExportedTypes())
        {
            string owner = TypeName(type);
            string kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
            string modifiers = type.IsAbstract && type.IsSealed ? "static " : type.IsInterface || type.IsValueType ? "" :
                type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : "";
            var parents = type.GetInterfaces().Where(t => t.IsPublic).Select(t => TypeName(t)).ToList();
            if (type.BaseType is { } parent && parent != typeof(object) && !type.IsValueType) parents.Add(TypeName(parent));
            yield return $"{modifiers}{kind} {owner}" + (parents.Count == 0 ? "" : " : " + string.Join(", ", parents.Order(StringComparer.Ordinal)));
            foreach (var constructor in type.GetConstructors(flags).Where(Visible))
                yield return $"{owner}: {Access(constructor)} ctor({Parameters(constructor.GetParameters(), nullability)})";
            foreach (var method in type.GetMethods(flags).Where(m => Visible(m) && (!m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal))))
                yield return $"{owner}: {Access(method)} {Modifiers(method)}{TypeName(method.ReturnType, nullability.Create(method.ReturnParameter))} {method.Name}({Parameters(method.GetParameters(), nullability)})";
            foreach (var property in type.GetProperties(flags))
            {
                var accessors = new[] { property.GetMethod, property.SetMethod }.Where(m => m != null && Visible(m)).Cast<MethodInfo>().ToArray();
                if (accessors.Length == 0) continue;
                string accessor = string.Join(" ", accessors.Select(m => Access(m) + (m == property.GetMethod ? " get;" :
                    m.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) ? " init;" : " set;")));
                var index = property.GetIndexParameters();
                yield return $"{owner}: {Modifiers(accessors[0])}{TypeName(property.PropertyType, nullability.Create(property))} {property.Name}" +
                    (index.Length == 0 ? "" : "[" + Parameters(index, nullability) + "]") + " { " + accessor + " }";
            }
            foreach (var field in type.GetFields(flags).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                yield return $"{owner}: {(field.IsPublic ? "public" : "protected")} {(field.IsLiteral ? "const " : field.IsStatic ? "static " : "")}{(field.IsInitOnly ? "readonly " : "")}{TypeName(field.FieldType, nullability.Create(field))} {field.Name}" +
                    (field.IsLiteral ? " = " + Literal(field.GetRawConstantValue()) : "");
            foreach (var item in type.GetEvents(flags).Where(e => e.AddMethod != null && Visible(e.AddMethod)))
                yield return $"{owner}: {Access(item.AddMethod!)} {Modifiers(item.AddMethod!)}event {TypeName(item.EventHandlerType!, nullability.Create(item))} {item.Name}";
        }
    }

    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    private static string Access(MethodBase method) => method.IsPublic ? "public" : "protected";
    private static string Modifiers(MethodInfo method) => method.IsStatic ? "static " : method.IsAbstract ? "abstract " :
        method.IsVirtual && !method.IsFinal ? "virtual " : "";
    private static string Parameters(ParameterInfo[] parameters, NullabilityInfoContext context) => string.Join(", ", parameters.Select(p =>
        (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + TypeName(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType, context.Create(p)) +
        " " + p.Name + (p.HasDefaultValue ? " = " + (p.DefaultValue == null && p.ParameterType.IsValueType ? "default" : Literal(p.DefaultValue)) : "")));
    private static string Literal(object? value) => value switch
    {
        null => "null", string text => System.Text.Json.JsonSerializer.Serialize(text),
        bool boolean => boolean ? "true" : "false", _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default"
    };
    private static string TypeName(Type type, NullabilityInfo? info = null)
    {
        string name;
        if (type.IsArray) name = TypeName(type.GetElementType()!, info?.ElementType) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        else if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            name = (definition.FullName ?? definition.Name).Split('`')[0] + "<" +
                string.Join(", ", arguments.Select((a, i) => TypeName(a, info?.GenericTypeArguments.ElementAtOrDefault(i)))) + ">";
        }
        else name = type.FullName ?? type.Name;
        return name + (!type.IsValueType && info?.ReadState == NullabilityState.Nullable ? "?" : "");
    }
}
