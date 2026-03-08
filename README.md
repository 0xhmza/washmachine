# Washmachine

Shellcode loader builder with a WinUI 3 desktop interface.

## Features

- **Multiple shellcode sources** – load from file, raw hex, URL (web delivery), or built-in test payloads
- **Web payload wizard** – guided flow to configure encoding, envelope, and web helper via Bin2Shell, then verify the hosted payload URL
- **Template engine** – YAML-driven C++ code templates with configurable snippets (anti-debugging, guardrails, process injection, shellcode execution)
- **Native compilation** – auto-detects MSVC (`cl.exe`), GCC (`g++.exe`), or Clang (`clang++.exe`) and compiles the generated source

## Requirements

| Component | Version |
|---|---|
| OS | Windows 10 1809+ / Windows 11 |
| .NET | 8.0 Desktop Runtime (x64) |
| Windows App SDK | 1.8 Runtime |
| C++ Compiler | Any of: MSVC (via VS Build Tools), MinGW-w64 `g++`, or `clang++` on PATH |
| Python | 3.10+ (for Bin2Shell) |

## Building

```
dotnet build
```

## Publishing (framework-dependent)

```
dotnet publish -c Release
```

Output goes to `Output\Release\publish\`.  
The binary requires .NET 8 Desktop Runtime and Windows App SDK Runtime on the target machine — no bundled runtime, keeps the package small and compatible.

## Project layout

```
Controllers/     UI coordinators (MVVM-ish)
Logging/         Logger abstractions
Models/          Data models, compilation plans
Services/        Bin2Shell runner, compiler, catalog, clipboard
Views/           WinUI pages and wizard windows
Assets/          YAML template catalog
```

## License

See repository for license details.
