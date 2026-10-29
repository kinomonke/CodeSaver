using BepInEx;
using GorillaNetworking;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CodeSaver;

[BepInPlugin(Constants.GUID, Constants.NAME, Constants.VERS)]
public class Plugin : BaseUnityPlugin
{
    private bool init;

    private void Awake() => Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Constants.NAME);

    private void Update()
    {
        if (init) return;
        init = CodeTab.TryInit();
    }
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

    public static bool IsValid(string code)
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

    public static bool RemoveAt(int index)
    {
        if (index < 0 || index >= _codes.Count) return false;

        _codes.RemoveAt(index);
        Save();
        return true;
    }
}

public static class CodeTab
{
    public const GorillaComputer.ComputerState State = (GorillaComputer.ComputerState)100;

    private const int PageSize = 5;

    private enum Mode
    {
        List,
        Add,
        Delete
    }

    private static Mode _mode = Mode.List;
    private static int _page;
    private static string _input = "";
    private static string msg = "";

    public static bool TryInit()
    {
        GorillaComputer computer = GorillaComputer.instance;
        if (computer == null || !computer.initialized || computer.OrderList == null) return false;

        if (!computer.OrderList.Any(item => item.State == State))
        {
            int index = Math.Min(1, computer.OrderList.Count);
            computer.OrderList.Insert(index, new GorillaComputer.StateOrderItem(State, "Codes"));
        }

        AccessTools.Method(typeof(GorillaComputer), "RefreshFunctionNames").Invoke(computer, null);
        computer.UpdateScreen();
        return true;
    }

    public static void ResetView()
    {
        _mode = Mode.List;
        _page = 0;
        _input = "";
        msg = "";
    }

    private static int PageCount => Math.Max(1, (CodeStore.Codes.Count + PageSize - 1) / PageSize);

    private static void ClampPage() => _page = Math.Max(0, Math.Min(_page, PageCount - 1));

    private static void NextPage() => _page = (_page + 1) % PageCount;

    private static bool TryGetRowIndex(GorillaKeyboardBindings button, out int index)
    {
        index = -1;
        if (button >= GorillaKeyboardBindings.up) return false;

        int row = (int)button - 1;
        if (row < 0 || row >= PageSize) return false;

        int candidate = _page * PageSize + row;
        if (candidate >= CodeStore.Codes.Count) return false;

        index = candidate;
        return true;
    }

    public static void HandleButton(GorillaKeyboardBindings button)
    {
        msg = "";

        switch (_mode)
        {
            case Mode.List:
                HandleList(button);
                break;
            case Mode.Add:
                HandleAdd(button);
                break;
            case Mode.Delete:
                HandleDelete(button);
                break;
        }

        ClampPage();
    }

    private static void HandleList(GorillaKeyboardBindings button)
    {
        switch (button)
        {
            case GorillaKeyboardBindings.option1:
                NextPage();
                return;
            case GorillaKeyboardBindings.option2:
                _mode = Mode.Add;
                _input = "";
                return;
            case GorillaKeyboardBindings.option3:
                if (CodeStore.Codes.Count > 0)
                {
                    _mode = Mode.Delete;
                    _page = 0;
                }
                return;
            case GorillaKeyboardBindings.delete:
                NetworkSystem.Instance.ReturnToSinglePlayer();
                return;
        }

        if (TryGetRowIndex(button, out int index))
        {
            string code = CodeStore.Codes[index];
            msg = "JOINING " + code;
            PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(code, JoinType.Solo);
        }
    }

    private static void HandleAdd(GorillaKeyboardBindings button)
    {
        switch (button)
        {
            case GorillaKeyboardBindings.option1:
            case GorillaKeyboardBindings.option3:
                _mode = Mode.List;
                _input = "";
                return;
            case GorillaKeyboardBindings.enter:
                if (_input.Length == 0) return;
                msg = CodeStore.Add(_input) ? "ADDED " + _input : "INVALID OR ALREADY SAVED";
                _input = "";
                return;
            case GorillaKeyboardBindings.delete:
                if (_input.Length > 0) _input = _input[..^1];
                return;
        }

        if (_input.Length >= 10) return;
        if (button >= GorillaKeyboardBindings.up && button <= GorillaKeyboardBindings.option3) return;

        string character = button >= GorillaKeyboardBindings.up ? button.ToString() : ((int)button).ToString();
        if (character.Length == 1 && char.IsLetterOrDigit(character[0]))
        {
            _input += character.ToUpperInvariant();
        }
    }

