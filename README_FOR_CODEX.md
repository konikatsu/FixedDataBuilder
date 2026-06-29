# README_FOR_CODEX

このファイルは、次の Codex がチャット履歴なしで FixedDataBuilder の作業を再開するための引き継ぎメモです。

## プロジェクト概要

FixedDataBuilder は、COBOL 固定長データのテストデータを作成・編集する C# WinForms / .NET 8 アプリです。

主な目的:

- 定義書 CSV を読み込む
- 固定長データをバイト列として読み書きする
- COMP-3 / packed decimal を画面上では通常の数値として扱う
- 項目縦 / レコード縦の表形式で複数レコードを編集する
- サンプル定義・サンプルデータ・Release zip を含めて GitHub で公開する

リポジトリ:

- 作業場所: `C:\dev\FixedDataBuilder`
- GitHub: `https://github.com/konikatsu/FixedDataBuilder`
- 現在の最新 Release: `v0.1.26` 予定（REDEFINES 非表示対応のRelease作成中）

## 現在の成功状態

最新の成功状態:

- `v0.1.25` を GitHub Release 済み
- 次の作業対象は `v0.1.26`: REDEFINES 項目を画面表示・項目表示選択の対象から除外する
- `release/FixedDataBuilder.exe` は v0.1.26 の Release ビルド済み
- `FixedDataBuilder-v0.1.26.zip` を作成済み
- zip の中身は exe 等と `samples/` が直下に入る形
- README は `docs/screen-image-redefines-hidden-v0.1.26.png` を参照する予定
- README に COMP-3 の現行前提 `正数=C / 負数=D / 符号なし=F` を記載済み
- `docs/development-history.md` に作成経緯を整理済み
- v0.1.20 で、履歴選択時の落ちやすさを抑制し、半角/全角スペースを記号化して空白部分だけ薄黄色で表示する改善を実装済み
- v0.1.21 で、COBOL コピー句（`.cbl` / `.cpy`）読み込みの初期対応を追加済み。PIC 句から既存の `FieldDefinition` に変換する。
- v0.1.22 で、コピー句 UTF-8/固定形式前提、英字項目名の日本語変換、REDEFINES 表示、`PIC XXXXXX` / `PIC 999V99`、コピー句由来データの UTF-8 + N 項目 UTF-16LE/UTF-32LE 混在読み込み推定を追加。
- v0.1.23 で、`項目表示` ボタンからグリッドに表示する項目を選択できるようにした。非表示項目も内部レコードには保持し、保存時のレコード構造から除外しない。
- v0.1.24 で、データ読み込み時に改行コード選択の後で型Nの文字コードを `Shift_JIS` / `UTF-8` / `UTF-16LE` / `UTF-32LE` から選べるようにする。コピー句サンプルとして、N項目UTF-16LE・その他UTF-8の `sample-copybook-utf16.dat` を追加する。
- v0.1.25 で、画面上部に `サンプル条件` 欄を追加する。既知のコピー句サンプルを誤った定義・改行・型N文字コードで開こうとした場合は、読み込み前に専用エラーを出す。コピー句サンプルを改行有無・型N文字コード別に追加する。
- v0.1.26 で、REDEFINES 項目を画面表示・項目表示選択の対象から除外する。

最後に push 済みの重要コミット:

- `cdf3bfb` Import FixedDataBuilder Excel files
- `7798c33` Document COMP-3 sign nibble assumptions
- `fe3dfe4` Document FixedDataBuilder development history
- `96ec0fb` Remove COBOL vendor sign note from history

## 現在できること

- 定義書 CSV 読み込み
- 定義書 CSV 作成・修正
- COBOL 風定義の読み込み
  - `X(n)`
  - `N(n)`
  - `9(n)`
  - `S9(n)`
  - `9(nVm)`
  - `S9(nVm)`
  - `9(n) COMP-3`
  - `S9(n) COMP-3`
