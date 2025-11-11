using Microsoft.CodeAnalysis;
using System.Text;

namespace WinTabber.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

[Generator]
public class LazyMethodGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MustBePartialRule = new(
        id: "LZ001",
        title: "Class must be partial",
        messageFormat: "The class '{0}' must be declared partial to use the [Lazy] attribute.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        if (!Debugger.IsAttached)
        {
            //Debugger.Launch();
        }
        var methodsWithLazy = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is MethodDeclarationSyntax m &&
                    m.AttributeLists.Count > 0,
                transform: static (ctx, _) =>
                {
                    var method = (MethodDeclarationSyntax)ctx.Node;
                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
                    if (symbol == null)
                        return null;

                    var hasLazyAttr = symbol.GetAttributes()
                        .Any(a => a.AttributeClass?.Name == "LazyAttribute" ||
                                  a.AttributeClass?.ToDisplayString().EndsWith(".LazyAttribute") == true);

                    if (!hasLazyAttr)
                        return null;

                    var containingClass = symbol.ContainingType;
                    if (!containingClass.DeclaringSyntaxReferences.Any(s =>
                            s.GetSyntax() is ClassDeclarationSyntax cls && cls.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))))
                    {
                        // report diagnostic
                        return new ClassAnalysisResult([
                            Diagnostic.Create(
                                MustBePartialRule,
                                method.Identifier.GetLocation(),
                                containingClass.Name)
                            ]);
                    }

                    return new ClassAnalysisResult(containingClass, symbol);
                })
            .Where(static m => m is not null)!;

        var grouped = methodsWithLazy.Where(mi => mi != null).Collect();
        context.RegisterPostInitializationOutput(ctx =>
            ctx.AddSource("LazyAttribute.g.cs", @"
                using System;
                [AttributeUsage(AttributeTargets.Method)]
                internal sealed class LazyAttribute : Attribute {}
            "));
        context.RegisterSourceOutput(grouped, (spc, list) =>
        {
            var methodLookup = list.ToLookup(m => m!.IsSuccess);
            var successes = methodLookup[true].Select(r => r!.ToMethodInfo()).ToList();
            var failures = methodLookup[false].SelectMany(f => f.ToDiagnostics());
            var byClass = successes.GroupBy(m => m!.ContainingClass, SymbolEqualityComparer.Default);

            foreach (var failure in failures)
            {
                spc.ReportDiagnostic(failure);
            }
            foreach (var group in byClass)
            {
                var classSymbol = group.Key;
                if (classSymbol is INamedTypeSymbol namedSymbol)
                {
                    var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
                        ? null
                        : classSymbol.ContainingNamespace.ToDisplayString();
                    var successfulMethods = methodLookup[true];
                    var source = GenerateClass(ns, namedSymbol, group.ToList()!);
                    spc.AddSource($"{classSymbol.Name}_Lazy.g.cs", SourceText.From(source, Encoding.UTF8));
                }
            }
        });
    }

    private static string GenerateClass(string? ns, INamedTypeSymbol classSymbol, List<MethodInfo> methods)
    {
        //if (!Debugger.IsAttached) Debugger.Launch();

        var sb = new StringBuilder();
        var currentNs = ns ?? string.Empty;
        var neededNamespaces = new HashSet<string>();


        foreach (var method in methods)
        {
            var returnType = method.Method.ReturnType;
            var returnNs = returnType.ContainingNamespace?.ToDisplayString();

            if (!string.IsNullOrEmpty(returnNs)
                && returnNs != currentNs
                && (!returnNs?.StartsWith("System") ?? false))
            {
                neededNamespaces.Add(returnNs!);
            }

            // If the method has parameters that are also external types, include their namespaces
            //foreach (var param in method.Method.ReturnType.Parameters)
            //{
            //    var paramNs = param.Type.ContainingNamespace?.ToDisplayString();
            //    if (!string.IsNullOrEmpty(paramNs)
            //        && paramNs != currentNs
            //        && !paramNs.StartsWith("System"))
            //    {
            //        neededNamespaces.Add(paramNs);
            //    }
            //}
        }

        foreach (var nsName in neededNamespaces.OrderBy(x => x))
        {
            sb.AppendLine($"using {nsName};");
        }

        if (ns is not null)
            sb.AppendLine($"namespace {ns};");

        sb.AppendLine();
        sb.AppendLine($"partial class {classSymbol.Name}");
        sb.AppendLine("{");

        foreach (var m in methods)
        {
            var type = m.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var methodName = m.Method.Name;
            var (fieldName, _) = GetNames(methodName);
            sb.AppendLine($"    private {type}? {fieldName} = null!;");
        }

        sb.AppendLine();

        foreach (var m in methods)
        {
            var type = m.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            
            //var containingType = returnType.ContainingType?.Name;
            //var extra = containingType is not null ? $"{containingType}."
            var methodName = m.Method.Name;
            var (fieldName, propertyName) = GetNames(methodName);
            var valueExpr = m.Method.ReturnType.IsValueType ? $".Value" : "";

            sb.AppendLine($"    public {type} {propertyName}");
            sb.AppendLine($"     {{");
            sb.AppendLine($"         get");
            sb.AppendLine($"         {{");
            sb.AppendLine($"             if ({fieldName} is null)");
            sb.AppendLine($"             {{");
            sb.AppendLine($"                 {fieldName} = {methodName}();");
            sb.AppendLine($"             }}");
            sb.AppendLine($"             return {fieldName}{valueExpr};");
            sb.AppendLine($"         }}");
            sb.AppendLine($"     }}");
        }

        if (!Debugger.IsAttached)
        {
            //Debugger.Launch();

        }
        //sb.AppendLine();
        //sb.AppendLine("    public void OnConstructed()");
        //sb.AppendLine("    {");
        //foreach (var m in methods)
        //{
        //    var type = m.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        //    var methodName = m.Method.Name;
        //    sb.AppendLine($"        _lazy{methodName} = new Lazy<{type}>(() => {methodName}());");
        //}
        //sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }


    private static (string FieldName, string PropertyName) GetNames(string methodName)
    {
        var propertyName = Regex.Replace(methodName, "^Get", string.Empty);
        var fieldName = $"_generated_lazy{propertyName}";
        return (fieldName, propertyName);
    }

    public record MethodInfo
    {
        public MethodInfo(INamedTypeSymbol containingClass, IMethodSymbol method)
        {
            ContainingClass = containingClass;
            Method = method;
        }
        public INamedTypeSymbol ContainingClass { get; }
        public IMethodSymbol Method { get; }

        public TypeSyntax ReturnType { get; }

    }
    private record ClassAnalysisResult
    {
        public ClassAnalysisResult(INamedTypeSymbol containingClass, IMethodSymbol method)
        {
            ContainingClass = containingClass;
            Method = method;
            Diagnostics = [];
        }

        public ClassAnalysisResult(Diagnostic[] diagnostics)
        {
            Diagnostics = diagnostics;
            Method = null;
            ReturnType = null;
            ContainingClass = null;
        }

        public bool IsSuccess => Method is not null;

        public MethodInfo ToMethodInfo()
        {
            if (!IsSuccess)
            {
                throw new InvalidOperationException();
            }

            return new MethodInfo(ContainingClass!, Method!);
        }

        public Diagnostic[] ToDiagnostics()
        {
            return Diagnostics!;
        }

        public Diagnostic[]? Diagnostics { get; private set; }
        public INamedTypeSymbol? ContainingClass { get; }
        public IMethodSymbol? Method { get; }
        public TypeSyntax ReturnType { get; }
    }
}

