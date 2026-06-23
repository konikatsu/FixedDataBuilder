# FixedDataBuilder 作成メモ

このメモは、FixedDataBuilder を作成するまでにユーザーが出した主な指示と、作業にかかった時間の目安を整理したものです。

時間は Git のコミット履歴と、このスレッドで確認できる作業内容からの概算です。会話・検討・手戻り・GitHub 認証待ちなどを完全に計測したものではありません。

## 目的

COBOL 固定長データのテストデータを作成・編集するための、独立した C# WinForms / .NET 8 ツールを作成する。

主な前提:

- SpaceRunViewer への追加ではなく、`FixedDataBuilder` として新規プロジェクト化する
- 固定長テキストではなく、COMP-3 / packed decimal を含むバイト列として扱う
- 文字コードはまず Shift_JIS 前提
- 定義書を見ながら、項目とレコードを表形式で編集できる画面にする

## ユーザーからの主な指示

### 初期MVP

- `C:\dev\FixedDataBuilder` に新規 C# WinForms / .NET 8 プロジェクトを作成
- README に目的、画面イメージ、定義書 CSV 例、今後の予定を書く
- 定義書 CSV を読み込み、固定長データを作成・編集する土台を作る
- 項目縦、レコード横の DataGridView を中心にする
- レコード追加、複製、削除、保存、検証を入れる
- COMP-3 は画面では通常の数値として表示・編集し、保存時に packed decimal へ変換する

### 定義書・サンプルデータ

- COBOL 風の定義表記にする
  - `項目名, 9(9)`
  - `項目名, X(10)`
  - `項目名, S9(11) COMP-3`
- 漢字項目は `N(1)` のように表す
- サンプルデータを具体化
  - 名前 `N(10)`: ジナン、キナコ、オジュン
  - 英名 `X(10)`: JINAN、KINAKO、OJUN
  - 年齢 `9(2V1)`: 7、3、18
  - 体重 `S9(3V2)`: 6.7、3.8、45.1
  - 攻撃力 `S9(9) COMP-3`: 100、50、-9999
- 攻撃力は packed decimal 形式のサンプルデータにする

### 画面・操作性

- 項目縦表示とレコード縦表示を切り替えられるようにする
- 横表示の場合は、上段に項目名、下段に型と桁数を出す
- 項目名・定義部分を薄い緑色にする
- 数値はゼロ埋めして表示できるようにする
- 符号付きの場合は符号を表示できるようにする
- 検証エラーがあるセルを薄赤にする
- 選択項目の HEX を表示する
- 画面上部に定義ファイルパス、データファイルパス、選択ボタンを並べる
- 長いパスでも末尾のファイル名が見えるようにする
- 読み込み時に、固定長データの改行あり / 改行なしを選べるようにする
- 保存時は読み込み時の改行あり / 改行なしを継承する
- その改行モードを画面に表示する
- 最近使ったファイルを直近20件まで持つ

### 保存・読込

- 上書き保存と名前を付けて保存を選べるようにする
- 固定長データの読み込み機能を追加する
- 読み込み・書き込みのテストを行う
- 定義とデータの不一致チェックを出力時に行う
- `N(n)` の未入力や不足分は全角スペースで保存する
- `ジナン` のように入力した場合、残りは全角スペースで埋める

### README・GitHub・Release

- README にスクリーンショットを載せる
- GitHub 上の README スクリーンショットを最新版に差し替える
- push 前に必ずキャプチャを撮って見せる
- Release へのリンクを README に貼る
- release フォルダには exe を入れる
- Release の zip に定義ファイルとサンプルデータも含める
- zip の中に `release` フォルダは作らず、exe と `samples/` を直下に置く
- ビルドの都度、必要に応じてバージョン番号を上げる
- GitHub へ push し、Release を作成する

### 定義保守

- GUI で定義 CSV を作成・修正できるようにする
- 型は選択式にする
- 桁数は入力式にする
- `定義作成` は空の新規作成にする
- 読み込み済み定義を編集する `定義修正` を分ける
- 型が数字以外の場合、小数桁は入力不可にする

### Excel連携

