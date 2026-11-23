# WorkingTimeChecker GUI

工数表とWeb勤怠を比較し、相違の確認、Confluenceへの結果出力、Rocket.Chatへの通知を行うWindows向けアプリケーションです。

GUIから実行する処理、設定ファイル、認証情報を管理できます。

## 主な機能

- Confluenceから工数表を取得
- C#による工数表集計
- Web勤怠CSVを取得
- 工数表とWeb勤怠を比較
- Confluenceへ結果を出力
- Rocket.Chatへ通知
- 本番実行とテスト実行を切り替え
- GUIから`config.yaml`を編集
- Config保存前の自動バックアップ
- Configバックアップを最新30件に制限
- Windows資格情報マネージャーによる認証情報管理
- Runtimeのバージョン管理
- Windowsの「インストールされているアプリ」への登録
- スタートメニューとデスクトップへのショートカット作成

## 動作環境

- Windows 10またはWindows 11
- x64環境
- .NET 10 SDK
- Windows PowerShell 5.1以上
- Visual Studio、または.NET 10対応の開発環境

## リポジトリ構成

```text
WorkingTimeChecker_GUI
├─ config
│  └─ config.example.yaml
│
├─ packaging
│  ├─ Install.ps1
│  └─ Uninstall.ps1
│
├─ resources
│  ├─ python-scripts
│  └─ requirements-runtime.lock.txt
│
├─ src
│  ├─ WorkingTimeChecker.Cli
│  ├─ WorkingTimeChecker.Core
│  ├─ WorkingTimeChecker.Gui
│  └─ WorkingTimeChecker.Runner
│
├─ build-gui-release.ps1
├─ build-release.ps1
├─ build-runner.ps1
├─ build-runtime.ps1
├─ prepare-python-runtime.ps1
├─ dev.ps1
└─ WorkingTimeChecker.slnx
```

## Gitで管理しないファイル

次のファイルやフォルダーは、ダウンロードした外部ライブラリ、ビルド生成物、利用者固有情報のため、Gitでは管理しません。

```text
resources/python-runtime/
resources/python-runtime.zip
resources/python-scripts.zip
src/WorkingTimeChecker.Gui/Payload/runtime.zip
src/WorkingTimeChecker.Gui/Payload/runtime-manifest.json
artifacts/
config.yaml
.env
migration/
bin/
obj/
.vs/
```

Gitでは、実際の設定ファイルではなく、次のサンプルだけを管理します。

```text
config/config.example.yaml
```

## 初回セットアップ

### 1. リポジトリを取得

```powershell
git clone `
    https://github.com/MasahiroSunai/WorkingTimeChecker_GUI.git

Set-Location `
    ".\WorkingTimeChecker_GUI"
```

### 2. ブランチを確認

```powershell
git status

git branch `
    --show-current
```

通常は次の状態です。

```text
On branch main
Your branch is up to date with 'origin/main'.
```

### 3. Python Runtimeを準備

Python Runtime本体とダウンロードしたPythonライブラリは、Gitに含まれていません。

次を実行して、開発PC上にPython Runtimeを準備します。

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File ".\prepare-python-runtime.ps1"
```

処理完了後は、次のファイルまたはフォルダーが作成されます。

```text
resources/python-runtime/
resources/python-runtime.zip
```

これらはGitへ追加しないでください。

### 4. Runtimeを作成

例としてRuntime `1.0.1`を作成します。

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File ".\build-runtime.ps1" `
    -RuntimeVersion "1.0.1" `
    -RuntimeIdentifier "win-x64"
```

Runtime作成後、GUIプロジェクトのPayloadへ次が配置されます。

```text
src/WorkingTimeChecker.Gui/Payload/runtime.zip
src/WorkingTimeChecker.Gui/Payload/runtime-manifest.json
```

これらもRelease作成時の生成物であり、Gitでは管理しません。

### 5. ソリューションをビルド

```powershell
dotnet build `
    ".\WorkingTimeChecker.slnx"
```

正常時は、次の4プロジェクトが成功します。

```text
WorkingTimeChecker.Core
WorkingTimeChecker.Cli
WorkingTimeChecker.Gui
WorkingTimeChecker.Runner
```

## Debug版の起動

