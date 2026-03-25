# TeliLand Overlay

Small Windows overlay app built with C# and WPF.

When you open the app, it shows a floating round `T-L` badge that stays above normal apps and webpages.

## Features

- Round green badge with black `T-L` text
- Always-on-top overlay window
- Drag and drop to any screen position
- Right-click the badge to exit
- Borderless transparent popup-style window

## Run in Visual Studio

Open [TeliLandOverlay.csproj](c:\Users\muham\Desktop\teli-land\TeliLandOverlay\TeliLandOverlay.csproj) and run it.

## Run from terminal

```powershell
cd TeliLandOverlay
dotnet run
```

## Publish for Windows

```powershell
cd TeliLandOverlay
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Published files will be created inside:

`TeliLandOverlay\bin\Release\net10.0-windows\win-x64\publish\`

You can create a shortcut to the published `.exe` and use that like an installed app.

## Build installer EXE

This project also includes an Inno Setup installer script that creates a shareable `setup.exe`.

```powershell
.\build-installer.ps1
```

Installer output will be created inside:

`dist\TeliLandOverlaySetup.exe`

The installer:

- Installs the app into the current user's profile
- Creates Start Menu shortcut
- Optionally creates Desktop shortcut
- Registers uninstall entry in Windows
