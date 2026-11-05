using System.Collections.Generic;
using Crystal.Input;

namespace Gw2.Client.Settings;

public static class GameKeys
{
    private const uint FirstFunctionKey = 32;
    private const uint FirstDigit = 48;
    private const uint FirstLetter = 65;
    private const uint LastLetter = 90;
    private const uint FirstNumPadDigit = 95;
    private const int FunctionKeyCount = 12;
    private const int DigitCount = 10;

    private static readonly Dictionary<uint, Keys> KeyByGameKey = Build();
    private static readonly Dictionary<Keys, uint> GameKeyByKey = Reverse(KeyByGameKey);

    public static Keys? KeyOf(uint gameKey) => KeyByGameKey.TryGetValue(gameKey, out var key) ? key : null;

    public static uint? GameKeyOf(Keys key) => GameKeyByKey.TryGetValue(key, out var gameKey) ? gameKey : null;

    public static string LabelOf(Keys key) => key switch
    {
        >= Keys.D1 and <= Keys.D9 => ((int)key - (int)Keys.D1 + 1).ToString(),
        Keys.D0 => "0",
        _ => key.ToString(),
    };

    private static Dictionary<Keys, uint> Reverse(Dictionary<uint, Keys> keyByGameKey)
    {
        var gameKeys = new Dictionary<Keys, uint>();
        foreach (var (gameKey, key) in keyByGameKey) gameKeys[key] = gameKey;
        return gameKeys;
    }

    private static Dictionary<uint, Keys> Build()
    {
        var keys = new Dictionary<uint, Keys>
        {
            [0] = Keys.LeftAlt, [1] = Keys.LeftControl, [2] = Keys.LeftShift, [3] = Keys.OemQuotes, [4] = Keys.OemPipe,
            [5] = Keys.CapsLock, [6] = Keys.OemComma, [7] = Keys.OemMinus, [8] = Keys.OemPlus, [9] = Keys.Escape,
            [10] = Keys.OemOpenBrackets, [11] = Keys.NumLock, [12] = Keys.OemPeriod, [13] = Keys.OemCloseBrackets,
            [14] = Keys.OemSemicolon, [15] = Keys.OemQuestion, [16] = Keys.PrintScreen, [17] = Keys.OemTilde,
            [18] = Keys.Back, [19] = Keys.Delete, [20] = Keys.Enter, [21] = Keys.Space, [22] = Keys.Tab, [23] = Keys.End,
            [24] = Keys.Home, [25] = Keys.Insert, [26] = Keys.PageDown, [27] = Keys.PageUp, [28] = Keys.Down,
            [29] = Keys.Left, [30] = Keys.Right, [31] = Keys.Up,
            [91] = Keys.Add, [92] = Keys.Decimal, [93] = Keys.Divide, [94] = Keys.Multiply, [105] = Keys.NumPadEnter,
            [106] = Keys.Subtract, [109] = Keys.RightAlt, [110] = Keys.RightControl,
        };
        for (var index = 0; index < FunctionKeyCount; index++) keys[FirstFunctionKey + (uint)index] = Keys.F1 + index;
        keys[FirstDigit] = Keys.D0;
        for (var index = 1; index < DigitCount; index++) keys[FirstDigit + (uint)index] = Keys.D1 + index - 1;
        for (var letter = FirstLetter; letter <= LastLetter; letter++) keys[letter] = Keys.A + (int)(letter - FirstLetter);
        keys[FirstNumPadDigit] = Keys.NumPad0;
        for (var index = 1; index < DigitCount; index++) keys[FirstNumPadDigit + (uint)index] = Keys.NumPad1 + index - 1;
        return keys;
    }
}
