# Stack Research

**Domain:** WPF desktop notepad with virtual desktop integration
**Researched:** 2026-03-02
**Confidence:** MEDIUM — core stack verified via official sources; AOT claim in spec is a CRITICAL FLAG

---

## CRITICAL FLAG: PublishAot=true Is Not Supported for WPF

The project spec (`resources/08-startup.md`, `PROJECT.md`) states `PublishAot=true`. This is **not currently supported** by WPF.

**Evidence (HIGH confidence):**
- GitHub issue dotnet/wpf#11205 ("Make WPF Native AOT compatible") was opened Oct 2025 and is closed as a duplicate of #3811.
- dotnet/wpf#3811 ("WPF is not trim-compatible") has been open since 2020 and remains unresolved as of 2026-03-02.
- The official Native AOT docs list supported workloads as console apps, ASP.NET Core APIs, and MAUI — not WPF.
- WinForms received `PublishAot` experimental support but WPF has not.

**Implication for the roadmap:** The `< 200ms to first interactive window` startup target and the "Native AOT" requirement are in conflict. Use **ReadyToRun + framework-dependent deploy** as the achievable path. Treat AOT as a future milestone requiring WPF team work.

**What "AOT-safe" still means for this project:**
- Avoid runtime reflection in *application code* (still valuable for trimming correctness).
- Use `Microsoft.Data.Sqlite` (AOT-annotated since 8.0) directly via ADO.NET, not via EF Core.
- No `dynamic`, no `Assembly.LoadFile`, no `Expression.Compile` in application code.
- WPF's own XAML pipeline uses reflection internally; this is accepted/unavoidable.

