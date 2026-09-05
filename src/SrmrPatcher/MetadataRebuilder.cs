using System.Buffers.Binary;

namespace SrmrPatcher;

internal sealed record MetadataRebuildOptions(
    string GameAssemblyPath,
    string MetadataPath,
    string OutputPath,
    bool AllowHashMismatch = false,
    bool AnalyzeLayout = false,
    bool Verbose = false);

internal sealed record MetadataRebuildResult(string OutputPath, long Size, string Sha256);

internal static class MetadataRebuilder
{
    private const string GameAssemblySha256 =
        "C721E373641239C9D75DBCD58E75BE89DBBF6D507D3A421ACE3754B4082272E3";
    private const string MetadataSha256 =
        "8A8C41F65145C50DC1796BB2BB6221FE4110FF8199A059DF6144558726E46506";
    private const int ProtectedHeaderSize = 0x240;
    private const int CanonicalHeaderSize = 0x100;
    private const int PayloadStart = 0x1204;

    private static readonly EncryptedSection[] EncryptedSections =
    [
        new(0, "string literals", 0x001204, 0x01F378, 0x2C6D7221354AF23D),
        new(1, "string literal data", 0x02057C, 0x095710, 0x1EA8A53189AF02FF),
        new(2, "metadata strings", 0x0B5C8C, 0x235B70, 0xBB0189AB3D528FCC),
        new(4, "properties", 0x2ED314, 0x075DDC, 0x17FE79C86C7918C7),
        new(5, "methods", 0x3630F0, 0x50B068, 0xB0404B4DA147BC91),
        new(11, "fields", 0xB9E1B0, 0x0F3A74, 0x77243721BBDFBFF3),
        new(21, "assemblies", 0xF6960C, 0x003340, 0x18CC9CABA9C67999)
    ];

    private static readonly int[] SectionLengths =
    [
        0x01F378,
        0x095710,
        0x235B70,
        0x001B18,
        0x075DDC,
        0x50B068,
        0x00B640,
        0x03A38C,
        0x1027A8,
        0x011988,
        0x1D655C,
        0x0F3A74,
        0x028C50,
        0x003E18,
        0x0168A0,
        0x0075C0,
        0x006D3C,
        0x0BA194,
        0x023498,
        0x1A6DB0,
        0x002008,
        0x003340,
        0x002618,
        0x001D80,
        0x13CCDC,
        0x068650,
        0x00F0A8,
        0x00A118,
        0x000000,
        0x000000,
        0x00255C
    ];

    private static readonly int[] SectionRecordSizes =
    [
        8, 1, 1, 24, 20, 36, 12, 12, 1, 12, 12, 12, 16, 4, 16, 4,
        4, 4, 8, 88, 40, 64, 8, 4, 1, 8, 4, 8, 8, 1, 4
    ];

    private static readonly IReadOnlyDictionary<int, int> KnownSectionPositions =
        EncryptedSections.ToDictionary(section => section.Index, section => section.Offset);

    private static readonly IReadOnlyDictionary<int, int> KnownSectionLengths =
        EncryptedSections.ToDictionary(section => section.Index, section => section.Length);

    public static MetadataRebuildResult Rebuild(MetadataRebuildOptions options)
    {
        byte[] gameAssembly = File.ReadAllBytes(options.GameAssemblyPath);
        byte[] protectedMetadata = File.ReadAllBytes(options.MetadataPath);
        ConfirmHashes(gameAssembly, protectedMetadata, options.AllowHashMismatch);

        int tableOffset = RvaToFileOffset(gameAssembly, 0x43DD130);
        byte[] substitutionTable = gameAssembly.AsSpan(tableOffset, 256).ToArray();
        if (substitutionTable.Distinct().Count() != 256)
        {
            throw new InvalidDataException(
                "The metadata substitution table is not a permutation");
        }

        byte[] shuffledHeader = Transform(
            protectedMetadata.AsSpan(0, ProtectedHeaderSize),
            substitutionTable,
            0x6C3D8CBE44CD3DFA);
        uint[] headerWords = new uint[144];
        for (int index = 0; index < headerWords.Length; index++)
        {
            headerWords[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                shuffledHeader.AsSpan(index * sizeof(uint), sizeof(uint)));
        }

        if (options.Verbose)
        {
            PrintHeaderWords(headerWords);
        }

        if (options.AnalyzeLayout)
        {
            AnalyzeLayout(headerWords);
        }

        int payloadEnd = ValidateLayout(protectedMetadata.Length);
        byte[] payload = protectedMetadata
            .AsSpan(PayloadStart, payloadEnd - PayloadStart)
            .ToArray();
        foreach (EncryptedSection section in EncryptedSections)
        {
            int relativeOffset = section.Offset - PayloadStart;
            byte[] decrypted = Transform(
                payload.AsSpan(relativeOffset, section.Length),
                substitutionTable,
                section.Seed);
            decrypted.CopyTo(payload, relativeOffset);
            Console.WriteLine(
                $"Decrypted section {section.Index,2} ({section.Name}): " +
                $"0x{section.Offset:X}-0x{section.Offset + section.Length:X}");
        }

        byte[] rebuilt = BuildCanonicalMetadata(payload);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
        File.WriteAllBytes(options.OutputPath, rebuilt);
        string outputHash = Hashing.CalculateSha256(rebuilt);

        Console.WriteLine(
            $"Ignored 0x{protectedMetadata.Length - payloadEnd:X} bytes " +
            "of protector trailer data");
        Console.WriteLine(
            $"Wrote canonical metadata v31: {options.OutputPath} " +
            $"({rebuilt.Length:N0} bytes)");
        Console.WriteLine($"Output SHA-256: {outputHash}");
        return new MetadataRebuildResult(options.OutputPath, rebuilt.Length, outputHash);
    }