- 画面に出ている形式で Excel に出力できるようにする
  - 項目縦
  - レコード縦
- Excel 出力後、作成したブックを開く
- Excel 出力結果のキャプチャを確認する
- FixedDataBuilder が出力した Excel を取り込めるようにする
- 項目縦 / レコード縦の Excel 形式を自動判定する

### COMP-3 / COBOL仕様

- IBM COBOL と富士通 COBOL で符号表現が違う可能性を確認する
- 本ソフトでは、COMP-3 はまず以下を前提にすることを README に明記する
  - 符号付き正数: `C`
  - 符号付き負数: `D`
  - 符号なし: `F`
- 今後の予定として、符号ニブル、桁数、端数処理を利用者がイメージできるように README に具体化する

## 作業の流れ

### 2026-06-19

初期MVPを作成し、COBOL風定義、サンプルデータ、README、スクリーンショット、Release zip の土台を整備。

主なコミット:

- `035ff2a` Initial FixedDataBuilder MVP
- `1e4e597` Support COBOL-style definitions and view layouts
- `118e347` Show signed zero-padded sample values
- `639e534` Package samples with release zip
- `9b44d6a` Add fixed data load and sample release

コミット時刻ベースの作業幅:

- 11:43 から 14:07 頃まで
- 約 2 時間 24 分

### 2026-06-22

読み書き機能、Excel出力、READMEスクリーンショット運用、定義保守画面、N型全角スペース、S9ゾーン符号、GitHub Release 周りを追加・修正。

主なコミット:

- `9ef4d72` Add Excel export
- `1aa501e` Document release screenshot checklist
- `119f264` Handle full-width padding and record shape
- `bfd816d` Support zoned signed display numbers
- `ebf033f` Add definition file editor
- `dc755cb` Improve definition editor and Excel export
- `b204d17` Make definition creation start empty
- `aa7d66b` Disable decimal digits for text definitions

コミット時刻ベースの作業幅:

- 16:36 から 18:16 頃まで
- 約 1 時間 40 分

### 2026-06-23

Excel取り込み、READMEのCOMP-3前提整理、今後予定の具体化を追加。

主なコミット:

- `cdf3bfb` Import FixedDataBuilder Excel files
- `7798c33` Document COMP-3 sign nibble assumptions

コミット時刻ベースの作業幅:

- 11:33 から 11:48 頃まで
- 約 15 分

## 時間の目安

Git コミット履歴から見た時間:

- 初回コミット: 2026-06-19 11:43
- 直近の整理コミット: 2026-06-23 11:48
- カレンダー上の経過: 約 3 日

コミットが集中している時間帯だけを足した概算:

- 2026-06-19: 約 2 時間 24 分
- 2026-06-22: 約 1 時間 40 分
- 2026-06-23: 約 15 分
- 合計: 約 4 時間 20 分

ただし、これはコミット時刻ベースの概算です。実際には以下も含まれます。

- 要件相談
- 画面キャプチャ確認
- ビルド・テスト
- GitHub Release 作成
- GitHub CLI 認証待ち
- README の表現調整
- 不具合報告に対する修正

そのため、会話・確認・手戻りを含めた体感の作業量は、コミット時刻の合計より長くなります。

## 現在できること

- 定義書 CSV 読み込み・作成・修正
- COBOL風定義表記の読み込み
- 固定長データ読み込み・保存
- 改行あり / 改行なしの選択と継承
- 項目縦 / レコード縦の表示切り替え
- レコード追加・複製・削除
- セル編集と検証
- 検証エラーセルの薄赤表示
- 選択項目 HEX 表示
- COMP-3 / packed decimal のエンコード・デコード
- N型の全角スペース埋め
- S9のゾーン符号保存
- Excel出力
- Excel出力後のブック起動
- FixedDataBuilder が出力した Excel の取り込み
- READMEスクリーンショットとRelease zipの運用

## 補足

README や Release の反映漏れを避けるため、`docs/release-checklist.md` にチェックリストを用意しています。今後も UI を変更した場合は、push 前にスクリーンショットを撮り、README の画像参照をバージョン付きファイル名へ更新する運用にします。
