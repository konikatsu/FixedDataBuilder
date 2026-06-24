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
- 現在の最新 Release: `v0.1.18`

## 現在の成功状態

最新の成功状態:

- `v0.1.18` を GitHub Release 済み
- `release/FixedDataBuilder.exe` は v0.1.18 の Release ビルド済み
- `FixedDataBuilder-v0.1.18.zip` を作成済み
- zip の中身は exe 等と `samples/` が直下に入る形
- README は `docs/screen-image-cobol-sample-v0.1.18.png` を参照
- README に COMP-3 の現行前提 `正数=C / 負数=D / 符号なし=F` を記載済み
- `docs/development-history.md` に作成経緯を整理済み

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
- 固定長データ読み込み
- 読み込み時の改行あり / 改行なし選択
- 保存時に読み込み時の改行形式を継承
- 上書き保存 / 名前を付けて保存
- 項目縦表示 / レコード縦表示の切り替え
- レコード追加・複製・削除
- 検証エラーセルの薄赤表示
- 選択項目 HEX 表示
- N型の未入力・不足分を全角スペースで保存
- S9の ASCII ゾーン符号保存
- COMP-3 / packed decimal の暫定エンコード・デコード
- Excel出力
- Excel出力後にブックを開く
- FixedDataBuilder が出力した Excel の取り込み

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

- 固定長データは Shift_JIS 前提
- `N(n)` は全角 n 文字領域として扱い、保存時の不足分は全角スペース
- `X(n)` は定義上は半角文字として扱う想定

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
- `FixedDataBuilder/DefinitionEditorForm.cs`
  - 定義 CSV 作成・修正画面
  - 型選択、小数桁制御、定義プレビュー
- `FixedDataBuilder/DefinitionCsvReader.cs`
  - 定義 CSV と COBOL 風定義の読み込み
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
- `samples/sample.dat`
  - サンプル固定長データ
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
