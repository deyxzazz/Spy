using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SpyTool2;

public record Il2CppMethodInfo(string Name, uint Token, ulong Rva)
{
    public bool StartsWith(string value) => Name.StartsWith(value);
    public bool Contains(string value, StringComparison comparisonType) => Name.Contains(value, comparisonType);
    public override string ToString() => Name;
    public static implicit operator string(Il2CppMethodInfo m) => m.Name;
}


public record Il2CppTypeInfo(string Namespace, string Name, List<string> Fields, List<Il2CppMethodInfo> Methods)
{
    public string FullName => Namespace == "-" ? Name : $"{Namespace}.{Name}";

    public string ToPseudoCSharp(string? focus = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Parsed from IL2CPP global-metadata.dat" + (Methods.Any(m => m.Rva > 0) ? " + GameAssembly.dll" : ""));
        if (Namespace != "-") sb.AppendLine($"namespace {Namespace}\n{{");
        string p = Namespace != "-" ? "    " : "";
        sb.AppendLine($"{p}public class {Name}\n{p}{{");
        if (Fields.Count > 0)
        {
            sb.AppendLine($"{p}    // Fields ({Fields.Count})");
            foreach (var f in Fields) sb.AppendLine($"{p}    public object {f};");
            sb.AppendLine();
        }
        if (Methods.Count > 0)
        {
            sb.AppendLine($"{p}    // Methods ({Methods.Count})");
            foreach (var m in Methods)
            {
                if (focus != null && m.Name == focus) sb.AppendLine($"{p}    // >>> SELECTED <<<");
                if (m.Rva > 0)
                    sb.AppendLine($"{p}    [Address(RVA = \"0x{m.Rva:X}\", Offset = \"0x{m.Rva:X}\")]");
                sb.AppendLine($"{p}    public void {m.Name}() {{ /* Token: 0x{m.Token:X8} */ }}");
            }
        }
        sb.AppendLine($"{p}}}");
        if (Namespace != "-") sb.AppendLine("}");
        return sb.ToString();
    }
}

