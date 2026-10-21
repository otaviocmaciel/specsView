using System.Runtime.InteropServices;

namespace SpecsView.Sensors.Native;

/// <summary>Just enough DXGI to list GPUs with their name, LUID and dedicated memory size.</summary>
internal static unsafe partial class Dxgi
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;

    // IDXGIFactory1 vtable: IUnknown(0-2) IDXGIObject(3-6) IDXGIFactory(7-11) EnumAdapters1 = 12.
    private const int FactoryEnumAdapters1 = 12;

    // IDXGIAdapter1 vtable: IUnknown(0-2) IDXGIObject(3-6) IDXGIAdapter(7-9) GetDesc1 = 10.
    private const int AdapterGetDesc1 = 10;
    private const int Release = 2;

    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public sealed record Adapter(string Name, uint LuidLow, int LuidHigh, ulong DedicatedMemory)
    {
        /// <summary>The LUID as it appears in PDH instance names: "luid_0x00000000_0x0000D6F2".</summary>
        public string PdhLuid => $"luid_0x{LuidHigh:X8}_0x{LuidLow:X8}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out nint factory);

    public static IReadOnlyList<Adapter> EnumerateHardwareAdapters()
    {
        var adapters = new List<Adapter>();
        if (CreateDXGIFactory1(IidFactory1, out var factory) < 0)
        {
            return adapters;
        }

        try
        {
            var enumAdapters = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)VTable(factory)[FactoryEnumAdapters1];
            for (uint i = 0; ; i++)
            {
                nint adapter;
                if (enumAdapters(factory, i, &adapter) == DxgiErrorNotFound)
                {
                    break;
                }

                try
                {
                    AdapterDesc1 desc;
                    var getDesc = (delegate* unmanaged[Stdcall]<nint, AdapterDesc1*, int>)VTable(adapter)[AdapterGetDesc1];
                    if (getDesc(adapter, &desc) >= 0 && (desc.Flags & DxgiAdapterFlagSoftware) == 0)
                    {
                        var name = new string(desc.Description).TrimEnd('\0');
                        adapters.Add(new Adapter(name, desc.LuidLow, desc.LuidHigh, desc.DedicatedVideoMemory));
                    }
                }
                finally
                {
                    ReleaseCom(adapter);
                }
            }
        }
        finally
        {
            ReleaseCom(factory);
        }

        return adapters;
    }

    private static void** VTable(nint comObject) => *(void***)comObject;

    private static void ReleaseCom(nint comObject) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)VTable(comObject)[Release])(comObject);
}