- COBOL コピー句（`.cbl` / `.cpy`）読み込み
  - `PIC X(n)` / `PIC N(n)`
  - `PIC XXXXXX` / `PIC 999V99`
  - `PIC 9(n)` / `PIC S9(n)`
  - `PIC 9(n)V9(m)` / `PIC S9(n)V9(m)`
  - `COMP-3` / `PACKED-DECIMAL`
  - 同一 PIC 行の `OCCURS n TIMES`
  - `REDEFINES` は物理長に加算せず保存時は書き出さない。画面表示・項目表示選択の対象からは除外する
  - 66 レベル、88 レベルは読み飛ばし
  - コピー句ファイルは UTF-8、固定形式の 7 桁目ルールあり
  - コピー句由来のデータは X/9/S9/COMP-3 を UTF-8、N 項目をデータ読み込み時に選択した文字コードで読み込む
- 固定長データ読み込み
- 読み込み時の改行あり / 改行なし選択
- 読み込み時の型N文字コード選択。改行コード選択後に `Shift_JIS` / `UTF-8` / `UTF-16LE` / `UTF-32LE` から選ぶ。既定値は `Shift_JIS`
- コピー句サンプルの対応条件表示。表にない組み合わせはエラーまたは文字化けすることを画面上部とREADMEに明示
- 保存時に読み込み時の改行形式を継承
- 上書き保存 / 名前を付けて保存
- 項目縦表示 / レコード縦表示の切り替え
- 表示項目の選択
- レコード縦表示時の項目ヘッダーにバイト位置ルーラを表示
- レコード追加・複製・削除
- 表とHEX表示のフォント名・サイズ設定と記憶
- 初期フォントは `MS ゴシック 12pt`
- 検証エラーセルの薄赤表示
- 文字項目で半角スペース埋めになる残り領域を薄黄色と `･` で表示
- 選択項目 HEX 表示
- N型の未入力・不足分を全角スペースで保存
- S9の ASCII ゾーン符号保存
- COMP-3 / packed decimal の暫定エンコード・デコード
- Excel出力
- Excel出力後にブックを開く
- FixedDataBuilder が出力した Excel の取り込み
- ファイル履歴リストから選択した定義ファイル・データファイルの読み込み
- ファイルパス欄で Enter を押したときの定義ファイル・データファイル読み込み

## 重要な仕様メモ

### COMP-3

現時点では以下の符号ニブル前提です。

```text
符号付き正数: C
符号付き負数: D
符号なし: F
```

例:

```text
S9(3) COMP-3  +123 -> 12 3C
S9(3) COMP-3  -123 -> 12 3D
9(3)  COMP-3   123 -> 12 3F
```

将来は符号ニブル、桁数、端数処理を設定化する予定です。

### 文字コード

- CSV定義由来の固定長データは Shift_JIS 前提
- コピー句由来の固定長データは X/9/S9/COMP-3 を UTF-8、N項目を選択した文字コードで扱う
- 型Nの選択肢は `Shift_JIS` / `UTF-8` / `UTF-16LE` / `UTF-32LE`
- `N(n)` は全角 n 文字領域として扱い、保存時の不足分は全角スペース
- `X(n)` は定義上は半角文字として扱う想定

### コピー句サンプル

すべて `samples/definition-english.cbl` 用です。以下の組み合わせ以外はエラーまたは文字化けします。

| データファイル | 改行区切り | 型N文字コード |
| --- | --- | --- |
| `sample-copybook-utf16.dat` | 改行あり (CRLF/LF) | UTF-16LE |
| `sample-copybook-crlf-utf8-nutf16.dat` | 改行あり (CRLF/LF) | UTF-16LE |
| `sample-copybook-crlf-utf8-nutf8.dat` | 改行あり (CRLF/LF) | UTF-8 |
| `sample-copybook-none-sjis-nsjis.dat` | 改行なし | Shift_JIS |
| `sample-copybook-none-utf8-nutf8.dat` | 改行なし | UTF-8 |
| `sample-copybook-none-utf8-nutf16.dat` | 改行なし | UTF-16LE |
| `sample-copybook-none-utf8-nutf32.dat` | 改行なし | UTF-32LE |

### Excel取込

対応対象は FixedDataBuilder が出力した `.xlsx` 形式です。

- 項目縦形式を自動判定
- レコード縦形式を自動判定
- Excel 内の定義から項目を復元
- 既に定義ファイルを開いている場合は、Excel 側の定義と一致チェックしてから取り込む

## 主なファイル

- `FixedDataBuilder/MainForm.cs`
  - メイン画面
  - ファイル選択、表示切替、保存、Excel出力/取込、検証呼び出し
  - コピー句サンプルの対応条件表示と、既知サンプルの誤選択チェック