public static class Il2CppMetadataParser
{
    public static (bool ok, string info, List<Il2CppTypeInfo> types) Parse(string path, string? gameAssemblyPath = null)
    {
        var list = new List<Il2CppTypeInfo>();
        try
        {
            if (!File.Exists(path)) return (false, "Файл не найден.", list);
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 0x110) return (false, $"Файл слишком мал ({b.Length} байт).", list);
            uint s = BitConverter.ToUInt32(b, 0);
            int v = BitConverter.ToInt32(b, 4);
            if (s != 0xFAB11BAF) return (false, $"Зашифрован или неверная сигнатура (0x{s:X8}).", list);

            long strO = BitConverter.ToInt32(b, 0x18);
            long strS = BitConverter.ToInt32(b, 0x1C);
            long fldO = BitConverter.ToInt32(b, 0x38);
            long fldS = BitConverter.ToInt32(b, 0x3C);
            long mtdO = BitConverter.ToInt32(b, 0x48);
            long mtdS = BitConverter.ToInt32(b, 0x4C);
            long typO = BitConverter.ToInt32(b, 0xA0);
            long typS = BitConverter.ToInt32(b, 0xA4);

            string Str(int idx)
            {
                if (idx <= 0 || idx >= strS) return "";
                long p = strO + idx;
                if (p < 0 || p >= b.Length) return "";
                int e = (int)p;
                while (e < b.Length && b[e] != 0) e++;
                return Encoding.UTF8.GetString(b, (int)p, e - (int)p);
            }

            bool IsValidIdent(string str)
            {
                if (string.IsNullOrEmpty(str)) return false;
                foreach (char c in str)
                    if (c < 32 || c > 126) return false;
                return true;
            }

            int tSt = 88;
            foreach (int cand in new[] { 88, 92, 84, 96, 100, 104 })
            {
                if (typS <= 0 || typS % cand != 0) continue;
                int probes = (int)Math.Min(typS / cand, 50);
                bool valid = true;
                for (int i = 0; i < probes; i++)
                {
                    long o = typO + (long)i * cand;
                    if (o + cand > b.Length) { valid = false; break; }
                    if (!IsValidIdent(Str(BitConverter.ToInt32(b, (int)o)))) { valid = false; break; }
                }
                if (valid) { tSt = cand; break; }
            }

            int mSt = v >= 31 ? 28 : (v >= 29 ? 32 : 40);
            foreach (int cand in new[] { 28, 32, 24, 36, 40 })
            {
                if (mtdS <= 0 || mtdS % cand != 0) continue;
                int totalM = (int)Math.Min(mtdS / cand, (b.Length - mtdO) / cand);
                if (totalM < 20) continue;
                bool valid = true;
                for (int i = 0; i < Math.Min(totalM, 500); i++)
                {
                    long o = mtdO + (long)i * cand;
                    if (!IsValidIdent(Str(BitConverter.ToInt32(b, (int)o)))) { valid = false; break; }
                }
                if (valid && totalM > 1000)
                {
                    int mid = totalM / 2;
                    for (int i = mid; i < mid + 100; i++)
                    {
                        long o = mtdO + (long)i * cand;
                        if (!IsValidIdent(Str(BitConverter.ToInt32(b, (int)o)))) { valid = false; break; }
                    }
                }
                if (valid) { mSt = cand; break; }
            }

            int fSt = 12;
            foreach (int cand in new[] { 12, 16, 8 })
            {
                if (fldS <= 0 || fldS % cand != 0) continue;
                int totalF = (int)Math.Min(fldS / cand, (b.Length - fldO) / cand);
                bool valid = true;
                for (int i = 0; i < Math.Min(totalF, 300); i++)
                {
                    long o = fldO + (long)i * cand;
                    if (!IsValidIdent(Str(BitConverter.ToInt32(b, (int)o)))) { valid = false; break; }
                }
                if (valid) { fSt = cand; break; }
            }

            long maxFields = Math.Max(0, Math.Min(fldS, b.Length - fldO) / fSt);
            long maxMethods = Math.Max(0, Math.Min(mtdS, b.Length - mtdO) / mSt);
            int cnt = (int)Math.Min(typS / tSt, (b.Length - typO) / tSt);

            // Auto-locate fieldStart, methodStart, fieldCount, methodCount inside TypeDefinition!
            // In IL2CPP TypeDefinition: fieldStart & methodStart are 32-bit indices, fieldCount & methodCount are 16-bit counts.
            int fStartOff = -1, mStartOff = -1, fCntOff = -1, mCntOff = -1;
            int bestScore = -1;

            for (int msOff = 0x10; msOff <= tSt - 8; msOff += 4)
            {
                for (int mcOff = 0x20; mcOff <= tSt - 2; mcOff += 2)
                {
                    if (mcOff >= msOff && mcOff < msOff + 4) continue;
                    int score = 0;
                    int nonZeroMatches = 0;

                    for (int i = 0; i < Math.Min(cnt - 1, 400); i++)
                    {
                        long o1 = typO + (long)i * tSt;
                        long o2 = o1 + tSt;
                        int ms1 = BitConverter.ToInt32(b, (int)o1 + msOff);
                        int ms2 = BitConverter.ToInt32(b, (int)o2 + msOff);
                        ushort mc1 = BitConverter.ToUInt16(b, (int)o1 + mcOff);

                        if (ms1 >= 0 && ms1 <= maxMethods && mc1 < 2000 && ms1 + mc1 == ms2)
                        {
                            score++;
                            if (mc1 > 0) nonZeroMatches++;
                        }
                    }

                    if (nonZeroMatches >= 5 && score > bestScore)
                    {
                        bestScore = score;
                        mStartOff = msOff;
                        mCntOff = mcOff;
                    }
                }
            }

            // Now find fieldStart & fieldCount using the exact same continuous-index invariant (fs1 + fc1 == fs2)!
            int bestFieldScore = -1;
            for (int fsOff = 0x10; fsOff <= tSt - 8; fsOff += 4)
            {
                if (fsOff == mStartOff) continue;
                for (int fcOff = 0x20; fcOff <= tSt - 2; fcOff += 2)
                {
                    if (fcOff == mCntOff || (fcOff >= fsOff && fcOff < fsOff + 4)) continue;
                    int score = 0;
                    int nonZeroMatches = 0;

                    for (int i = 0; i < Math.Min(cnt - 1, 400); i++)
                    {
                        long o1 = typO + (long)i * tSt;
                        long o2 = o1 + tSt;
                        int fs1 = BitConverter.ToInt32(b, (int)o1 + fsOff);
                        int fs2 = BitConverter.ToInt32(b, (int)o2 + fsOff);
                        ushort fc1 = BitConverter.ToUInt16(b, (int)o1 + fcOff);

                        if (fs1 >= 0 && fs1 <= maxFields && fc1 < 2000 && fs1 + fc1 == fs2)
                        {
                            score++;
                            if (fc1 > 0) nonZeroMatches++;
                        }
                    }

                    if (nonZeroMatches >= 5 && score > bestFieldScore)
                    {
                        bestFieldScore = score;
                        fStartOff = fsOff;
                        fCntOff = fcOff;
                    }
                }
            }

            if (mStartOff < 0 || mCntOff < 0)
            {
                mStartOff = 0x24;
                mCntOff = tSt == 88 ? 0x40 : 0x3A;
            }
            if (fStartOff < 0 || fCntOff < 0)
            {
                fStartOff = 0x20;
                fCntOff = tSt == 88 ? 0x44 : 0x38;
            }

            // Try to auto-locate GameAssembly.dll if not provided explicitly
            if (string.IsNullOrEmpty(gameAssemblyPath) || !File.Exists(gameAssemblyPath))
            {
                try
                {
                    // Typical path: GameRoot\Game_Data\il2cpp_data\Metadata\global-metadata.dat
                    var metaDir = Path.GetDirectoryName(path);
                    var il2cppDir = metaDir != null ? Path.GetDirectoryName(metaDir) : null;
                    var dataDir = il2cppDir != null ? Path.GetDirectoryName(il2cppDir) : null;
                    var rootDir = dataDir != null ? Path.GetDirectoryName(dataDir) : null;
                    foreach (var candDir in new[] { rootDir, dataDir, metaDir })
                    {
                        if (candDir != null && File.Exists(Path.Combine(candDir, "GameAssembly.dll")))
                        {
                            gameAssemblyPath = Path.Combine(candDir, "GameAssembly.dll");
                            break;
                        }
                    }
                }
                catch { }
            }

            // Map Token (0x06000001 + methodIdx) -> RVA from GameAssembly.dll if found
            Dictionary<uint, ulong> tokenToRva = new();
            bool hasGa = false;
            if (!string.IsNullOrEmpty(gameAssemblyPath) && File.Exists(gameAssemblyPath))
            {
                tokenToRva = GameAssemblyParser.ExtractMethodRvas(gameAssemblyPath, (int)maxMethods);
                hasGa = tokenToRva.Count > 0;
            }

            int tokenOff = mSt - 8; // In MethodDefinition, token (uint32) is 8 bytes before the end (followed by flags, iflags, slot, paramCount)
            for (int i = 0; i < cnt; i++)
            {
                long o = typO + (long)i * tSt;
                string nm = Str(BitConverter.ToInt32(b, (int)o));
                string ns = Str(BitConverter.ToInt32(b, (int)o + 4));
                if (string.IsNullOrEmpty(nm)) continue;

                int fS = BitConverter.ToInt32(b, (int)o + fStartOff);
                int mS = BitConverter.ToInt32(b, (int)o + mStartOff);
                ushort fC = BitConverter.ToUInt16(b, (int)o + fCntOff);
                ushort mC = BitConverter.ToUInt16(b, (int)o + mCntOff);

                var fl = new List<string>();
                if (fS >= 0 && fC > 0 && fC < 2000 && (long)fS + fC <= maxFields)
                {
                    for (int f = 0; f < fC; f++)
                    {
                        long fo = fldO + (long)(fS + f) * fSt;
                        if (fo + 4 <= b.Length)
                        {
                            string fn = Str(BitConverter.ToInt32(b, (int)fo));
                            if (!string.IsNullOrEmpty(fn)) fl.Add(fn);
                        }
                    }
                }

                var mt = new List<Il2CppMethodInfo>();
                if (mS >= 0 && mC > 0 && mC < 4000 && (long)mS + mC <= maxMethods)
                {
                    for (int m = 0; m < mC; m++)
                    {
                        long mo = mtdO + (long)(mS + m) * mSt;
                        if (mo + mSt <= b.Length)
                        {
                            string mn = Str(BitConverter.ToInt32(b, (int)mo));
                            uint tok = tokenOff >= 4 ? BitConverter.ToUInt32(b, (int)mo + tokenOff) : (uint)(0x06000001 + mS + m);
                            if (tok == 0) tok = (uint)(0x06000001 + mS + m);
                            tokenToRva.TryGetValue(tok, out ulong rva);
                            if (!string.IsNullOrEmpty(mn)) mt.Add(new Il2CppMethodInfo(mn, tok, rva));
                        }
                    }
                }

                list.Add(new Il2CppTypeInfo(string.IsNullOrEmpty(ns) ? "-" : ns, nm, fl, mt));
            }

            try
            {
                string dumpPath = Path.Combine(Path.GetDirectoryName(path)!, "dump.cs");
                var sb = new StringBuilder();
                foreach (var t in list) sb.AppendLine(t.ToPseudoCSharp());
                File.WriteAllText(dumpPath, sb.ToString());
            }
            catch { }

            string gaInfo = hasGa ? $" + GameAssembly ({tokenToRva.Count:N0} RVA)" : "";
            return (true, $"IL2CPP v{v} (m=0x{mStartOff:X}/0x{mCntOff:X}, f=0x{fStartOff:X}/0x{fCntOff:X}) • {list.Count:N0} типов{gaInfo}", list);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка парсинга: {ex.Message}", list);
        }
    }
}

