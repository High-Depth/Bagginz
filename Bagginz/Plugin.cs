using System;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
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
        PrintDebug($"Bagginz: {action} initiated...");

        // IMMEDIATELY try to find and click the context menu item
        // This MUST happen while the context menu is still open!
        if (TryAutoSelectAndClickContextMenuItem())
        {
            // Menu item clicked - now complete the saddlebag operation
            Task.Run(() => CompleteTransferAfterContextClick());
        }
        else
        {
            PrintDebug("Bagginz: Could not find context menu action.");
            _isOperationPending = false;
        }
    }

    private void CompleteTransferAfterContextClick()
    {
        try
        {
            // Wait for context menu to close and action to process
            System.Threading.Thread.Sleep(150);

            var isSaddlebagOpen = IsSaddlebagOpen();

            if (!isSaddlebagOpen)
            {
                // Open saddlebag to complete the transfer
                _commandManager.ProcessCommand("/saddlebag");
                System.Threading.Thread.Sleep(500);
            }

            // Close saddlebag
            System.Threading.Thread.Sleep(200);
            _commandManager.ProcessCommand("/saddlebag");
            
            PrintDebug("Bagginz: Transfer complete!");
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

    private bool TryAutoSelectAndClickContextMenuItem()
    {
        var agent = GetInventoryContextAgent();
        if (agent == null)
            return false;

        var contextAddon = _gameGui.GetAddonByName("ContextMenu", 1);
        if (contextAddon.IsNull)
            return false;

        var addon = (AtkUnitBase*)contextAddon.Address;
        if (addon == null || !addon->IsVisible)
            return false;

        var isSaddlebagOpen = IsSaddlebagOpen();
        int targetIndex = -1;
        string targetText = string.Empty;

        var maxItems = Math.Min(agent->ContextItemCount, 50);
        for (int i = 0; i < maxItems; i++)
        {
            var param = agent->EventParams[agent->ContexItemStartIndex + i];
            if (param.Type != ValueType.String && param.Type != ValueType.ManagedString)
                continue;

            var text = ReadAtkValueString(param);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.Trim();

            if (isSaddlebagOpen)
            {
                if (trimmed.Equals("Remove All from Saddlebag", StringComparison.OrdinalIgnoreCase) ||
                    (trimmed.Contains("Remove", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("Saddlebag", StringComparison.OrdinalIgnoreCase)) ||
                    trimmed.Equals("Remove All", StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    targetText = trimmed;
                    break;
                }
            }
            else
            {
                if (trimmed.Equals("Add All to Saddlebag", StringComparison.OrdinalIgnoreCase) ||
                    (trimmed.Contains("Add All", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("Saddlebag", StringComparison.OrdinalIgnoreCase)))
                {
                    targetIndex = i;
                    targetText = trimmed;
                    break;
                }
            }
        }

        if (targetIndex < 0)
            return false;

        // Click the menu item
        GenerateCallback(addon, 0, targetIndex, 0U, 0, 0);

        PrintDebug($"Bagginz: Clicked '{targetText}'");
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

    private static unsafe void GenerateCallback(AtkUnitBase* addon, uint idx1, int idx2, uint idx3, uint idx4, uint idx5)
    {
        var values = stackalloc AtkValue[5];
        values[0].Type = ValueType.Int;
        values[0].Int = (int)idx1;
        values[1].Type = ValueType.Int;
        values[1].Int = idx2;
        values[2].Type = ValueType.UInt;
        values[2].UInt = idx3;
        values[3].Type = ValueType.UInt;
        values[3].UInt = idx4;
        values[4].Type = ValueType.UInt;
        values[4].UInt = idx5;

        addon->FireCallback(2, values);
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
        return string.Empty;
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