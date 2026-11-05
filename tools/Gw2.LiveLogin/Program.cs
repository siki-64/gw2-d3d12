using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Gw2.Client.Net.Login;

var characterSelectHold = TimeSpan.FromSeconds(2);

string executable = Gw2Installation.ExecutablePath() ?? throw new InvalidOperationException("Gw2-64.exe is not installed");
string dat = Gw2Installation.DatPath() ?? Path.Combine(Path.GetDirectoryName(executable)!, "Gw2.dat");

LoginRequest request;
if (args.Contains("--remembered")) request = new LoginRequest(LoginMethod.RememberedAccount, "", "", RemembersLogin: true);
else if (args.Contains("--account"))
{
    Console.Write("account email: ");
    string email = Console.ReadLine() ?? "";
    Console.Write("password: ");
    request = new LoginRequest(LoginMethod.Account, email, ReadHidden(), RemembersLogin: args.Contains("--remember"));
}
else request = new LoginRequest(LoginMethod.Steam, "", "", RemembersLogin: false);

using var login = new LoginFlow(executable, dat, RememberedLoginStore.ForCurrentUser());
Console.WriteLine($"steam login available: {login.CanLogInWithSteam()}");
if (request.Method == LoginMethod.Steam && !login.CanLogInWithSteam()) return;
login.Progress += status => Console.WriteLine(status);
login.MessageReceived += messageId => Console.WriteLine($"  received 0x{messageId:X}");
login.Start(request);

while (login.Stage is not (LoginStage.CharacterSelect or LoginStage.Failed or LoginStage.LoggedOut))
{
    if (login.Stage == LoginStage.WaitingForSecondFactor && login.SecondFactorType is { } authType)
    {
        Console.Write($"{authType} code: ");
        login.SubmitSecondFactor(Console.ReadLine() ?? "");
        while (login.Stage == LoginStage.WaitingForSecondFactor) Thread.Sleep(10);
    }
    Thread.Sleep(10);
}
if (login.Stage == LoginStage.CharacterSelect)
{
    Thread.Sleep(characterSelectHold);
    Console.WriteLine($"{login.Characters.Count} characters at character select");
    login.Logout();
}
login.WaitForExit(TimeSpan.FromSeconds(10));
Console.WriteLine(login.Error is { } error ? $"failed: {error}" : "done");

static string ReadHidden()
{
    var text = new System.Text.StringBuilder();
    for (var key = Console.ReadKey(true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(true))
    {
        if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
        else text.Append(key.KeyChar);
    }
    Console.WriteLine();
    return text.ToString();
}
