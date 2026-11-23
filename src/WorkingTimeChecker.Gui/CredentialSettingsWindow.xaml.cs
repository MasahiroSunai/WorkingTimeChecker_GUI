using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkingTimeChecker.Core.Security;

namespace WorkingTimeChecker.Gui;

/// <summary>
/// Windows資格情報マネージャーの
/// 登録・更新・削除を行う画面です。
/// </summary>
public partial class CredentialSettingsWindow : Window
{
    private const string CredentialUserName =
        "environment-variable";

    private static readonly string[] WebAttendanceNames =
    [
        "APPS_ID",
        "APPS_PASSWORD",
        "APPS_TOTP_SECRET",
    ];

    private static readonly string[] ConfluenceNames =
    [
        "DNDEV_AWS_ID",
        "DNDEV_AWS_PASSWORD",
        "GENIIE_ID",
        "GENIIE_PASSWORD",
    ];

    private static readonly string[] RocketChatNames =
    [
        "ROCKETCHAT_USER_ID",
        "ROCKETCHAT_TOKEN",
    ];

    private readonly WindowsCredentialService
        _credentialService =
            new();

    /// <summary>
    /// 認証情報設定画面を作成します。
    /// </summary>
    public CredentialSettingsWindow()
    {
        InitializeComponent();

        Loaded +=
            CredentialSettingsWindow_Loaded;
    }

    private void CredentialSettingsWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        LoadRegisteredUserIds();
        RefreshStatus();

