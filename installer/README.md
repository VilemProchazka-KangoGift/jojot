# JoJot release packages

Use the .NET 11 SDK and the release version from `JoJot/JoJot.csproj`. Run these commands from the repository root.

## Windows

Publish with production data and shell identity enabled. The x64 build uses Native AOT. The ARM64 build can use the self-contained managed runtime when the ARM64 C++ toolchain is unavailable.

```powershell
dotnet publish experiments/JoJot.Avalonia -c Release -r win-x64 --self-contained -p:PublishAot=true -p:JoJotProduction=true -o .local/release/win-x64
dotnet publish experiments/JoJot.Avalonia -c Release -r win-arm64 --self-contained -p:PublishAot=false -p:JoJotProduction=true -o .local/release/win-arm64
iscc "/DPublishDir=$PWD/.local/release/win-x64" installer/jojot.iss
iscc "/DPublishDir=$PWD/.local/release/win-arm64" /DTargetArm64 installer/jojot.iss
```

Both installers share the Windows app identity and installation path. The ARM64 setup requires ARM64 Windows and writes a separate installer file.

## Linux and macOS

The Unix packages use the existing platform data paths documented in [Cross-platform support](../experiments/CROSS_PLATFORM.md). Install the Linux package on a Debian-family amd64 system with glibc 2.38 or newer using `sudo apt install ./JoJot-2026.11.8-linux-amd64.deb`. On a Mac, extract the archive matching the Mac's architecture and move JoJot.app to Applications. The macOS bundles were checked on the build host. Launch and Gatekeeper behavior still need validation on a Mac. macOS distribution signing and notarization require a Mac and Apple Developer credentials.

```bash
version=2026.11.8
dotnet publish experiments/JoJot.Avalonia -c Release -r linux-x64 --self-contained -p:PublishAot=true -o .local/release/linux-x64
bash installer/package-unix.sh "$version" linux-x64 .local/release/linux-x64 installer/output
for rid in osx-arm64 osx-x64; do
  dotnet publish experiments/JoJot.Avalonia -c Release -r "$rid" --self-contained -p:PublishAot=false -o ".local/release/$rid"
  bash installer/package-unix.sh "$version" "$rid" ".local/release/$rid" installer/output
done
```

Build Linux Native AOT on the oldest supported Linux base to avoid introducing newer glibc symbol requirements. The macOS archives bundle the architecture-specific .NET runtime. [Microsoft Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
