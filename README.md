# ⚡ Spy (v13.0) — Unity Reverse Engineering & Asset Suite

[![License: GPL v3](https://shields.io)](LICENSE)
[![.NET 9.0](https://shields.io)](https://microsoft.com)
[![Avalonia UI](https://shields.io)](https://avaloniaui.net)

![Spy Preview](<img width="1280" height="675" alt="изображение" src="https://github.com/user-attachments/assets/34f8c9ae-5ef7-4d4d-9596-2fa53cbeedcd" />
)

**Spy** is a modern, lightweight desktop reverse-engineering, decompilation, and asset-editing utility for **Unity** games, built with **C# (.NET 9.0)** and **Avalonia UI**. It supports both **Mono** and **IL2CPP** titles (up to **metadata v31** / Unity 6).

---

## ✨ What's New in v13.0
- **Built-in IL2CPP Dumper (`AssetRipper.Cpp2IL.Core`)**:
  - Native dumping of `GameAssembly.dll` + `global-metadata.dat` directly inside the app with full support for modern metadata versions (**v24 – v31+**).
  - Generates **`DummyDll/*.dll`**, **`dump.cs`** (classes, fields with `FieldOffset`, methods with `RVA`), and **`script.json`** (for IDA Pro / Ghidra) with automatic loading straight into the class tree.
  - Automatic Unity engine version detection from `UnityPlayer.dll` and metadata headers.
- **Custom Frameless Window (Custom Window Chrome)**:
  - Sleek IDE-style borderless title bar with adaptive styling across all **8 built-in color themes** (Dracula, Nord, Cyberpunk, Light, etc.).
- **Live Bilingual UI (`RU` / `EN`)**:
  - Instant switching between Russian and English right from the top bar without restarting the application.
- **Persistent Workspace State (`AppState`)**:
  - Automatically remembers your selected theme, UI language, window size/maximized state, and last opened game (`spy_state.json`).
- **Interactive `Credits` Tab**:
  - Animated icy blue-and-white gradient paired with an interactive 3D floating wireframe cube background.

---

## 🛠 Core Capabilities
- **Mono & IL2CPP Code Inspector**: Browse assemblies, namespaces, types, fields, methods, and syntax-highlighted decompiled C# code (`ICSharpCode.Decompiler` + `AvaloniaEdit`).
- **IL Patcher (`DllPatcher`)**: Modify IL instructions and save patched `.dll` assemblies directly.
- **Unity Asset Viewer & Editor (`AssetsTools.NET`)**: Inspect `.assets`, `globalgamemanagers`, and asset bundles, edit properties, and export game resources.
- **Project Exporter**: Export full project structures and decompiled assemblies in one click.

---

## 🚀 Quick Start (Release v13.0)
1. Go to the **[Releases](https://github.com/deyxzazz/Spy/releases/latest)** page.
2. Download **`Spy-v13.0-win-x64.zip`**.
3. Extract the archive to any folder and run **`Spy.exe`**.

---

## 🧑‍💻 Building from Source
Requirements: **.NET 9.0 SDK** (Windows x64).

```bash
git clone https://github.com/deyxzazz/Spy
cd Spy
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./Release_v13.0
```

---

## 📄 License
Distributed under the **GNU General Public License v3.0 (GPL-3.0)**. See [`LICENSE`](LICENSE) for more information.

---

## 👤 Author
- **Developer:** [DEYXZAZZ](https://github.com/deyxzazz/Spy)