        MessageText.Text =
            "パスワード、TOTP秘密鍵、トークンは表示しません。" +
            "変更する項目だけ入力してください。";
    }

    private void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadRegisteredUserIds();
        RefreshStatus();

        MessageText.Text =
            "登録状態を更新しました。";
    }

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var savedNames =
                new List<string>();

            SaveIfEntered(
                "DNDEV_AWS_ID",
                DnDevIdTextBox.Text,
                savedNames);

            SaveIfEntered(
                "DNDEV_AWS_PASSWORD",
                DnDevPasswordBox.Password,
                savedNames);

            SaveIfEntered(
                "GENIIE_ID",
                GeniieIdTextBox.Text,
                savedNames);

            SaveIfEntered(
                "GENIIE_PASSWORD",
                GeniiePasswordBox.Password,
                savedNames);

            SaveIfEntered(
                "APPS_ID",
                AppsIdTextBox.Text,
                savedNames);

            SaveIfEntered(
                "APPS_PASSWORD",
                AppsPasswordBox.Password,
                savedNames);

            SaveIfEntered(
                "APPS_TOTP_SECRET",
                AppsTotpSecretBox.Password,
                savedNames);

            SaveIfEntered(
                "ROCKETCHAT_USER_ID",
                RocketChatUserIdTextBox.Text,
                savedNames);

            SaveIfEntered(
                "ROCKETCHAT_TOKEN",
                RocketChatTokenBox.Password,
                savedNames);

            if (savedNames.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "変更する項目を1つ以上入力してください。" +
                    Environment.NewLine +
                    "空欄の項目は既存値を変更しません。",
                    "入力なし",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            ClearSecretInputValues();
            LoadRegisteredUserIds();
            RefreshStatus();

            MessageText.Text =
                $"{savedNames.Count}項目を保存しました。";

            MessageBox.Show(
                this,
                $"{savedNames.Count}項目を" +
                "Windows資格情報マネージャーへ保存しました。",
                "保存完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageText.Text =
                "保存に失敗しました。";

            MessageBox.Show(
                this,
                exception.Message,
                "認証情報の保存エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DeleteAllButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                this,
                "WorkingTimeCheckerの認証情報9項目を" +
                "すべて削除します。" +
                Environment.NewLine +
                Environment.NewLine +
                "削除後は処理を実行できなくなります。" +
                Environment.NewLine +
                "よろしいですか？",
                "認証情報の削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var removedCount = 0;

            foreach (var environmentVariableName in
                     CredentialNames
                         .RequiredEnvironmentVariableNames)
            {
                var targetName =
                    CredentialNames.GetTargetName(
                        environmentVariableName);

                if (_credentialService.Remove(
                        targetName))
                {
                    removedCount++;
                }
            }

            ClearInputValues();
            RefreshStatus();

            MessageText.Text =
                $"{removedCount}項目を削除しました。";

            MessageBox.Show(
                this,
                $"{removedCount}項目を削除しました。",
                "削除完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "認証情報の削除エラー",
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

    private void SaveIfEntered(
        string environmentVariableName,
        string? value,
        ICollection<string> savedNames)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return;
        }

        var targetName =
            CredentialNames.GetTargetName(
                environmentVariableName);

        _credentialService.Save(
            targetName,
            CredentialUserName,
            value);

        var savedCredential =
            _credentialService.Get(
                targetName);

        if (savedCredential is null ||
            !string.Equals(
                savedCredential.Secret,
                value,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "保存した認証情報を確認できません: " +
                environmentVariableName);
        }

        savedNames.Add(
            environmentVariableName);
    }

    /// <summary>
    /// 保存済みのIDだけを画面へ表示します。
    /// パスワード、TOTP秘密鍵、トークンは表示しません。
    /// </summary>
    private void LoadRegisteredUserIds()
    {
        DnDevIdTextBox.Text =
            GetRegisteredValue(
                "DNDEV_AWS_ID");

        GeniieIdTextBox.Text =
            GetRegisteredValue(
                "GENIIE_ID");

        AppsIdTextBox.Text =
            GetRegisteredValue(
                "APPS_ID");

        RocketChatUserIdTextBox.Text =
            GetRegisteredValue(
                "ROCKETCHAT_USER_ID");
    }

    /// <summary>
    /// Windows資格情報マネージャーから
    /// 登録済みの値を取得します。
    /// </summary>
    private string GetRegisteredValue(
        string environmentVariableName)
    {
        var targetName =
            CredentialNames.GetTargetName(
                environmentVariableName);

        var credential =
            _credentialService.Get(
                targetName);

        return
            credential?.Secret
            ?? string.Empty;
    }

    /// <summary>
    /// パスワード、TOTP秘密鍵、トークンの
    /// 登録済み表示を更新します。
    /// 実際の値や文字数は表示しません。
    /// </summary>
    private void RefreshSecretMaskStatus()
    {
        UpdateSecretMaskStatus(
            AppsPasswordStatusText,
            "APPS_PASSWORD");

        UpdateSecretMaskStatus(
            AppsTotpSecretStatusText,
            "APPS_TOTP_SECRET");

        UpdateSecretMaskStatus(
            DnDevPasswordStatusText,
            "DNDEV_AWS_PASSWORD");

        UpdateSecretMaskStatus(
            GeniiePasswordStatusText,
            "GENIIE_PASSWORD");

        UpdateSecretMaskStatus(
            RocketChatTokenStatusText,
            "ROCKETCHAT_TOKEN");
    }

    /// <summary>
    /// 指定した秘密情報が登録済みの場合だけ
    /// 固定長のマスクを表示します。
    /// </summary>
    private void UpdateSecretMaskStatus(
        TextBlock statusText,
        string environmentVariableName)
    {
        var targetName =
            CredentialNames.GetTargetName(
                environmentVariableName);

        var credential =
            _credentialService.Get(
                targetName);

        statusText.Visibility =
            credential is not null &&
            !string.IsNullOrEmpty(
                credential.Secret)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void RefreshStatus()
    {
                RefreshSecretMaskStatus();
UpdateGroupStatus(
            WebAttendanceStatusText,
            WebAttendanceNames);

        UpdateGroupStatus(
            ConfluenceStatusText,
            ConfluenceNames);

        UpdateGroupStatus(
            RocketChatStatusText,
            RocketChatNames);
    }

    private void UpdateGroupStatus(
        TextBlock statusText,
        IReadOnlyCollection<string> names)
    {
        var registeredCount =
            names.Count(
                IsRegistered);

        if (registeredCount == names.Count)
        {
            statusText.Text =
                $"登録済み（{registeredCount}/{names.Count}）";

            statusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        10,
                        120,
                        65));

            return;
        }

        var missingNames =
            names
                .Where(
                    name =>
                        !IsRegistered(name))
                .ToArray();

        statusText.Text =
            $"未登録あり（{registeredCount}/{names.Count}）" +
            Environment.NewLine +
            "不足: " +
            string.Join(
                ", ",
                missingNames);

        statusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    180,
                    45,
                    45));
    }

    private bool IsRegistered(
        string environmentVariableName)
    {
        var targetName =
            CredentialNames.GetTargetName(
                environmentVariableName);

        var credential =
            _credentialService.Get(
                targetName);

        return
            credential is not null &&
            !string.IsNullOrEmpty(
                credential.Secret);
    }

    /// <summary>
    /// 保存後に秘密情報の入力欄だけを消します。
    /// ID欄は、保存内容を確認できるように残します。
    /// </summary>
    private void ClearSecretInputValues()
    {
        DnDevPasswordBox.Clear();
        GeniiePasswordBox.Clear();

        AppsPasswordBox.Clear();
        AppsTotpSecretBox.Clear();

        RocketChatTokenBox.Clear();
    }

    private void ClearInputValues()
    {
        DnDevIdTextBox.Clear();
        DnDevPasswordBox.Clear();
        GeniieIdTextBox.Clear();
        GeniiePasswordBox.Clear();

        AppsIdTextBox.Clear();
        AppsPasswordBox.Clear();
        AppsTotpSecretBox.Clear();

        RocketChatUserIdTextBox.Clear();
        RocketChatTokenBox.Clear();
    }
}