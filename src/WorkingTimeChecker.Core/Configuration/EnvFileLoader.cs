namespace WorkingTimeChecker.Core.Configuration;

public static class EnvFileLoader
{
    public static int Load(string envFilePath)
    {
        if (!File.Exists(envFilePath))
        {
            throw new FileNotFoundException(
                ".envファイルが見つかりません。",
                envFilePath
            );
        }

        var loadedCount = 0;

        foreach (var rawLine in File.ReadLines(envFilePath))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(
                name,
                value,
                EnvironmentVariableTarget.Process
            );

            loadedCount++;
        }

        return loadedCount;
    }
}
