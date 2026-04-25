# Bagginz

A Dalamud plugin for FFXIV that adds quick deposit/withdraw options to the chocobo saddlebag via right-click context menu.

## Features

- **Right-click on inventory items** → Shows "Deposit To Saddlebag" menu item
- **Right-click on saddlebag items** → Shows "Withdraw to Inventory" menu item
- Automatically opens saddlebag, performs the action, and closes it

## Installation

1. Build the plugin (requires .NET SDK and Dalamud dev environment)
2. Copy the output to yourDalamud plugins folder
3. Enable the plugin in-game

## Development

### Requirements
- .NET 10.0 SDK
- Dalamud.NET.Sdk
- FFXIVClientStructs

### Building
```bash
cd Bagginz
dotnet build
```

## Usage

1. Open your inventory (press I)
2. Right-click any item
3. Select "Deposit To Saddlebag"
4. The plugin will:
   - Open the saddlebag (via `/saddlebag` command)
   - Deposit the item
   - Close the saddlebag

When viewing the saddlebag inventory:
1. Right-click any item
2. Select "Withdraw to Inventory"
3. The plugin will withdraw the item to your main inventory

## Technical Notes

- Uses `/saddlebag` chat command to toggle saddlebag
- Monitors context menu for "Add All to Saddlebag" / "Remove All from Saddlebag" actions
- Auto-selects the appropriate action and closes the context menu
- Operation completes within ~1 second