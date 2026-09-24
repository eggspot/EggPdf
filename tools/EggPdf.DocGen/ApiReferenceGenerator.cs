using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace EggPdf.DocGen;

/// <summary>
/// Builds the HTML API reference for a namespace from reflection (what exists, in declaration
/// order) and the compiler's XML documentation file (what each thing is for). A public type or
/// member without a &lt;summary&gt; makes generation fail, so the reference is complete by
/// construction and the code comments are the single source of truth.
/// </summary>
public static class ApiReferenceGenerator
{
    public const string BeginMarker = "<!-- BEGIN GENERATED API REFERENCE: dotnet run --project tools/EggPdf.DocGen (edit the XML doc comments, not this block) -->";
    public const string EndMarker = "<!-- END GENERATED API REFERENCE -->";

    private static readonly string[] ObjectMembers = { "Equals", "GetHashCode", "ToString", "GetType" };

    /// <summary>Preferred reading order; types not listed follow alphabetically, enums last.</summary>
    private static readonly string[] TypeOrder =
    {
        "Document", "DocumentBuilder", "DocumentDescriptor", "PageDescriptor", "ColumnDescriptor", "Container",
        "RowDescriptor", "GridDescriptor", "ListDescriptor", "TableDescriptor", "TableRowDescriptor",
        "Color", "Colors", "Length", "CssTransform", "GridTrack", "PageSize",
    };

    /// <summary>Types whose members are listed by name only (enum values, named colors) -- the type's own summary explains them.</summary>
    private static bool IsCompact(Type t) => t.IsEnum || t.Name == "Colors";

    public static string Generate(Assembly assembly, string @namespace)
    {
        var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        if (!File.Exists(xmlPath))
            throw new FileNotFoundException("XML documentation file not found (GenerateDocumentationFile must be on): " + xmlPath);
        var docs = LoadDocs(xmlPath);

        var types = assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace == @namespace && !t.IsNested)
            .OrderBy(t => t.IsEnum ? 2 : Array.IndexOf(TypeOrder, t.Name) >= 0 ? 0 : 1)
            .ThenBy(t => Array.IndexOf(TypeOrder, t.Name) >= 0 ? Array.IndexOf(TypeOrder, t.Name) : 0)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        var undocumented = new List<string>();
        var sb = new StringBuilder();
        sb.AppendLine(BeginMarker);

        var nonEnumTypes = types.Where(t => !t.IsEnum).ToList();
        sb.Append("      <p>Types: ");
        sb.Append(string.Join(", ", nonEnumTypes.Select(t => $"<a href=\"#ref-{t.Name}\">{t.Name}</a>")));
        sb.AppendLine(", and the <a href=\"#ref-enums\">keyword enums</a>.</p>");

        foreach (var type in nonEnumTypes)
            AppendType(sb, type, docs, undocumented);

        AppendEnums(sb, types.Where(t => t.IsEnum).ToList(), docs, undocumented);

        sb.Append("      ").AppendLine(EndMarker);

        if (undocumented.Count > 0)
            throw new InvalidOperationException(
                "Public API without an XML <summary> (add one -- it becomes the published reference):" + Environment.NewLine +
                string.Join(Environment.NewLine, undocumented.Select(u => "  " + u)));

