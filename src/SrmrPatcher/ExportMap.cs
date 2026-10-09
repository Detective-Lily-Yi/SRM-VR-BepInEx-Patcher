namespace SrmrPatcher
{
    internal sealed class ExportMap
    {
        private ExportMap(Dictionary<string, string> names)
        {
            Names = names;
            ProtectedNames = new HashSet<string>(names.Values, StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, string> Names { get; }

        public IReadOnlySet<string> ProtectedNames { get; }

        public static ExportMap Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Export map does not exist", path);
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0].Trim() != "standard_export,renamed_export")
            {
                throw new InvalidDataException("The export map has an invalid header");
            }

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var protectedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                string[] fields = line.Split(',', count: 2);
                if (fields.Length != 2 || string.IsNullOrWhiteSpace(fields[0]) ||
                    string.IsNullOrWhiteSpace(fields[1]))
                {
                    throw new InvalidDataException("The export map contains an empty export name");
                }

                string canonicalName = fields[0].Trim();
                string protectedName = fields[1].Trim();
                if (!names.TryAdd(canonicalName, protectedName))
                {
                    throw new InvalidDataException(
                        $"The export map contains duplicate canonical name {canonicalName}");
                }

                if (!protectedNames.Add(protectedName))
                {
                    throw new InvalidDataException(
                        $"The export map contains duplicate protected name {protectedName}");
                }
            }

            if (names.Count != 234)
            {
                throw new InvalidDataException(
                    $"Expected 234 export mappings, but found {names.Count}");
            }

            return new ExportMap(names);
        }
    }
}
