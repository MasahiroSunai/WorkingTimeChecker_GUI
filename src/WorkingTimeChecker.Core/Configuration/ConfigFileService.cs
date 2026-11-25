namespace WorkingTimeChecker.Core.Configuration;

/// <summary>
/// YAML形式のアプリ設定を読み書きします。
/// </summary>
public sealed class ConfigFileService
{
    /// <summary>
    /// 設定ファイルの全行を読み込みます。
    /// </summary>
    public IReadOnlyList<string> ReadAllLines(
        string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configFilePath);

        if (!File.Exists(configFilePath))
        {
            throw new FileNotFoundException(
                "config.yamlが見つかりません。",
                configFilePath);
        }

        return File.ReadAllLines(
            configFilePath);
    }

    /// <summary>
    /// YAML内の指定された値を取得します。
    /// </summary>
    /// <param name="configFilePath">
    /// 設定ファイルのパスです。
    /// </param>
    /// <param name="sectionName">
    /// systemやpathsなどのセクション名です。
    /// </param>
    /// <param name="keyName">
    /// dry_runなどの項目名です。
    /// </param>
    public string? ReadScalar(
        string configFilePath,
        string sectionName,
        string keyName)
    {
        var lines =
            ReadAllLines(
                configFilePath);

        return FindYamlValue(
            lines,
            sectionName,
            keyName);
    }

    /// <summary>
    /// 設定内容を一時ファイル経由で保存します。
    /// </summary>
    public void WriteAllText(
        string configFilePath,
        string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configFilePath);

        ArgumentNullException.ThrowIfNull(
            content);

        var directory =
            Path.GetDirectoryName(
                configFilePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "設定ファイルの保存先フォルダーを取得できません。");
        }

        Directory.CreateDirectory(
            directory);

        var temporaryFilePath =
            configFilePath + ".tmp";

        try
        {
            File.WriteAllText(
                temporaryFilePath,
                content,
                new System.Text.UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.Move(
                temporaryFilePath,
                configFilePath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(
                    temporaryFilePath);
            }
        }
    }

    /// <summary>
    /// 指定されたセクション内の値を取得します。
    /// </summary>
    private static string? FindYamlValue(
        IReadOnlyList<string> lines,
        string sectionName,
        string keyName)
    {
        var inTargetSection =
            false;

        var sectionIndent =
            -1;

        foreach (var sourceLine in lines)
        {
            var line =
                RemoveYamlComment(
                    sourceLine);

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmedLine =
                line.Trim();

            var indent =
                line.Length -
                line.TrimStart().Length;

            if (trimmedLine.Equals(
                    sectionName + ":",
                    StringComparison.Ordinal))
            {
                inTargetSection =
                    true;

                sectionIndent =
                    indent;

                continue;
            }

            if (!inTargetSection)
            {
                continue;
            }

            if (indent <= sectionIndent)
            {
                return null;
            }

            var valuePrefix =
                keyName + ":";

            if (!trimmedLine.StartsWith(
                    valuePrefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var value =
                trimmedLine[
                    valuePrefix.Length..]
                .Trim();

            return UnquoteYamlValue(
                value);
        }

        return null;
    }

    /// <summary>
    /// 引用符の外側にあるYAMLコメントを除去します。
    /// </summary>
    private static string RemoveYamlComment(
        string line)
    {
        var isInsideSingleQuote =
            false;

        var isInsideDoubleQuote =
            false;

        for (var index = 0;
             index < line.Length;
             index++)
        {
            var character =
                line[index];

            if (character == '\'' &&
                !isInsideDoubleQuote)
            {
                isInsideSingleQuote =
                    !isInsideSingleQuote;

                continue;
            }

            if (character == '"' &&
                !isInsideSingleQuote)
            {
                isInsideDoubleQuote =
                    !isInsideDoubleQuote;

                continue;
            }

            if (character == '#' &&
                !isInsideSingleQuote &&
                !isInsideDoubleQuote)
            {
                return line[..index];
            }
        }

        return line;
    }

    /// <summary>
    /// YAML値を囲む引用符を除去します。
    /// </summary>
    private static string UnquoteYamlValue(
        string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var hasDoubleQuotes =
            value.StartsWith(
                '"') &&
            value.EndsWith(
                '"');

        var hasSingleQuotes =
            value.StartsWith(
                '\'') &&
            value.EndsWith(
                '\'');

        if (hasDoubleQuotes ||
            hasSingleQuotes)
        {
            return value[1..^1];
        }

        return value;
    }
}
