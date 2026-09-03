# SecRandom → SecScore launcher plugin

A [SecRandom](https://github.com/SECTL/SecRandom) plugin that adds a button to SecRandom's **floating
window**. Clicking it **opens or focuses** the
[SecScore](https://github.com/SECTL/SecScore) main window:

- If SecScore is running, its window is raised to the foreground (works even when it is closed to the
  system tray). Focus uses the Win32 `AttachThreadInput` workaround so a `NoActivate` floating window
  can still bring SecScore forward.
- If SecScore is not running, the plugin launches the executable. The path is user-configurable
  (Settings → SecScore) and auto-detected as a fallback (running process, Program Files, local user
  installs, and a sibling `SecScore` folder).

Windows-focused; on other platforms the button is a logged no-op.

## Requirements

- .NET 10 SDK
- The SecRandom source checkout (the SDK is not published, so the plugin compiles against it from
  source). Our reference points at `D:\code\secrandom_src\SecRandom-master` via the
  `<SecRandomSourceDir>` property in `src\SecScoreLauncher\SecScoreLauncher.csproj`.

## Build

Because `SecRandom.PluginSdk` is not published to public feeds, the plugin compiles against the SDK
**from source** (and therefore also `SecRandom.Core` + Avalonia/FluentAvalonia, so the first build is
slow). Two prerequisites:

1. A .NET 10 SDK.
2. The SecRandom source checkout at the path in `<SecRandomSourceDir>` in
   `src/SecScoreLauncher/SecScoreLauncher.csproj` (default `..\..\..\secrandom_src\SecRandom-master`).

On a machine with an up-to-date Windows you can build directly:

```bash
dotnet build src/SecScoreLauncher/SecScoreLauncher.csproj -c Release
```

### ⚠️ Windows 10 CET workaround (this machine)

This machine's Windows 10 build ships `ntdll.dll` `10.0.19041.1566`, below the `10.0.19041.5007`
that .NET 10 requires for CET, and the CPU's CET shadow stacks are enabled. As a result the .NET SDK's
native compiler apphost (`csc.exe`) refuses to start with
`Your Windows doesn't fully support CET`. Running the compiler through the `dotnet` muxer
(`dotnet exec csc.dll`) works, so `build.ps1` points the MSBuild `Csc` task at a `csc.cmd` wrapper and
sets `DOTNET_EnableCETShadowStacks=0`:

```powershell
./build.ps1
```

Alternatively, fix the root cause on the OS: install the latest Windows Update (so `ntdll.dll` is
`>= 10.0.19041.5007`), or disable CET shadow stacks in firmware/registry and reboot — then the plain
`dotnet build` command above is enough.

Either way the installable package is produced at:

```
src/SecScoreLauncher/srpx/SecScoreLauncher.srpx
```

## Install into SecRandom

1. Copy `SecScoreLauncher.srpx` into the SecRandom app's package folder:
   - Installed app: `<SecRandom data folder>\cache\plugin-packages\`
   - (Dev/portable: `<SecRandom package root>\data\cache\plugin-packages\`)
2. Restart SecRandom.

The button is **not shown by default** — enable it in Settings → Personalized → Floating Window, and
switch the **SecScore** button on.

## Configure the SecScore path (optional)

Open Settings → **SecScore**. Set the `SecScore.exe` path, or click **自动检测** (auto-detect) and then
**保存** (save). Leave it empty to always auto-detect at launch.

## How it works

- `Plugin.cs` — entrance (`PluginBase`) that registers a hosted service and a settings page.
- `Services/SecScoreLauncherService.cs` — hosted service that registers the floating-window button
  (`FloatingWindowButtonDescriptor`) via `IFloatingWindowButtonRegistry`.
- `Services/SecScoreLauncherConfig.cs` — exe path persistence (`settings.json` under the plugin config
  folder) and auto-detect.
- `Win32/SecScoreWindowFocus.cs` — `FindWindowW("SecScore")` → `ForceForeground`, else launch via
  `Process.Start`.
- `Views/SettingsPages/SecScoreLauncherSettingsPage.cs` — path editor.
