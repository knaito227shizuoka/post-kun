# post-kun 計画書

自作アプリ(ローカルの Web API など)に対して GET / POST などの HTTP リクエストを送り、レスポンスを確認するための Windows デスクトップアプリ。
Postman を小さく・軽く・オフラインで使える形にしたイメージ。

`open-folder` の作り方(技術スタック・フォルダ構成・テスト方針・補助スクリプト・ドキュメント分担)を踏襲する。
違いは **常駐 + ホットキー呼び出しではなく、普通のウィンドウを開いて操作するアプリ** である点。

---

## 1. 開発方針(open-folder から引き継ぐもの)

| 項目 | 方針 |
|---|---|
| 言語 / UI | C# / WinForms |
| ターゲット | `net6.0-windows`(インストール済み SDK 6.0.300 に合わせる) |
| 依存 | **NuGet パッケージなし**。`HttpClient` / `System.Text.Json` など .NET 標準ライブラリのみ |
| nullable | 有効。警告を放置しない |
| DPI | `PerMonitorV2` |
| 言語 | UI・メッセージ・コメントは日本語 |
| csproj | ルートに置き、`source/` を暗黙 glob でコンパイル。`tests/**` は除外し `InternalsVisibleTo` を付与 |
| テスト | オフラインのため xUnit 等は使わず、依存ゼロの自前コンソールランナー(終了コード=失敗数) |
| ドキュメント | 利用者向け=`README.md`、AIエージェント向け=`AGENTS.md`(`CLAUDE.md` は AGENTS.md を読む指示だけ) |
| 補助スクリプト | `restart.ps1` / `restart.bat`(publish して起動) |

### open-folder と変えるもの

| open-folder | post-kun |
|---|---|
| 不可視の MainForm + グローバルホットキー | 通常のウィンドウ(タスクバーに表示、リサイズ可) |
| 常駐 / スタートアップ登録 | 使うときに起動して閉じたら終了。`startup.bat` は作らない |
| 2個目は静かに終了 | 2個目は既存ウィンドウを前面に出して終了(後述) |
| 外部プロセス(`cmd /c code`)起動 | `HttpClient` で HTTP 通信 |

---

## 2. 利用者から見える完成形

### 画面レイアウト(案)

```
┌───────────────────────────────────────────────────────────────────┐
│ post-kun                                          [環境: local ▼] │
├──────────────┬────────────────────────────────────────────────────┤
│ 保存済み     │ [GET ▼] [ {{baseUrl}}/api/users          ] [送信] │
│  ・ユーザ一覧 │ ┌ Params ┬ Headers ┬ Body ┐                        │
│  ・ログイン   │ │ key        │ value          │ ☑               │
│  ・...       │ │ page       │ 1              │ ☑               │
│──────────────│ └────────────────────────────────┘                 │
│ 履歴         ├────────────────────────────────────────────────────┤
│  14:02 GET … │ 200 OK   123 ms   1.2 KB                           │
│  13:58 POST… │ ┌ Body ┬ Headers ┐  [☑ 整形]  [コピー]            │
│              │ │ {                                    │           │
│              │ │   "id": 1, ...                       │           │
└──────────────┴────────────────────────────────────────────────────┘
```

- 左ペイン: 保存済みリクエスト一覧 + 送信履歴
- 右上: リクエスト編集
- 右下: レスポンス表示
- 左右・上下は `SplitContainer` で幅/高さを変更でき、サイズは保存する

### リクエスト編集

- **メソッド**: `GET` / `POST` / `PUT` / `PATCH` / `DELETE` / `HEAD` / `OPTIONS`(ComboBox)
- **URL 入力欄**: `{{変数名}}` で環境変数を埋め込める
- **送信ボタン**: `Ctrl+Enter` でも送信。送信中は「キャンセル」ボタンに変わる
- **Params タブ**: key / value / 有効チェック の表(`DataGridView`)。URL のクエリ文字列と双方向同期は初期版ではしない(送信時に URL へ付加するだけ)
- **Headers タブ**: key / value / 有効チェック の表
- **Body タブ**: 種別を選ぶ
  - なし
  - JSON(`Content-Type: application/json` を自動付与。送信前に JSON 構文チェックして警告)
  - フォーム(`application/x-www-form-urlencoded`。key/value 表)
  - テキスト(Content-Type は Headers で指定)

### レスポンス表示

- ステータスコード + 理由句(2xx 緑 / 3xx 青 / 4xx 橙 / 5xx 赤)
- 所要時間(ms)、サイズ
- **Body タブ**: JSON なら整形表示(「整形」チェックで ON/OFF)、それ以外はそのまま。等幅フォント
- **Headers タブ**: レスポンスヘッダ一覧
- 「コピー」ボタンで Body をクリップボードへ
- 通信エラー(接続拒否・タイムアウト・DNS 失敗など)は、レスポンス欄に分かりやすい日本語メッセージで表示。例外でアプリを落とさない

### 保存済みリクエスト

