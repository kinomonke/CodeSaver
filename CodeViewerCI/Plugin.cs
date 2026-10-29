using BepInEx;
using ComputerInterface.Behaviors.UI;
using ComputerInterface.Enumerations;
using ComputerInterface.Extensions;
using ComputerInterface.Interfaces;
using ComputerInterface.Models;
using GorillaNetworking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CodeSaverCI;

[BepInDependency("tonimacaroni.computerinterface", "2.1.3")]
[BepInPlugin(Constants.GUID, Constants.NAME, Constants.VERS)]
public class Plugin : BaseUnityPlugin
{

}

public class CodeEntry : IComputerViewEntry
{
    public string EntryName => "Code Saver";

    public Type EntryComputerView => typeof(CodeView);
}

public static class CodeStore
{
    private static readonly string FilePath = Path.Combine(Paths.ConfigPath, "CodeSaver.txt");
    private static readonly List<string> _codes = Load();

    public static IReadOnlyList<string> Codes => _codes;

    private static List<string> Load()
    {
        if (File.Exists(FilePath))
        {
            return [.. File.ReadAllLines(FilePath)
                .Select(line => line.Trim().ToUpperInvariant())
                .Where(IsValid)
                .Distinct()];
        }

        return [];
    }

    private static void Save() => File.WriteAllLines(FilePath, _codes);

    private static bool IsValid(string code)
    {
        return !string.IsNullOrWhiteSpace(code) && code.Length <= 10 && code.All(char.IsLetterOrDigit);
    }

    public static bool Add(string code)
    {
        code = code.Trim().ToUpperInvariant();
        if (!IsValid(code) || _codes.Contains(code)) return false;

        _codes.Add(code);
        Save();
        return true;
    }

    public static bool Remove(string code)
    {
        bool removed = _codes.Remove(code);
        if (removed) Save();
        return removed;
    }
}

public abstract class PagedListView : ComputerView
{
    private const int PageSize = 8;

    protected abstract int ItemCount { get; }

    protected int SelectedIndex { get; private set; }

    protected int PageStart => SelectedIndex / PageSize * PageSize;

    protected int PageLength => Math.Max(0, Math.Min(PageSize, ItemCount - PageStart));

    protected int PageCount => Math.Max(1, (ItemCount + PageSize - 1) / PageSize);

    protected void ResetSelection() => SelectedIndex = 0;

    protected void ClampSelection()
    {
        SelectedIndex = ItemCount == 0 ? 0 : Math.Min(SelectedIndex, ItemCount - 1);
    }

    protected bool HandleNavigation(EKeyboardButton pressedButton)
    {
        if (ItemCount == 0) return false;

        switch (pressedButton)
        {
            case EKeyboardButton.Up:
                SelectedIndex = (SelectedIndex - 1 + ItemCount) % ItemCount;
                return true;
            case EKeyboardButton.Down:
                SelectedIndex = (SelectedIndex + 1) % ItemCount;
                return true;
        }

        return false;
    }

    protected string RowPrefix(int row)
    {
        return PageStart + row == SelectedIndex ? $"<color=#{PrimaryColor}>></color> " : "  ";
    }

    protected void AppendPageFooter(StringBuilder str)
    {
        str.AppendLine($"Page {PageStart / PageSize + 1}/{PageCount}");
    }
}

public class CodeView : ComputerView
{
    public static CodeView Instance;

    private readonly List<Tuple<string, Type>> _codeViews = [
            new("Codes", typeof(CodeViewerView)),
            new("Add Codes", typeof(CodeAdderView)),
            new("Delete Codes", typeof(CodeDeleterView))
        ];

    private int _index;

    public CodeView() => Instance ??= this;

    public override void OnViewShown(object[] arguments)
    {
        base.OnViewShown(arguments);
        UpdateViewScreen();
    }

    public override void OnButtonPressed(EKeyboardButton pressedButton)
    {
        switch (pressedButton)
        {
            case EKeyboardButton.Up:
                _index = (_index - 1 + _codeViews.Count) % _codeViews.Count;
                UpdateViewScreen();
                break;
            case EKeyboardButton.Down:
                _index = (_index + 1) % _codeViews.Count;
                UpdateViewScreen();
                break;
            case EKeyboardButton.Enter:
                ShowView(_codeViews[_index].Item2);
                break;
            case EKeyboardButton.Back:
                ReturnToMainMenu();
                break;
        }
    }

    protected override string GetViewText()
    {
        StringBuilder str = new();

        str.BeginCenter().Repeat("=", ScreenWidth).AppendLine();
        str.Append("Code Saver").AppendLine();
        str.Repeat("=", ScreenWidth).EndAlign().AppendLines(2);

        for (int i = 0; i < _codeViews.Count; i++)
        {
            string prefix = i == _index ? $"<color=#{PrimaryColor}>></color> " : "  ";
            str.AppendLine(prefix + _codeViews[i].Item1);
        }

        return str.ToString();
    }
}

