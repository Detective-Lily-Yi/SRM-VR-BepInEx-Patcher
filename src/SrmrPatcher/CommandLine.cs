namespace SrmrPatcher;

internal sealed class CommandLine
{
    private readonly Dictionary<string, string> options;
    private readonly HashSet<string> flags;

    private CommandLine(
        Dictionary<string, string> options,
        HashSet<string> flags)
    {
        this.options = options;
        this.flags = flags;
    }

    public static CommandLine Parse(
        string[] args,
        IEnumerable<string> optionNames,
        IEnumerable<string> flagNames)
    {
        var knownOptions = new HashSet<string>(optionNames, StringComparer.Ordinal);
        var knownFlags = new HashSet<string>(flagNames, StringComparer.Ordinal);
        knownFlags.Add("--help");

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (knownFlags.Contains(argument))
            {
                if (!flags.Add(argument))
                {
                    throw new ArgumentException($"Duplicate option: {argument}");
                }

                continue;
            }

            if (!knownOptions.Contains(argument))
            {
                throw new ArgumentException($"Unknown option: {argument}");
            }

            if (options.ContainsKey(argument))
            {
                throw new ArgumentException($"Duplicate option: {argument}");
            }

            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option {argument} requires a value");
            }

            options.Add(argument, args[index]);
        }

        return new CommandLine(options, flags);
    }

    public bool HasFlag(string name) => flags.Contains(name);

    public string? GetOptional(string name) =>
        options.TryGetValue(name, out string? value) ? value : null;

    public string GetRequired(string name) =>
        GetOptional(name) ?? throw new ArgumentException($"Missing required option: {name}");
}