---

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| WPF | .NET 10 built-in | UI framework | Non-negotiable per spec. Mature Windows desktop UI, DirectX-accelerated rendering, excellent XAML tooling. |
| .NET 10 (`net10.0-windows`) | 10.x (LTS) | Runtime | LTS release, supported through 2028. Latest WPF improvements. Already in .csproj. |
| C# 13 | Shipped with .NET 10 | Language | Primary language of the project. |
| Microsoft.Data.Sqlite | 10.0.3 | SQLite ADO.NET provider | AOT-annotated since 8.0 (verified via dotnet/efcore#29725 — closed completed). First-party Microsoft library, lightweight, no EF Core overhead. Exactly what the spec calls for. |
| SQLite | Bundled with Microsoft.Data.Sqlite | Embedded database | Single-file, WAL mode, zero network dependency. Already in spec. |

**Confidence: HIGH** — All core technologies are first-party Microsoft, current, and verified via NuGet.org and official docs.

### Deployment Strategy

| Approach | Property | Purpose | Why |
|----------|----------|---------|-----|
| ReadyToRun | `<PublishReadyToRun>true</PublishReadyToRun>` | Pre-compile IL to native before publish | Eliminates JIT cost at startup. Best achievable AOT-like approach for WPF. Replaces `PublishAot=true` which WPF does not support. |
| Self-Contained | `<SelfContainedDeployment>` optional | Bundle runtime | Optional: framework-dependent works if .NET 10 is pre-installed. Self-contained adds cold-start cost from larger binary. |
| Single File | `<PublishSingleFile>true</PublishSingleFile>` | Single EXE | Optional for distribution polish. Known regression in .NET 9+ for WPF (dotnet/wpf#10714) — test early. |

**Confidence: MEDIUM** — ReadyToRun is the established WPF startup optimization technique. Sub-200ms is ambitious for self-contained WPF; framework-dependent warm starts (second launch after OS has loaded CLR) are much faster. Cold-start for self-contained WPF is documented at 2-3 seconds even on high-end machines (dotnet/runtime#78379).

**Startup target reality check:** 200ms is achievable on warm starts (post-first-boot) with a framework-dependent deployment and ReadyToRun. It is NOT achievable for cold starts with self-contained. The spec's target should be validated against this reality.

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Grabacr07/VirtualDesktop (`VirtualDesktop` NuGet) | 5.0.5 | C# wrapper for IVirtualDesktopManager COM APIs | Virtual desktop detection, window moves, switch notifications. Targets net6+ windows10.0.19041. Does NOT advertise .NET 10 or AOT support — verify compatibility before adopting. |
| Slions.VirtualDesktop.WPF | 6.9.2 | WPF-specific extensions for virtual desktop (Window.MoveToDesktop, etc.) | Only if Grabacr07 lacks needed WPF helpers. Same .NET 9/10 caveat applies — listed frameworks end at net8.0-windows. |
| Hand-rolled COM interop (alternative) | N/A | Direct `IVirtualDesktopManager` + `IVirtualDesktopNotification` via `[ComImport]` | Preferred if third-party virtual desktop wrappers fail .NET 10 compat. The COM GUIDs changed in Windows 11 24H2; any wrapper must handle version detection. See MScholtes/VirtualDesktop for reference implementations across OS versions. |
| `System.IO.Pipes` (BCL) | Built-in | Named pipe IPC for single-instance | Already in .NET BCL. No dependency needed. Use `NamedPipeServerStream` / `NamedPipeClientStream`. |
| `System.Threading.Mutex` (BCL) | Built-in | Single-instance guard | Already in .NET BCL. Named global mutex (`Global\JoJot`) is the standard pattern. |
| P/Invoke `RegisterHotKey` / `UnregisterHotKey` | Win32 | Global hotkey (Win+Shift+N) | No .NET API exists for global hotkeys. P/Invoke via `WindowInteropHelper.Handle` and `HwndSource.AddHook` is the established WPF pattern. NHotkey library (thomaslevesque/NHotkey) wraps this cleanly if preferred. |

**Confidence on virtual desktop libraries: LOW** — Neither Grabacr07/VirtualDesktop nor Slions.VirtualDesktop explicitly lists .NET 10 support. Both depend on C#/WinRT. The COM GUIDs for undocumented virtual desktop interfaces change between Windows builds; a wrapper that worked on 22H2 may break on 24H2. Hand-rolling the COM interop with build-version dispatch (as MScholtes does) may be more reliable.

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| Visual Studio 2022 17.12+ | Primary IDE | WPF designer, XAML hot reload, .NET 10 support. Required for AOT publish toolchain (C++ desktop workload needed). |
| `dotnet` CLI | Build and publish | `dotnet build JoJot/JoJot.slnx`, `dotnet publish -r win-x64 -c Release` |
| DB Browser for SQLite | SQLite inspection during development | Free, useful for verifying WAL mode, schema, and data during testing. |
| Process Monitor / Process Explorer | Startup profiling | Essential for measuring actual startup time and identifying slow module loads. |
| WPF Snoop | WPF visual tree debugging | Useful for theming and layout work. |

---

## Installation

```xml
<!-- JoJot.csproj additions -->
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <UseWPF>true</UseWPF>

  <!-- Startup optimization: ReadyToRun replaces PublishAot (WPF is not AOT-compatible) -->
  <PublishReadyToRun>true</PublishReadyToRun>

  <!-- Trimming: do NOT enable TrimMode for WPF — it will produce 393+ warnings and break at runtime -->
  <!-- <PublishTrimmed>false</PublishTrimmed> -->
</PropertyGroup>

<ItemGroup>
  <!-- SQLite: AOT-safe since 8.0, version matches .NET 10 runtime cadence -->
  <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.3" />
</ItemGroup>
```

```bash
# Build
dotnet build JoJot/JoJot.slnx

# Run
dotnet run --project JoJot/JoJot.csproj

# Publish with ReadyToRun (startup optimization)
dotnet publish JoJot/JoJot.csproj -r win-x64 -c Release -p:PublishReadyToRun=true

# Verify Microsoft.Data.Sqlite version
dotnet list JoJot/JoJot.csproj package
```

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| `Microsoft.Data.Sqlite` (ADO.NET direct) | `Entity Framework Core + Sqlite` | If the data model grew complex enough to need LINQ queries and migrations tooling. For JoJot's 4-table schema with raw SQL, EF Core adds overhead and complicates AOT. Don't use it. |
| `Microsoft.Data.Sqlite` | `SQLite-net` (praeclarum) | If you want a micro-ORM. SQLite-net is lighter than EF but less AOT-tested than Microsoft.Data.Sqlite. Not necessary for this schema. |
| Hand-rolled COM interop for virtual desktop | `Slions.VirtualDesktop.WPF` or `VirtualDesktop` NuGet | If a third-party wrapper verifiably supports .NET 10 + Windows 11 24H2 without C#/WinRT runtime issues. Adopt a wrapper only after confirming it builds and runs on the target OS version. |
| `ReadyToRun` publish | `PublishAot=true` | Only if WPF gains Native AOT support (tracked in dotnet/wpf#3811). As of 2026-03-02, this is not available. |
| P/Invoke `RegisterHotKey` directly | `NHotkey` library | Use NHotkey if you want XAML-declared hotkeys or a cleaner abstraction. It's MIT, actively maintained, and wraps the same P/Invoke. Either choice is fine. |
| Named mutex + named pipe (BCL only) | `Microsoft.Extensions.Hosting` single-instance | Hosting adds unnecessary complexity for a single-binary desktop app. BCL primitives are simpler, faster to initialize, and have no additional dependencies. |
| WPF `ResourceDictionary` for theming | Third-party theme library (Mahapps, etc.) | The spec uses 10 color tokens with light/dark/system. A custom `ResourceDictionary` is sufficient. Third-party theme libraries add weight and may conflict with the minimalist design goal. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `PublishAot=true` in the `.csproj` | WPF is not trim-compatible. Setting this will produce build failures or a broken binary. The feature request is open but unresolved as of 2026-03-02. | `PublishReadyToRun=true` for startup optimization. |
| `<PublishTrimmed>true</PublishTrimmed>` | WPF produces 393+ trimmer warnings; trimming will remove types WPF needs at runtime, causing crashes in XAML parsing and style application. | Do not trim WPF apps. |
| `Entity Framework Core` | EF Core adds significant startup cost via model building and reflection. Its AOT story (pre-compiled queries, experimental) is complex and not needed for a 4-table schema. | Raw `SqliteConnection` / `SqliteCommand` via ADO.NET. |
| `System.Reflection.Emit` in application code | Incompatible with AOT intent; also breaks if trimming is ever enabled partially. | Static dispatch, source generators, or concrete type implementations. |
| Multiple `SqliteConnection` instances | The spec explicitly prohibits this. WAL mode allows concurrent reads but only one writer. Multiple connections create lock contention and risk data corruption. | One singleton `SqliteConnection` per process, all writes serialized (e.g., `SemaphoreSlim(1,1)` or `lock`). |
| WPF's built-in `TextBox` undo stack | The spec disallows it because WPF clears the undo stack when `Text` is set programmatically — which happens on every tab switch. The native undo cannot be transferred between tabs. | Custom in-memory undo/redo stack (two-tier: 50 fine-grained + 20 coarse checkpoints). |
| `Application.Current.Dispatcher.Invoke` for all writes | Synchronous dispatch from background threads to the UI thread adds latency. | `Dispatcher.InvokeAsync` (returns Task), or post work to a dedicated background thread with `SemaphoreSlim` for SQLite serialization. |

---

## Stack Patterns by Variant

**If targeting cold-start performance:**
- Use framework-dependent deployment (NOT self-contained); warm starts after first OS boot are sub-200ms.
- Enable `PublishReadyToRun=true`.
- Defer all non-critical initialization after the window is shown (background thread for migrations, desktop GUID resolution, etc.).
- The spec's startup sequence in `08-startup.md` already implements this correctly.

**If the virtual desktop COM wrapper breaks on a new Windows build:**
- Fall back to hand-rolling `[ComImport]` interfaces directly.
- Reference MScholtes/VirtualDesktop for the per-OS-build GUID dispatch pattern.
- The `IVirtualDesktopManager` public interface (documented on MSDN) is stable; the undocumented `IVirtualDesktopNotification` interfaces change GUIDs across builds.

**If global hotkey registration conflicts with another app:**
- `RegisterHotKey` returns `false` when the key combination is already taken.
- Handle this gracefully: log it, show a preferences reminder, and allow the user to choose a different combination.
- Do NOT crash or silently fail.

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| `Microsoft.Data.Sqlite` 10.0.3 | `net10.0-windows` | Matches .NET 10 runtime cadence. AOT-annotated since 8.0. |
| `VirtualDesktop` 5.0.5 (Grabacr07) | Requires net6.0-windows10.0.19041+ | .NET 10 SHOULD work via TFM compatibility, but this is unverified. Test early. Depends on C#/WinRT. |
| `Slions.VirtualDesktop.WPF` 6.9.2 | net6–net8 windows10.0.19041 | .NET 10 "computed" compatible but not explicitly tested. Same caveat as above. |
| WPF | `net10.0-windows` | Fully supported. LTS through 2028. |
| `PublishReadyToRun` | `net10.0-windows`, `win-x64` / `win-arm64` | Supported on all .NET 8+ self-contained and framework-dependent builds. |

---

## Sources

- [NuGet: Microsoft.Data.Sqlite 10.0.3](https://www.nuget.org/packages/microsoft.data.sqlite/) — latest stable version confirmed
- [dotnet/efcore#29725 — NativeAOT/trimming compat for Microsoft.Data.Sqlite](https://github.com/dotnet/efcore/issues/29725) — closed/completed in 8.0 milestone (HIGH confidence)
- [dotnet/wpf#11205 — Make WPF Native AOT compatible](https://github.com/dotnet/wpf/issues/11205) — closed as duplicate of #3811, unresolved (HIGH confidence)
- [dotnet/wpf#3811 — WPF is not trim-compatible](https://github.com/dotnet/wpf/issues/3811) — open, 393+ trimmer warnings (HIGH confidence)
- [Microsoft Docs: Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) — WPF not listed as supported workload (HIGH confidence)
- [dotnet/runtime#78379 — Self-contained WPF cold start 2-3s](https://github.com/dotnet/runtime/issues/78379) — cold start benchmark data (MEDIUM confidence)
- [Microsoft Docs: WPF Application Startup Time](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time) — startup optimization techniques (HIGH confidence)
- [NuGet: Slions.VirtualDesktop.WPF 6.9.2](https://www.nuget.org/packages/Slions.VirtualDesktop.WPF) — package metadata, framework support (MEDIUM confidence)
- [GitHub: Grabacr07/VirtualDesktop](https://github.com/Grabacr07/VirtualDesktop) — C# wrapper details (MEDIUM confidence)
- [GitHub: MScholtes/VirtualDesktop](https://github.com/MScholtes/VirtualDesktop) — per-OS-version COM GUID dispatch pattern (MEDIUM confidence)
- [Microsoft Docs: IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager) — public COM interface documentation (HIGH confidence)
- [NuGet: VirtualDesktop 5.0.5](https://www.nuget.org/packages/VirtualDesktop/) — package metadata (MEDIUM confidence)
- [NuGet: Microsoft.Data.Sqlite.Core 10.0.3](https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core/) — Core variant reference

---

*Stack research for: WPF desktop notepad + virtual desktop integration (JoJot)*
*Researched: 2026-03-02*
