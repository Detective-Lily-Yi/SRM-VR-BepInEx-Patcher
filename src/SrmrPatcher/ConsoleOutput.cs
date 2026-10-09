namespace SrmrPatcher
{
    internal static class ConsoleOutput
    {
        public static void PrintSummary(IEnumerable<KeyValuePair<string, object?>> values)
        {
            KeyValuePair<string, object?>[] entries = values.ToArray();
            int width = entries.Max(entry => entry.Key.Length);
            foreach ((string name, object? value) in entries)
            {
                Console.WriteLine($"{name.PadRight(width)} : {value}");
            }
        }
    }
}