```powershell
$guiExePath =
    ".\src\WorkingTimeChecker.Gui\bin\Debug\net10.0-windows\WorkingTimeChecker.Gui.exe"

Start-Process `
    -FilePath $guiExePath `
    -WorkingDirectory (
        Split-Path `
            -Parent `
            $guiExePath
    )
```

## 設定ファイル

### Gitで管理する設定サンプル

```text
config/config.example.yaml
```

### 実行時に使用する設定

初回起動またはインストール時に、`config.example.yaml`から次の実設定を作成します。

```text
%LOCALAPPDATA%\PERSOL_CROSS_TECHNOLOGY\
WorkingTimeChecker\Config\config.yaml
```

既存の`config.yaml`がある場合は上書きしません。

### 安全な初期設定

サンプル設定の初期値は次のとおりです。

```yaml
dry_run: true
send_mail: false
```

GUIでは次の名称で表示します。

| `config.yaml`の値 | GUI表示 |
|---|---|
| `dry_run: false` | 本番実行 |
| `dry_run: true` | テスト実行 |

初回はテスト実行を使用し、設定検証と各処理の確認が終わってから本番実行へ切り替えてください。

### Configのバックアップ

GUIの「アプリ設定」で保存すると、現在の`config.yaml`を次へバックアップします。

```text
%LOCALAPPDATA%\PERSOL_CROSS_TECHNOLOGY\
WorkingTimeChecker\Config\Backup
```

バックアップ名の例:

```text
config_20261123_171815_123.yaml
```

バックアップは最新30件を保持し、31件目以降は古いものから自動削除します。

## 認証情報

認証情報は`.env`や`config.yaml`へ保存しません。

Windows資格情報マネージャーへ保存します。

GUIの「認証情報設定」から、次の9項目を登録します。

### Web勤怠

- ユーザーID
- パスワード
- TOTP秘密鍵

### dndev Confluence

- ユーザーID
- パスワード

### geniie Confluence

- ユーザーID
- パスワード

### Rocket.Chat

- ユーザーID
- 認証トークン

認証情報の実値をGit、設定サンプル、ログ、READMEへ記載しないでください。

## GUIの使い方

### アプリ設定

「アプリ設定」では次を編集できます。

- 基本設定
- ファイルパス
- Confluence設定
- Web勤怠設定
- Rocket.Chat設定
- Selenium詳細設定
- 本番実行とテスト実行の切り替え

保存前に入力値を検査し、現在の設定をバックアップします。

### 認証情報設定

「認証情報設定」ではWindows資格情報マネージャーの登録状態を確認し、認証情報を登録または更新できます。

保存済みのパスワード、TOTP秘密鍵、認証トークンは画面に表示しません。

### 設定を検証

本番処理を実行する前に、メイン画面の「設定を検証」を実行してください。

確認対象の例:

- `config.yaml`が存在する
- Runtimeが利用できる
- Windows資格情報マネージャーに必要項目が登録されている
- 工数集計ファイルを参照できる
- 必須のファイルパスが設定されている

## Release構成のビルド

```powershell
dotnet build `
    ".\WorkingTimeChecker.slnx" `
    --configuration Release
```

## Release配布物の作成

例として、GUI `1.0.4`、Runtime `1.0.1`の配布物を作成します。

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File ".\build-gui-release.ps1" `
    -GuiVersion "1.0.4" `
    -RuntimeVersion "1.0.1" `
    -RuntimeIdentifier "win-x64"
```

生成先:

```text
artifacts/WorkingTimeChecker_1.0.4_win-x64
artifacts/WorkingTimeChecker_1.0.4_win-x64.zip
```

Release作成時には次を確認します。

- GUIのRelease発行
- RunnerのRelease発行
- Runtime ZIPの作成
- Runtime manifestの作成
- 秘密情報の混入検査
- `Install.ps1`の追加
- `Uninstall.ps1`の追加
- `release-info.json`の作成
- `SHA256SUMS.txt`の作成
- 配布用ZIPの作成

## Releaseの構成