        return sb.ToString().Replace("\r\n", "\n");
    }

    // ── sections ─────────────────────────────────────────────────────────────

    private static void AppendType(StringBuilder sb, Type type, Dictionary<string, XElement> docs, List<string> undocumented)
    {
        var summary = SummaryHtml(docs, "T:" + FullName(type));
        if (summary == null) undocumented.Add(type.Name + " (type)");

        sb.AppendLine();
        sb.AppendLine($"      <h3 id=\"ref-{type.Name}\">{type.Name}{Kind(type)}</h3>");
        sb.AppendLine($"      <p>{summary}</p>");

        if (type.Name == "Colors")
        {
            var names = Members(type).OfType<FieldInfo>().Select(f => $"<code>{f.Name}</code>");
            sb.AppendLine($"      <p>Named colors: {string.Join(", ", names)}.</p>");
            return;
        }

        sb.AppendLine("      <table><thead><tr><th>Member</th><th>Purpose</th></tr></thead><tbody>");
        foreach (var member in Members(type))
        {
            var text = SummaryHtml(docs, DocId(member));
            if (text == null) undocumented.Add(type.Name + "." + member.Name);
            sb.AppendLine($"        <tr><td><code>{WebUtility.HtmlEncode(Signature(member))}</code></td><td>{text}</td></tr>");
        }
        sb.AppendLine("      </tbody></table>");
    }

    private static void AppendEnums(StringBuilder sb, List<Type> enums, Dictionary<string, XElement> docs, List<string> undocumented)
    {
        sb.AppendLine();
        sb.AppendLine("      <h3 id=\"ref-enums\">Keyword enums</h3>");
        sb.AppendLine("      <table><thead><tr><th>Enum</th><th>Purpose</th><th>Values</th></tr></thead><tbody>");
        foreach (var e in enums)
        {
            var summary = SummaryHtml(docs, "T:" + FullName(e));
            if (summary == null) undocumented.Add(e.Name + " (type)");
            var values = string.Join(", ", Enum.GetNames(e).Select(n => $"<code>{n}</code>"));
            sb.AppendLine($"        <tr><td id=\"ref-{e.Name}\"><code>{e.Name}</code></td><td>{summary}</td><td>{values}</td></tr>");
        }
        sb.AppendLine("      </tbody></table>");
    }

    private static string Kind(Type t)
        => t.IsEnum ? " (enum)"
         : t.IsAbstract && t.IsSealed ? " (static)"
         : t.IsValueType ? " (struct)"
         : "";

    /// <summary>Public members declared on the type, in source order: constructors, methods, properties, fields, implicit conversions.</summary>
    private static IEnumerable<MemberInfo> Members(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = new List<MemberInfo>();
        members.AddRange(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Where(_ => type.IsValueType || !type.IsAbstract));
        members.AddRange(type.GetMethods(flags).Where(m =>
            (!m.IsSpecialName || m.Name == "op_Implicit" || m.Name == "op_Explicit") && !ObjectMembers.Contains(m.Name)));
        members.AddRange(type.GetProperties(flags));
        members.AddRange(type.GetFields(flags).Where(f => !f.IsSpecialName));
        return members.OrderBy(m => m.MetadataToken);
    }

    // ── signatures ───────────────────────────────────────────────────────────

    private static string Signature(MemberInfo member)
    {
        switch (member)
        {
            case ConstructorInfo c:
                return c.DeclaringType!.Name + "(" + Params(c.GetParameters()) + ")";
            case MethodInfo m when m.Name == "op_Implicit" || m.Name == "op_Explicit":
                return (m.Name == "op_Implicit" ? "implicit" : "explicit") + " operator " + TypeName(m.ReturnType) + "(" + Params(m.GetParameters()) + ")";
            case MethodInfo m:
                return (m.IsStatic ? "static " : "") + TypeName(m.ReturnType) + " " + m.Name + "(" + Params(m.GetParameters()) + ")";
            case PropertyInfo p:
                var isStatic = (p.GetMethod ?? p.SetMethod)!.IsStatic;
                return (isStatic ? "static " : "") + TypeName(p.PropertyType) + " " + p.Name + (p.CanWrite ? " { get; set; }" : " { get; }");
            case FieldInfo f:
                return "static " + (f.IsInitOnly ? "readonly " : "") + TypeName(f.FieldType) + " " + f.Name;
            default:
                return member.Name;
        }
    }

    private static string Params(ParameterInfo[] ps)
        => string.Join(", ", ps.Select(p => TypeName(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + Default(p) : "")));

    private static string Default(ParameterInfo p)
    {
        var v = p.RawDefaultValue;
        if (v == null || v == DBNull.Value)
            return p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) == null ? "default" : "null";

        // Reflection reports an enum default as its underlying number; show the member name.
        var enumType = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        if (enumType.IsEnum) v = Enum.ToObject(enumType, v);

        return v switch
        {
            bool b => b ? "true" : "false",
            string s => "\"" + s + "\"",
            float f => f.ToString(CultureInfo.InvariantCulture) + "f",
            double d => d.ToString(CultureInfo.InvariantCulture),
            Enum e => e.GetType().Name + "." + e,
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "default",
        };
    }

    private static string TypeName(Type t)
    {
        if (t == typeof(void)) return "void";
        if (t == typeof(string)) return "string";
        if (t == typeof(float)) return "float";
        if (t == typeof(double)) return "double";
        if (t == typeof(int)) return "int";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(byte)) return "byte";
        if (t.IsArray) return TypeName(t.GetElementType()!) + "[]";
        var nullable = Nullable.GetUnderlyingType(t);
        if (nullable != null) return TypeName(nullable) + "?";
        if (!t.IsGenericType) return t.Name;
        var name = t.Name.Substring(0, t.Name.IndexOf('`'));
        return name + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
    }

    // ── XML documentation ────────────────────────────────────────────────────

    private static Dictionary<string, XElement> LoadDocs(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var m in doc.Descendants("member"))
        {
            var name = (string?)m.Attribute("name");
            if (name != null) map[name] = m;
        }
        return map;
    }

    /// <summary>The summary rendered as HTML, or null when missing/empty.</summary>
    private static string? SummaryHtml(Dictionary<string, XElement> docs, string id)
    {
        if (!docs.TryGetValue(id, out var member)) return null;
        var summary = member.Element("summary");
        if (summary == null) return null;
        var html = Collapse(RenderNodes(summary.Nodes()));
        return html.Length == 0 ? null : html;
    }

    private static string Collapse(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    private static string RenderNodes(IEnumerable<XNode> nodes)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText t:
                    sb.Append(WebUtility.HtmlEncode(t.Value));
                    break;
                case XElement e when e.Name == "c":
                    sb.Append("<code>").Append(WebUtility.HtmlEncode(e.Value)).Append("</code>");
                    break;
                case XElement e when e.Name == "code":
                    sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(Dedent(e.Value))).Append("</code></pre>");
                    break;
                case XElement e when e.Name == "see":
                    sb.Append("<code>").Append(WebUtility.HtmlEncode(CrefName((string?)e.Attribute("cref") ?? (string?)e.Attribute("langword") ?? ""))).Append("</code>");
                    break;
                case XElement e when e.Name == "paramref":
                    sb.Append("<code>").Append(WebUtility.HtmlEncode((string?)e.Attribute("name") ?? "")).Append("</code>");
                    break;
                case XElement e:
                    sb.Append(RenderNodes(e.Nodes()));
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>"M:EggPdf.Fluent.Container.Column(System.Action{...})" -> "Column"; "T:EggPdf.Fluent.Length" -> "Length".</summary>
    private static string CrefName(string cref)
    {
        var s = cref.Length > 1 && cref[1] == ':' ? cref.Substring(2) : cref;
        int paren = s.IndexOf('(');
        if (paren >= 0) s = s.Substring(0, paren);
        int dot = s.LastIndexOf('.');
        return dot >= 0 ? s.Substring(dot + 1) : s;
    }

    private static string Dedent(string code)
    {
        var lines = code.Replace("\r", "").Split('\n').ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart()));
    }

    // ── XML doc IDs (the keys the compiler writes) ───────────────────────────

    private static string FullName(Type t) => (t.FullName ?? t.Name).Replace('+', '.');

    private static string DocId(MemberInfo member)
    {
        var owner = FullName(member.DeclaringType!);
        switch (member)
        {
            case ConstructorInfo c:
                return "M:" + owner + ".#ctor" + ParamIds(c.GetParameters());
            case MethodInfo m:
                var id = "M:" + owner + "." + m.Name + ParamIds(m.GetParameters());
                return m.Name is "op_Implicit" or "op_Explicit" ? id + "~" + TypeId(m.ReturnType) : id;
            case PropertyInfo p:
                return "P:" + owner + "." + p.Name;
            case FieldInfo f:
                return "F:" + owner + "." + f.Name;
            default:
                return owner + "." + member.Name;
        }
    }

    private static string ParamIds(ParameterInfo[] ps)
        => ps.Length == 0 ? "" : "(" + string.Join(",", ps.Select(p => TypeId(p.ParameterType))) + ")";

    private static string TypeId(Type t)
    {
        if (t.IsArray) return TypeId(t.GetElementType()!) + "[]";
        if (!t.IsGenericType) return FullName(t);
        var name = FullName(t.GetGenericTypeDefinition());
        name = name.Substring(0, name.IndexOf('`'));
        return name + "{" + string.Join(",", t.GetGenericArguments().Select(TypeId)) + "}";
    }
}
