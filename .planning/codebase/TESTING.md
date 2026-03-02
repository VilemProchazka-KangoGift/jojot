# Testing Patterns

**Analysis Date:** 2026-03-02

## Test Framework

**Status:** Not configured

**Currently:** No test framework is configured in the project. The solution contains only the main WPF application project (`JoJot.csproj`).

**Project Configuration:**
- Single project structure: `JoJot/JoJot.csproj`
- Solution file: `JoJot/JoJot.slnx` (new .slnx format)
- No separate test projects found

**Recommended Setup (not yet implemented):**
- **Test Framework:** xUnit or NUnit (industry standard for C# .NET 10)
- **Mocking Library:** Moq or NSubstitute
- **Test Runner:** Built-in dotnet test command

## Project Structure for Testing

**Current Layout:**
```
JoJot/
├── JoJot.csproj           # Main WPF application project
├── JoJot.slnx             # Solution file
├── App.xaml               # Application root
├── App.xaml.cs            # Application code-behind
├── MainWindow.xaml        # Main window UI
├── MainWindow.xaml.cs     # Main window code-behind
├── AssemblyInfo.cs        # Assembly metadata
└── obj/                   # Build output (not committed)
```

**Recommended Test Project Structure:**
```
JoJot.Tests/
├── JoJot.Tests.csproj     # Test project (to be created)
├── [ComponentName]Tests.cs # Unit tests for components
├── Fixtures/              # Test data and builders
└── Mocks/                 # Mock implementations
```

## Where to Add Tests

**UI Component Tests:**
- Location: `JoJot.Tests/UITests/`
- Pattern: One test class per XAML code-behind file
- Example: `MainWindowTests.cs` for `MainWindow.xaml.cs`

**Service/Logic Tests:**
- Location: `JoJot.Tests/[ServiceCategory]/`
- Pattern: One test class per service or utility class

**Fixtures and Test Data:**
- Location: `JoJot.Tests/Fixtures/`
- Builder pattern for complex test objects

## Build and Test Commands

**Build:**
```bash
dotnet build JoJot/JoJot.slnx
```

**Run (Application):**
```bash
dotnet run --project JoJot/JoJot.csproj
```

**Test Commands (when test project is added):**
```bash
dotnet test                           # Run all tests
dotnet test --logger console         # Run with console output
dotnet test --collect:"XPlat Code Coverage"  # Generate coverage report
dotnet test JoJot.Tests.csproj --watch  # Watch mode (when available)
```

**Coverage:**
```bash
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura
```

## Test File Organization

**Naming Pattern (recommended):**
- Test files: `[ComponentName]Tests.cs` or `[ComponentName]Test.cs`
- Located adjacent to source file or in parallel test structure
- Example: Tests for `MainWindow.xaml.cs` → `MainWindowTests.cs`

**File Location:**
- Option 1 (Parallel): `JoJot.Tests/MainWindowTests.cs`
- Option 2 (Co-located): Not recommended for WPF projects; keep tests separate

## Test Structure

**Recommended Pattern (not yet implemented):**

```csharp
using Xunit;
using JoJot;

namespace JoJot.Tests
{
    public class MainWindowTests : IDisposable
    {
        private MainWindow _window;

        public MainWindowTests()
        {
            // Arrange: Create instance for testing
            _window = new MainWindow();
        }

        public void Dispose()
        {
            _window?.Close();
        }

        [Fact]
        public void Constructor_InitializesComponent_Successfully()
        {
            // Act
            var mainWindow = new MainWindow();

            // Assert
            Assert.NotNull(mainWindow);
        }

        [Theory]
        [InlineData("Test Title")]
        public void Window_SetTitle_UpdatesTitle(string title)
        {
            // Arrange
            var window = new MainWindow();

            // Act
            window.Title = title;

            // Assert
            Assert.Equal(title, window.Title);
        }
    }
}
```

**Pattern Components:**
- **Arrange-Act-Assert (AAA):** Clear separation of test phases
- **One assertion per test method (when possible)**
- **[Fact] attribute:** For simple unit tests
- **[Theory] attribute:** For parameterized tests
- **IDisposable:** For proper cleanup of UI components

## Mocking

**Not Currently Used** (minimal external dependencies)

**When to Mock:**
- Database access (when data layer is added)
- External API calls
- File system operations
- Time-dependent operations

**Recommended Library:** Moq
```csharp
using Moq;

// Example (not implemented):
var mockService = new Mock<IDataService>();
mockService
    .Setup(s => s.GetData())
    .ReturnsAsync(new TestData());
```

## Test Types

**Unit Tests:**
- Scope: Test individual classes/methods in isolation
- Location: `JoJot.Tests/Unit/`
- Focus: Code-behind logic, business rules, utilities
- Example: `MainWindowInitializationTests.cs`

**Integration Tests:**
- Scope: Test multiple components working together
- Location: `JoJot.Tests/Integration/`
- Focus: XAML binding, event handling, data flow
- Status: Not yet planned

**UI Tests:**
- Scope: WPF UI automation
- Framework: UI Automation (built-in) or Coded UI Test Framework
- Location: `JoJot.Tests/UI/`
- Status: Not yet implemented

## Fixtures and Factories

**Test Data Pattern (recommended):**

```csharp
namespace JoJot.Tests.Fixtures
{
    public static class WindowFixtures
    {
        public static MainWindow CreateMainWindow()
        {
            return new MainWindow();
        }
    }

    public class WindowBuilder
    {
        private string _title = "Test Window";

        public WindowBuilder WithTitle(string title)
        {
            _title = title;
            return this;
        }

        public MainWindow Build()
        {
            var window = new MainWindow();
            window.Title = _title;
            return window;
        }
    }
}
```

**Usage in Tests:**
```csharp
[Fact]
public void Test_UsesFixture()
{
    // Using factory method
    var window = WindowFixtures.CreateMainWindow();

    // Or using builder
    var builtWindow = new WindowBuilder()
        .WithTitle("Custom Title")
        .Build();
}
```

## Coverage

**Requirements:** Not enforced (to be defined)

**Recommended Target:** 80% code coverage for business logic

**Generate Coverage Report:**
```bash
dotnet test /p:CollectCoverage=true \
  /p:CoverageFormat=opencover \
  /p:Threshold=80 \
  /p:ThresholdType=line
```

**View Coverage:**
```bash
# Using ReportGenerator (requires: dotnet tool install -g reportgenerator)
reportgenerator -reports:"**/coverage.opencover.xml" -targetdir:"coverage-report"
```

## CI/CD Integration

**GitHub Actions (recommended, not yet configured):**

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0'
      - run: dotnet restore
      - run: dotnet build
      - run: dotnet test --no-build --verbosity normal
```

## Dependencies Required for Testing

**To be added to test project when created:**

```xml
<!-- xUnit -->
<PackageReference Include="xunit" Version="2.6.x" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.5.x">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>

<!-- Moq for mocking -->
<PackageReference Include="Moq" Version="4.20.x" />

<!-- Code coverage -->
<PackageReference Include="coverlet.collector" Version="6.x">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

## Async Testing

**Pattern (when needed):**

```csharp
[Fact]
public async Task AsyncOperation_CompletesSuccessfully()
{
    // Arrange
    var window = new MainWindow();

    // Act
    await SomeAsyncOperation();

    // Assert
    Assert.True(condition);
}

[Theory]
[InlineData(1000)]
public async Task DataLoad_CompletesWithinTimeout(int timeoutMs)
{
    // Use Task.Delay for async scenarios
    var task = SomeAsyncMethod();
    var completedTask = await Task.WhenAny(task, Task.Delay(timeoutMs));
    Assert.Same(task, completedTask);
}
```

## WPF-Specific Testing Considerations

**UI Thread:**
- WPF tests must run on the UI thread
- Use `[STAThread]` for test methods if needed
- Consider using `Dispatcher.CurrentDispatcher` for thread management

**Data Binding:**
- Test binding expressions through dependency properties
- Verify value changes propagate correctly
- Example:
```csharp
[Fact]
public void Binding_UpdatesUI_WhenSourceChanges()
{
    // Arrange
    var window = new MainWindow();
    var originalValue = GetUIValue();

    // Act
    UpdateDataSource(newValue);
    window.DataContext = dataSource;

    // Assert
    Assert.Equal(newValue, GetUIValue());
}
```

**XAML Compilation:**
- XAML is compiled to code; verify InitializeComponent() runs correctly
- Test code-behind logic separately from XAML generation

---

*Testing analysis: 2026-03-02*