- 「保存」(`Ctrl+S`)で現在のリクエストに名前を付けて保存
- 一覧クリックで編集欄に読み込み。右クリックで「名前変更」「複製」「削除」
- 並びは名前順(open-folder の `ByName` と同じ考え方)
- フォルダ分け(コレクション)は初期版ではしない

### 送信履歴

- 送信するたびに先頭へ追加(メソッド・URL・ステータス・時刻)。上限 100 件
- クリックで当時のリクエスト内容を編集欄へ復元

### 環境変数

- 自作アプリの `http://localhost:3000` / ステージング などを切り替えるための仕組み
- 環境 = 名前 + 変数(key/value)の集合。右上の ComboBox で切替
- URL・ヘッダ値・Body 中の `{{key}}` を送信直前に置換。未定義の変数があれば送信前に警告
- 環境の編集ダイアログ(追加・編集・削除)

### その他の設定

- タイムアウト(秒、既定 30)
- 自己署名証明書を許可する(既定 OFF。ローカルの https 開発サーバ向け)
- リダイレクトを自動で追う(既定 ON)

---

## 3. 設定・データの永続化

保存先: `%APPDATA%\post-kun\`

| ファイル | 内容 |
|---|---|
| `settings.json` | ウィンドウ位置・サイズ・分割位置、選択中の環境、タイムアウト等のオプション |
| `requests.json` | 保存済みリクエスト |
| `environments.json` | 環境と変数 |
| `history.json` | 送信履歴(最大 100 件) |

- JSON はインデント付き・PascalCase(open-folder と同じ)
- 壊れたファイルは `*.bak` へ退避して空の初期値で起動を続ける(open-folder の `TryBackupCorruptedFile` と同じ方針)
- 保存先フォルダがなければ自動作成
- スキーマ案:

```json
// requests.json
{
  "Requests": [
    {
      "Name": "ユーザ一覧",
      "Method": "GET",
      "Url": "{{baseUrl}}/api/users",
      "Params":  [ { "Key": "page", "Value": "1", "Enabled": true } ],
      "Headers": [ { "Key": "Authorization", "Value": "Bearer {{token}}", "Enabled": true } ],
      "BodyType": "None",          // None | Json | Form | Text
      "Body": "",
      "FormFields": []
    }
  ]
}

// environments.json
{
  "Environments": [
    { "Name": "local", "Variables": [ { "Key": "baseUrl", "Value": "http://localhost:3000", "Enabled": true } ] }
  ]
}
```

---

## 4. プロセスとウィンドウの契約

- エントリポイントに `[STAThread]`、`PerMonitorV2`
- Mutex 名 `Local\post-kun`。2個目のプロセスは既存ウィンドウを前面に出して終了する
  (`FindWindow` → `ShowWindow(SW_RESTORE)` → `SetForegroundWindow`)
- 終了時(`FormClosing`)にウィンドウ位置・サイズ・分割位置を保存。送信中のリクエストはキャンセルする
- 保存済みのウィンドウ位置が画面外なら、現在の画面内へ補正する
- `HttpClient` はアプリ内で使い回す(オプション変更時のみ作り直す)。送信は `async/await` で UI スレッドをブロックしない
- 送信中は `CancellationTokenSource` で中断できる

---

## 5. ファイル構成

```text
post-kun.csproj
README.md
AGENTS.md
CLAUDE.md
plan.md
restart.ps1
restart.bat
.gitignore              # bin/ obj/ publish/
source/
  Program.cs            # Mutex、既存ウィンドウの前面化、WinForms 初期化、MainForm 起動
  MainForm.cs           # メインウィンドウ(レイアウト、ショートカット、各パネルの組み立て)
  RequestEditor.cs      # メソッド/URL/Params/Headers/Body の編集 UserControl
  ResponseViewer.cs     # ステータス/時間/サイズ/Body/Headers 表示 UserControl
  SidePanel.cs          # 保存済み一覧 + 履歴の UserControl
  KeyValueGrid.cs       # key/value/有効 の DataGridView ラッパ(Params/Headers/Form/環境変数で共用)
  EnvironmentDialog.cs  # 環境の追加・編集・削除
  OptionsDialog.cs      # タイムアウト・証明書・リダイレクト設定
  Models.cs             # RequestDef / KeyValueItem / EnvironmentDef / HistoryItem
  Storage.cs            # 各 JSON の読み書き、.bak 退避
  RequestBuilder.cs     # 純粋関数: 変数置換、URL+クエリ組み立て、HttpRequestMessage 生成
  HttpSender.cs         # HttpClient 管理、送信、時間計測、エラー変換
  JsonFormatter.cs      # 純粋関数: JSON 整形/検証
tests/
  PostKun.Tests.csproj
  PureFunctionTests.cs
