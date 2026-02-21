# Windows 11 Widgets Glossary

## General Windows 11 Widgets Terms

| Term | Definition |
|------|------------|
| **Widgets Board** | The slide-out panel (Win+W) that displays pinned widgets on Windows 11 |
| **Widget Provider** | An app/service that implements `IWidgetProvider` COM interface to supply widgets to the system |
| **Widget Definition** | A widget *type* declared in the manifest — what appears in the widget picker as an addable option |
| **Widget Instance** | A specific pinned widget on the board — created when user adds a definition, has a unique `widgetId` |
| **Adaptive Card** | JSON-based declarative UI format used for widget content rendering |
| **IWidgetProvider** | COM interface that the widget provider must implement (CreateWidget, DeleteWidget, OnAction, etc.) |
| **IWidgetProvider2** | Extended interface with customization support |
| **COM Out-of-Process Server** | The executable that hosts the widget provider, activated by Windows when widgets are needed |
| **MSIX Package** | The packaging format for deploying widget providers to Windows |
| **App Extension** | The `<uap3:AppExtension>` mechanism that registers a widget provider with the system |
| **CLSID** | COM Class GUID that must match between code attributes, constants, and manifest entries |
| **Widget Lifecycle** | States: Created → Active → Suspended → Deleted |
| **Customization Card** | Settings UI shown when user clicks gear icon on a widget |
| **Size** | Widget dimensions: small, medium, or large |

## This Project's Terms

| Term | Definition |
|------|------------|
| **LlmTokenWidget** | The overall project/solution name |
| **Claude Usage Widget** | Widget definition ID for Claude Code token tracking |
| **Zai Usage Widget** | Widget definition ID for Z.ai/GLM token tracking |
| **Copilot Usage Widget** | Widget definition ID for GitHub Copilot premium request tracking |
| **Alibaba Qwen Usage Widget** | Widget definition ID for Alibaba Qwen token tracking |
| **rebuild-deploy.ps1** | Script for full rebuild, process cleanup, and package registration |
| **5-Hour Rolling Window** | Claude Code Pro/Max plan token budget period |
| **Cooldown** | Time until token budget resets when over limit |
