# Building and Deploying LLM Token Widget

## Phase 1 Implementation Status

✅ **Completed:**
- Solution structure created with 3 projects + packaging project
- COM widget provider boilerplate implemented
- Static "Hello Widget" Adaptive Card ready
- GUID synchronized across all 3 critical locations:
  - `WidgetProvider.cs` [Guid] attribute: `9F910C81-08A4-461F-93A6-96809C70A95D`
  - `Program.cs` CLSID_WidgetProvider: `9F910C81-08A4-461F-93A6-96809C70A95D`
  - `Package.appxmanifest` COM + widget registration: `9F910C81-08A4-461F-93A6-96809C70A95D`
- Two widget definitions registered:
  - `Claude_Usage_Widget` - Claude Code Usage
  - `LLM_Summary_Widget` - LLM Usage Summary
- Placeholder images created

## Building with Visual Studio 2022

### Prerequisites

1. **Developer Mode**: Ensure Windows Developer Mode is enabled
   - Settings → Privacy & Security → For developers → Developer Mode: ON

2. **Visual Studio Requirements**:
   - Visual Studio 2022 (you have VS Insiders installed)
   - "Windows application development" workload
   - .NET 8 SDK

### Build Steps

1. **Open Solution**:
   ```
   LlmTokenWidget.sln
   ```

2. **Verify Configuration**:
   - Configuration: **Debug**
   - Platform: **x64**
   - Startup Project: **LlmTokenWidget.Package**

3. **Restore NuGet Packages**:
   - Right-click solution → Restore NuGet Packages
   - Or: Build → Restore NuGet Packages

4. **Build the Solution**:
   - Build → Build Solution (Ctrl+Shift+B)
   - Expected result: All projects build successfully

5. **Deploy the Package**:
   - Right-click `LlmTokenWidget.Package` → Deploy
   - Or: Build → Deploy LlmTokenWidget.Package
   - This installs the MSIX package to your local machine

### Verification Steps

After successful deployment:

1. **Open Widgets Board**:
   - Press `Win+W` to open Windows 11 Widgets Board

2. **Add Widget**:
   - Click the "+" button in Widgets Board
   - Search for "Claude" or "LLM"
   - You should see:
     - **Claude Code Usage** (supports Small/Medium/Large sizes)
     - **LLM Usage Summary** (supports Medium/Large sizes)

3. **Add Widget to Board**:
   - Click on "Claude Code Usage" to add it
   - Widget should appear showing:
     - "Hello Widget!" header
     - "LLM Token Usage Widget - Phase 1 Scaffold"
     - Current size indicator

4. **Test Resize**:
   - Right-click widget → Resize
   - Try Small/Medium/Large sizes
   - Widget should update with new size

## Build Using MSBuild (Command Line Alternative)

If you prefer command line, you can use Visual Studio's MSBuild directly:

```powershell
# Set MSBuild path
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\amd64\MSBuild.exe"

# Restore packages
& $msbuild LlmTokenWidget.sln /t:Restore /p:Configuration=Debug /p:Platform=x64

# Build solution
& $msbuild LlmTokenWidget.sln /t:Build /p:Configuration=Debug /p:Platform=x64

# Deploy packaging project
& $msbuild packaging\LlmTokenWidget.Package\LlmTokenWidget.Package.wapproj /t:Deploy /p:Configuration=Debug /p:Platform=x64
```

## Troubleshooting

### Widget doesn't appear in picker

1. **Verify GUID consistency**:
   ```powershell
   # Search for the GUID in all files
   Select-String -Path .\**\*.cs,.\**\*.appxmanifest -Pattern "9F910C81-08A4-461F-93A6-96809C70A95D"
   ```
   Should find exactly 3 matches in: WidgetProvider.cs, Program.cs, Package.appxmanifest

2. **Check Event Viewer**:
   - Event Viewer → Windows Logs → Application
   - Look for errors from "WidgetService" or COM activation errors

3. **Verify Developer Mode**:
   - Settings → System → For developers → Developer Mode should be ON

4. **Redeploy**:
   - Uninstall previous version: Settings → Apps → Installed apps → "LLM Token Widget" → Uninstall
   - Clean solution: Build → Clean Solution
   - Rebuild and redeploy

### Build fails with "DesktopBridge" errors

- This is expected with `dotnet build` command
- Use Visual Studio or the MSBuild command above instead
- Windows App SDK with MSIX packaging requires Visual Studio's build tools

### Deploy fails with certificate errors

- Debug mode uses temporary test certificates
- Right-click `Package.appxmanifest` → Open With → XML Editor
- Verify `Publisher="CN=TestCert"` matches your test certificate
- Or create a new test certificate: Right-click Package project → Properties → Signing → Create Test Certificate

## Project Structure

```
LlmTokenWidget.sln
├── src/
│   ├── LlmTokenWidget.Core/           # Interfaces and models (Phase 2)
│   ├── LlmTokenWidget.Providers/      # Provider implementations (Phase 2)
│   └── LlmTokenWidget.App/            # COM widget provider
│       ├── Program.cs                 # COM server entry point
│       ├── FactoryHelper.cs           # COM class factory
│       └── WidgetProvider.cs          # IWidgetProvider implementation
└── packaging/
    └── LlmTokenWidget.Package/        # MSIX packaging
        ├── Package.appxmanifest       # COM + widget registration
        └── Images/                    # Widget assets
```

## Next Steps

Once Phase 1 verification is complete (widget appears and displays "Hello Widget!"), proceed to:

- **Phase 2**: Implement Claude Code local data provider
  - Create `JsonlParser` to read `~/.claude/projects/**/*.jsonl`
  - Implement `CooldownEstimator` for rolling 5-hour window calculation
  - Replace static card with live token usage data

## Files Requiring GUID Synchronization (CRITICAL)

These three locations MUST have the same GUID: `9F910C81-08A4-461F-93A6-96809C70A95D`

1. `src\LlmTokenWidget.App\WidgetProvider.cs` - Line 9:
   ```csharp
   [Guid("9F910C81-08A4-461F-93A6-96809C70A95D")]
   ```

2. `src\LlmTokenWidget.App\Program.cs` - Line 10:
   ```csharp
   private static readonly Guid CLSID_WidgetProvider = new("9F910C81-08A4-461F-93A6-96809C70A95D");
   ```

3. `packaging\LlmTokenWidget.Package\Package.appxmanifest` - Two locations:
   ```xml
   <com:Class Id="9F910C81-08A4-461F-93A6-96809C70A95D" ... />
   <CreateInstance ClassId="9F910C81-08A4-461F-93A6-96809C70A95D" />
   ```

If you change the GUID, you MUST update all 4 occurrences or the widget will fail to activate.
