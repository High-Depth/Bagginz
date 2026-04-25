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
    private long _menuOpenTick;

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
        
        // Register chat command
        _commandManager.AddHandler("/bagginz", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open saddlebag for deposit/withdraw"
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args == "debug")
        {
            var isOpen = IsSaddlebagOpen();
            var contextAddon = _gameGui.GetAddonByName("ContextMenu", 1);
            PrintDebug($"Saddlebag: {isOpen}, ContextMenu: {!contextAddon.IsNull}");
            return;
        }
        
        // Toggle saddlebag
        _commandManager.ProcessCommand("/saddlebag");
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory)
            return;

        var target = args.Target as MenuTargetInventory;
        if (target?.TargetItem == null)
            return;

        var isSaddlebag = IsSaddlebagOpen();
        _menuOpenTick = Environment.TickCount64;

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

        // Run on framework to ensure we're in the right context
        _framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        _framework.Update -= OnFrameworkUpdate;
        
        Task.Run(() => DoTransfer());
    }

    private void DoTransfer()
    {
        try
        {
            var isSaddlebagOpen = IsSaddlebagOpen();
            
            // Step 1: Open saddlebag first
            if (!isSaddlebagOpen)
            {
                PrintDebug("Bagginz: Opening saddlebag...");
                _commandManager.ProcessCommand("/saddlebag");
                System.Threading.Thread.Sleep(600);
            }

            // Step 2: Wait for context menu to appear and click it
            for (int retry = 0; retry < 10; retry++)
            {
                System.Threading.Thread.Sleep(100);
                
                if (TryClickContextMenuItem(_depositOperation))
                {
                    PrintDebug("Bagginz: Clicked transfer action!");
                    System.Threading.Thread.Sleep(300);
                    break;
                }
                
                if (retry == 9)
                {
                    PrintDebug("Bagginz: Could not find menu, do it manually");
                }
            }

            // Step 3: Close saddlebag if we opened it
            if (!isSaddlebagOpen)
            {
                System.Threading.Thread.Sleep(200);
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

    private bool TryClickContextMenuItem(bool deposit)
    {
        var contextAddon = _gameGui.GetAddonByName("ContextMenu", 1);
        if (contextAddon.IsNull)
            return false;

        var addon = (AtkUnitBase*)contextAddon.Address;
        if (addon == null || !addon->IsVisible)
            return false;

        var isSaddlebagOpen = IsSaddlebagOpen();
        
        var agent = GetInventoryContextAgent();
        if (agent == null)
            return false;

        int targetIndex = -1;
        string targetText = "";

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
                if (trimmed.Contains("Remove") && (trimmed.Contains("Saddlebag") || trimmed == "Remove All"))
                {
                    targetIndex = i;
                    targetText = trimmed;
                    break;
                }
            }
            else
            {
                if (trimmed.Contains("Add All") && trimmed.Contains("Saddlebag"))
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
        _commandManager.RemoveHandler("/bagginz");
    }
}