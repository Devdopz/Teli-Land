# Teli-Land

Simple Windows overlay app built with C# and WPF.

It shows a small round `T-L` badge that stays on top of apps and webpages.

## Features

- Always-on-top floating overlay
- Draggable round badge
- Green badge with black `T-L` text
- Right-click to exit

## Run

```powershell
cd TeliLandOverlay
dotnet run
```

## Build

```powershell
cd TeliLandOverlay
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Installer

```powershell
.\build-installer.ps1
```
