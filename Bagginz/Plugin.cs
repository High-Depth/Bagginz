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
    private bool _depositOperation;

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

        var isSaddlebag = IsSaddlebagOpen();

        args.AddMenuItem(new MenuItem
        {
            Name = isSaddlebag ? "Withdraw to Inventory" : "Deposit To Saddlebag",
            OnClicked = menuArgs => ExecuteTransfer(deposit: !isSaddlebag)
        });
    }

    private void ExecuteTransfer(bool deposit)
    {
        if (_isOperationPending)
            return;

        _isOperationPending = true;
        _depositOperation = deposit;
        
        var action = deposit ? "Deposit" : "Withdraw";
        PrintDebug($"Bagginz: {action} starting...");
        PrintDebug("Bagginz: Opening saddlebag...");

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
                System.Threading.Thread.Sleep(800);
            }

            // Now the saddlebag is open
            // "Add All to Saddlebag" / "Remove All from Saddlebag" options are now available
            // Try to find and click them automatically
            
            bool foundAndClicked = false;
            
            // Try multiple times to catch the context menu
            for (int attempt = 0; attempt < 15; attempt++)
            {
                if (TryFindAndClickTransferOption(deposit))
                {
                    foundAndClicked = true;
                    PrintDebug("Bagginz: AUTO-CLICK SUCCESS!");
                    break;
                }
                System.Threading.Thread.Sleep(50);
            }

            if (!foundAndClicked)
            {
                if (deposit)
                {
                    PrintDebug("Tip: Shift+RightClick item to deposit");
                }
                else
                {
                    PrintDebug("Tip: RightClick item in saddlebag to withdraw");
                }
            }

            // Wait a moment then close saddlebag (if we opened it)
            System.Threading.Thread.Sleep(500);
            
            if (!isSaddlebagOpen)
            {
                _commandManager.ProcessCommand("/saddlebag");
            }

            PrintDebug(foundAndClicked ? "Bagginz: DONE!" : "Bagginz: Done");
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

    private bool TryFindAndClickTransferOption(bool deposit)
    {
        var contextAddon = _gameGui.GetAddonByName("ContextMenu", 1);
        if (contextAddon.IsNull)
            return false;

        var addon = (AtkUnitBase*)contextAddon.Address;
        if (addon == null || !addon->IsVisible)
            return false;

        var isSaddlebagOpenNow = IsSaddlebagOpen();
        
        var agent = GetInventoryContextAgent();
        if (agent == null)
            return false;

        int targetIndex = -1;
        string targetText = "";

        var maxItems = Math.Min(agent->ContextItemCount, 64);
        
        // First pass: check for menu items
        for (int i = 0; i < maxItems; i++)
        {
            var param = agent->EventParams[agent->ContexItemStartIndex + i];
            if (param.Type != ValueType.String && param.Type != ValueType.ManagedString)
                continue;

            var text = ReadAtkValueString(param);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.Trim();

            // Looking for saddlebag transfer options
            if (isSaddlebagOpenNow)
            {
                // In saddlebag - looking for withdraw
                if (trimmed.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("Withdraw", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmed.Contains("Saddlebag", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Inventory", StringComparison.OrdinalIgnoreCase) ||
                        trimmed == "Remove All")
                    {
                        targetIndex = i;
                        targetText = trimmed;
                        break;
                    }
                }
            }
            else
            {
                // In inventory - looking for deposit  
                if (trimmed.Contains("Add", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmed.Contains("Saddlebag", StringComparison.OrdinalIgnoreCase) ||
                        trimmed == "Add All")
                    {
                        targetIndex = i;
                        targetText = trimmed;
                        break;
                    }
                }
            }
        }

        if (targetIndex < 0)
            return false;

        // Click it!
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