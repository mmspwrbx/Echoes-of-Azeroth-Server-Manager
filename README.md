# Echoes of Azeroth Server Manager

An open-source Windows desktop manager for running and monitoring an AzerothCore 3.3.5a server stack. It provides one WPF interface for MySQL, `authserver.exe`, and `worldserver.exe`, without BAT files or shell-based service commands.

[![.NET 9](https://img.shields.io/badge/.NET-9.0-8D70E8)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-5A93E8)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/license-MIT-4ED6A0)](LICENSE)

## Features

- Start, stop, and restart the complete server stack or each component independently.
- Readiness-based startup for MySQL, AuthServer, and WorldServer.
- Graceful reverse-order shutdown with configurable timeouts.
- Live component state and port monitoring.
- Separate WorldServer, AuthServer, and manager log channels.
- Direct WorldServer console commands when the process is owned by the manager.
- Editable server paths, executable names, arguments, ports, and log location.
- English and Russian interface languages.

## Requirements

- Windows 10 or Windows 11
- An existing AzerothCore 3.3.5a server installation

The standalone published build includes the .NET runtime. The following development tools are required only when building from source:

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (the repository pins SDK 9.0.318)
- Visual Studio 2022 17.12 or newer with the **.NET desktop development** workload

## Build and run

```powershell
dotnet restore .\EchoesOfAzeroth.ServerManager.slnx
dotnet build .\EchoesOfAzeroth.ServerManager.slnx -c Release
.\EchoesOfAzeroth.ServerManager\bin\Release\net9.0-windows\EchoesOfAzeroth.ServerManager.exe
```

The executable requests administrator privileges because controlling Windows services requires elevation.

## Production publish

Generate the Windows x64 production release with:

```powershell
dotnet publish .\EchoesOfAzeroth.ServerManager\EchoesOfAzeroth.ServerManager.csproj -c Release /p:PublishProfile=WinX64
```

The reusable profile is stored at `EchoesOfAzeroth.ServerManager/Properties/PublishProfiles/WinX64.pubxml`. It creates a self-contained, single-file Windows x64 executable and deliberately leaves trimming and ReadyToRun disabled for predictable WPF compatibility.

Version `0.1.0` is published to:

```text
C:\Projects\Development\Echoes of Azeroth\release\ServerManager\0.1.0
```

To prepare a future release, change `VersionPrefix` once in `Directory.Build.props`.

### Application icon

Place the final multi-resolution Windows icon at:

```text
EchoesOfAzeroth.ServerManager\Assets\AppIcon.ico
```

The file must be named exactly `AppIcon.ico`. When present, it is used by the generated executable, Windows Explorer, the application window, and the taskbar. The repository intentionally does not include a fake placeholder icon.

## Download

[Download the latest finished build](https://github.com/mmspwrbx/Echoes-of-Azeroth-Server-Manager/releases/latest).

The download link will become available after the first packaged GitHub release is published.

## Runtime behavior

- Full startup waits for the MySQL service and TCP port, the AuthServer process and TCP port, and finally the `WORLD: World Initialized` console marker.
- Full shutdown runs in reverse order. WorldServer receives `server shutdown 0`; AuthServer receives a native Windows console-control event. Force termination is used only after the configured graceful timeout.
- Processes already launched from the configured server directory are detected and are not duplicated.
- Console input is available only for a WorldServer launched by this manager because Windows cannot attach a new standard-input pipe to an arbitrary existing process.
- Component state and ports are refreshed every two seconds.
- UI logs are bounded to 1,500 entries per channel. Manager logs are persisted under `%LOCALAPPDATA%\EchoesOfAzeroth\ServerManager\logs` by default.
- Settings are stored per user at `%LOCALAPPDATA%\EchoesOfAzeroth\ServerManager\settings.json`.
- Closing the application gracefully stops AuthServer and WorldServer instances owned by the manager. MySQL and externally launched server processes are left unchanged.

## Configuration

Executable names, command-line arguments, ports, timeouts, interface language, server root, MySQL service name, and log directory can be changed in the Settings tab.

The manager does not modify AzerothCore source code, configuration files, or databases.

## Open source

This project is open source. Anyone may use, study, fork, and modify it under the terms of the [MIT License](LICENSE). Contributions and improvements are welcome.

## License

Copyright © 2026 mmspwrbx. Distributed under the [MIT License](LICENSE).