    private static void ConfirmHashes(
        byte[] gameAssembly,
        byte[] protectedMetadata,
        bool allowMismatch)
    {
        var mismatches = new List<(string Name, string Expected, string Actual)>();
        string actualGameAssemblyHash = Hashing.CalculateSha256(gameAssembly);
        string actualMetadataHash = Hashing.CalculateSha256(protectedMetadata);
        if (actualGameAssemblyHash != GameAssemblySha256)
        {
            mismatches.Add(("GameAssembly.dll", GameAssemblySha256, actualGameAssemblyHash));
        }

        if (actualMetadataHash != MetadataSha256)
        {
            mismatches.Add(("global-metadata.dat", MetadataSha256, actualMetadataHash));
        }

        if (mismatches.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine("WARNING: Unsupported input SHA-256 hash(es):");
        foreach ((string name, string expected, string actual) in mismatches)
        {
            Console.Error.WriteLine($"  {name}");
            Console.Error.WriteLine($"    Actual:   {actual}");
            Console.Error.WriteLine($"    Expected: {expected}");
        }

        Console.Error.WriteLine(
            "This tool is build-specific. Continuing may fail or produce unusable output.");
        if (allowMismatch)
        {
            Console.Error.WriteLine(
                "Continuing because --allow-hash-mismatch was specified.");
            return;
        }

        Console.Error.Write("Continue anyway? [y/N]: ");
        string answer = Console.ReadLine() ?? string.Empty;
        if (!answer.Equals("y", StringComparison.OrdinalIgnoreCase) &&
            !answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cancelled because an input file hash did not match");
        }

        Console.Error.WriteLine(
            "Continuing with unsupported hashes at the user's request.");
    }

    private static int ValidateLayout(int protectedMetadataLength)
    {
        if (SectionLengths.Length != 31)
        {
            throw new InvalidDataException(
                "Expected exactly 31 metadata-v31 section lengths");
        }

        int position = PayloadStart;
        for (int sectionIndex = 0; sectionIndex < SectionLengths.Length; sectionIndex++)
        {
            int length = SectionLengths[sectionIndex];
            if (KnownSectionPositions.TryGetValue(sectionIndex, out int knownPosition) &&
                position != knownPosition)
            {
                throw new InvalidDataException(
                    $"Section {sectionIndex} layout mismatch: expected " +
                    $"0x{knownPosition:X}, reconstructed 0x{position:X}");
            }

            if (KnownSectionLengths.TryGetValue(sectionIndex, out int knownLength) &&
                length != knownLength)
            {
                throw new InvalidDataException(
                    $"Section {sectionIndex} length mismatch: expected " +
                    $"0x{knownLength:X}, reconstructed 0x{length:X}");
            }

            position += length;
        }

        if (position > protectedMetadataLength)
        {
            throw new InvalidDataException(
                $"Reconstructed payload ends beyond the input file: 0x{position:X}");
        }

        return position;
    }

    private static byte[] BuildCanonicalMetadata(byte[] payload)
    {
        byte[] rebuilt = new byte[CanonicalHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(rebuilt.AsSpan(0, 4), 0xFAB11BAF);
        BinaryPrimitives.WriteUInt32LittleEndian(rebuilt.AsSpan(4, 4), 31);

        int canonicalOffset = CanonicalHeaderSize;
        for (int sectionIndex = 0; sectionIndex < SectionLengths.Length; sectionIndex++)
        {
            int headerOffset = 8 + sectionIndex * 8;
            BinaryPrimitives.WriteInt32LittleEndian(
                rebuilt.AsSpan(headerOffset, 4),
                canonicalOffset);
            BinaryPrimitives.WriteInt32LittleEndian(
                rebuilt.AsSpan(headerOffset + 4, 4),
                SectionLengths[sectionIndex]);
            canonicalOffset += SectionLengths[sectionIndex];
        }

        payload.CopyTo(rebuilt, CanonicalHeaderSize);
        if (canonicalOffset != rebuilt.Length)
        {
            throw new InvalidDataException(
                "Internal output-length mismatch: " +
                $"header ends at 0x{canonicalOffset:X}, output is 0x{rebuilt.Length:X}");
        }

        return rebuilt;
    }

    private static byte[] Transform(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> table,
        ulong seed)
    {
        byte[] output = new byte[data.Length];
        ulong state = seed;
        unchecked
        {
            for (int index = 0; index < data.Length; index++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                ulong selector = state ^ (state >> 8) ^ (state >> 16) ^ (state >> 24);
                output[index] = (byte)(data[index] ^ table[(int)(selector & 0xFF)]);
            }
        }

        return output;
    }

    private static int RvaToFileOffset(ReadOnlySpan<byte> pe, int rva)
    {
        if (!pe[..2].SequenceEqual("MZ"u8))
        {
            throw new InvalidDataException("GameAssembly input is not a PE file");
        }

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(pe.Slice(0x3C, 4));
        if (!pe.Slice(peOffset, 4).SequenceEqual("PE\0\0"u8))
        {
            throw new InvalidDataException("GameAssembly has an invalid PE signature");
        }

        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(pe.Slice(peOffset + 6, 2));
        int optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(
            pe.Slice(peOffset + 20, 2));
        int sectionTable = peOffset + 24 + optionalHeaderSize;
        for (int index = 0; index < sectionCount; index++)
        {
            int entry = sectionTable + index * 40;
            int virtualSize = BinaryPrimitives.ReadInt32LittleEndian(pe.Slice(entry + 8, 4));
            int virtualAddress = BinaryPrimitives.ReadInt32LittleEndian(pe.Slice(entry + 12, 4));
            int rawSize = BinaryPrimitives.ReadInt32LittleEndian(pe.Slice(entry + 16, 4));
            int rawOffset = BinaryPrimitives.ReadInt32LittleEndian(pe.Slice(entry + 20, 4));
            if (virtualAddress <= rva && rva < virtualAddress + Math.Max(virtualSize, rawSize))
            {
                return rawOffset + rva - virtualAddress;
            }
        }

        throw new InvalidDataException($"RVA 0x{rva:X} is not backed by a PE section");
    }

    private static void PrintHeaderWords(IReadOnlyList<uint> headerWords)
    {
        Console.WriteLine("Decrypted protected header words:");
        for (int index = 0; index < headerWords.Count; index += 8)
        {
            IEnumerable<string> words = Enumerable
                .Range(index, Math.Min(8, headerWords.Count - index))
                .Select(wordIndex => $"{wordIndex * 4:X3}:{headerWords[wordIndex]:X8}");
            Console.WriteLine("  " + string.Join(' ', words));
        }
    }

    private static void AnalyzeLayout(IReadOnlyList<uint> headerWords)
    {
        PrintAnchorSegment(headerWords, 6, 10, 0x86E158, 0xB9E1B0);
        PrintAnchorSegment(headerWords, 12, 20, 0xC91C24, 0xF6960C);
    }

    private static void PrintAnchorSegment(
        IReadOnlyList<uint> headerWords,
        int first,
        int last,
        int start,
        int end)
    {
        const int maxDelta = 0x2000;
        var results = new List<IReadOnlyList<int>>();
        uint[] candidateLengths = headerWords.Distinct().Order().ToArray();

        bool PositionIsRepresented(int position) =>
            headerWords.Any(word => Math.Abs((long)word - position) <= maxDelta);

        void Visit(int section, int position, IReadOnlyList<int> path)
        {
            if (results.Count >= 10_000)
            {
                return;
            }

            if (section > last)
            {
                if (position == end)
                {
                    results.Add(path);
                }

                return;
            }

            int recordSize = SectionRecordSizes[section];
            int remainingSections = last - section;
            foreach (uint candidate in candidateLengths)
            {
                if (candidate > int.MaxValue)
                {
                    continue;
                }

                int length = (int)candidate;
                if (length % recordSize != 0 || position + length > end)
                {
                    continue;
                }

                int nextPosition = position + length;
                if (remainingSections > 0 && !PositionIsRepresented(nextPosition))
                {
                    continue;
                }

                Visit(section + 1, nextPosition, [.. path, length]);
            }
        }

        Visit(first, start, []);
        Console.WriteLine(
            $"Anchor segment {first}-{last}: 0x{start:X}->0x{end:X}; " +
            $"{results.Count} candidates");
        foreach (IReadOnlyList<int> result in results.Take(100))
        {
            Console.WriteLine("  " + string.Join(' ', result.Select(length => $"0x{length:X}")));
        }
    }

    private sealed record EncryptedSection(
        int Index,
        string Name,
        int Offset,
        int Length,
        ulong Seed);
}
