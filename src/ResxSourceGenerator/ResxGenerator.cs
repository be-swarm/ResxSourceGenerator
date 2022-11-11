using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BeSwarm.ResxSourceGeneraor;

[Generator]
public sealed class ResxGenerator : IIncrementalGenerator
{
	private static readonly DiagnosticDescriptor InvalidResx = new(
		id: "MFRG0001",
		title: "Couldn't parse Resx file",
		messageFormat: "Couldn't parse Resx file '{0}'",
		category: "ResxGenerator",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor InvalidPropertiesForNamespace = new(
		id: "MFRG0002",
		title: "Couldn't compute namespace",
		messageFormat: "Couldn't compute namespace for file '{0}'",
		category: "ResxGenerator",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor InvalidPropertiesForResourceName = new(
		id: "MFRG0003",
		title: "Couldn't compute resource name",
		messageFormat: "Couldn't compute resource name for file '{0}'",
		category: "ResxGenerator",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor InconsistentProperties = new(
		id: "MFRG0004",
		title: "Inconsistent properties",
		messageFormat: "Property '{0}' values for '{1}' are inconsistent",
		category: "ResxGenerator",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{

//#if DEBUG
//		if (!Debugger.IsAttached)
//		{
//			Debugger.Launch();
//		}
//#endif
		context.RegisterSourceOutput(
			source: context.AnalyzerConfigOptionsProvider.Combine(context.CompilationProvider.Combine(context.AdditionalTextsProvider.Where(text => text.Path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)).Collect())),
			action: (ctx, source) => Execute(ctx, source.Left, source.Right.Left, source.Right.Right));
	}

	private static void Execute(SourceProductionContext context, AnalyzerConfigOptionsProvider options, Compilation compilation, System.Collections.Immutable.ImmutableArray<AdditionalText> files)
	{
		var hasNotNullIfNotNullAttribute = compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute") != null;

		// Group additional file by resource kind ((a.resx, a.en.resx, a.en-us.resx), (b.resx, b.en-us.resx))
		var resxGroups = files
			.GroupBy(file => GetResourceName(file.Path), StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var resxGroug in resxGroups)
		{
			var rootNamespaceConfiguration = GetMetadataValue(context, options, "RootNamespace", resxGroug);
			var projectDirConfiguration = GetMetadataValue(context, options, "ProjectDir", resxGroug);
			var namespaceConfiguration = GetMetadataValue(context, options, "Namespace", "DefaultResourcesNamespace", resxGroug);
			var resourceNameConfiguration = GetMetadataValue(context, options, "ResourceName", globalName: null, resxGroug);
			var classNameConfiguration = GetMetadataValue(context, options, "ClassName", globalName: null, resxGroug);
			var assemblyName = compilation.AssemblyName;

			var rootNamespace = rootNamespaceConfiguration ?? assemblyName ?? "";
			var projectDir = projectDirConfiguration ?? assemblyName ?? "";
			var defaultResourceName = ComputeResourceName(rootNamespace, projectDir, resxGroug.Key);
			var defaultNamespace = ComputeNamespace(rootNamespace, projectDir, resxGroug.Key);

			var ns = namespaceConfiguration ?? defaultNamespace;
			var resourceName = resourceNameConfiguration ?? defaultResourceName;
			var className = classNameConfiguration ?? ToCSharpNameIdentifier(Path.GetFileName(resxGroug.Key)) + "Res";

			if (ns == null)
			{
				context.ReportDiagnostic(Diagnostic.Create(InvalidPropertiesForNamespace, location: null, resxGroug.First().Path));
			}

			if (resourceName == null)
			{
				context.ReportDiagnostic(Diagnostic.Create(InvalidPropertiesForResourceName, location: null, resxGroug.First().Path));
			}

			var entries = LoadResourceFiles(context, resxGroug);

			var content = $@"
// Debug info:
// key: {resxGroug.Key}
// files: {string.Join(", ", resxGroug.Select(f => f.Path))}
// RootNamespace (metadata): {rootNamespaceConfiguration}
// ProjectDir (metadata): {projectDirConfiguration}
// Namespace / DefaultResourcesNamespace (metadata): {namespaceConfiguration}
// ResourceName (metadata): {resourceNameConfiguration}
// ClassName (metadata): {classNameConfiguration}
// AssemblyName: {assemblyName}
// RootNamespace (computed): {rootNamespace}
// ProjectDir (computed): {projectDir}
// defaultNamespace: {defaultNamespace}
// defaultResourceName: {defaultResourceName}
// Namespace: {ns}
// ResourceName: {resourceName}
// ClassName: {className}
";

			if (resourceName != null && entries != null)
			{
				content += GenerateCode(ns, className, resourceName, entries, hasNotNullIfNotNullAttribute);
			}

			context.AddSource($"{Path.GetFileName(resxGroug.Key)}.resx.g.cs", SourceText.From(content, Encoding.UTF8));
		}
	}

	private static string GenerateCode(string? ns, string className, string resourceName, List<ResxEntry> entries, bool enableNullableAttributes)
	{
		var sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("#nullable enable");

		if (ns != null)
		{
			sb.AppendLine("namespace " + ns + ";");

		}

		sb.AppendLine("    public partial class " + className);
		sb.AppendLine("    {");
		sb.AppendLine("        private  global::System.Resources.ResourceManager? resourceMan;");
		sb.AppendLine();
		sb.AppendLine("        public " + className + "() { }");
		sb.AppendLine(@"
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
     
        private global::System.Resources.ResourceManager ResourceManager=>  resourceMan ?? (resourceMan=new global::System.Resources.ResourceManager(""" + resourceName + @""", typeof(" + className + @").Assembly));
          
       

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        public global::System.Globalization.CultureInfo? Culture { get; set; }

        " + AppendNotNullIfNotNull("defaultValue") + @"
      
        public string? GetString(string name, string? defaultValue, params object?[]? args)
        {
            string? str = ResourceManager.GetString(name, Culture);
            if (str == null)
            {
                if (defaultValue == null || args == null)
                {
                    return defaultValue;
                }
                else
                {
                    return string.Format(Culture, defaultValue, args);
                }
            }

            if (args != null)
            {
                return string.Format(Culture, str, args);
            }
            else
            {
                return str;
            }
        }");


		foreach (var entry in entries.OrderBy(e => e.Name))
		{
			if (string.IsNullOrEmpty(entry.Name))
				continue;

			if (entry.IsText)
			{
				var summary = new XElement("summary",
					new XElement("para", $"Looks up a localized string for \"{entry.Name}\"."));
				if (!string.IsNullOrWhiteSpace(entry.Comment))
				{
					summary.Add(new XElement("para", entry.Comment));
				}

				if (!entry.IsFileRef)
				{
					summary.Add(new XElement("para", $"Value: \"{entry.Value}\"."));
				}

				var comment = summary.ToString().Replace(Environment.NewLine, Environment.NewLine + "       /// ",
					StringComparison.Ordinal);

				sb.AppendLine(@"
        /// " + comment);
				sb.AppendLine();
				// Is a resource string with parameter ?
				bool parameter = false;
				if (entry.Value != null)
				{
					var args = Regex.Matches(entry.Value, "\\{(?<num>[0-9]+)(\\:[^}]*)?\\}", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
						.Cast<Match>()
						.Select(m => int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture))
						.Distinct()
						.DefaultIfEmpty(-1)
						.Max();

					if (args >= 0)
					{
						var inParams = string.Join(", ", Enumerable.Range(0, args + 1).Select(arg => "object? arg" + arg.ToString(CultureInfo.InvariantCulture)));
						var callParams = string.Join(", ", Enumerable.Range(0, args + 1).Select(arg => "arg" + arg.ToString(CultureInfo.InvariantCulture)));

						sb.Append(@" public  string? " + ToCSharpNameIdentifier(entry.Name) + $"({inParams})=>");
						sb.AppendLine($"GetString(\"{entry.Name}\",\"<?{entry.Name}?>\",{callParams});");
						parameter = true;
					}
				}
				if(!parameter)
				
				{
					sb.Append(@" public  string? " + ToCSharpNameIdentifier(entry.Name) + "()=>");
					sb.AppendLine($"GetString(\"{entry.Name}\",\"<?{entry.Name}?>\",null);");
				}

			}

			sb.AppendLine("");
		}
		sb.AppendLine("}");
		return sb.ToString();

		string? AppendNotNullIfNotNull(string paramName)
		{
			if (!enableNullableAttributes)
				return null;

			return "[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"" + paramName + "\")]\n";
		}
	}

	private static string? ComputeResourceName(string rootNamespace, string projectDir, string resourcePath)
	{
		var fullProjectDir = EnsureEndSeparator(Path.GetFullPath(projectDir));
		var fullResourcePath = Path.GetFullPath(resourcePath);

		if (fullProjectDir == fullResourcePath)
			return rootNamespace;

		if (fullResourcePath.StartsWith(fullProjectDir, StringComparison.Ordinal))
		{
			var relativePath = fullResourcePath[fullProjectDir.Length..];
			return rootNamespace + '.' + relativePath.Replace('/', '.').Replace('\\', '.');
		}

		return null;
	}

	private static string? ComputeNamespace(string rootNamespace, string projectDir, string resourcePath)
	{
		var fullProjectDir = EnsureEndSeparator(Path.GetFullPath(projectDir));
		var fullResourcePath = EnsureEndSeparator(Path.GetDirectoryName(Path.GetFullPath(resourcePath))!);

		if (fullProjectDir == fullResourcePath)
			return rootNamespace;

		if (fullResourcePath.StartsWith(fullProjectDir, StringComparison.Ordinal))
		{
			var relativePath = fullResourcePath[fullProjectDir.Length..];
			return rootNamespace + '.' + relativePath.Replace('/', '.').Replace('\\', '.').TrimEnd('.');
		}

		return null;
	}

	private static List<ResxEntry>? LoadResourceFiles(SourceProductionContext context, IGrouping<string, AdditionalText> resxGroug)
	{
		var entries = new List<ResxEntry>();
		foreach (var entry in resxGroug.OrderBy(file => file.Path, StringComparer.Ordinal))
		{
			var content = entry.GetText(context.CancellationToken);
			if (content == null)
				continue;

			try
			{
				var document = XDocument.Parse(content.ToString());
				foreach (var element in document.XPathSelectElements("/root/data"))
				{
					var name = element.Attribute("name")?.Value;
					var type = element.Attribute("type")?.Value;
					var comment = element.Attribute("comment")?.Value;
					var value = element.Element("value")?.Value;

					var existingEntry = entries.Find(e => e.Name == name);
					if (existingEntry != null)
					{
						existingEntry.Comment ??= comment;
					}
					else
					{
						entries.Add(new ResxEntry { Name = name, Value = value, Comment = comment, Type = type });
					}
				}
			}
			catch
			{
				context.ReportDiagnostic(Diagnostic.Create(InvalidResx, location: null, entry.Path));
				return null;
			}
		}

		return entries;
	}

	private static string? GetMetadataValue(SourceProductionContext context, AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider, string name, IEnumerable<AdditionalText> additionalFiles)
	{
		return GetMetadataValue(context, analyzerConfigOptionsProvider, name, name, additionalFiles);
	}

	private static string? GetMetadataValue(SourceProductionContext context, AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider, string name, string? globalName, IEnumerable<AdditionalText> additionalFiles)
	{
		string? result = null;
		foreach (var file in additionalFiles)
		{
			if (analyzerConfigOptionsProvider.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles." + name, out var value))
			{
				if (result != null && value != result)
				{
					context.ReportDiagnostic(Diagnostic.Create(InconsistentProperties, location: null, name, file.Path));
					return null;
				}

				result = value;
			}
		}

		if (!string.IsNullOrEmpty(result))
			return result;

		if (globalName != null && analyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property." + globalName, out var globalValue) && !string.IsNullOrEmpty(globalValue))
			return globalValue;

		return null;
	}

	private static string ToCSharpNameIdentifier(string name)
	{
		// https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/lexical-structure#identifiers
		// https://docs.microsoft.com/en-us/dotnet/api/system.globalization.unicodecategory?view=net-5.0
		var sb = new StringBuilder();
		foreach (var c in name)
		{
			var category = char.GetUnicodeCategory(c);
			switch (category)
			{
				case UnicodeCategory.UppercaseLetter:
				case UnicodeCategory.LowercaseLetter:
				case UnicodeCategory.TitlecaseLetter:
				case UnicodeCategory.ModifierLetter:
				case UnicodeCategory.OtherLetter:
				case UnicodeCategory.LetterNumber:
					sb.Append(c);
					break;

				case UnicodeCategory.DecimalDigitNumber:
				case UnicodeCategory.ConnectorPunctuation:
				case UnicodeCategory.Format:
					if (sb.Length == 0)
					{
						sb.Append('_');
					}
					sb.Append(c);
					break;

				default:
					sb.Append('_');
					break;
			}
		}

		return sb.ToString();
	}

	private static string EnsureEndSeparator(string path)
	{
		if (path[^1] == Path.DirectorySeparatorChar)
			return path;

		return path + Path.DirectorySeparatorChar;
	}

	private static string GetResourceName(string path)
	{
		var pathWithoutExtension = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
		var indexOf = pathWithoutExtension.LastIndexOf('.');
		if (indexOf < 0)
			return pathWithoutExtension;

		return Regex.IsMatch(pathWithoutExtension[(indexOf + 1)..], "^[a-zA-Z]{2}(-[a-zA-Z]{2})?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
			? pathWithoutExtension[0..indexOf]
			: pathWithoutExtension;
	}

	private sealed class ResxEntry
	{
		public string? Name { get; set; }
		public string? Value { get; set; }
		public string? Comment { get; set; }
		public string? Type { get; set; }

		public bool IsText
		{
			get
			{
				if (Type == null)
					return true;

				if (Value != null)
				{
					var parts = Value.Split(';');
					if (parts.Length > 1)
					{
						var type = parts[1];
						if (type.StartsWith("System.String,", StringComparison.Ordinal))
							return true;
					}
				}

				return false;
			}
		}

		public string? FullTypeName
		{
			get
			{
				if (IsText)
					return "string";

				if (Value != null)
				{
					var parts = Value.Split(';');
					if (parts.Length > 1)
					{
						var type = parts[1];
						return type.Split(',')[0];
					}
				}

				return null;
			}
		}

		public bool IsFileRef => Type != null && Type.StartsWith("System.Resources.ResXFileRef,", StringComparison.Ordinal);
	}
}
static class StringExtensions
{
	public static string Replace(this string str, string oldValue, string newValue, StringComparison comparison)
	{
		var sb = new StringBuilder();

		var previousIndex = 0;
		var index = str.IndexOf(oldValue, comparison);
		while (index != -1)
		{
			sb.Append(str, previousIndex, index - previousIndex);
			sb.Append(newValue);
			index += oldValue.Length;

			previousIndex = index;
			index = str.IndexOf(oldValue, index, comparison);
		}

		sb.Append(str, previousIndex, str.Length - previousIndex);
		return sb.ToString();
	}
}

