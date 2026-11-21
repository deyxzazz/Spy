using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SpyTool2;
public static class DllPatcher
{
    public static (bool ok, string msg) CompileAndInject(string dll, string code, string typeName, string? methodName = null, string? _ = null)
    {
        try
        {
            string dir = Path.GetDirectoryName(dll)!;
            var refs = Directory.GetFiles(dir, "*.dll").Where(f => !f.Equals(dll, StringComparison.OrdinalIgnoreCase)).Select(f => MetadataReference.CreateFromFile(f)).Cast<MetadataReference>().ToList();
            using var mod = ModuleDefMD.Load(File.ReadAllBytes(dll));
            var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(CleanUsings(code, mod, dir)), CSharpSyntaxTree.ParseText(BuildStubs(mod, typeName, methodName)) };
            var comp = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(dll), trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));
            using var pe = new MemoryStream();
            var res = comp.Emit(pe);
            if (!res.Success) return (false, string.Join("\n", res.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"[Стр.{d.Location.GetLineSpan().StartLinePosition.Line + 1}] {d.Id}: {d.GetMessage()}").Distinct().Take(10)));
            if (!File.Exists(dll + ".bak")) File.Copy(dll, dll + ".bak");
            pe.Position = 0;
            using var pMod = ModuleDefMD.Load(pe);
            var sType = pMod.GetTypes().FirstOrDefault(t => t.FullName == typeName);
            var dType = mod.GetTypes().FirstOrDefault(t => t.FullName == typeName);
            if (sType == null || dType == null) return (false, $"Класс {typeName} не найден.");
            var imp = new Importer(mod, ImporterOptions.TryToUseDefs);
            int cnt = 0;
            foreach (var sm in sType.Methods.Where(m => m.HasBody && (string.IsNullOrEmpty(methodName) || m.Name == methodName)))
            {
                var dm = dType.Methods.FirstOrDefault(m => m.Name == sm.Name && m.Parameters.Count == sm.Parameters.Count);
                if (dm != null) { CopyBody(sm, dm, imp); cnt++; }
            }
            if (cnt == 0) return (false, "Методы не найдены.");
            mod.Write(dll);
            return (true, $"Вшито в {Path.GetFileName(dll)} (методов: {cnt})!");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static string CleanUsings(string code, ModuleDefMD mod, string dir)
    {
        var ns = new HashSet<string>(mod.GetTypes().Select(t => (string)t.Namespace).Where(s => !string.IsNullOrEmpty(s))) { "System", "System.Collections", "System.Collections.Generic", "System.Diagnostics", "System.IO", "System.Linq", "System.Text", "System.Threading", "UnityEngine" };
        foreach (var f in Directory.GetFiles(dir, "*.dll")) try { using var m = ModuleDefMD.Load(f); foreach (var t in m.GetTypes()) if (!string.IsNullOrEmpty(t.Namespace)) ns.Add(t.Namespace); } catch { }
        return string.Join("\n", code.Split('\n').Select(l => { var t = l.Trim(); return t.StartsWith("using ") && t.EndsWith(";") && !t.Contains("=") && !ns.Contains(t[6..^1].Trim()) ? "// " + l : l; }));
    }

    private static string BuildStubs(ModuleDefMD mod, string targetType, string? skipMethod)
    {
        var sb = new StringBuilder("#pragma warning disable 0108,0114,0626,0649,0169\n");
        foreach (var t in mod.Types.Where(t => !t.Name.StartsWith("<") && !t.HasGenericParameters && t.Name != "MonoBehaviour" && !(t.FullName == targetType && string.IsNullOrEmpty(skipMethod))))
        {
            bool hasNs = !string.IsNullOrEmpty(t.Namespace);
            if (hasNs) sb.AppendLine($"namespace {t.Namespace} {{");
            WriteTypeStub(sb, t, targetType, skipMethod);
            if (hasNs) sb.AppendLine("}");
        }
        return sb.ToString();
    }

    private static void WriteTypeStub(StringBuilder sb, TypeDef t, string targetType, string? skipMethod)
    {
        if (t.Name.StartsWith("<") || t.HasGenericParameters) return;
        if (t.IsEnum)
        {
            sb.AppendLine($"public enum {Id(t.Name)} {{ {string.Join(", ", t.Fields.Where(f => f.IsLiteral).Select(f => $"{Id(f.Name)}={f.Constant?.Value ?? 0}"))} }}");
            return;
        }
        if (t.BaseType?.FullName == "System.MulticastDelegate")
        {
            var inv = t.Methods.FirstOrDefault(m => m.Name == "Invoke");
            if (inv != null)
            {
                var dps = inv.Parameters.Where(p => !p.IsHiddenThisParameter).Select((p, i) => $"{Sig(p.Type)} p{i}");
                sb.AppendLine($"public delegate {Sig(inv.ReturnType)} {Id(t.Name)}({string.Join(",", dps)});");
            }
            return;
        }
        string kind = t.IsInterface ? "interface" : (t.IsValueType ? "struct" : "class");
        string bs = "";
        if (!t.IsInterface && !t.IsValueType && t.BaseType != null && t.BaseType.FullName != "System.Object")
        {
            string bName = Sig(t.BaseType.ToTypeSig());
            if (!bName.Contains("Photon.")) bs = $" : {bName}";
        }
        sb.AppendLine($"public partial {kind} {Id(t.Name)}{bs} {{");
        foreach (var nt in t.NestedTypes) WriteTypeStub(sb, nt, targetType, skipMethod);
        foreach (var f in t.Fields.Where(f => !f.Name.StartsWith("<")))
            sb.AppendLine(f.IsLiteral ? $"public const {Sig(f.FieldType)} {Id(f.Name)}=default({Sig(f.FieldType)});" : $"{(t.IsInterface ? "" : (f.IsStatic ? "public static " : "public "))}{Sig(f.FieldType)} {Id(f.Name)};");
        foreach (var pr in t.Properties.Where(p => !p.Name.StartsWith("<") && p.PropertySig.Params.Count == 0))
        {
            string st = (pr.GetMethod ?? pr.SetMethod)?.IsStatic == true ? "public static " : "public ";
            string acc = t.IsInterface
          ? $"{(pr.GetMethod != null ? "get;" : "")}{(pr.SetMethod != null ? "set;" : "")}"
          : $"{(pr.GetMethod != null ? "get { throw null; }" : "")}{(pr.SetMethod != null ? "set { }" : "")}";
            sb.AppendLine($"{(t.IsInterface ? "" : st)}{Sig(pr.PropertySig.RetType)} {Id(pr.Name)} {{ {acc} }}");
        }
        foreach (var m in t.Methods.Where(m => !m.Name.StartsWith("<") && !m.IsSpecialName && !m.IsConstructor && !m.IsStaticConstructor && !m.HasGenericParameters && !(t.FullName == targetType && m.Name == skipMethod)))
        {
            var plist = m.Parameters.Where(p => !p.IsHiddenThisParameter).ToList();
            var ps = plist.Select((p, i) =>
            {
                bool isParams = i == plist.Count - 1 && p.Type is SZArraySig && (p.ParamDef?.CustomAttributes.Any(a => a.TypeFullName == "System.ParamArrayAttribute") == true);
                string pref = isParams ? "params " : (p.ParamDef?.IsOut == true ? "out " : (p.Type.IsByRef ? "ref " : ""));
                return $"{pref}{Sig(p.Type)} {(string.IsNullOrEmpty(p.Name) ? $"p{i}" : Id(p.Name))}";
            });
            sb.AppendLine(t.IsInterface ? $"{Sig(m.ReturnType)} {Id(m.Name)}({string.Join(",", ps)});" : $"{(m.IsStatic ? "public static " : "public ")}{Sig(m.ReturnType)} {Id(m.Name)}({string.Join(",", ps)}){{throw null;}}");
        }
        sb.AppendLine("}");
    }


    private static string Sig(TypeSig? s) => s switch
    {
        null or GenericVar or GenericMVar => "object",
        ByRefSig b => Sig(b.Next),
        SZArraySig a => Sig(a.Next) + "[]",
        GenericInstSig g => $"{g.GenericType.FullName.Split('`').First().Replace('/', '.')}<{string.Join(",", g.GenericArguments.Select(Sig))}>",
        _ => s.FullName.Replace('/', '.') switch { "System.Void" => "void", "System.Boolean" => "bool", "System.Byte" => "byte", "System.SByte" => "sbyte", "System.Int16" => "short", "System.UInt16" => "ushort", "System.Int32" => "int", "System.UInt32" => "uint", "System.Int64" => "long", "System.UInt64" => "ulong", "System.Single" => "float", "System.Double" => "double", "System.String" => "string", "System.Object" => "object", var fn => fn.Contains('`') ? "object" : fn }
    };

    private static string Id(string n)
    {
        string c = new(n.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
        return c is "event" or "object" or "string" or "int" or "float" or "bool" or "base" or "params" or "out" or "ref" or "in" or "lock" or "operator" or "checked" or "unchecked" or "fixed" or "namespace" or "class" or "struct" or "enum" or "delegate" or "default" ? "@" + c : c;
    }

    public static (bool ok, string msg) StubMethod(string dll, string typeName, string methodName)
    {
        try
        {
            if (!File.Exists(dll + ".bak")) File.Copy(dll, dll + ".bak");
            using var mod = ModuleDefMD.Load(File.ReadAllBytes(dll));
            var dm = mod.GetTypes().FirstOrDefault(t => t.FullName == typeName)?.Methods.FirstOrDefault(m => m.Name == methodName);
            if (dm == null) return (false, "Метод не найден.");
            var b = new CilBody();
            if (dm.ReturnType.FullName != "System.Void") b.Instructions.Add((dm.ReturnType.IsPrimitive || dm.ReturnType.FullName == "System.Boolean" ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull).ToInstruction());
            b.Instructions.Add(OpCodes.Ret.ToInstruction());
            dm.Body = b; mod.Write(dll);
            return (true, $"Метод {methodName} заменён заглушкой!");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static void CopyBody(MethodDef src, MethodDef dst, Importer imp)
    {
        var nb = new CilBody { InitLocals = src.Body.InitLocals };
        foreach (var l in src.Body.Variables) nb.Variables.Add(new Local(imp.Import(l.Type)));
        var map = new Dictionary<Instruction, Instruction>();
        foreach (var i in src.Body.Instructions)
        {
            object? op = i.Operand switch { ITypeDefOrRef t => imp.Import(t), IMethod m => imp.Import(m), IField f => imp.Import(f), Local l => nb.Variables[l.Index], Parameter p => dst.Parameters[p.Index], _ => i.Operand };
            var cl = new Instruction(i.OpCode, op); map[i] = cl; nb.Instructions.Add(cl);
        }
        foreach (var i in nb.Instructions)
        {
            if (i.Operand is Instruction t && map.TryGetValue(t, out var m)) i.Operand = m;
            else if (i.Operand is Instruction[] ts) i.Operand = ts.Select(t => map.TryGetValue(t, out var m2) ? m2 : t).ToArray();
        }
        dst.Body = nb;
    }
}