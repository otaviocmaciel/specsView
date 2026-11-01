using System.Runtime.InteropServices;

namespace SpecsView.App.Native;

internal static partial class WinTrust
{
    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;

    // WINTRUST_ACTION_GENERIC_VERIFY_V2: Authenticode policy.
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint StructSize;
        public nint FilePath;
        public nint File;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint StructSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint hwnd, ref Guid action, ref TrustData data);

    /// <summary>True when the file carries a valid Authenticode signature chaining to a trusted root.</summary>
    public static unsafe bool IsSignatureValid(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        try
        {
            var file = new FileInfo { StructSize = (uint)sizeof(FileInfo), FilePath = pathPtr };
            var data = new TrustData
            {
                StructSize = (uint)sizeof(TrustData),
                UiChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                FileInfo = (nint)(&file),
                StateAction = StateActionVerify,
            };

            var action = GenericVerifyV2;
            var result = WinVerifyTrust(-1, ref action, ref data);

            data.StateAction = StateActionClose;
            WinVerifyTrust(-1, ref action, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(pathPtr);
        }
    }
}
