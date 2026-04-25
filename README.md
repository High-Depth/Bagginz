# Bagginz

A Dalamud plugin for FFXIV that adds quick deposit/withdraw options to the chocobo saddlebag via right-click context menu.

## Features

- **Right-click on inventory items** → Shows "Deposit To Saddlebag" menu item
- **Right-click on saddlebag items** → Shows "Withdraw to Inventory" menu item
- Automatically opens saddlebag, performs the action, and closes it

## Installation

### Method 1: Custom Repository (Recommended for development)

1. Open Dalamud settings in FFXIV
2. Go to "Experimental" tab
3. Add this URL as a custom repository:
   ```
   https://raw.githubusercontent.com/High-Depth/Bagginz/main/pluginmaster.json
   ```
4. Search for "Bagginz" in the plugin installer and install it

### Method 2: Manual Installation

1. Build the plugin: `dotnet build`
2. Copy `bin/Debug/Bagginz.dll` and `bin/Debug/Bagginz.json` to your plugins folder

## Development

### Requirements
- .NET 10.0 SDK
- Dalamud.NET.Sdk (included via XLDeploy)

### Building
```bash
cd Bagginz
dotnet build
```

The DLL will be output to `bin/Debug/Bagginz.dll`

## Technical Notes

- Uses `/saddlebag` chat command to toggle saddlebag
- Monitors context menu for "Add All to Saddlebag" / "Remove All from Saddlebag" actions
- Auto-selects the appropriate action and closes the context menu
- Operation completes within ~1 second

## Building the Custom Repo

The `pluginmaster.json` file is automatically generated during build and points to the GitHub releases.

To update the plugin in-game:
1. Make code changes
2. Build: `dotnet build -c Release`
3. Create a GitHub release with the DLL and JSON files
4. Update the pluginmaster.json to point to the new version