# HyperTizen

HyperTizen is a Hyperion / HyperHDR capturer for Tizen TVs.

# Installation

To install HyperTizen, you need to have a Samsung TV (Tizen) that has at least Tizen 6.5 (2022+).

You'll need Tizen Studio to install the app on your TV. You can download it from the [official website](https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/installing-tv-sdk.html).

A Samsung TV only accepts packages signed with a Samsung certificate that lists that TV. So the files in a release cannot be installed as they are: you sign them with your own certificate first.

1. Download `io.gh.reisxd.HyperTizen-<version>.tpk` from the [releases page](https://github.com/stanislavnastasiu-dev/HyperTizen/releases/latest).

2. Change the Host PC IP address to your PC's IP address by following [this](https://developer.samsung.com/smarttv/develop/getting-started/using-sdk/tv-device.html#Connecting-the-TV-and-SDK)

3. Create a certificate profile that includes your TV. You can follow [this guide](https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/creating-certificates.html).

4. Sign the package with your profile (see [Resigning the package](#resigning-the-package)).

5. Install the signed package, the one the previous step wrote to its output folder, not the download:
```bash
tizen install -n path/to/output/dir/io.gh.reisxd.HyperTizen-<version>.tpk
```

Note that `tizen` is in `C:\tizen-studio\tools\ide\bin` on Windows and in `~/tizen-studio/tools/ide/bin` on Linux.

If you get `install failed[118, -12], reason: Check certificate error` error, the package is not signed with a certificate that lists your TV.

6. Add the UI, in one of two ways:

   - **As a TizenBrew module.** Install TizenBrew to your TV by following [this](https://github.com/reisxd/TizenBrew/blob/main/docs/README.md) guide, then add `stanislavnastasiu-dev/HyperTizen/HyperTizenUI` as a GitHub module to the module manager. You can access the module manager by pressing the [GREEN] button on the remote. The module is taken from the newest version tag of this repository, so it matches the latest release.
   - **As a TV app.** Download `HyperTizenUI-<version>.wgt` from the same release, then sign and install it like the service (see below).

The release of the original project, [reisxd/HyperTizen](https://github.com/reisxd/HyperTizen/releases), does not include what this fork adds (the setup screens, Preview and Settings). The same goes for the HyperTizen entry in [Tizen Community Packages](https://github.com/Apps2Samsung/tizen-community-packages), a community catalogue of TV apps: it is the original project's release.

## Resigning the package

Sign the service package:
```bash
tizen package -t tpk -s YourProfileName -o path/to/output/dir -- path/to/io.gh.reisxd.HyperTizen-<version>.tpk

# Example:
# tizen package -t tpk -s HyperTizen -o release -- io.gh.reisxd.HyperTizen-1.1.0.tpk
```

Sign and install the UI package, if you are not using the TizenBrew module:
```bash
tizen package -t wgt -s YourProfileName -o path/to/output/dir -- path/to/HyperTizenUI-<version>.wgt
tizen install -n path/to/output/dir/HyperTizenUI-<version>.wgt
```

## Upgrading from 1.1.0 or the original project

Up to 1.1.0 the colors along the bottom edge and the left edge were sent in reverse order. From 1.2.0 every color is sent where it is measured. If you reversed those LEDs in your Hyperion or HyperHDR layout to make up for it, undo that.

The service now measures 14 places by default instead of 16 (the two in the middle of the screen never reached the LEDs). You can change the number of zones per edge under Settings, Capture zones.

# Development

There are four projects:

- `HyperTizen.Core`: the service logic (control server, Hyperion client, capture loop, image encoding).
- `HyperTizen`: the TV service. It only runs on a TV.
- `HyperTizen.Desktop`: runs the same logic on your PC with simulated screen colors.
- `HyperTizen.Core.Tests`: tests for the core library.

and two solutions:

- `HyperTizen.sln`: only what goes on the TV (`HyperTizen` and `HyperTizen.Core`). The Tizen extension for VS Code builds the first solution it finds in the folder, with its own .NET SDK, which is older than the one the desktop host and the tests need. Keep this solution free of them, and keep it first by name.
- `HyperTizenDesktop.sln`: all four projects, for working on your PC.

## Running on your PC

Open `HyperTizenDesktop.sln` and press F5 with the **Desktop** profile selected; `HyperTizen.Desktop` is the default startup project. If Visual Studio remembers a different one from before, right-click `HyperTizen.Desktop` and choose **Set as Startup Project**. From a terminal:

```bash
dotnet run --project HyperTizen.Desktop
```

The control server listens on `ws://127.0.0.1:8086`, and the same address serves the TV UI: open `http://127.0.0.1:8086/` in a browser and use the arrow keys, Enter and Escape in place of the remote. Colors are a simulated rainbow; everything else, including the connection to Hyperion / HyperHDR, is real. Settings are stored in `%LOCALAPPDATA%\HyperTizen\settings.json`, or in the file named by the `HYPERTIZEN_SETTINGS` environment variable.

To watch the real TV from your PC instead (for example the live Preview while the TV shows your content), add the TV's address to the page address: `http://127.0.0.1:8086/?service=192.168.1.50`. The page then talks only to the service on that TV, and Home shows which one it is.

Run the tests with:

```bash
dotnet test HyperTizen.Core.Tests
```

## Running on the TV

1. Install Tizen Studio or the Tizen extension for VS Code, and create a Samsung certificate profile (see [Resigning the package](#resigning-the-package)).
2. Put the TV in developer mode with your PC's IP address.
3. Copy `tv.local.example.json` to `tv.local.json` and set `tvIp` and `signingProfile` (and `tizenStudioPath` if Tizen Studio is installed somewhere other than `C:\tizen-studio`). The script uses Tizen Studio's `tizen` CLI when it finds it, otherwise the extension's `tz` CLI.
4. Select the **TV** profile and press F5, or run:

```bash
powershell -File scripts/deploy-tv.ps1
```

The script builds, signs, installs and starts the service, checks that port 8086 answers, then shows the TV log.

### The UI on the TV

End users add the UI as a TizenBrew module (see Installation). While developing you can install it directly as a TV app:

```bash
powershell -File scripts/deploy-ui.ps1
```

The script packages `HyperTizenUI`, signs it with your certificate profile, installs it and opens it on the TV. Run `scripts/deploy-tv.ps1` first so the service on the TV understands the UI.

To check the UI on your PC without a TV, run the browser smoke test (Edge or Chrome required, port 8086 free):

```bash
dotnet build HyperTizen.Desktop
node scripts/ui-smoke.js
node --test "HyperTizenUI/tests/*.test.js"
```
