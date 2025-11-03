# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 🚨 IMPORTANT: Environment Information
**Current Working Environment:**
- **Operating System**: Windows 10/11
- **Shell**: PowerShell/CMD (NOT bash/linux commands)
- **Path Separator**: Backslash `\` (NOT forward slash `/`)
- **File Paths**: Use Windows format (e.g., `D:\UGit\Avalonia.Samples\`)
- **Commands**: Use Windows-compatible commands (PowerShell preferred)

**Command Reminders:**
- Use `dir` instead of `ls` for directory listing
- Use `type` instead of `cat` for file reading
- Use `copy` instead of `cp` for file copying
- Use `del` instead of `rm` for file deletion
- Use `move` instead of `mv` for file moving
- Use PowerShell syntax when possible

**Always remember you are working on WINDOWS!**

## Project Overview

This is the **Avalonia.Samples** repository - a comprehensive collection of minimal samples demonstrating Avalonia UI framework capabilities. Each sample focuses on specific aspects of Avalonia development, organized by category and difficulty level.

## Solution Structure

The main solution file is `Avalonia.Samples.sln` which contains all sample projects organized into solution folders:

- **MVVM**: Model-View-ViewModel pattern samples (BasicMvvmSample, CommandSample, ValueConversionSample, ValidationSample)
- **DataTemplates**: Data template implementation samples (BasicDataTemplateSample, FuncDataTemplateSample, IDataTemplateSample)
- **Routing**: Navigation and routing samples (BasicViewLocatorSample)
- **CustomControls**: Custom control development samples (RatingControlSample, SnowflakesControlSample)
- **Drawing**: Graphics and drawing samples (BattleCity, RectPainter)
- **ViewInteraction**: UI interaction samples (MvvmDialogSample, DialogManagerSample)
- **Testing**: Automated UI testing samples (TestableApp.Headless.NUnit, TestableApp.Headless.XUnit, TestableApp.Appium)
- **CompleteApps**: Full application examples (SimpleToDoList, Avalonia.MusicStore)
- **GameDevToolkit**: Game development toolkit sample

## Common Development Commands

### Building
```powershell
# Build entire solution
dotnet build Avalonia.Samples.sln

# Build specific project (use backslashes for Windows paths)
dotnet build MVVM\BasicMvvmSample\BasicMvvmSample.csproj

# Build in release mode
dotnet build Avalonia.Samples.sln -c Release
```

### Running Samples
```powershell
# Run a specific sample
dotnet run --project MVVM\BasicMvvmSample\BasicMvvmSample.csproj

# Run complete app
dotnet run --project CompleteApps\Avalonia.MusicStore\Avalonia.MusicStore.csproj
```

### Testing
```powershell
# Run all tests
dotnet test

# Run specific test project
dotnet test Testing\TestableApp.Headless.NUnit\TestableApp.Headless.NUnit.csproj

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Architecture Patterns

### MVVM Implementation
- Most samples use ReactiveUI for MVVM pattern
- Some samples use CommunityToolkit.Mvvm (notably in CompleteApps)
- ViewLocator pattern is demonstrated in Routing samples for automatic View-ViewModel mapping

### Sample Structure
Each sample follows a consistent structure:
- `Program.cs`: Application entry point
- `App.axaml`: Application-level resources and configuration
- `Views/`: XAML view files
- `ViewModels/`: MVVM view models
- `Models/`: Data models (if needed)

### Target Frameworks
- Most samples target .NET 9.0
- Testing projects target .NET 8.0
- All projects use nullable reference types enabled

## Key Technologies

- **Avalonia UI 11.3.6**: Cross-platform UI framework
- **ReactiveUI**: Reactive MVVM framework
- **CommunityToolkit.Mvvm**: Modern MVVM implementation (in some samples)
- **NUnit/XUnit**: Testing frameworks
- **Avalonia.Headless**: Headless testing support

## Development Guidelines

### Adding New Samples
1. Create new project in appropriate solution folder
2. Add README file describing the sample
3. Update main README.adoc with sample information
4. Include difficulty level and buzz words
5. Follow existing naming conventions

### Sample Categories and Difficulties
- 🐣 **Beginner**: No prior Avalonia knowledge required
- 🐥 **Easy**: Basic Avalonia knowledge needed
- 🐔 **Normal**: Experienced with Avalonia
- 🐉 **Hard**: Advanced/non-standard scenarios

### Common Dependencies
- `Avalonia.Desktop`: Desktop platform support
- `Avalonia.Themes.Fluent`: Fluent theme
- `Avalonia.Fonts.Inter`: Inter font family
- `Avalonia.Diagnostics`: Debug tools (debug only)
- `Avalonia.ReactiveUI`: ReactiveUI integration

## Testing Strategy

The repository includes three testing approaches:
1. **Headless NUnit**: Automated testing without UI
2. **Headless XUnit**: Alternative testing framework
3. **Appium**: Cross-platform UI automation (advanced)

Testing projects reference the main TestableApp project and demonstrate UI testing patterns for Avalonia applications.