- `FixedDataBuilder/DefinitionEditorForm.cs`
  - 定義 CSV 作成・修正画面
  - 型選択、小数桁制御、定義プレビュー
- `FixedDataBuilder/DefinitionCsvReader.cs`
  - 定義 CSV と COBOL 風定義の読み込み
- `FixedDataBuilder/CopybookDefinitionReader.cs`
  - COBOL コピー句 `.cbl` / `.cpy` の PIC 句読み込み
- `FixedDataBuilder/DefinitionCsvWriter.cs`
  - 定義 CSV 書き出し
- `FixedDataBuilder/FixedLengthDataReader.cs`
  - 固定長データ読み込み
- `FixedDataBuilder/FixedLengthDataWriter.cs`
  - 固定長データ保存
- `FixedDataBuilder/PackedDecimal.cs`
  - COMP-3 / packed decimal のエンコード・デコード
- `FixedDataBuilder/NumericValueFormatter.cs`
  - 数値表示、ゼロ埋め、符号表示
- `FixedDataBuilder/RecordValidator.cs`
  - セル値の型・桁数チェック
- `FixedDataBuilder/ExcelExporter.cs`
  - 外部ライブラリなしの `.xlsx` 出力
- `FixedDataBuilder/ExcelImporter.cs`
  - FixedDataBuilder 出力 `.xlsx` の取り込み
- `samples/definition.csv`
  - サンプル定義
- `samples/definition.cbl`
  - サンプル COBOL コピー句
- `samples/sample.dat`
  - サンプル固定長データ
- `samples/sample-shiftjis.dat`
  - コピー句追加対応前と同じ内容の Shift_JIS サンプル固定長データ
- `samples/sample-copybook-utf16.dat`
  - `definition-english.cbl` 用のコピー句サンプル固定長データ。N項目は UTF-16LE、その他は UTF-8
- `samples/sample-copybook-crlf-utf8-nutf16.dat`
  - `sample-copybook-utf16.dat` と同内容の分かりやすい名前のコピー
- `samples/sample-copybook-crlf-utf8-nutf8.dat`
  - 改行あり、型N UTF-8
- `samples/sample-copybook-none-sjis-nsjis.dat`
  - 改行なし、型N Shift_JIS
- `samples/sample-copybook-none-utf8-nutf8.dat`
  - 改行なし、型N UTF-8
- `samples/sample-copybook-none-utf8-nutf16.dat`
  - 改行なし、型N UTF-16LE
- `samples/sample-copybook-none-utf8-nutf32.dat`
  - 改行なし、型N UTF-32LE
- `docs/release-checklist.md`
  - README画像やRelease漏れ防止のチェックリスト
- `docs/development-history.md`
  - これまでの指示と作業時間の整理

## 保護すべきもの・注意するもの

### 触ってよいが慎重に扱うもの

- `release/`
  - Release ビルド済み成果物を置いている
  - 機能変更やバージョン更新時は `dotnet publish` で更新する
- `samples/`
  - README、スクリーンショット、Release zip と整合する必要がある
- `docs/screen-image-cobol-sample-v*.png`
  - README が参照している画面画像
  - UI変更時は最新版画像に差し替える
- `FixedDataBuilder-v*.zip`
  - Release アセット用のローカル zip
  - Git 管理対象ではない
- `%LOCALAPPDATA%\FixedDataBuilder\settings.txt`
  - フォント名・フォントサイズ設定を保存するユーザー別設定ファイル
  - 設定がない場合は `MS ゴシック 12pt` を既定値にする

### 既知の未追跡ファイル

以下は過去作業の確認用ファイルです。ユーザーから明示されない限り、コミットしないでください。

- `docs/definition-editor-v0.1.15.png`
- `docs/excel-field-rows.xlsx`
- `docs/excel-record-rows.xlsx`
- `docs/pre-push-v0.1.3.png`

### 破壊的操作禁止

- `git reset --hard`
- `git checkout --`
- 未追跡ファイルの一括削除
- `release/` や `samples/` の不用意な削除

ユーザーが作った可能性のあるファイルを勝手に消さないでください。

## 実行・検証方法

### ビルド

```powershell
dotnet build FixedDataBuilder\FixedDataBuilder.csproj -c Release
```