    private static void HandleDelete(GorillaKeyboardBindings button)
    {
        switch (button)
        {
            case GorillaKeyboardBindings.option1:
                NextPage();
                return;
            case GorillaKeyboardBindings.option2:
            case GorillaKeyboardBindings.option3:
                _mode = Mode.List;
                _page = 0;
                return;
        }

        if (TryGetRowIndex(button, out int index))
        {
            string code = CodeStore.Codes[index];
            CodeStore.RemoveAt(index);
            msg = "DELETED " + code;
            if (CodeStore.Codes.Count == 0)
            {
                _mode = Mode.List;
                _page = 0;
            }
        }
    }

    public static string BuildScreen()
    {
        ClampPage();

        StringBuilder str = new();

        switch (_mode)
        {
            case Mode.Add:
                str.AppendLine("ADD CODE");
                str.AppendLine();
                str.AppendLine("ROOM CODE: " + _input + "_");
                str.AppendLine();
                str.AppendLine("PRESS ENTER TO SAVE THE CODE.");
                str.AppendLine("PRESS DELETE TO REMOVE A CHARACTER.");
                str.AppendLine("PRESS OPTION 1 TO GO BACK.");
                break;
            case Mode.Delete:
                str.AppendLine("DELETE CODE");
                str.AppendLine();
                AppendRows(str);
                str.AppendLine();
                str.AppendLine("PAGE " + (_page + 1) + "/" + PageCount);
                str.AppendLine("PRESS 1-" + PageSize + " TO DELETE THE CODE.");
                str.AppendLine("OPTION 1 NEXT PAGE, OPTION 3 BACK.");
                break;
            default:
                str.AppendLine("CODE SAVER");
                str.AppendLine();
                if (CodeStore.Codes.Count == 0)
                {
                    str.AppendLine("NO CODES HAVE BEEN SAVED.");
                    str.AppendLine();
                    str.AppendLine("PRESS OPTION 2 TO ADD A CODE.");
                }
                else
                {
                    AppendRows(str);
                    str.AppendLine();
                    str.AppendLine("PAGE " + (_page + 1) + "/" + PageCount);
                    str.AppendLine("PRESS 1-" + PageSize + " TO JOIN THE CODE.");
                    str.AppendLine("OPTION 1 NEXT PAGE, OPTION 2 ADD,");
                    str.AppendLine("OPTION 3 DELETE, DELETE LEAVES ROOM.");
                }
                break;
        }

        if (msg.Length > 0)
        {
            str.AppendLine();
            str.AppendLine("<color=#85ffa5>" + msg + "</color>");
        }

        return str.ToString();
    }

    private static void AppendRows(StringBuilder str)
    {
        int start = _page * PageSize;
        int end = Math.Min(start + PageSize, CodeStore.Codes.Count);

        for (int i = start; i < end; i++)
        {
            str.AppendLine((i - start + 1) + ") " + CodeStore.Codes[i]);
        }
    }
}

[HarmonyPatch(typeof(GorillaComputer), nameof(GorillaComputer.PressButton))]
internal static class PressButtonPatch
{
    private static bool Prefix(GorillaComputer __instance, GorillaKeyboardBindings buttonPressed)
    {
        if (__instance.currentState != CodeTab.State) return true;

        if (buttonPressed == GorillaKeyboardBindings.up || buttonPressed == GorillaKeyboardBindings.down)
        {
            CodeTab.ResetView();
            return true;
        }

        CodeTab.HandleButton(buttonPressed);
        __instance.UpdateScreen();
        return false;
    }
}

[HarmonyPatch(typeof(GorillaComputer), nameof(GorillaComputer.UpdateScreen))]
internal static class UpdateScreenPatch
{
    private static void Postfix(GorillaComputer __instance)
    {
        if (__instance.currentState != CodeTab.State) return;
        if (NetworkSystem.Instance == null || NetworkSystem.Instance.WrongVersion) return;

        __instance.screenText.Set(CodeTab.BuildScreen());
    }
}

class Constants
{
    public const string GUID = "kinomonke.codesaver",
        NAME = "CodeSaver",
        VERS = "1.00";
}