public static class GameAssemblyParser
{
    // Scans x64 GameAssembly.dll PE sections for Il2CppCodeRegistration / methodPointers table
    public static Dictionary<uint, ulong> ExtractMethodRvas(string dllPath, int expectedMethodCount)
    {
        var map = new Dictionary<uint, ulong>();
        try
        {
            byte[] pe = File.ReadAllBytes(dllPath);
            if (pe.Length < 0x400 || BitConverter.ToUInt16(pe, 0) != 0x5A4D) return map;
            int e_lfanew = BitConverter.ToInt32(pe, 0x3C);
            if (e_lfanew <= 0 || e_lfanew + 0x108 >= pe.Length) return map;
            if (BitConverter.ToUInt32(pe, e_lfanew) != 0x00004550) return map;

            ushort numSections = BitConverter.ToUInt16(pe, e_lfanew + 6);
            ushort optHeaderSize = BitConverter.ToUInt16(pe, e_lfanew + 20);
            ushort magic = BitConverter.ToUInt16(pe, e_lfanew + 24);
            if (magic != 0x20B) return map; // x64 PE32+ only

            ulong imageBase = BitConverter.ToUInt64(pe, e_lfanew + 24 + 24);
            int secTableOff = e_lfanew + 24 + optHeaderSize;

            var sections = new List<(string name, uint va, uint vsz, uint raw, uint rsz)>();
            uint textVa = 0, textEnd = 0;

            for (int i = 0; i < numSections; i++)
            {
                int so = secTableOff + i * 40;
                if (so + 40 > pe.Length) break;
                string sName = Encoding.ASCII.GetString(pe, so, 8).TrimEnd('\0');
                uint vsz = BitConverter.ToUInt32(pe, so + 8);
                uint va = BitConverter.ToUInt32(pe, so + 12);
                uint rsz = BitConverter.ToUInt32(pe, so + 16);
                uint raw = BitConverter.ToUInt32(pe, so + 20);
                sections.Add((sName, va, vsz, raw, rsz));
                if (sName == ".text" || sName == "il2cpp")
                {
                    if (textVa == 0 || va < textVa) textVa = va;
                    if (va + vsz > textEnd) textEnd = va + vsz;
                }
            }

            if (textVa == 0 || expectedMethodCount <= 0) return map;

            int VaToOffset(ulong va)
            {
                if (va < imageBase) return -1;
                ulong rva = va - imageBase;
                foreach (var sec in sections)
                {
                    if (rva >= sec.va && rva < sec.va + sec.rsz)
                        return (int)(sec.raw + (rva - sec.va));
                }
                return -1;
            }

            bool IsCodePtr(ulong ptr)
            {
                if (ptr <= imageBase) return false;
                ulong rva = ptr - imageBase;
                return rva >= textVa && rva < textEnd;
            }

            // Search data sections (.rdata / .data) for Il2CppCodeRegistration:
            // In v29-v31, Il2CppCodeRegistration contains:
            // uint64_t reversePInvokeWrapperCount; void* reversePInvokeWrappers;
            // uint64_t genericMethodPointersCount; void* genericMethodPointers;
            // uint64_t genericAdjustorThunksCount; void* genericAdjustorThunks; (in some versions)
            // uint64_t invokerPointersCount; void* invokerPointers;
            // uint64_t unresolvedIndirectCallCount; void* unresolvedIndirectCallPointers;
            // uint64_t interopDataCount; void* interopData;
            // uint64_t windowsRuntimeFactoryCount; void* windowsRuntimeFactoryTable;
            // uint64_t codeGenModulesCount; const Il2CppCodeGenModule** codeGenModules;
            foreach (var sec in sections.Where(s => s.name is ".rdata" or ".data"))
            {
                int start = (int)sec.raw;
                int end = (int)Math.Min(pe.Length - 16, sec.raw + sec.rsz);

                for (int off = start; off < end; off += 8)
                {
                    ulong count = BitConverter.ToUInt64(pe, off);
                    if (count < 5 || count > 250) continue; // codeGenModulesCount is typically 20..120 DLLs!

                    ulong modulesPtr = BitConverter.ToUInt64(pe, off + 8);
                    int modArrayOff = VaToOffset(modulesPtr);
                    if (modArrayOff < 0 || modArrayOff + (int)count * 8 > pe.Length) continue;

                    // Validate that every entry in codeGenModules points to a valid Il2CppCodeGenModule
                    // Il2CppCodeGenModule layout:
                    // +0x00: const char* moduleName (ends with ".dll")
                    // +0x08: uint32_t methodPointerCount
                    // +0x10: const Il2CppMethodPointer* methodPointers
                    bool allModulesValid = true;
                    int validDlls = 0;

                    for (int m = 0; m < (int)count; m++)
                    {
                        ulong modPtr = BitConverter.ToUInt64(pe, modArrayOff + m * 8);
                        int modOff = VaToOffset(modPtr);
                        if (modOff < 0 || modOff + 0x20 > pe.Length) { allModulesValid = false; break; }

                        ulong namePtr = BitConverter.ToUInt64(pe, modOff);
                        int nameOff = VaToOffset(namePtr);
                        if (nameOff < 0 || nameOff + 5 > pe.Length) { allModulesValid = false; break; }

                        int ne = nameOff;
                        while (ne < pe.Length && ne - nameOff < 128 && pe[ne] != 0) ne++;
                        string dllName = Encoding.ASCII.GetString(pe, nameOff, ne - nameOff);
                        if (!dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) { allModulesValid = false; break; }
                        validDlls++;
                    }

                    if (!allModulesValid || validDlls < 5) continue;

                    // Found authentic Il2CppCodeRegistration! Extract method RVAs for every module!
                    for (int m = 0; m < (int)count; m++)
                    {
                        int modOff = VaToOffset(BitConverter.ToUInt64(pe, modArrayOff + m * 8));
                        uint mCount = BitConverter.ToUInt32(pe, modOff + 8);
                        ulong mPtrsVa = BitConverter.ToUInt64(pe, modOff + 16);
                        int mPtrsOff = VaToOffset(mPtrsVa);
                        if (mPtrsOff < 0 || mCount == 0 || mPtrsOff + (int)mCount * 8 > pe.Length) continue;

                        for (uint idx = 0; idx < mCount; idx++)
                        {
                            ulong fnVa = BitConverter.ToUInt64(pe, mPtrsOff + (int)idx * 8);
                            if (IsCodePtr(fnVa))
                            {
                                uint token = 0x06000001 + idx;
                                // Note: tokens are module-scoped; store globally if unique or prefer Assembly-CSharp
                                if (!map.ContainsKey(token))
                                    map[token] = fnVa - imageBase;
                            }
                        }
                    }

                    if (map.Count > 0) return map;
                }
            }
        }
        catch { }
        return map;
    }
}
