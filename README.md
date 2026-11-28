# ⚡ Spy (v14.0) — Unity Reverse Engineering

<img width="1919" height="1013" alt="изображение" src="https://github.com/user-attachments/assets/108c2edb-3322-4809-a7c6-81c0c497b37d" />


[![License: GPL v3](https://shields.io/badge/License-GPLv3-blue)](LICENSE)
[![.NET 9.0](https://shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com)
[![Avalonia UI](https://shields.io/badge/Avalonia_UI-11+-8B44AC)](https://avaloniaui.net)

**Spy** is a modern desktop reverse-engineering and asset-editing utility for **Unity** (Mono & IL2CPP up to Unity 6) built with **C# (.NET 9.0)** and **Avalonia UI**, featuring a built-in IL2CPP dumper, code inspector, asset viewer, and IL patcher.

## 🚀 Quick Start
1. Download **Spy-v14.0.zip** from the **[Releases](https://github.com/deyxzazz/Spy/releases)** page.
2. Extract and run **Spy.exe**.

## 👨‍💻 Building from Source
Requires **[.NET 9.0 SDK](https://dotnet.microsoft.com/ru-ru/download/dotnet/9.0)**:
```bash
git clone https://github.com/deyxzazz/Spy/
cd Spy
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./Release_v14.0
```

## 📄 License & Author
- **License:** GNU General Public License v3.0 (GPL-3.0)
- **Developer:** [DEYXZAZZ](https://github.com/deyxzazz/)
