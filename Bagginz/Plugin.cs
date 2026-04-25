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

        // Try to click the menu item FIRST, while context menu is still open!
        bool clicked = false;
        for (int i = 0; i < 10; i++)
        {
            if (TryClickContextMenuItem(deposit))
            {
                clicked = true;
                break;
            }
            System.Threading.Thread.Sleep(30);
        }

        // Now open saddlebag
        Task.Run(() => CompleteTransfer(clicked));
    }

    private void CompleteTransfer(bool menuWasClicked)
    {
        try
        {
            var isSaddlebagOpen = IsSaddlebagOpen();
            
            System.Threading.Thread.Sleep(100);

            if (!isSaddlebagOpen)
            {
                _commandManager.ProcessCommand("/saddlebag");
                System.Threading.Thread.Sleep(500);
            }

            if (!isSaddlebagOpen)
            {
                _commandManager.ProcessCommand("/saddlebag");
            }
            
            PrintDebug(menuWasClicked ? "Bagginz: DONE!" : "Bagginz: Done (manual)");
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

    private bool TryClickContextMenuItem(bool deposit)
    {
        var contextAddon = _gameGui.GetAddonByName("ContextMenu", 1);
        if (contextAddon.IsNull)
        {
            PrintDebug("NO CONTEXT MENU");
            return false;
        }

        var addon = (AtkUnitBase*)contextAddon.Address;
        if (addon == null || !addon->IsVisible)
        {
            PrintDebug("CONTEXT MENU NOT VISIBLE");
            return false;
        }

        var isSaddlebagOpen = IsSaddlebagOpen();
        
        var agent = GetInventoryContextAgent();
        if (agent == null)
        {
            PrintDebug("NO AGENT");
            return false;
        }

        int targetIndex = -1;
        string targetText = "";

        var maxItems = Math.Min(agent->ContextItemCount, 64);
        
        PrintDebug($"SCAN: {maxItems} items (sb={isSaddlebagOpen})");
        
        for (int i = 0; i < maxItems; i++)
        {
            var param = agent->EventParams[agent->ContexItemStartIndex + i];
            if (param.Type != ValueType.String && param.Type != ValueType.ManagedString)
                continue;

            var text = ReadAtkValueString(param);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.Trim();
            PrintDebug($"[{i}]: {trimmed}");
        }

        // Search again with matching
        for (int i = 0; i < maxItems; i++)
        {
            var param = agent->EventParams[agent->ContexItemStartIndex + i];
            if (param.Type != ValueType.String && param.Type != ValueType.ManagedString)
                continue;

            var text = ReadAtkValueString(param);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.Trim();

            bool found = false;
            if (isSaddlebagOpen)
            {
                if (trimmed.Contains("Remove") || trimmed.Contains("Withdraw"))
                {
                    if (trimmed.Contains("Saddlebag") || trimmed.Contains("Inventory") || trimmed == "Remove All")
                    {
                        found = true;
                    }
                }
            }
            else
            {
                if (trimmed.Contains("Add"))
                {
                    if (trimmed.Contains("Saddlebag") || trimmed == "Add All")
                    {
                        found = true;
                    }
                }
            }

            if (found)
            {
                targetIndex = i;
                targetText = trimmed;
                PrintDebug($"FOUND [{i}]: {targetText}");
                break;
            }
        }

        if (targetIndex < 0)
        {
            PrintDebug("NOT FOUND");
            return false;
        }

        PrintDebug($"CLICK [{targetIndex}]");

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