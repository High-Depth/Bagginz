using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Bagginz;

public sealed unsafe class Bagginz : IDalamudPlugin
{
    private readonly IContextMenu _contextMenu;
    private readonly IChatGui _chatGui;
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly ICommandManager _commandManager;

    private bool _isOperationPending;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_A = 0x41;
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public Bagginz(
        IContextMenu contextMenu,
        IChatGui chatGui,
        IGameGui gameGui,
        IFramework framework,
        ICommandManager commandManager)
    {
        _contextMenu = contextMenu;
        _chatGui = chatGui;
        _gameGui = gameGui;
        _framework = framework;
        _commandManager = commandManager;

        _contextMenu.OnMenuOpened += OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory)
            return;

        var target = args.Target as MenuTargetInventory;
        if (target?.TargetItem == null)
            return;

        // Always show "Deposit To Saddlebag" for main inventory items
        // The user right-clicked in their inventory, they want to deposit
        args.AddMenuItem(new MenuItem
        {
            Name = "Deposit To Saddlebag",
            OnClicked = menuArgs => ExecuteTransfer(deposit: true)
        });
    }

    private void ExecuteTransfer(bool deposit)
    {
        if (_isOperationPending)
            return;

        _isOperationPending = true;
        
        var action = deposit ? "Deposit" : "Withdraw";
        PrintDebug($"Bagginz: {action}...");

        Task.Run(() => DoTransfer(deposit));
    }

    private void DoTransfer(bool deposit)
    {
        try
        {
            var isSaddlebagOpen = IsSaddlebagOpen();
            
            // Open saddlebag if not already open
            if (!isSaddlebagOpen)
            {
                _commandManager.ProcessCommand("/saddlebag");
                System.Threading.Thread.Sleep(700);
            }

            // Wait for saddlebag UI to be ready
            System.Threading.Thread.Sleep(300);

            // Try auto-click first
            bool autoClicked = TryClickTransferOption(deposit);
            
            if (autoClicked)
            {
                PrintDebug("Bagginz: Auto-clicked transfer!");
            }
            else
            {
                // Fallback: use keyboard shortcut
                PrintDebug("Bagginz: Using keyboard shortcut...");
                PressKeyboardShortcut();
                System.Threading.Thread.Sleep(500);
            }

            // Close saddlebag if we opened it
            if (!isSaddlebagOpen)
            {
                _commandManager.ProcessCommand("/saddlebag");
            }

            PrintDebug("Bagginz: Done!");
        }
        catch (Exception ex)
        {
            PrintDebug($"Bagginz: Error - {ex.Message}");
        }
        finally
        {
            _isOperationPending = false;
        }
    }

    private bool TryClickTransferOption(bool deposit)
    {
        var contextAddon = _gameGui.GetAddonByName("ContextMenu", 1);
        if (contextAddon.IsNull)
            return false;

        var addon = (AtkUnitBase*)contextAddon.Address;
        if (addon == null || !addon->IsVisible)
            return false;

        var agent = GetInventoryContextAgent();
        if (agent == null)
            return false;

        int targetIndex = -1;
        var maxItems = Math.Min(agent->ContextItemCount, 64);
        
        for (int i = 0; i < maxItems; i++)
        {
            var param = agent->EventParams[agent->ContexItemStartIndex + i];
            if (param.Type != ValueType.String && param.Type != ValueType.ManagedString)
                continue;

            var text = ReadAtkValueString(param);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.Trim();

            // Search for Add All to Saddlebag
            if (trimmed.Contains("Add", StringComparison.OrdinalIgnoreCase) && 
                trimmed.Contains("Saddlebag", StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = i;
                break;
            }
            // Also check for just "Add All" as fallback
            if (trimmed == "Add All")
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
            return false;

        // Click it
        var values = stackalloc AtkValue[5];
        values[0].Type = ValueType.Int;
        values[0].Int = 0;
        values[1].Type = ValueType.Int;
        values[1].Int = targetIndex;
        values[2].Type = ValueType.UInt;
        values[2].UInt = 0;
        values[3].Type = ValueType.UInt;
        values[3].UInt = 0;
        values[4].Type = ValueType.UInt;
        values[4].UInt = 0;

        addon->FireCallback(2, values);
        
        return true;
    }

    private void PressKeyboardShortcut()
    {
        // Press 'A' key - in saddlebag, 'A' is the shortcut for "Add All"
        keybd_event(VK_A, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        System.Threading.Thread.Sleep(50);
        keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static unsafe AgentInventoryContext* GetInventoryContextAgent()
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
            return null;

        return (AgentInventoryContext*)agentModule->GetAgentByInternalId(AgentId.InventoryContext);
    }

    private bool IsSaddlebagOpen()
    {
        var buddy1 = _gameGui.GetAddonByName("InventoryBuddy", 1);
        var buddy2 = _gameGui.GetAddonByName("InventoryBuddy2", 1);
        return (!buddy1.IsNull && buddy1.IsVisible) || (!buddy2.IsNull && buddy2.IsVisible);
    }

    private static unsafe string ReadAtkValueString(AtkValue value)
    {
        try
        {
            if (value.Type == ValueType.String)
            {
                var ptr = value.String;
                if (ptr != null)
                    return ptr.ToString();
            }
        }
        catch { }
        return "";
    }

    private void PrintDebug(string message)
    {
        _chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Debug,
            Message = new SeString(new TextPayload(message))
        });
    }

    public void Dispose()
    {
        _contextMenu.OnMenuOpened -= OnMenuOpened;
    }
}