public class CodeViewerView : PagedListView
{
    protected override int ItemCount => CodeStore.Codes.Count;

    public override void OnViewShown(object[] arguments)
    {
        base.OnViewShown(arguments);
        ResetSelection();
        UpdateViewScreen();
    }

    public override void OnButtonPressed(EKeyboardButton pressedButton)
    {
        if (HandleNavigation(pressedButton))
        {
            UpdateViewScreen();
            return;
        }

        switch (pressedButton)
        {
            case EKeyboardButton.Enter:
                if (ItemCount == 0) return;
                string code = CodeStore.Codes[SelectedIndex];
                PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(code, JoinType.Solo);
                break;
            case EKeyboardButton.Option1:
                NetworkSystem.Instance.ReturnToSinglePlayer();
                return;
            case EKeyboardButton.Back:
                ReturnToPreviousView();
                break;
        }
    }

    protected override string GetViewText()
    {
        StringBuilder str = new();

        str.BeginCenter().Repeat("=", ScreenWidth).AppendLine();
        str.Append("Codes").AppendLine();
        str.Repeat("=", ScreenWidth).EndAlign().AppendLines(2);

        if (ItemCount == 0)
        {
            str.AppendLine("No codes have been saved.");
            return str.ToString();
        }

        for (int row = 0; row < PageLength; row++)
        {
            str.AppendLine(RowPrefix(row) + CodeStore.Codes[PageStart + row]);
        }

        str.AppendLine();
        AppendPageFooter(str);
        str.AppendLine("Press enter to join the selected room.");
        str.AppendLine("Press Option 1 to leave the current room.");

        return str.ToString();
    }
}

public class CodeAdderView : ComputerView
{
    private readonly UITextInputHandler _textInput = new();
    private string _message = "";

    public override void OnViewShown(object[] arguments)
    {
        base.OnViewShown(arguments);
        _textInput.Text = "";
        _message = "";
        UpdateViewScreen();
    }

    public override void OnButtonPressed(EKeyboardButton pressedButton)
    {
        switch (pressedButton)
        {
            case EKeyboardButton.Back:
                ReturnToPreviousView();
                return;
            case EKeyboardButton.Enter:
                if (_textInput.Text.Length == 0) break;
                string code = _textInput.Text.ToUpperInvariant();
                _message = CodeStore.Add(code) ? $"Added {code}" : "Already saved/invalid code";
                _textInput.Text = "";
                break;
            default:
                _textInput.HandleButtonPress(pressedButton);
                _message = "";
                break;
        }

        UpdateViewScreen();
    }

    protected override string GetViewText()
    {
        StringBuilder str = new();

        str.BeginCenter().Repeat("=", ScreenWidth).AppendLine();
        str.Append("Add Codes").AppendLine();
        str.Repeat("=", ScreenWidth).EndAlign().AppendLines(2);

        str.AppendLine("Room code: " + _textInput.Text + "_").AppendLine();
        str.AppendLine("Press Enter to add a code.");

        if (_message.Length > 0) str.AppendLine().AppendLine(_message);

        return str.ToString();
    }
}

public class CodeDeleterView : PagedListView
{
    protected override int ItemCount => CodeStore.Codes.Count;

    public override void OnViewShown(object[] arguments)
    {
        base.OnViewShown(arguments);
        ResetSelection();
        UpdateViewScreen();
    }

    public override void OnButtonPressed(EKeyboardButton pressedButton)
    {
        if (HandleNavigation(pressedButton))
        {
            UpdateViewScreen();
            return;
        }

        switch (pressedButton)
        {
            case EKeyboardButton.Enter:
                if (ItemCount == 0) return;
                CodeStore.Remove(CodeStore.Codes[SelectedIndex]);
                ClampSelection();
                UpdateViewScreen();
                break;
            case EKeyboardButton.Back:
                ReturnToPreviousView();
                break;
        }
    }

    protected override string GetViewText()
    {
        StringBuilder str = new();

        str.BeginCenter().Repeat("=", ScreenWidth).AppendLine();
        str.Append("Delete Codes").AppendLine();
        str.Repeat("=", ScreenWidth).EndAlign().AppendLines(2);

        if (ItemCount == 0)
        {
            str.AppendLine("No codes have been saved.");
            return str.ToString();
        }

        for (int row = 0; row < PageLength; row++)
        {
            str.AppendLine(RowPrefix(row) + CodeStore.Codes[PageStart + row]);
        }

        str.AppendLine();
        AppendPageFooter(str);
        str.AppendLine("Press Enter to delete.");

        return str.ToString();
    }
}

class Constants
{
    public const string GUID = "kinomonke.codesaver",
        NAME = "CodeSaver",
        VERS = "1.00";
}