```

AssemblyName / RootNamespace は `PostKun`。

---

## 6. テスト方針

open-folder と同じく依存ゼロの自前ランナー。`dotnet run --project tests`(終了コード=失敗数)。
UI と通信から切り離した **純粋関数** を表形式でテストする。

- `RequestBuilder.ExpandVariables`
  - 単一 / 複数の `{{key}}` 置換
  - 未定義変数の検出(リストで返す)
  - 無効化された変数は使わない
  - `{{` だけ等の不完全な記法はそのまま
- `RequestBuilder.BuildUrl`
  - クエリなし / あり
  - 既存の `?a=1` がある URL への追記
  - 値の URL エンコード(日本語・スペース・`&`)
  - 無効化された Params は付けない
  - `http://` なしの入力に `http://` を補う
- `RequestBuilder.BuildFormBody`
  - 通常 / エンコードが必要な値
- `JsonFormatter.TryFormat`
  - 正しい JSON の整形
  - 不正 JSON は false と元文字列
  - 日本語がエスケープされずに表示される
- `HttpSender.DescribeError`(例外 → 日本語メッセージ)
  - 接続拒否 / タイムアウト / キャンセル / DNS 失敗
- `Storage` の壊れた JSON 読み込み(一時フォルダを使い `.bak` が作られること)

通信部分は、必要になったら `HttpListener` でテスト内にローカルサーバを立てて結合テストを追加する(初期版では任意)。

---

## 7. 実装順

各段階で `dotnet build` を通し、未検証コードを溜めない。

> **進捗(2026-11-17)**: 段階 1〜4 と 段階 6 の一部 完了 ✅(ビルド警告0、テスト 123 件 + UI スモーク 18 件パス)。
> - 段階4: パラメータ・ヘッダーはフラットな表、ボディは **ツリー型の表**(オブジェクト・配列の階層を GUI で作成、JSON から取り込み可)。クエリ文字列と JSON はアプリが組み立ててプレビュー表示
> - 利用者の要望で仕様変更: **URL に ? は禁止**(貼り付けたクエリは表へ自動で移す)。ボディの「テキスト」形式はやめ、「JSON オブジェクト / JSON 配列 / フォーム」の表入力に統一
> - 段階6のうち: 保存済みリクエスト(左の一覧・名前順・複製/削除・未保存確認)、ウィンドウ位置/サイズ/分割位置の保存 は完了。**送信履歴は未実装**
> - 段階5のうち: レスポンスのヘッダータブは完了
> - 次は: 送信履歴(段階6の残り)→ 環境変数(段階7)→ オプション(段階8)

1. **土台**: csproj・Program(Mutex)・空の MainForm・tests プロジェクトを作ってビルド
2. **モデルと純粋関数**: `Models` / `RequestBuilder` / `JsonFormatter` を実装しテストを書く
3. **最小の送受信**: メソッド + URL + 送信ボタン + レスポンス Body 表示だけで GET が通るところまで
4. **リクエスト編集**: `KeyValueGrid`、Params / Headers / Body タブ、POST(JSON・フォーム・テキスト)
5. **レスポンス表示の充実**: ステータス色・時間・サイズ・ヘッダ・整形・コピー・エラー表示・キャンセル
6. **永続化**: `Storage`、保存済みリクエスト、履歴、ウィンドウ位置/サイズ保存
7. **環境変数**: 環境ダイアログ、切替 ComboBox、`{{key}}` 置換、未定義警告
8. **オプション**: タイムアウト・自己署名証明書・リダイレクト
9. **仕上げ**: ショートカット(`Ctrl+Enter` / `Ctrl+S` / `Ctrl+L` で URL へ)、DPI 確認、`restart.ps1`、README・AGENTS.md・CLAUDE.md
10. **確認**: ビルド・テスト・publish 版を起動し、手元の自作アプリへ GET / POST して動作確認

---

## 8. 受け入れ条件

- `dotnet build` が警告・エラーなしで成功する
- `dotnet run --project tests` が終了コード 0
- GET / POST / PUT / PATCH / DELETE が送れ、ステータス・時間・ヘッダ・Body が表示される
- JSON / フォーム / テキストの Body を送れる
- JSON レスポンスが整形表示される(日本語がそのまま読める)
- 環境を切り替えると `{{baseUrl}}` などが切り替わる
- 保存済みリクエスト・環境・履歴・ウィンドウサイズが再起動後も残る
- 接続できないサーバやタイムアウトでもアプリが落ちず、理由が表示される
- 送信中に UI が固まらず、キャンセルできる
- 2回目の起動で既存ウィンドウが前面に出る
- 壊れた JSON は `.bak` に退避され、アプリは起動を続ける

---

## 9. 初期版でやらないこと(将来の候補)

- コレクション(フォルダ分け)
- URL とクエリ表の双方向同期
- multipart/form-data・ファイル送信
- 認証ヘルパ(Basic / Bearer の専用 UI。ヘッダ手入力で代替)
- cURL 形式のインポート / エクスポート
- レスポンスの保存、テストスクリプト、WebSocket
- ダークテーマ

---

## 10. 決めておきたいこと(要確認)

- [ ] 自作アプリの API は JSON 中心で良いか(フォーム・multipart の優先度)
- [ ] 2個目の起動は「既存ウィンドウを前面に」で良いか、複数ウィンドウを許可するか
- [ ] 保存済みリクエストのフォルダ分けは初期版で必要か
- [ ] 自己署名証明書の https(ローカル開発サーバ)を使う予定はあるか
