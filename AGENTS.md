# ルール
- 日本語で絵文字を含めて返答します。
- セッション開始時にこのファイルを読みます。コード構造を変えたら更新します。
- 利用者向けの導入・使い方は `README.md`、編集者(AIエージェント)向けの運用知識・契約はこのファイル、全体計画と進捗は `plan.md` に書きます。

# アプリ概要
自作アプリ(ローカルの Web API など)へ GET / POST などを送ってレスポンスを確認する、通常ウィンドウ型の HTTP 試験ツール(C# / WinForms / net6.0-windows / 依存パッケージなし)。
`open-folder`(`C:\Users\azcat\Documents\github\open-folder`)の構成を踏襲しているが、常駐・ホットキーはない。

画面: 左=保存済みリクエスト一覧(名前順) / 右上=編集欄(名前・メソッド・URL・送信、タブ: パラメータ / ヘッダー / ボディ、送信URLプレビュー) / 右下=レスポンス(本文 / ヘッダー)。
**パラメータ・ヘッダー・ボディはすべて表で入力し、クエリ文字列と JSON はアプリが組み立てる**。ボディはツリー型の表で、オブジェクト・配列の階層を GUI で作れる。

# ビルド・テスト・起動

```
dotnet run --project post-kun.csproj   # ソースから起動
dotnet build post-kun.csproj           # ビルドのみ
dotnet run --project tests             # テスト(終了コード=失敗数)
dotnet run --project tests -- --ui     # 画面部品のスモークテスト(透明フォームで RequestEditor / BodyTreeGrid を操作)
.\restart.ps1                          # 起動中の exe を終了 → publish → 起動
```

- csproj は `source/` 以下の .cs を暗黙 glob でコンパイルする。`tests/**` は除外済み。
- オフライン環境のため test SDK が使えず、`tests` は依存ゼロの自前コンソールランナー(`dotnet test` は使えない)。
- `tests` には `HttpListener` でローカルのエコーサーバを立てる結合テスト(GET/POST/404/タイムアウト/キャンセル/接続拒否)も含む。空きポートは `TcpListener(port 0)` で取得する。
- `Storage` のテストは一時フォルダを渡して行う(`%APPDATA%` には触れない)。`--ui` も設定ファイルに触れない。
- csproj の XML コメントに `--` を書くとビルドが壊れる(MSB4025)。
- スクリーンショット確認: PowerShell で `GetWindowRect` + `CopyFromScreen`(スクリプト実行には `-ExecutionPolicy Bypass` が必要)。

# 守るべき挙動・契約(壊さないこと)
- **Mutex 名** `Local\post-kun`。2個目のプロセスは `FindWindow(null, "post-kun")` で既存ウィンドウを探して前面化し、終了する。そのため **メインウィンドウのタイトルは `Program.WindowTitle`("post-kun")から変えない**(未保存マークもタイトルではなく編集欄の「● 未保存」ラベルで出す)。
- **保存先** `%APPDATA%\post-kun\`: `requests.json`(`{ Requests: RequestDef[] }`)と `settings.json`(`AppSettings`: ウィンドウ位置・サイズ・最大化・左ペイン幅・編集欄の高さ・最後に開いていた名前)。JSON はインデント付き・PascalCase・enum は文字列・日本語はエスケープしない。書き込みは `.tmp` に書いてから置き換え、読めないファイルは `.bak` へ退避して空で続行。
- **RequestDef スキーマ**: `Name, Method, Url, Params[]{Key,Value,Enabled}, Headers[]{…}, BodyType(None|Json|JsonArray|Form), BodyFields[]{Key,Value,Enabled,Type(String|Number|Boolean|Null|Object|Array),Children?}`。`Children` は Object/Array のときだけ持ち、それ以外は null(書き出さない)。プロパティ名・enum 名の変更は保存データを壊す。
- **一覧は常に名前順**(`RequestDef.ByName`: ja-JP、大文字小文字・全半角・かなの種類を無視、同順は序数比較)。名前の重複は大文字小文字無視で禁止。
- **URL に ? は入れない**。URL 欄で `?` キーは拒否し、貼り付け等で入ったら `RequestBuilder.SplitQuery` でクエリをパラメータの表へ移す。読み込み時(`SetRequest`)も同様に分ける。
- JSON ボディ: オブジェクト内でキーが空の行は未入力として飛ばす。配列の子のキーは使わない(表では `[0]` 表示)。読めない値・重複キーは「場所(`a.b[0].c`): 理由」でエラーにし、送信しない(`RequestBuildException`)。プレビューにも ⚠ で出す。整形(インデント2)した JSON をそのまま送る。
- フォーム: トップレベルの行だけを使う。null は空文字、オブジェクト / 配列は1行 JSON 文字列。
- `HttpClient` はアプリ内で1つを使い回す(`HttpSender`)。`HttpClient.Timeout` は無限にし、タイムアウトは `CancellationTokenSource` で行う。タイムアウト時は `TimeoutException` に変換し、利用者のキャンセル(`OperationCanceledException`)と区別する。
- 4xx / 5xx も例外にせずレスポンスとして表示する。通信例外は `HttpSender.DescribeError` で日本語メッセージにし、アプリを落とさない。
- 変数置換は `{{key}}`(前後の空白可)。未定義・無効の変数は置換せず残し、送信前に確認する。同名は先勝ち。環境の UI は未実装(現在は変数リストを空で渡している)。
- 本文のデコードは Content-Type の charset に従う(Shift_JIS 等のため `CodePagesEncodingProvider` を登録済み)。不明・未指定は UTF-8。

# ファイル
- `plan.md` : 全体計画・進捗
- `source`
  - `Program.cs` : エントリポイント(Mutex、2個目の起動時の既存ウィンドウ前面化、`MainForm` 起動)
  - `MainForm.cs` : メインウィンドウ。左の一覧(OwnerDraw、メソッド色付き)・新規/保存/削除・右クリックで複製/削除、未保存確認(切替・新規・終了時)、送信、ウィンドウ状態の保存復元。ショートカット `Ctrl+Enter` 送信 / `Ctrl+S` 保存 / `Ctrl+N` 新規 / `Ctrl+L` URL へ
  - `RequestEditor.cs` : 編集欄 UserControl。`SetRequest` / `GetRequest(commit)` / `Changed` / `SendClicked`。URL の ? 禁止、タブの件数表示、送信URL・ボディのプレビュー
  - `KeyValueGrid.cs` : 有効/キー/値 のフラットな表(パラメータ・ヘッダー)。`GridStyle`(表の共通見た目)もここ
  - `BodyTreeGrid.cs` : ボディのツリー型の表(有効/キー/型/値)。ツールバー: 行を追加・子を追加・↑↓・削除・JSON から取り込み。`JsonImportDialog` もここ
  - `ResponseViewer.cs` : レスポンス表示(ステータス色・時間・サイズ・整形・コピー・本文/ヘッダーのタブ)
  - `Models.cs` : `KeyValueItem` / `BodyField`(ツリーのノード)/ `BodyValueType` / `BodyType` / `RequestDef`(`ByName` / `MakeCopyName`)/ `ResponseResult` / `RequestBuildException`
  - `RequestBuilder.cs` : 純粋関数(`ExpandVariables` / `NormalizeUrl` / `SplitQuery` / `BuildUrl` / `BuildFormBody` / `BuildJsonBody` / `TryParseJsonToFields` / `BuildMessage`)
  - `Storage.cs` : JSON ファイルの読み書き(`RequestStore` / `AppSettings`、`Serialize` / `Clone`)
  - `HttpSender.cs` : HttpClient 管理・送信・時間計測・本文デコード・例外の日本語化
  - `JsonFormatter.cs` : レスポンス JSON の整形(`TryFormat`)と Content-Type 判定
- `tests` : `PureFunctionTests.cs`(純粋関数 + Storage + ローカルサーバ結合テスト、`--ui` で画面部品スモークテスト)
- `restart.ps1` / `restart.bat` : publish 版の再ビルドと起動

# 実装上の注意
- レイアウトは `AutoScaleMode.Dpi` + Dock / TableLayoutPanel / SplitContainer。表の列幅は `Fill` + `FillWeight`(比率)で決め、px 指定しない。
- Dock は Controls の後ろから順に適用されるため、`Dock.Fill` のコントロールを先に `Controls.Add` する。UserControl 自身にも `Dock = Fill` を付けること(付け忘れると 150x150 のまま小さく表示される)。
- TableLayoutPanel の AutoSize 行で `Button` に `AutoSize + Dock.Fill` を付けると行が縦に伸びる。Anchor を使う。
- OwnerDrawFixed の ListBox は、ハンドル作成後に `ItemHeight` を変えるとハンドルが作り直されて選択が外れる。`ItemHeight` は構築時に決め、`Shown` でも選択を復元している。
- SplitContainer の `SplitterDistance` はサイズ確定後(`Load` / `BeginInvoke`)に設定する。
- **BodyTreeGrid はモデル(BodyField ツリー)が正**。セル編集は `CellValueChanged` でノードへ書き戻し、構造変更(追加・移動・削除・型変更)は表を `Rebuild` で作り直す。型変更は確認ダイアログと作り直しを伴うので `BeginInvoke` でセル確定イベントの外に出す(イベント中に `Rows.Clear` / `EndEdit` すると再入例外)。
- 同じ理由で、表の `Changed` から呼ばれるプレビュー更新は `GetRequest(commit: false)` で読む。保存・送信の直前は `GetRequest()`(既定で入力中のセルを確定)。
- BodyTreeGrid の型セルは `CellClick` で `BeginEdit` + `DroppedDown = true` して1クリックで開く(DataGridView 既定のコンボは複数回クリックが必要)。
- 未保存判定は「読み込み・保存時の `Storage.Serialize` の結果」と現在の内容の比較。
- 送信ボタンは送信中「キャンセル」に変わり、同じボタン(または `Ctrl+Enter`)でキャンセルする。
