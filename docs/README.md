# HyperTizen

HyperTizen is a Hyperion / HyperHDR capturer for Tizen TVs.

# Installation

To install HyperTizen, you need to have a Samsung TV (Tizen) that has at least Tizen 6.5 (2022+).

You'll need Tizen Studio to install the app on your TV. You can download it from the [official website](https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/installing-tv-sdk.html).

1. Download the latest release from the [releases page](https://github.com/reisxd/HyperTizen/releases/latest).

2. Change the Host PC IP address to your PC's IP address by following [this](https://developer.samsung.com/smarttv/develop/getting-started/using-sdk/tv-device.html#Connecting-the-TV-and-SDK)

3. Install the package:
```bash
tizen install -n path/to/io.gh.reisxd.HyperTizen.tpk
```

Note that `tizen` is in `C:\tizen-studio\tools\ide\bin` on Windows and in `~/tizen-studio/tools/ide/bin` on Linux.

If you get `install failed[118, -12], reason: Check certificate error` error, you'll have to resign the package.

4. Install TizenBrew to your TV. Follow [this](https://github.com/reisxd/TizenBrew/blob/main/docs/README.md) guide.

5. Add `reisxd/HyperTizen/HyperTizenUI` as a GitHub module to the module manager. You can access the module manager by pressing the [GREEN] button on the remote.

## Resigning the package

1. Change the Host PC IP address to your PC's IP address by following [this](https://developer.samsung.com/smarttv/develop/getting-started/using-sdk/tv-device.html#Connecting-the-TV-and-SDK)

2. After following the guide for the Tizen Studio installation, you have to create a certificate profile. You can follow [this guide](https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/creating-certificates.html).

3. Sign the package:
```bash
tizen package -t tpk -s YourProfileName -o path/to/output/dir -- path/to/io.gh.reisxd.HyperTizen.tpk

# Example:
# tizen package -t tpk -s HyperTizen -o release -- io.gh.reisxd.HyperTizen.tpk
```

4. You should now be able to install the package.

# Development

The solution has four projects:

- `HyperTizen.Core`: the service logic (control server, Hyperion client, capture loop, image encoding).
- `HyperTizen`: the TV service. It only runs on a TV.
- `HyperTizen.Desktop`: runs the same logic on your PC with simulated screen colors.
- `HyperTizen.Core.Tests`: tests for the core library.

## Running on your PC

Open `HyperTizen.sln` and press F5 with the **Desktop** profile selected; `HyperTizen.Desktop` is the default startup project. If Visual Studio remembers a different one from before, right-click `HyperTizen.Desktop` and choose **Set as Startup Project**. From a terminal:

```bash
dotnet run --project HyperTizen.Desktop
```

The control server listens on `ws://127.0.0.1:8086`, and the same address serves the TV UI: open `http://127.0.0.1:8086/` in a browser and use the arrow keys, Enter and Escape in place of the remote. Colors are a simulated rainbow; everything else, including the connection to Hyperion / HyperHDR, is real. Settings are stored in `%LOCALAPPDATA%\HyperTizen\settings.json`, or in the file named by the `HYPERTIZEN_SETTINGS` environment variable.

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
