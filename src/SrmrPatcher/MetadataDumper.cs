using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SrmrPatcher;

internal sealed record MetadataDumpResult(
    int ProcessId,
    long Address,
    int MetadataVersion,
    int HeaderSize,
    int BytesWritten,
    string OutputPath)
{
    public IEnumerable<KeyValuePair<string, object?>> AsSummary()
    {
        yield return new("Process ID", ProcessId);
        yield return new("Address", $"0x{Address:X}");
        yield return new("Metadata version", MetadataVersion);
        yield return new("Header size", HeaderSize);
        yield return new("Bytes written", BytesWritten);
        yield return new("Output path", OutputPath);
    }
}

internal static partial class MetadataDumper
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const int ScanChunkSize = 8 * 1024 * 1024;
    private static ReadOnlySpan<byte> MetadataMagic => [0xAF, 0x1B, 0xB1, 0xFA];

    public static MetadataDumpResult Dump(int processId, string outputPath)
    {
        nint process = OpenProcess(
            ProcessVmRead | ProcessQueryInformation,
            inheritHandle: false,
            processId);
        if (process == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
        }

        try
        {
            long candidateAddress = FindPatternInPrivateMemory(process, MetadataMagic);
            if (candidateAddress < 0)
            {
                throw new InvalidOperationException(
                    "No valid decrypted IL2CPP metadata header was found " +
                    "in private process memory");
            }

            byte[]? header = ReadProcessBytes(process, candidateAddress, 1024);
            MetadataLayout? layout = header is null ? null : GetMetadataLayout(header);
            if (layout is null)
            {
                throw new InvalidDataException(
                    "The metadata signature was found, but its header is invalid");
            }

            byte[]? metadata = ReadProcessBytes(process, candidateAddress, layout.Length);
            if (metadata is null)
            {
                throw new InvalidOperationException(
                    "The metadata header was found, but the complete metadata buffer " +
                    "could not be read");
            }

            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, metadata);
            return new MetadataDumpResult(
                processId,
                candidateAddress,
                layout.Version,
                layout.HeaderSize,
                metadata.Length,
                outputPath);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static long FindPatternInPrivateMemory(
        nint process,
        ReadOnlySpan<byte> pattern)
    {
        long address = 0;
        nuint informationSize = (nuint)Marshal.SizeOf<MemoryBasicInformation>();
        while (VirtualQueryEx(
            process,
            (nint)address,
            out MemoryBasicInformation information,
            informationSize) != 0)
        {
            long baseAddress = information.BaseAddress;
            long regionSize = checked((long)information.RegionSize);
            bool readable = information.State == MemCommit &&
                information.Type == MemPrivate &&
                (information.Protect & PageGuard) == 0 &&
                (information.Protect & PageNoAccess) == 0;
            if (readable && regionSize >= pattern.Length)
            {
                long regionOffset = 0;
                byte[] overlap = [];
                while (regionOffset < regionSize)
                {
                    int requested = (int)Math.Min(ScanChunkSize, regionSize - regionOffset);
                    byte[] chunk = new byte[requested];
                    bool succeeded = ReadProcessMemory(
                        process,
                        (nint)(baseAddress + regionOffset),
                        chunk,
                        (nuint)requested,
                        out nuint bytesRead);
                    int actual = checked((int)bytesRead);
                    if (!succeeded && actual == 0)
                    {
                        break;
                    }

                    byte[] combined = new byte[overlap.Length + actual];
                    overlap.CopyTo(combined, 0);
                    chunk.AsSpan(0, actual).CopyTo(combined.AsSpan(overlap.Length));
                    int match = combined.AsSpan().IndexOf(pattern);
                    if (match >= 0)
                    {
                        return baseAddress + regionOffset - overlap.Length + match;
                    }

                    int overlapLength = Math.Min(pattern.Length - 1, actual);
                    overlap = chunk.AsSpan(actual - overlapLength, overlapLength).ToArray();
                    regionOffset += actual;
                }
            }

            long nextAddress = baseAddress + regionSize;
            if (nextAddress <= address)
            {
                break;
            }

            address = nextAddress;
        }

        return -1;
    }

    private static byte[]? ReadProcessBytes(nint process, long address, int length)
    {
        byte[] buffer = new byte[length];
        bool succeeded = ReadProcessMemory(
            process,
            (nint)address,
            buffer,
            (nuint)length,
            out nuint bytesRead);
        return succeeded && bytesRead == (nuint)length ? buffer : null;
    }

    private static MetadataLayout? GetMetadataLayout(ReadOnlySpan<byte> header)
    {
        if (header.Length < 16 || !header[..4].SequenceEqual(MetadataMagic))
        {
            return null;
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4));
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8, 4));
        if (version is < 20 or > 40 ||
            headerSize is < 0x80 ||
            headerSize > header.Length ||
            (headerSize - 8) % 8 != 0)
        {
            return null;
        }

        long metadataLength = headerSize;
        for (int offset = 8; offset < headerSize; offset += 8)
        {
            uint tableOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(offset, 4));
            uint tableSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(offset + 4, 4));
            metadataLength = Math.Max(metadataLength, (long)tableOffset + tableSize);
        }

        if (metadataLength <= headerSize || metadataLength > 1024L * 1024 * 1024)
        {
            return null;
        }

        return new MetadataLayout(version, headerSize, checked((int)metadataLength));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private sealed record MetadataLayout(int Version, int HeaderSize, int Length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint VirtualQueryEx(
        nint process,
        nint address,
        out MemoryBasicInformation buffer,
        nuint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(
        nint process,
        nint address,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
