# Spy
<img width="1280" height="675" alt="изображение" src="https://github.com/user-attachments/assets/32cb9f6c-7d57-4538-90d9-5674a034f64d" />



**Spy** is a lightweight desktop reverse-engineering, decompilation, and asset-editing utility for **Unity** games, built with **.NET 8** and **Avalonia UI**.

It combines C# assembly decompilation/patching and Unity `.assets` inspection/replacement in a single cross-version interface supporting both legacy **Unity 3.x / 4.x** and modern **Unity 2023+**.

---

## Features

### 1. Assembly Decompiler & Live C# Editor
- **Tree Browser & Search:** Quickly filter namespaces, classes, and methods inside `Assembly-CSharp.dll` and other managed assemblies.
- **Syntax Highlighting & Search:** Built-in AvaloniaEdit viewer with line numbers, word wrap, C# language version selector (`C# 7.3` – `C# 12`), and `Ctrl+F` search.
- **Live Method & Class Patching:** Edit any method or entire class in C# and compile changes directly back into the game's `.dll` via Roslyn & Mono.Cecil, or stub methods in one click.
- **Code Export:** Export decompiled classes to `.cs` files.

### 2. Unity Asset Inspector & Editor
- **Universal Asset Support:** Reads standard Unity 5+ `.assets` / `mainData` / `globalgamemanagers.assets` via `AssetsTools.NET`, plus a built-in native parser for legacy **Unity 3.x / 4.x** asset archives.
- **Texture2D Extraction & Replacement:** Export textures directly to `.png` (including `RGBA32`, `ARGB32`, `RGB24`, `Alpha8`, `DXT1`, and `DXT5` formats) and replace textures in-place from `.png` files.
- **TextAsset & Raw Binary Editing:** Edit text assets in a built-in editor window, extract `.txt` / `.bin` dumps, or replace any asset from a file via the toolbar or right-click context menu.

### 3. Safety & Workflow
- **Automatic Backups:** Automatically creates `.bak` copies before modifying any game `.dll` or `.assets` file.
- **Themes & Session State:** Supports System, Dark, and Light themes and remembers your last opened project, assembly, and method.

---

## Getting Started

### Option 1: Download Prebuilt Binary (Windows x64)
1. Go to the [**Releases**](https://github.com/deyxzazz/Spy/) page.
2. Download **`Spy-win-x64.zip`**, extract it, and run **`Spy.exe`** (no .NET runtime installation required).

### Option 2: Build from Source
Requirements: **.NET 8.0 SDK**

```bash
git clone https://github.com/deyxzazz/Spy/
cd Spy
dotnet build -c Release
```

---

## Disclaimer

This tool is intended for educational purposes, security research, and personal modding of games you own. Always respect copyright laws and game terms of service.

## License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for more information.

lol