```text
WorkingTimeChecker_1.0.4_win-x64
├─ Application
│  ├─ WorkingTimeChecker.Gui.exe
│  ├─ Defaults
│  │  └─ config.example.yaml
│  └─ Payload
│     ├─ runtime.zip
│     └─ runtime-manifest.json
│
├─ Runner
│  └─ WorkingTimeChecker.Runner.exe
│
├─ Install.ps1
├─ Uninstall.ps1
├─ release-info.json
└─ SHA256SUMS.txt
```

Releaseに次のファイルを含めないでください。

```text
config.yaml
.env
Config/Backup/
開発ログ
認証情報
個人PCの絶対パス
```

## インストール

配布ZIPを展開し、次を実行します。

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File ".\Install.ps1"
```

インストール先:

```text
%LOCALAPPDATA%\PERSOL_CROSS_TECHNOLOGY\
WorkingTimeChecker
```

主な配置先:

```text
Application/
Runner/
Runtime/
Config/
Logs/
```

インストール後は、次から起動できます。

- デスクトップショートカット
- スタートメニュー
- インストール先のGUI実行ファイル

スタートメニューの構成:

```text
PERSOL CROSS TECHNOLOGY
└─ WorkingTimeChecker
   └─ WorkingTimeChecker
```

## アンインストール

Windowsの次の画面からアンインストールできます。

```text
設定
→ アプリ
→ インストールされているアプリ
→ WorkingTimeChecker
```

または、配布物の`Uninstall.ps1`を実行します。

```powershell
powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File ".\Uninstall.ps1"
```

## 開発時の確認

### 通常ビルド

```powershell
dotnet build `
    ".\WorkingTimeChecker.slnx"
```

### クリーンビルド

```powershell
dotnet clean `
    ".\WorkingTimeChecker.slnx"

dotnet build `
    ".\WorkingTimeChecker.slnx"
```

### Release構成

```powershell
dotnet build `
    ".\WorkingTimeChecker.slnx" `
    --configuration Release
```

### Git状態

```powershell
git status

git log `
    --oneline `
    --decorate `
    -5
```

## Gitへ登録してはいけないファイルの確認

```powershell
git ls-files |
Where-Object {
    $_ -match '^resources/python-runtime' -or
    $_ -match '^resources/python-scripts\.zip$' -or
    $_ -match '^src/WorkingTimeChecker\.Gui/Payload/runtime' -or
    $_ -match '(^|/)config\.yaml$' -or
    $_ -match '(^|/)\.env($|\.)' -or
    $_ -match '^artifacts/' -or
    $_ -match '^migration/'
}
```

何も表示されなければ正常です。

## 個人情報と固定パスの確認

```powershell
git grep `
    -n `
    -I `
    -E `
    'C:\\Users\\|C:\\work\\|@persol|masahiro|sunai' `
    -- `
    ':!*.md'
```

何も表示されなければ正常です。

## 開発用ブランチ

構造変更やMVVM化は、`main`へ直接実装せず、作業ブランチで行います。

```powershell
git switch `
    -c `
    "refactor/mvvm-and-core-structure"
```

リファクタリングは次の順序で進めます。

1. クリーンcloneからのビルド確認
2. Core直下のクラスを役割別フォルダーへ整理
3. Config処理を`Core/Configuration`へ移動
4. `SettingsWindow`をMVVM化
5. `CredentialSettingsWindow`をMVVM化
6. `MainWindow`をMVVM化
7. nullable警告を解消
8. Release配布物を再作成して回帰確認

## 外部ライブラリとライセンス

Python RuntimeとPythonの外部ライブラリはGitへ直接登録しません。

配布物へ同梱する場合は、次を確認してください。

- Python本体のライセンス
- 各Pythonパッケージのライセンス
- 各`.dist-info`内のライセンス文書
- バイナリへ含まれる追加ライブラリのライセンス
- 社内のOSS利用ルール
- 必要な著作権表示と免責事項

完成した配布ZIPは通常のGitコミットへ含めず、GitHub Releasesなどで管理します。

## 安全上の注意

- 実際のパスワードやトークンをソースへ書かない
- 実設定の`config.yaml`をGitへ登録しない
- `.env`をGitへ登録しない
- Python Runtimeや生成ZIPをGitへ登録しない
- 最初はテスト実行を使用する
- 設定検証後に本番実行へ切り替える
- Release前に秘密情報の混入検査を行う