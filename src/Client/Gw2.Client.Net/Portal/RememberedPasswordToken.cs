using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Gw2.Client.Net.Portal;

public static unsafe class RememberedPasswordToken
{
    private const uint NoUserInterface = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint Length;
        public byte* Data;
    }

    public static byte[] Unprotect(byte[] protectedPassword)
    {
        fixed (byte* input = protectedPassword)
        {
            var protectedBlob = new DataBlob { Length = (uint)protectedPassword.Length, Data = input };
            DataBlob plain;
            if (!CryptUnprotectData(&protectedBlob, null, null, null, null, NoUserInterface, &plain)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                return new ReadOnlySpan<byte>(plain.Data, (int)plain.Length).ToArray();
            }
            finally
            {
                new Span<byte>(plain.Data, (int)plain.Length).Clear();
                LocalFree((nint)plain.Data);
            }
        }
    }

    public static byte[] Protect(byte[] passwordToken)
    {
        fixed (byte* input = passwordToken)
        {
            var plainBlob = new DataBlob { Length = (uint)passwordToken.Length, Data = input };
            DataBlob protectedBlob;
            if (!CryptProtectData(&plainBlob, null, null, null, null, NoUserInterface, &protectedBlob)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                return new ReadOnlySpan<byte>(protectedBlob.Data, (int)protectedBlob.Length).ToArray();
            }
            finally
            {
                LocalFree((nint)protectedBlob.Data);
            }
        }
    }

    [DllImport("crypt32", SetLastError = true)]
    private static extern bool CryptProtectData(DataBlob* input, char* description, DataBlob* entropy, void* reserved, void* prompt, uint flags, DataBlob* output);

    [DllImport("crypt32", SetLastError = true)]
    private static extern bool CryptUnprotectData(DataBlob* input, char** description, DataBlob* entropy, void* reserved, void* prompt, uint flags, DataBlob* output);

    [DllImport("kernel32")]
    private static extern nint LocalFree(nint memory);
}
