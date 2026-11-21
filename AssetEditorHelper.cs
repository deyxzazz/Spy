using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
namespace SpyTool2;
public static class AssetEditorHelper
{
    private static string _cachedPath = "";
    private static BundleFileInstance? _cachedBun;
    private static readonly List<AssetsFileInstance> _cachedInsts = new();
    public static void ClearCache() { _cachedPath = ""; _cachedBun = null; _cachedInsts.Clear(); }

    private static (AssetsFileInstance? inst, AssetFileInfo? inf) OpenAsset(AssetsManager am, string f, long pid)
    {
        if (_cachedPath != f)
        {
            _cachedPath = f; _cachedBun = null; _cachedInsts.Clear();
            if (f.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
            {
                _cachedBun = am.LoadBundleFile(f, true);
                for (int i = 0; i < _cachedBun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    var di = _cachedBun.file.BlockAndDirInfo.DirectoryInfos[i];
                    if (di.Name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || di.Name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) continue;
                    try { var ins = am.LoadAssetsFileFromBundle(_cachedBun, i, true); try { am.LoadClassDatabaseFromPackage(ins.file.Metadata.UnityVersion); } catch { } _cachedInsts.Add(ins); } catch { }
                }
            }
            else { var ins = am.LoadAssetsFile(f, true); try { am.LoadClassDatabaseFromPackage(ins.file.Metadata.UnityVersion); } catch { } _cachedInsts.Add(ins); }
        }
        foreach (var ins in _cachedInsts) { var inf = ins.file.GetAssetInfo(pid); if (inf != null) return (ins, inf); }
        return (null, null);
    }

    public static (WriteableBitmap? bmp, string? txt, string info) GetAssetPreviewData(AssetsManager am, string f, long pid, string tp)
    {
        string err = "";
        try
        {
            if (tp == "Texture2D")
            {
                byte[]? bg = null; int wd = 0, ht = 0;
                try
                {
                    var (inst, inf) = OpenAsset(am, f, pid);
                    if (inst != null && inf != null)
                    {
                        var r = inst.file.Reader;
                        long start = inf.GetAbsoluteByteOffset(inst.file), end = start + inf.ByteSize;
                        int fmt = 0; byte[]? rawTex = null;
                        lock (r)
                        {
                            r.Position = start;
                            int nl = r.ReadInt32();
                            int hdrOff = 4 + nl + ((4 - (nl & 3)) & 3);
                            for (int offProbe = 0; offProbe <= 12; offProbe += 4)
                            {
                                r.Position = start + hdrOff + offProbe;
                                if (r.Position + 16 > end) break;
                                int w = r.ReadInt32(), h = r.ReadInt32(), imgSz = r.ReadInt32(), tf = r.ReadInt32();
                                if (w > 0 && w <= 8192 && h > 0 && h <= 8192 && tf > 0 && tf < 100 && imgSz >= 0)
                                {
                                    wd = w; ht = h; fmt = tf;
                                    if (imgSz > 0 && r.Position + 20 + imgSz <= end)
                                    {
                                        r.Position += 16;
                                        int dLen = r.ReadInt32();
                                        if (dLen > 0 && r.Position + dLen <= end) rawTex = r.ReadBytes(dLen);
                                    }
                                    if ((rawTex == null || rawTex.Length == 0) && _cachedBun != null)
                                    {
                                        int rem = (int)Math.Min(180, end - r.Position);
                                        if (rem > 20)
                                        {
                                            byte[] tail = r.ReadBytes(rem);
                                            int arcIdx = Encoding.ASCII.GetString(tail).IndexOf("archive:/", StringComparison.Ordinal);
                                            if (arcIdx >= 16)
                                            {
                                                ulong sOff = BitConverter.ToUInt64(tail, arcIdx - 16);
                                                uint sSz = BitConverter.ToUInt32(tail, arcIdx - 8);
                                                int sLen = BitConverter.ToInt32(tail, arcIdx - 4);
                                                if (sLen > 0 && arcIdx + sLen <= tail.Length && sSz > 0)
                                                {
                                                    string rn = Path.GetFileName(Encoding.UTF8.GetString(tail, arcIdx, sLen));
                                                    int dIdx = _cachedBun.file.BlockAndDirInfo.DirectoryInfos.FindIndex(d => d.Name.EndsWith(rn, StringComparison.OrdinalIgnoreCase));
                                                    if (dIdx >= 0)
                                                    {
                                                        byte[] resAll = BundleHelper.LoadAssetDataFromBundle(_cachedBun.file, dIdx);
                                                        if ((long)(sOff + sSz) <= resAll.Length)
                                                        {
                                                            rawTex = new byte[sSz];
                                                            Buffer.BlockCopy(resAll, (int)sOff, rawTex, 0, (int)sSz);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                        if (rawTex != null && rawTex.Length > 0)
                        {
                            var tx = new TextureFile { m_Width = wd, m_Height = ht, m_TextureFormat = fmt, pictureData = rawTex };
                            bg = tx.DecodeTextureRaw(rawTex);
                            if (bg == null || bg.Length == 0) err = $"Fmt: {(TextureFormat)fmt}, bytes: {rawTex.Length}";
                        }
                        else err = $"Не найдены пиксели ({wd}x{ht}, fmt={fmt})";
                    }
                    else err = "Не найден в кэше";
                }
                catch (Exception ex) { err = ex.Message; }
                if (bg == null || bg.Length == 0) { var e = LegacyUnityAssets.ReadEntries(f).FirstOrDefault(x => x.PathId == pid); var r = e != null ? LegacyUnityAssets.TryExtractTextureBgra(f, e) : null; if (r != null) { wd = r.Value.width; ht = r.Value.height; bg = r.Value.bgra; } }
                if (bg != null && bg.Length > 0 && wd > 0 && ht > 0)
                {
                    var wb = new WriteableBitmap(new PixelSize(wd, ht), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                    using (var fb = wb.Lock()) Marshal.Copy(bg, 0, fb.Address, Math.Min(bg.Length, fb.RowBytes * ht));
                    return (wb, null, $"Текстура: {wd}x{ht} px");
                }
            }
            else if (tp == "TextAsset")
            {
                string? tx = null;
                try { var (inst, inf) = OpenAsset(am, f, pid); if (inst != null && inf != null) tx = Encoding.UTF8.GetString(am.GetBaseField(inst, inf).Get("m_Script").AsByteArray); } catch (Exception ex) { err = ex.Message; }
                if (tx == null) { var e = LegacyUnityAssets.ReadEntries(f).FirstOrDefault(x => x.PathId == pid); if (e != null) tx = LegacyUnityAssets.TryExtractText(f, e); }
                if (tx != null) return (null, tx.Length > 15000 ? tx[..15000] + "\n..." : tx, $"Текст: {tx.Length} симв.");
            }
        }
        catch (Exception ex) { err = ex.Message; }
        return (null, null, $"Тип: {tp}\n{(string.IsNullOrEmpty(err) ? "Предпросмотр недоступен." : err)}");
    }



    public static async Task<string> ExportAsset(Window w, AssetsManager am, string f, long pid, string nm, string md)
    {
        try
        {
            string sf = string.Concat(nm.Split(Path.GetInvalidFileNameChars())); if (string.IsNullOrWhiteSpace(sf)) sf = $"Asset_{pid}";
            if (md == "png")
            {
                byte[]? bg = null; int wd = 0, ht = 0;
                try { var (inst, inf) = OpenAsset(am, f, pid); if (inst != null && inf != null) { var tx = TextureFile.ReadTextureFile(am.GetBaseField(inst, inf)); bg = tx.FillPictureData(inst); wd = tx.m_Width; ht = tx.m_Height; } } catch { }
                if (bg == null || bg.Length == 0) { var e = LegacyUnityAssets.ReadEntries(f).FirstOrDefault(x => x.PathId == pid); var r = e != null ? LegacyUnityAssets.TryExtractTextureBgra(f, e) : null; if (r != null) { wd = r.Value.width; ht = r.Value.height; bg = r.Value.bgra; } }
                if (bg == null || bg.Length == 0) return "Не Texture2D.";
                var s = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = sf + ".png", DefaultExtension = "png" }); if (s == null) return "Отменено.";
                using var wb = new WriteableBitmap(new PixelSize(wd, ht), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                using (var fb = wb.Lock()) Marshal.Copy(bg, 0, fb.Address, Math.Min(bg.Length, fb.RowBytes * ht));
                wb.Save(s.Path.LocalPath); return $"Сохранено: {Path.GetFileName(s.Path.LocalPath)} ({wd}x{ht})";
            }
            if (md == "txt")
            {
                string? tx = null;
                try { var (inst, inf) = OpenAsset(am, f, pid); if (inst != null && inf != null) tx = Encoding.UTF8.GetString(am.GetBaseField(inst, inf).Get("m_Script").AsByteArray); } catch { }
                if (tx == null) { var e = LegacyUnityAssets.ReadEntries(f).FirstOrDefault(x => x.PathId == pid); if (e != null) tx = LegacyUnityAssets.TryExtractText(f, e); }
                if (tx == null) return "Не TextAsset.";
                var s = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = sf + ".txt", DefaultExtension = "txt" }); if (s == null) return "Отменено.";
                await File.WriteAllTextAsync(s.Path.LocalPath, tx, Encoding.UTF8); return $"Сохранено: {Path.GetFileName(s.Path.LocalPath)}";
            }
            var sr = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = sf + ".bin", DefaultExtension = "bin" }); if (sr == null) return "Отменено.";
            try { var (inst, inf) = OpenAsset(am, f, pid); if (inst != null && inf != null) { using var fs = File.OpenRead(inst.path); fs.Position = inf.GetAbsoluteByteOffset(inst.file); var rw = new byte[inf.ByteSize]; fs.ReadExactly(rw); await File.WriteAllBytesAsync(sr.Path.LocalPath, rw); return $"Дамп: {Path.GetFileName(sr.Path.LocalPath)}"; } } catch { }
            var le = LegacyUnityAssets.ReadEntries(f).FirstOrDefault(x => x.PathId == pid);
            if (le != null) { await File.WriteAllBytesAsync(sr.Path.LocalPath, LegacyUnityAssets.ExtractRawBytes(f, le)); return $"Дамп: {Path.GetFileName(sr.Path.LocalPath)}"; }
            return "Не найдено.";
        }
        catch (Exception ex) { return $"Ошибка: {ex.Message}"; }
    }

    public static async Task<string> ReplaceAssetFromFile(Window w, AssetsManager am, string f, long pid, string tp)
    {
        try
        {
            var fs = await w.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Файл замены (.png, .txt, .bin)", AllowMultiple = false }); if (fs.Count == 0) return "Отменено.";
            string src = fs.First().Path.LocalPath; if (!File.Exists(f + ".bak")) File.Copy(f, f + ".bak");
            byte[]? bg = null; int pw = 0, ph = 0;
            if (src.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { using var bm = new Bitmap(src); pw = bm.PixelSize.Width; ph = bm.PixelSize.Height; bg = new byte[pw * ph * 4]; var h = GCHandle.Alloc(bg, GCHandleType.Pinned); try { bm.CopyPixels(new PixelRect(0, 0, pw, ph), h.AddrOfPinnedObject(), bg.Length, pw * 4); } finally { h.Free(); } }
            try
            {
                var inst = am.LoadAssetsFile(f, true); var inf = inst.file.GetAssetInfo(pid);
                if (inf != null)
                {
                    var bf = am.GetBaseField(inst, inf);
                    if ((AssetClassID)inf.TypeId == AssetClassID.Texture2D && bg != null) { var tx = TextureFile.ReadTextureFile(bf); tx.SetPictureData(bg, pw, ph); tx.WriteTo(bf); inf.SetNewData(bf); }
                    else if ((AssetClassID)inf.TypeId == AssetClassID.TextAsset) { bf.Get("m_Script").AsByteArray = await File.ReadAllBytesAsync(src); inf.SetNewData(bf); }
                    else inf.SetNewData(await File.ReadAllBytesAsync(src));
                    string tmp = f + ".tmp"; using (var wr = new AssetsFileWriter(tmp)) inst.file.Write(wr); am.UnloadAllAssetsFiles(); ClearCache(); File.Move(tmp, f, true);
                    return $"PathID={pid} заменён! Бэкап: .bak";
                }
            }
            catch { }
            var e = LegacyUnityAssets.ReadEntries(f).FirstOrDefault(x => x.PathId == pid);
            if (e != null)
            {
                if (e.ClassId == 28 && bg != null) LegacyUnityAssets.ReplaceTexture2DFromBgra(f, e, pw, ph, bg);
                else if (e.ClassId == 49 && (src.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || src.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) LegacyUnityAssets.ReplaceTextAsset(f, e, await File.ReadAllTextAsync(src, Encoding.UTF8));
                else LegacyUnityAssets.ReplaceAssetBytes(f, e.PathId, await File.ReadAllBytesAsync(src));
                return $"PathID={pid} заменён! Бэкап: .bak";
            }
            return "Не найдено.";
        }
        catch (Exception ex) { return $"Ошибка: {ex.Message}"; }
    }
    public static async Task<string> EditTextAssetDialog(Window ow, AssetsManager am, string f, long pid, string nm)
    {
        string? tx = null; bool lg = false; LegacyAssetEntry? le = null;
        try { var (inst, inf) = OpenAsset(am, f, pid); if (inst != null && inf != null && (AssetClassID)inf.TypeId == AssetClassID.TextAsset) tx = Encoding.UTF8.GetString(am.GetBaseField(inst, inf).Get("m_Script").AsByteArray); } catch { }
        if (tx == null) { le = LegacyUnityAssets.ReadEntries(f).FirstOrDefault(x => x.PathId == pid); if (le != null) { tx = LegacyUnityAssets.TryExtractText(f, le); lg = true; } }
        if (tx == null) return "Не TextAsset.";
        var bx = new TextBox { Text = tx, FontFamily = new FontFamily("Consolas"), FontSize = 13, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap };
        string st = "Без сохранения."; var sb = new Button { Content = "Сохранить", Padding = new Thickness(16, 6) }; var cb = new Button { Content = "Отмена", Padding = new Thickness(14, 6) };
        var wn = new Window { Title = $"Текст — {nm}", Width = 820, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        sb.Click += (_,_) => {
            try
            {
                if (!File.Exists(f + ".bak")) File.Copy(f, f + ".bak");
                if (!lg) { var inst = am.LoadAssetsFile(f, true); var inf = inst.file.GetAssetInfo(pid)!; var bf = am.GetBaseField(inst, inf); bf.Get("m_Script").AsByteArray = Encoding.UTF8.GetBytes(bx.Text ?? ""); inf.SetNewData(bf); string tmp = f + ".tmp"; using (var w = new AssetsFileWriter(tmp)) inst.file.Write(w); am.UnloadAllAssetsFiles(); ClearCache(); File.Move(tmp, f, true); }
                else if (le != null) LegacyUnityAssets.ReplaceTextAsset(f, le, bx.Text ?? "");
                st = $"«{nm}» сохранён! Бэкап: .bak"; wn.Close();
            }
            catch (Exception ex) { st = $"Ошибка: {ex.Message}"; }
        };
        cb.Click += (_,_) => wn.Close();
        var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(10) }; bt.Children.Add(sb); bt.Children.Add(cb);
        var dk = new DockPanel(); DockPanel.SetDock(bt, Dock.Bottom); dk.Children.Add(bt); dk.Children.Add(bx); wn.Content = dk; await wn.ShowDialog(ow); return st;
    }
}