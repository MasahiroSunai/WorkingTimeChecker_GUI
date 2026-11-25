using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WorkingTimeChecker.Core;
using WorkingTimeChecker.Core.Application;
using WorkingTimeChecker.Core.Configuration;

namespace WorkingTimeChecker.Gui;

/// <summary>
/// config.yamlを編集する画面です。
/// 既存の項目順、コメント、階層を維持して値だけ更新します。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ConfigBackupService _configBackupService =
        new();

    private readonly ConfigFileService _configFileService =
        new();

    private readonly ConfigValidationService _configValidationService;

    private const string DryRunKey =
        "system.dry_run";

    private static readonly Regex SettingLinePattern =
        new(
            @"^(?<indent>\s*)(?<key>[^:#]+):(?<rest>.*)$",
            RegexOptions.Compiled);

    private static readonly HashSet<string> BooleanKeys =
        new(
            StringComparer.Ordinal)
        {
            "system.send_mail",
        };

    private readonly string _configPath;

    private readonly Dictionary<string, FrameworkElement>
        _editors =
            new(StringComparer.Ordinal);

    private readonly Dictionary<string, string>
        _labels =
            new(StringComparer.Ordinal)
            {
                ["system.encoding"] = "文字コード",
                ["system.timezone"] = "タイムゾーン",
                ["system.toAddress"] = "メール送信先",
                ["system.target_month"] = "対象年月",
                ["system.dry_run"] = "実行モード",
                ["system.send_mail"] = "メール送信",

                ["confluence.dndev.url"] = "dndev URL",
                ["confluence.dndev.download_page_id"] =
                    "dndev 取得ページID",
                ["confluence.dndev.upload_page_id"] =
                    "dndev 更新ページID",
                ["confluence.dndev.cert_dir"] =
                    "dndev 証明書フォルダー",

                ["confluence.geniie.url"] = "geniie URL",
                ["confluence.geniie.download_page_id"] =
                    "geniie 取得ページID",
                ["confluence.geniie.upload_page_id"] =
                    "geniie 更新ページID",

                ["apps.base_url"] = "Web勤怠 URL",
                ["apps.filter_id"] = "レポートID",
                ["apps.csv_suffix"] = "CSVファイル名末尾",

                ["paths.workload_download_dir"] =
                    "工数表ダウンロード先",
                ["paths.workload_aggregate_file"] =
                    "工数集計ファイル",
                ["paths.web_attendance_file"] =
                    "Web勤怠CSV",
                ["paths.member_file"] =
                    "メンバーファイル",
                ["paths.work_map_file"] =
                    "工数マッピングファイル",

                ["rocketchat.url"] = "Rocket.Chat URL",
                ["rocketchat.room"] = "通知先ルーム",

                ["selenium.login.username_input"] =
                    "ユーザーID入力欄",
                ["selenium.login.password_input"] =
                    "パスワード入力欄",
                ["selenium.login.login_button"] =
                    "ログインボタン",
                ["selenium.totp.input"] =
                    "TOTP入力欄",
                ["selenium.totp.submit_button"] =
                    "TOTP送信ボタン",
                ["selenium.export.encoding_select"] =
                    "文字コード選択欄",
                ["selenium.export.encoding_value"] =
                    "文字コード選択値",
                ["selenium.export.format_select"] =
                    "形式選択欄",
                ["selenium.export.format_value"] =
                    "形式選択値",
                ["selenium.export.export_button"] =
                    "エクスポートボタン",
            };

    public bool SettingsSaved { get; private set; }

    public SettingsWindow(
        string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configPath);

        _configPath =
            Path.GetFullPath(
                configPath);

        _configValidationService =
            new ConfigValidationService(
                _configFileService);

        InitializeComponent();

        Loaded +=
            SettingsWindow_Loaded;
    }

    private void SettingsWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        LoadSettings();
    }

    private void ReloadButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadSettings();

        MessageText.Text =
            "設定を再読み込みしました。";
    }

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            ValidateInputValues();

            var originalLines =
                File.ReadAllLines(
                    _configPath,
                    Encoding.UTF8);

            var updatedLines =
                UpdateLines(
                    originalLines);

            var backupPath =
                _configBackupService.CreateBackup(
                    _configPath,
                    ApplicationPaths.ConfigBackupDirectory);

            var temporaryPath =
                _configPath +
                ".tmp";

            File.WriteAllLines(
                temporaryPath,
                updatedLines,
                new UTF8Encoding(false));

            var validationResult =
                _configValidationService.ValidateFile(temporaryPath);

            if (!validationResult.IsValid)
            {
                throw new InvalidDataException(
                    string.Join(
                        Environment.NewLine,
                        validationResult.Errors));
            }

            File.Copy(
                temporaryPath,
                _configPath,
                overwrite: true);

            File.Delete(
                temporaryPath);

            SettingsSaved =
                true;

            MessageText.Text =
                "設定を保存しました。バックアップ: " +
                Path.GetFileName(
                    backupPath);

            MessageBox.Show(
                this,
                "config.yamlを保存しました。" +
                Environment.NewLine +
                Environment.NewLine +
                "バックアップ:" +
                Environment.NewLine +
                backupPath,
                "保存完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageText.Text =
                "設定の保存に失敗しました。";

            MessageBox.Show(
                this,
                exception.Message,
                "設定保存エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void LoadSettings()
    {
        if (!File.Exists(
                _configPath))
        {
            throw new FileNotFoundException(
                "config.yamlが見つかりません。",
                _configPath);
        }

        _editors.Clear();

        SystemSettingsPanel.Children.Clear();
        PathsSettingsPanel.Children.Clear();
        ConfluenceSettingsPanel.Children.Clear();
        ServiceSettingsPanel.Children.Clear();
        SeleniumSettingsPanel.Children.Clear();

        var settings =
            ParseSettings(
                File.ReadAllLines(
                    _configPath,
                    Encoding.UTF8));

        foreach (var setting in settings)
        {
            AddEditor(
                setting.Key,
                setting.Value);
        }

        MessageText.Text =
            "設定ファイル: " +
            _configPath;
    }

    private static Dictionary<string, string>
        ParseSettings(
            IReadOnlyList<string> lines)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        var sectionKeys =
            new Dictionary<int, string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(
                    line) ||
                line.TrimStart().StartsWith(
                    "#",
                    StringComparison.Ordinal) ||
                line.TrimStart().StartsWith(
                    "-",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var match =
                SettingLinePattern.Match(
                    line);

            if (!match.Success)
            {
                continue;
            }

            var indent =
                match.Groups["indent"].Value.Length;

            var level =
                indent / 2;

            var key =
                match.Groups["key"].Value.Trim();

            var rest =
                match.Groups["rest"].Value.Trim();

            var obsoleteLevels =
                sectionKeys.Keys
                    .Where(
                        existingLevel =>
                            existingLevel >= level)
                    .ToArray();

            foreach (var obsoleteLevel in
                     obsoleteLevels)
            {
                sectionKeys.Remove(
                    obsoleteLevel);
            }

            if (string.IsNullOrEmpty(
                    rest))
            {
                sectionKeys[level] =
                    key;

                continue;
            }

            var pathParts =
                sectionKeys
                    .Where(
                        pair =>
                            pair.Key < level)
                    .OrderBy(
                        pair =>
                            pair.Key)
                    .Select(
                        pair =>
                            pair.Value)
                    .Append(
                        key);

            var fullKey =
                string.Join(
                    ".",
                    pathParts);

            result[fullKey] =
                RemoveMatchingQuotes(
                    RemoveInlineComment(
                        rest));
        }

        return result;
    }

    private void AddEditor(
        string fullKey,
        string value)
    {
        var panel =
            ResolvePanel(
                fullKey);

        var container =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        12),
            };

        container.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        230),
            });

        container.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star),
            });

        var label =
            new TextBlock
            {
                Text =
                    GetDisplayName(
                        fullKey),

                VerticalAlignment =
                    VerticalAlignment.Center,

                Margin =
                    new Thickness(
                        0,
                        0,
                        14,
                        0),

                ToolTip =
                    fullKey,
            };

        Grid.SetColumn(
            label,
            0);

        container.Children.Add(
            label);

        FrameworkElement editor;

        if (string.Equals(
                fullKey,
                DryRunKey,
                StringComparison.Ordinal))
        {
            var dryRun =
                bool.TryParse(
                    value,
                    out var dryRunValue) &&
                dryRunValue;

            editor =
                new ComboBox
                {
                    MinHeight =
                        32,

                    VerticalContentAlignment =
                        VerticalAlignment.Center,

                    ToolTip =
                        "本番実行ではdry_run: false、" +
                        "テスト実行ではdry_run: trueになります。",

                    ItemsSource =
                        new[]
                        {
                            "本番実行",
                            "テスト実行",
                        },

                    SelectedIndex =
                        dryRun
                            ? 1
                            : 0,
                };
        }
        else if (BooleanKeys.Contains(
                     fullKey))
        {
            editor =
                new CheckBox
                {
                    IsChecked =
                        bool.TryParse(
                            value,
                            out var boolValue) &&
                        boolValue,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Content =
                        "有効",
                };
        }
        else
        {
            editor =
                new TextBox
                {
                    Text =
                        value,

                    ToolTip =
                        fullKey,
                };
        }

        Grid.SetColumn(
            editor,
            1);

        container.Children.Add(
            editor);

        panel.Children.Add(
            container);

        _editors[fullKey] =
            editor;
    }

    private StackPanel ResolvePanel(
        string fullKey)
    {
        if (fullKey.StartsWith(
                "system.",
                StringComparison.Ordinal))
        {
            return SystemSettingsPanel;
        }

        if (fullKey.StartsWith(
                "paths.",
                StringComparison.Ordinal))
        {
            return PathsSettingsPanel;
        }

        if (fullKey.StartsWith(
                "confluence.",
                StringComparison.Ordinal))
        {
            return ConfluenceSettingsPanel;
        }

        if (fullKey.StartsWith(
                "selenium.",
                StringComparison.Ordinal))
        {
            return SeleniumSettingsPanel;
        }

        return ServiceSettingsPanel;
    }

    private string GetDisplayName(
        string fullKey)
    {
        return
            _labels.TryGetValue(
                fullKey,
                out var displayName)
                ? displayName
                : fullKey;
    }

    private void ValidateInputValues()
    {
        var targetMonth =
            GetStringValue(
                "system.target_month");

        if (!Regex.IsMatch(
                targetMonth,
                @"^\d{4}[/-]\d{1,2}$"))
        {
            throw new InvalidDataException(
                "対象年月は2026/11の形式で入力してください。");
        }

        foreach (var urlKey in new[]
                 {
                     "confluence.dndev.url",
                     "confluence.geniie.url",
                     "apps.base_url",
                     "rocketchat.url",
                 })
        {
            var value =
                GetStringValue(
                    urlKey);

            if (!Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidDataException(
                    GetDisplayName(urlKey) +
                    "のURL形式が不正です。");
            }
        }

        foreach (var requiredKey in
                 _editors.Keys)
        {
            if (BooleanKeys.Contains(
                    requiredKey) ||
                string.Equals(
                    requiredKey,
                    DryRunKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    GetStringValue(
                        requiredKey)))
            {
                throw new InvalidDataException(
                    GetDisplayName(requiredKey) +
                    "が空です。");
            }
        }
    }

    private string[] UpdateLines(
        IReadOnlyList<string> originalLines)
    {
        var result =
            originalLines.ToArray();

        var sectionKeys =
            new Dictionary<int, string>();

        for (var index = 0;
             index < result.Length;
             index++)
        {
            var line =
                result[index];

            var match =
                SettingLinePattern.Match(
                    line);

            if (!match.Success)
            {
                continue;
            }

            var indentText =
                match.Groups["indent"].Value;

            var level =
                indentText.Length / 2;

            var key =
                match.Groups["key"].Value.Trim();

            var rest =
                match.Groups["rest"].Value.Trim();

            var obsoleteLevels =
                sectionKeys.Keys
                    .Where(
                        existingLevel =>
                            existingLevel >= level)
                    .ToArray();

            foreach (var obsoleteLevel in
                     obsoleteLevels)
            {
                sectionKeys.Remove(
                    obsoleteLevel);
            }

            if (string.IsNullOrEmpty(
                    rest))
            {
                sectionKeys[level] =
                    key;

                continue;
            }

            var fullKey =
                string.Join(
                    ".",
                    sectionKeys
                        .Where(
                            pair =>
                                pair.Key < level)
                        .OrderBy(
                            pair =>
                                pair.Key)
                        .Select(
                            pair =>
                                pair.Value)
                        .Append(
                            key));

            if (!_editors.ContainsKey(
                    fullKey))
            {
                continue;
            }

            result[index] =
                indentText +
                key +
                ": " +
                FormatYamlValue(
                    fullKey);
        }

        return result;
    }

    private string FormatYamlValue(
        string fullKey)
    {
        if (string.Equals(
                fullKey,
                DryRunKey,
                StringComparison.Ordinal))
        {
            var dryRunComboBox =
                (ComboBox)_editors[
                    fullKey];

            return
                dryRunComboBox.SelectedIndex == 1
                    ? "true"
                    : "false";
        }

        if (BooleanKeys.Contains(
                fullKey))
        {
            var checkBox =
                (CheckBox)_editors[
                    fullKey];

            return
                checkBox.IsChecked == true
                    ? "true"
                    : "false";
        }

        var value =
            GetStringValue(
                fullKey);

        return
            "'" +
            value.Replace(
                "'",
                "''",
                StringComparison.Ordinal) +
            "'";
    }

    private string GetStringValue(
        string fullKey)
    {
        if (!_editors.TryGetValue(
                fullKey,
                out var editor))
        {
            throw new InvalidOperationException(
                "設定項目が見つかりません: " +
                fullKey);
        }

        return
            editor is TextBox textBox
                ? textBox.Text.Trim()
                : string.Empty;
    }

    private static string RemoveInlineComment(
        string value)
    {
        if (value.StartsWith(
                "'",
                StringComparison.Ordinal) ||
            value.StartsWith(
                "\"",
                StringComparison.Ordinal))
        {
            return value;
        }

        var commentIndex =
            value.IndexOf(
                " #",
                StringComparison.Ordinal);

        return
            commentIndex >= 0
                ? value[..commentIndex].TrimEnd()
                : value;
    }

    private static string RemoveMatchingQuotes(
        string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        if ((value[0] == '\'' &&
             value[^1] == '\'') ||
            (value[0] == '"' &&
             value[^1] == '"'))
        {
            var unquoted =
                value[1..^1];

            return
                value[0] == '\''
                    ? unquoted.Replace(
                        "''",
                        "'",
                        StringComparison.Ordinal)
                    : unquoted;
        }

        return value;
    }
}
