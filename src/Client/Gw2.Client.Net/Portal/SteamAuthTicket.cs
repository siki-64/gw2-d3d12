using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Gw2.Client.Net.Portal;

public sealed class SteamApiUnavailableException : Exception
{
    public SteamApiUnavailableException(string message) : base(message) { }
}

public sealed record SteamLogin(ulong SteamId, byte[] Ticket);

public static unsafe class SteamAuthTicket
{
    public const int MaximumTicketLength = 4096;
    public const int AuthSessionTicketResponseCallback = 163;
    public const int ResultOk = 1;
    public const int InitErrorLength = 1024;
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    [StructLayout(LayoutKind.Sequential)]
    private struct RegisteredCallback
    {
        public nint* VirtualTable;
        public byte Flags;
        public int Callback;
    }

    private static int s_answeredResult;
    private static uint s_answeredTicket;
    private static volatile bool s_answered;

    [UnmanagedCallersOnly]
    private static void OnCallback(RegisteredCallback* self, AuthSessionTicketResponse* response)
    {
        s_answeredTicket = response->Ticket;
        s_answeredResult = response->Result;
        s_answered = true;
    }

    [UnmanagedCallersOnly]
    private static int CallbackSize(RegisteredCallback* self) => sizeof(AuthSessionTicketResponse);

    [StructLayout(LayoutKind.Sequential)]
    private struct AuthSessionTicketResponse
    {
        public uint Ticket;
        public int Result;
    }

    public static string DeployLibrary(byte[] library, string folderName, string libraryName)
    {
        string folder = Path.Combine(Path.GetTempPath(), folderName);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, libraryName);
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(library)) File.WriteAllBytes(path, library);
        return path;
    }

    public static SteamLogin Request(string libraryPath, uint appId)
    {
        Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());
        nint library = NativeLibrary.Load(libraryPath);
        var initFlat = (delegate* unmanaged<byte*, int>)NativeLibrary.GetExport(library, "SteamAPI_InitFlat");
        var steamUser = (delegate* unmanaged<nint>)NativeLibrary.GetExport(library, "SteamAPI_SteamUser_v023");
        var getAuthSessionTicket = (delegate* unmanaged<nint, byte*, int, uint*, void*, uint>)NativeLibrary.GetExport(library, "SteamAPI_ISteamUser_GetAuthSessionTicket");
        var registerCallback = (delegate* unmanaged<RegisteredCallback*, int, void>)NativeLibrary.GetExport(library, "SteamAPI_RegisterCallback");
        var unregisterCallback = (delegate* unmanaged<RegisteredCallback*, void>)NativeLibrary.GetExport(library, "SteamAPI_UnregisterCallback");
        var runCallbacks = (delegate* unmanaged<void>)NativeLibrary.GetExport(library, "SteamAPI_RunCallbacks");
        var getSteamId = (delegate* unmanaged<nint, ulong>)NativeLibrary.GetExport(library, "SteamAPI_ISteamUser_GetSteamID");

        byte* initError = stackalloc byte[InitErrorLength];
        int initResult = initFlat(initError);
        if (initResult != 0)
            throw new SteamApiUnavailableException($"SteamAPI_InitFlat returned {initResult}: {Marshal.PtrToStringUTF8((nint)initError)}");

        var virtualTable = (nint*)NativeMemory.Alloc(3, (nuint)sizeof(nint));
        var callback = (RegisteredCallback*)NativeMemory.AllocZeroed((nuint)sizeof(RegisteredCallback));
        virtualTable[0] = (nint)(delegate* unmanaged<RegisteredCallback*, AuthSessionTicketResponse*, void>)&OnCallback;
        virtualTable[1] = virtualTable[0];
        virtualTable[2] = (nint)(delegate* unmanaged<RegisteredCallback*, int>)&CallbackSize;
        callback->VirtualTable = virtualTable;
        try
        {
            var ticket = new byte[MaximumTicketLength];
            uint ticketLength;
            uint handle;
            fixed (byte* ticketBytes = ticket) handle = getAuthSessionTicket(steamUser(), ticketBytes, ticket.Length, &ticketLength, null);
            if (handle == 0) throw new SteamApiUnavailableException("GetAuthSessionTicket returned no ticket");
            s_answered = false;
            registerCallback(callback, AuthSessionTicketResponseCallback);
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < ResponseTimeout)
            {
                runCallbacks();
                if (s_answered && s_answeredTicket == handle)
                {
                    if (s_answeredResult != ResultOk) throw new SteamApiUnavailableException($"the auth session ticket answered result {s_answeredResult}");
                    return new SteamLogin(getSteamId(steamUser()), ticket.AsSpan(0, (int)ticketLength).ToArray());
                }
                Thread.Sleep(10);
            }
        }
        finally
        {
            unregisterCallback(callback);
            NativeMemory.Free(callback);
            NativeMemory.Free(virtualTable);
        }
        throw new TimeoutException("Steam did not confirm the auth session ticket");
    }
}
