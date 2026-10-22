using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static readonly Dictionary<ModuleDefinition, Dictionary<string, TypeDefinition>> _typeIndexes = new();

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            Console.WriteLine("usage: IlPatcher dump|types|findstr|patch|verify ...");
            return 2;
        }

        switch (args[0])
        {
            case "dump":
                return Dump(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
            case "types":
                return Types(args[1], args.Length > 2 ? args[2] : null);
            case "findstr":
                return FindString(args[1], args[2]);
            case "patch":
                return Patch(args[1], args[2], args[3], args.Length > 4 ? args[4] : null);
            case "verify":
                return Verify(args[1], args[2]);
            case "diff":
                return Diff(args[1], args[2]);
            case "resources":
                return Resources(args[1], args.Length > 2 ? args[2] : null);
            default:
                Console.WriteLine("unknown command");
                return 2;
        }
    }

    // ---------------- dump / inspect ----------------

    private static int Dump(string path, string typeFilter, string methodFilter)
    {
        var asm = AssemblyDefinition.ReadAssembly(path);
        Console.WriteLine($"Assembly: {asm.Name.Name} {asm.Name.Version}");
        Console.WriteLine($"Runtime : {asm.MainModule.RuntimeVersion}");
        Console.WriteLine($"Types   : {asm.MainModule.Types.Count}");

        foreach (var f in asm.MainModule.GetType("PCL.ModSecret")?.Fields ?? Enumerable.Empty<FieldDefinition>())
            if (f.HasConstant)
                Console.WriteLine($"const  : ModSecret.{f.Name} = {f.Constant} ({f.Constant?.GetType().Name})");

        foreach (var type in AllTypes(asm.MainModule))
        {
            if (typeFilter != null && !type.FullName.Contains(typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var m in type.Methods)
            {
                if (methodFilter != null && !m.Name.Contains(methodFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (!m.HasBody) continue;
                Console.WriteLine();
                Console.WriteLine($"=== {m.FullName}");
                foreach (var ins in m.Body.Instructions)
                    Console.WriteLine($"  IL_{ins.Offset:x4}: {ins.OpCode,-12} {FormatOperand(ins.Operand)}");
            }
        }
        return 0;
    }

    private static int Types(string path, string filter)
    {
        var asm = AssemblyDefinition.ReadAssembly(path);
        foreach (var t in AllTypes(asm.MainModule))
            if (filter == null || t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine(t.FullName);
        return 0;
    }

    private static int FindString(string path, string pattern)
    {
        var asm = AssemblyDefinition.ReadAssembly(path);
        foreach (var type in AllTypes(asm.MainModule))
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                var hits = m.Body.Instructions.Where(i => i.Operand is string s && s.Contains(pattern, StringComparison.Ordinal)).ToList();
                if (hits.Count == 0) continue;
                Console.WriteLine();
                Console.WriteLine($"=== {m.FullName}  (hits={hits.Count})");
                int first = m.Body.Instructions.IndexOf(hits[0]);
                for (int k = Math.Max(0, first - 12); k < Math.Min(m.Body.Instructions.Count, first + 25); k++)
                {
                    var ins = m.Body.Instructions[k];
                    Console.WriteLine($"  IL_{ins.Offset:x4}: {ins.OpCode,-12} {FormatOperand(ins.Operand)}");
                }
            }
        return 0;
    }

    // ---------------- patch ----------------

    private static int Patch(string srcPath, string refPath, string dstPath, string depDirs)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(srcPath)));
        foreach (var d in (depDirs ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            resolver.AddSearchDirectory(d);

        var readerParameters = new ReaderParameters { AssemblyResolver = resolver };
        var target = AssemblyDefinition.ReadAssembly(srcPath, readerParameters);
        var reference = AssemblyDefinition.ReadAssembly(refPath, new ReaderParameters { AssemblyResolver = resolver });
        var tm = target.MainModule;
        var rm = reference.MainModule;

        // 1) 常量 ModSecret.RestrictMcDownload = False
        var secretType = GetType(tm, "PCL.ModSecret");
        var restrictField = secretType.Fields.FirstOrDefault(f => f.Name == "RestrictMcDownload")
                            ?? throw new Exception("RestrictMcDownload field not found");
        Console.WriteLine($"[1] ModSecret.RestrictMcDownload: {restrictField.Constant} -> False");
        restrictField.Constant = false;

        // 2) PageDownloadModify.LoadInstanceFinished: CardMinecraft 可见性 Collapsed(2) -> Visible(0)
        var modifyType = GetType(tm, "PCL.PageDownloadModify");
        var loadFinished = modifyType.Methods.First(m => m.Name == "LoadInstanceFinished");
        bool patchedVisibility = false;
        var ilM = loadFinished.Body.GetILProcessor();
        var instrs = loadFinished.Body.Instructions;
        for (int i = 0; i < instrs.Count - 1; i++)
        {
            if (instrs[i].OpCode == OpCodes.Callvirt && instrs[i].Operand is MethodReference mr &&
                mr.Name == "get_CardMinecraft" && instrs[i + 1].OpCode == OpCodes.Ldc_I4_2)
            {
                ilM.Replace(instrs[i + 1], Instruction.Create(OpCodes.Ldc_I4_0));
                patchedVisibility = true;
                Console.WriteLine($"[2] PageDownloadModify.LoadInstanceFinished: CardMinecraft Collapsed -> Visible (IL_{instrs[i + 1].Offset:x4})");
                break;
            }
        }
        if (!patchedVisibility) throw new Exception("CardMinecraft visibility pattern not found");

        // 3) 用无限制版本的方法体替换被限制逻辑破坏的方法
        PatchMethodBody(tm, rm, "PCL.PageDownloadInstall", "LoaderInit", new Dictionary<string, string> { ["_Lambda$__R3-5"] = "_Lambda$__R3-4" });
        PatchMethodBody(tm, rm, "PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", null);
        PatchMethodBody(tm, rm, "PCL.ModModpack", "ModpackInstall", null, paramCount: 0);
        PatchMethodBody(tm, rm, "PCL.ModModpack", "ModpackInstall", null, paramCount: 3);

        target.Write(dstPath);
        Console.WriteLine($"[4] written: {dstPath}");
        return 0;
    }

    private static void PatchMethodBody(ModuleDefinition tm, ModuleDefinition rm,
        string typeName, string methodName, Dictionary<string, string> renames, int paramCount = -1)
    {
        var tType = GetType(tm, typeName);
        var rType = GetType(rm, typeName);
        var tMethod = tType.Methods.FirstOrDefault(m => m.Name == methodName && (paramCount < 0 || m.Parameters.Count == paramCount))
                      ?? throw new Exception($"target method not found: {typeName}::{methodName}/{paramCount}");
        var rMethod = rType.Methods.FirstOrDefault(m => m.Name == methodName && m.Parameters.Count == tMethod.Parameters.Count)
                      ?? throw new Exception($"reference method not found: {typeName}::{methodName}");
        if (!rMethod.HasBody) throw new Exception("reference method has no body: " + rMethod.FullName);

        CloneBody(tMethod, rMethod, rm, tm, renames ?? new Dictionary<string, string>());
        Console.WriteLine($"[3] transplanted body: {tMethod.FullName}  ({rMethod.Body.Instructions.Count} IL)");
    }

    private static void CloneBody(MethodDefinition target, MethodDefinition source,
        ModuleDefinition srcModule, ModuleDefinition dstModule, Dictionary<string, string> renames)
    {
        var body = new MethodBody(target) { InitLocals = source.Body.InitLocals };
        target.Body = body;

        foreach (var v in source.Body.Variables)
            body.Variables.Add(new VariableDefinition(MapType(v.VariableType, srcModule, dstModule)));

        var map = new Dictionary<Instruction, Instruction>();
        foreach (var ins in source.Body.Instructions)
        {
            var ni = CreateLike(ins);
            map[ins] = ni;
            body.Instructions.Add(ni);
        }

        foreach (var ins in source.Body.Instructions)
        {
            var ni = map[ins];
            ni.Operand = ins.Operand switch
            {
                null => null,
                Instruction i => map[i],
                Instruction[] arr => arr.Select(x => map[x]).ToArray(),
                VariableDefinition v => body.Variables[v.Index],
                ParameterDefinition p => target.Parameters[p.Index],
                MethodReference mr => MapMethod(mr, srcModule, dstModule, renames),
                FieldReference fr => MapField(fr, srcModule, dstModule),
                TypeReference tr => MapType(tr, srcModule, dstModule),
                _ => ins.Operand
            };
        }

        foreach (var h in source.Body.ExceptionHandlers)
        {
            body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType)
            {
                TryStart = h.TryStart == null ? null : map[h.TryStart],
                TryEnd = h.TryEnd == null ? null : map[h.TryEnd],
                HandlerStart = h.HandlerStart == null ? null : map[h.HandlerStart],
                HandlerEnd = h.HandlerEnd == null ? null : map[h.HandlerEnd],
                FilterStart = h.FilterStart == null ? null : map[h.FilterStart],
                CatchType = h.CatchType == null ? null : MapType(h.CatchType, srcModule, dstModule)
            });
        }
    }

    private static Instruction CreateLike(Instruction src) => src.Operand switch
    {
        null => Instruction.Create(src.OpCode),
        sbyte sb => Instruction.Create(src.OpCode, sb),
        byte b => Instruction.Create(src.OpCode, b),
        int i => Instruction.Create(src.OpCode, i),
        long l => Instruction.Create(src.OpCode, l),
        float f => Instruction.Create(src.OpCode, f),
        double d => Instruction.Create(src.OpCode, d),
        string s => Instruction.Create(src.OpCode, s),
        Instruction t => Instruction.Create(src.OpCode, t),
        Instruction[] arr => Instruction.Create(src.OpCode, arr),
        VariableDefinition v => Instruction.Create(src.OpCode, v),
        ParameterDefinition p => Instruction.Create(src.OpCode, p),
        MethodReference mr => Instruction.Create(src.OpCode, mr),
        FieldReference fr => Instruction.Create(src.OpCode, fr),
        TypeReference tr => Instruction.Create(src.OpCode, tr),
        CallSite cs => Instruction.Create(src.OpCode, cs),
        _ => throw new Exception("unsupported operand: " + src.Operand.GetType())
    };

    private static MethodReference MapMethod(MethodReference mr, ModuleDefinition srcModule,
        ModuleDefinition dstModule, Dictionary<string, string> renames)
    {
        MethodDefinition def = null;
        try { def = mr.Resolve(); } catch { /* 外部引用解析失败时走 ImportReference */ }
        if (def == null || def.Module != srcModule)
            return dstModule.ImportReference(mr);

        string name = def.Name;
        if (renames.TryGetValue(name, out var renamed)) name = renamed;

        var tType = GetType(dstModule, def.DeclaringType.FullName);
        var target = tType.Methods.FirstOrDefault(m => m.Name == name && SameSignature(m, def))
                     ?? throw new Exception($"cannot map method: {def.DeclaringType.FullName}::{def.Name}");

        MethodReference result = target;
        if (mr is GenericInstanceMethod gim)
        {
            var ng = new GenericInstanceMethod(target);
            foreach (var ga in gim.GenericArguments) ng.GenericArguments.Add(MapType(ga, srcModule, dstModule));
            result = ng;
        }
        return result;
    }

    private static FieldReference MapField(FieldReference fr, ModuleDefinition srcModule, ModuleDefinition dstModule)
    {
        FieldDefinition def = null;
        try { def = fr.Resolve(); } catch { }
        if (def == null || def.Module != srcModule)
            return dstModule.ImportReference(fr);

        var tType = GetType(dstModule, def.DeclaringType.FullName);
        return tType.Fields.FirstOrDefault(f => f.Name == def.Name)
               ?? throw new Exception($"cannot map field: {def.DeclaringType.FullName}::{def.Name}");
    }

    private static TypeReference MapType(TypeReference tr, ModuleDefinition srcModule, ModuleDefinition dstModule)
    {
        switch (tr)
        {
            case GenericParameter:
                return tr;
            case ArrayType at:
                return new ArrayType(MapType(at.ElementType, srcModule, dstModule), at.Rank);
            case ByReferenceType br:
                return new ByReferenceType(MapType(br.ElementType, srcModule, dstModule));
            case PointerType pt:
                return new PointerType(MapType(pt.ElementType, srcModule, dstModule));
            case GenericInstanceType git:
            {
                var elem = MapType(git.ElementType, srcModule, dstModule);
                var ng = new GenericInstanceType(elem);
                foreach (var a in git.GenericArguments) ng.GenericArguments.Add(MapType(a, srcModule, dstModule));
                return ng;
            }
        }

        TypeDefinition def = null;
        try { def = tr.Resolve(); } catch { }
        if (def != null && def.Module == srcModule)
            return GetType(dstModule, def.FullName);
        return dstModule.ImportReference(tr);
    }

    private static bool SameSignature(MethodDefinition a, MethodDefinition b)
    {
        if (a.Parameters.Count != b.Parameters.Count) return false;
        if (a.ReturnType.FullName != b.ReturnType.FullName) return false;
        for (int i = 0; i < a.Parameters.Count; i++)
            if (a.Parameters[i].ParameterType.FullName != b.Parameters[i].ParameterType.FullName)
                return false;
        return true;
    }

    private static TypeDefinition GetType(ModuleDefinition module, string fullName)
    {
        if (!_typeIndexes.TryGetValue(module, out var index))
        {
            index = AllTypes(module).ToDictionary(t => t.FullName, t => t);
            _typeIndexes[module] = index;
        }
        if (index.TryGetValue(fullName, out var t)) return t;
        throw new Exception("type not found: " + fullName);
    }

    // ---------------- verify ----------------

    private static int Diff(string aPath, string bPath)
    {
        var a = AssemblyDefinition.ReadAssembly(aPath).MainModule;
        var b = AssemblyDefinition.ReadAssembly(bPath).MainModule;
        int changed = 0, total = 0;

        foreach (var ta in AllTypes(a))
        {
            var tb = b.GetType(ta.FullName);
            foreach (var ma in ta.Methods)
            {
                total++;
                var mb = tb?.Methods.FirstOrDefault(x => Signature(x) == Signature(ma));
                if (mb == null)
                {
                    changed++;
                    Console.WriteLine($"MISSING {ta.FullName}::{Signature(ma)}");
                    continue;
                }
                if (Normalize(ma).SequenceEqual(Normalize(mb))) continue;
                changed++;
                Console.WriteLine($"CHANGED {ta.FullName}::{Signature(ma)}");
            }
        }
        Console.WriteLine($"methods={total} changed={changed}");
        return 0;
    }

    private static int Resources(string path, string extractDir)
    {
        var asm = AssemblyDefinition.ReadAssembly(path);
        foreach (var r in asm.MainModule.Resources.OfType<EmbeddedResource>())
        {
            var data = r.GetResourceData();
            Console.WriteLine($"{r.Name}  {data.Length}");
            if (extractDir != null)
            {
                var p = Path.Combine(extractDir, r.Name.Replace('/', '_'));
                Directory.CreateDirectory(extractDir);
                File.WriteAllBytes(p, data);
            }
        }
        return 0;
    }

    private static int Verify(string path, string refPath)
    {
        var asm = AssemblyDefinition.ReadAssembly(path);
        var rasm = AssemblyDefinition.ReadAssembly(refPath);
        var tm = asm.MainModule;
        var rm = rasm.MainModule;

        int fail = 0;
        void Check(bool ok, string msg)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + msg);
            if (!ok) fail++;
        }

        var restrict = tm.GetType("PCL.ModSecret")?.Fields.FirstOrDefault(f => f.Name == "RestrictMcDownload");
        Check(restrict != null && Equals(restrict.Constant, false), $"ModSecret.RestrictMcDownload = {restrict?.Constant}");

        var loaderInit = tm.GetType("PCL.PageDownloadInstall")?.Methods.FirstOrDefault(m => m.Name == "LoaderInit");
        var liText = DumpText(loaderInit);
        Check(!liText.Contains("关闭下载"), "LoaderInit 不再包含「关闭下载」");
        Check(liText.Contains("DlClientListLoader"), "LoaderInit 直接引用 DlClientListLoader");
        Check(loaderInit.Body.Instructions.Count == 18, $"LoaderInit IL 条数 = {loaderInit.Body.Instructions.Count} (期望 18)");

        var modify = tm.GetType("PCL.PageDownloadModify")?.Methods.First(m => m.Name == "LoadInstanceFinished");
        var mi = modify.Body.Instructions;
        bool visible = false;
        for (int i = 0; i < mi.Count - 1; i++)
            if (mi[i].Operand is MethodReference m2 && m2.Name == "get_CardMinecraft" && mi[i + 1].OpCode == OpCodes.Ldc_I4_0)
                visible = true;
        Check(visible, "PageDownloadModify: CardMinecraft = Visible");

        var detail = tm.GetType("PCL.PageDownloadResourceDetail")?.Methods.First(m => m.Name == "InitializeStackResourceVersions");
        var dt = DumpText(detail);
        Check(dt.Contains("_Lambda$__R18-5") && dt.Contains("TargetResourceType"), "详情页版本列表包含 ModPack 安装入口");

        var packNoArg = tm.GetType("PCL.ModModpack")?.Methods.First(m => m.Name == "ModpackInstall" && m.Parameters.Count == 0);
        var pna = DumpText(packNoArg);
        Check(pna.Contains("SelectFile"), "ModpackInstall() 恢复文件选择逻辑");
        Check(!pna.Contains("为遵守相关法律法规"), "ModpackInstall() 不再含合规提示");

        var packArg = tm.GetType("PCL.ModModpack")?.Methods.First(m => m.Name == "ModpackInstall" && m.Parameters.Count == 3);
        var pa = DumpText(packArg);
        Check(!pa.Contains("为遵守相关法律法规"), "ModpackInstall(path,...) 不再抛合规异常");
        Check(pa.Contains("InstallPack"), "ModpackInstall(path,...) 恢复整合包安装分发");

        // 与方法参考实现逐条比对（忽略偏移与源文件路径字面量）
        foreach (var (type, method, count) in new[] {
            ("PCL.PageDownloadInstall", "LoaderInit", -1),
            ("PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", -1),
            ("PCL.ModModpack", "ModpackInstall", 0),
            ("PCL.ModModpack", "ModpackInstall", 3) })
        {
            var a = tm.GetType(type).Methods.First(m => m.Name == method && (count < 0 || m.Parameters.Count == count));
            var b = rm.GetType(type).Methods.First(m => m.Name == method && m.Parameters.Count == a.Parameters.Count);
            Check(Normalize(a).SequenceEqual(Normalize(b)), $"{type}::{method}/{a.Parameters.Count} 与无限制构建语义一致");
        }

        Console.WriteLine(fail == 0 ? "ALL CHECKS PASSED" : $"{fail} CHECK(S) FAILED");
        return fail == 0 ? 0 : 1;
    }

    private static string Signature(MethodDefinition m) =>
        (m.HasThis ? "instance " : "static ") + m.ReturnType.FullName + " " + m.Name +
        "<" + m.GenericParameters.Count + ">(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")";

    private static string DumpText(MethodDefinition m)
    {
        var sb = new StringBuilder();
        foreach (var ins in m.Body.Instructions)
            sb.AppendLine($"{ins.OpCode} {FormatOperand(ins.Operand)}");
        return sb.ToString();
    }

    private static IEnumerable<string> Normalize(MethodDefinition m)
    {
        if (!m.HasBody)
        {
            yield return "NOBODY";
            yield break;
        }
        foreach (var ins in m.Body.Instructions)
        {
            string op = ins.Operand switch
            {
                Instruction i => "IL",
                Instruction[] a => "IL[]",
                string s => "\"" + s.Replace("D:\\Documents\\deepseek-harness\\default-workspace\\PCL", "SRC")
                                   .Replace("E:\\Projects\\PCL\\本体代码", "SRC") + "\"",
                _ => FormatOperand(ins.Operand).Replace("_Lambda$__R3-5", "_Lambda$__R3-4")
            };
            yield return ins.OpCode + " " + op;
        }
    }

    // ---------------- helpers ----------------

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            yield return t;
            foreach (var n in t.NestedTypes) stack.Push(n);
        }
    }

    internal static string FormatOperand(object operand) => operand switch
    {
        null => "",
        string s => "\"" + s + "\"",
        Instruction i => $"IL_{i.Offset:x4}",
        Instruction[] arr => string.Join(", ", arr.Select(i => $"IL_{i.Offset:x4}")),
        MethodReference mr => mr.FullName,
        FieldReference fr => fr.FullName,
        TypeReference tr => tr.FullName,
        VariableDefinition v => $"V_{v.Index}",
        ParameterDefinition p => p.Name,
        _ => operand.ToString()
    };
}
