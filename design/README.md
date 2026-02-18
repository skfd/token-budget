# Adaptive Card Designer Sample Data

This folder contains sample data files for each Adaptive Card template in
`src/LlmTokenWidget.App/AdaptiveCards/`. Use these when designing or previewing
cards in the [Adaptive Cards Designer](https://adaptivecards.io/designer/).

## How to use

1. Open the designer at https://adaptivecards.io/designer/
2. Paste the card template JSON (e.g. `src/LlmTokenWidget.App/AdaptiveCards/claude-large.json`) into the **Card Payload Editor**.
3. Paste the matching sample data JSON (e.g. `design/claude/large.json`) into the **Sample Data Editor**.
4. The designer will substitute the `${…}` bindings with the sample values.

## Folder structure

```
design/
├── claude/          # Claude Code widget (claude-small/medium/large.json)
│   ├── small.json
│   ├── medium.json
│   └── large.json
├── zai/             # Z.ai / opencode widget (zai-small/medium/large.json)
│   ├── small.json
│   ├── medium.json
│   └── large.json
├── copilot/         # GitHub Copilot widget (copilot-small/medium/large.json)
│   ├── small.json
│   ├── medium.json
│   └── large.json
└── qwen/            # Qwen Code widget (qwen-small/medium/large.json)
    ├── small.json
    ├── medium.json
    └── large.json
```

## Maintenance requirement ⚠️

**These sample data files must be kept in sync with the card templates and the
data-building code in `WidgetProvider.cs`.**

When you:
- **Add a new template variable** (`${foo}`) to a card JSON → add `"foo"` to the
  matching sample data file(s).
- **Remove or rename a template variable** → update or remove it from the
  matching sample data file(s).
- **Add a new card size or provider** → create a new sample data file in the
  appropriate subfolder.
- **Change what a variable contains** (e.g. format change) → update the sample
  value so it still reflects realistic output.

The pixel data-URI values for `barFillUrl`, `trackUrl`, etc. come from the
constants in `WidgetProvider.cs` (`DarkGreenPx`, `DarkTrackPx`, …). The samples
here use the dark-theme variants so they render clearly in the designer's default
dark preview.