サンドボックス環境では `obj` への書き込み権限で失敗することがあります。その場合は権限付きで同じコマンドを再実行します。

### Release publish

```powershell
dotnet publish FixedDataBuilder\FixedDataBuilder.csproj -c Release -o release
```

### サンプル起動

```powershell
.\release\FixedDataBuilder.exe --definition .\samples\definition.csv --data .\samples\sample.dat --separator crlf
.\release\FixedDataBuilder.exe --definition .\samples\definition-english.cbl --data .\samples\sample-copybook-utf16.dat --separator crlf --national-encoding utf16
```

GUI 起動やスクリーンショット撮影は権限付き実行が必要になることがあります。

### zip 作成ルール

Release zip は以下の形にします。

```text
FixedDataBuilder.exe
FixedDataBuilder.dll
FixedDataBuilder.deps.json
FixedDataBuilder.runtimeconfig.json
FixedDataBuilder.pdb
samples/
```

zip の中に `release/` フォルダを作らないでください。

確認例:

```powershell
tar -tf FixedDataBuilder-v0.1.xx.zip
```

### Excel取込の確認

一時的なスモークテストを作る場合は `.codex-smoke/` 配下を使い、完了後に削除します。

過去に確認した内容:

- 項目縦形式の Excel を出力して取り込める
- レコード縦形式の Excel を出力して取り込める

## README・Release運用

ユーザーは GitHub README のスクリーンショット反映漏れを気にしています。

UI変更がある場合:

1. サンプル定義とサンプルデータを読み込んだ状態でスクリーンショットを撮る
2. `docs/screen-image-cobol-sample-vX.Y.Z.png` のようにバージョン付きで保存する
3. README の画像参照を新しい画像へ更新する
4. push 前にキャプチャをユーザーへ見せる
5. `docs/release-checklist.md` を確認する

機能変更や配布物更新がある場合:

1. `FixedDataBuilder.csproj` の Version / AssemblyVersion / FileVersion / InformationalVersion を上げる
2. `dotnet build`
3. 必要ならスモークテスト
4. `dotnet publish ... -o release`
5. zip 作成
6. README / screenshot 更新
7. commit / push
8. `gh release create vX.Y.Z FixedDataBuilder-vX.Y.Z.zip ...`
9. `gh release view vX.Y.Z --repo konikatsu/FixedDataBuilder --json tagName,url,assets`

## 既知の失敗・注意点

- `dotnet build` / `dotnet publish` が `Access to the path ... obj\*.tmp is denied` で失敗することがある
  - サンドボックス由来なので、必要に応じて権限付きで再実行する
- スクリーンショット撮影時に別ウィンドウがかぶったことがある
  - WinFormsウィンドウ自体を直接キャプチャする方式の方が安定
- GitHub README は画像キャッシュで古く見えることがある
  - 画像ファイル名をバージョン付きにして差し替える運用
- GitHub CLI Release 作成が HTTP 401 で止まったことがある
  - ユーザーが `gh auth refresh` を実施し解消済み
- PowerShell 5 から .NET 8 WinForms DLL を直接ロードするテストは失敗した
  - フォーム内部テストは .NET 8 の一時プロジェクトで行う

## 次に読むべき場所

作業前に読む順番:

1. `README.md`
2. `docs/release-checklist.md`
3. `docs/development-history.md`
4. `FixedDataBuilder/MainForm.cs`
5. 変更対象に応じて以下
   - 定義関連: `DefinitionCsvReader.cs`, `DefinitionEditorForm.cs`
   - 固定長読み書き: `FixedLengthDataReader.cs`, `FixedLengthDataWriter.cs`
   - COMP-3: `PackedDecimal.cs`
   - 数値表示: `NumericValueFormatter.cs`
   - Excel: `ExcelExporter.cs`, `ExcelImporter.cs`

## ユーザー対応メモ

- ユーザーとは日本語でやり取りする
- ユーザーは push 前の確認、スクリーンショット、README反映を重視している
- UI変更時は push 前にキャプチャを見せる
- README と GitHub 上の表示が本当に更新されているかを意識する
- Release zip の中に `release/` フォルダを作らない
- サンプルを読み込んだ状態の画面キャプチャを好む
- 不確実な仕様、特に COBOL / COMP-3 / 処理系差は断言しすぎない
