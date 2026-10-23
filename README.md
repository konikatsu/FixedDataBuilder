# FixedDataBuilder

FixedDataBuilder は、COBOL 固定長データのテストデータを作成・編集するための C# WinForms / .NET 8 ツールです。

定義書 CSV・COBOL コピー句を読み込み、項目とレコードを表形式で見比べながら編集できます。COMP-3 / packed decimal 項目は、画面上では通常の数値として表示・入力し、保存時に packed decimal のバイト列へ変換します。

## 画面イメージ

![FixedDataBuilder のスクリーンショット](docs/screen-image-utf8-reload-v0.1.38.png)

項目縦表示とレコード縦表示を切り替えできます。画面上部には選択中の定義ファイルとデータファイルのパスを表示します。最近使ったファイルは `ファイル` メニューから、定義ファイル・データファイルそれぞれ直近 20 件まで選べます。

文字・数値・見出しは左寄せで表示します。読み込み直後の列幅は、見出しと表示データが収まる幅に自動調整します。空白を示す `･` / `□` も幅に含めます。読み込み後は列の境界をドラッグして手動調整できます。

レコード縦表示の左端は「行」を見出しとし、`1`、`2`、`3`…の番号だけを表示します。各項目のバイト位置は `[1-22]` の形式で表示します。データ読込プレビューも「行」と番号だけの表示です。

## ドラッグ＆ドロップで開く

エクスプローラーから、ファイルを画面上の表やファイル欄へドラッグ＆ドロップして開けます。

- 定義 CSV（`.csv`）や COBOL コピー句（`.cbl` / `.cpy` / `.cob`）は、定義として読み込みます。
- 固定長データは、先に定義を開いてからドロップします。通常のメニュー操作と同じ `データ読込条件` 画面で、改行区切り・データ文字コード・型N文字コード・プレビューを確認できます。
- 定義1件と固定長データ1件をまとめてドロップすると、定義、データの順に読み込みます。選択順は問いません。
- データの拡張子が `.csv` の場合は、画面上部の `データファイル` 欄へ単独でドロップしてください。拡張子から判定できない定義は `定義ファイル` 欄へドロップできます。
- FixedDataBuilder が出力した Excel（`.xlsx`）も、単独でドロップして取り込めます。

フォルダー、3件以上のファイル、定義同士・データ同士など種類を判別できない組み合わせは受け付けません。定義とデータが両方 `.csv` の場合は、対応するファイル欄へ1件ずつドロップしてください。

## UTF-8 で読み込む

定義を開いてからデータファイルを選び、`データ読込条件` の `2. データ文字コード（X・数字）` で `Shift_JIS` または `UTF-8` を選んでください。選択を変えるとプレビューも更新されます。保存時と再読み込み時も、選んだ文字コードを引き継ぎます。

`3. 型N 文字コード` は別に指定できます。選択肢は `Shift_JIS` / `UTF-16LE` / `UTF-32LE` です。UTF-8 の入力は BOM あり・なしの両方を読み込めます。UTF-8 での保存は BOM なしです。`X(n)` は選択した文字コードで n バイト以内になるよう入力してください。

![データ読込条件画面](docs/screen-image-data-load-options-v0.1.38.png)

## 再読み込み

`ファイル → 再読み込み` または **F5** で、現在のデータファイルを読み直します。外部で更新された固定長データや取り込み元の Excel を再読み込みできます。定義・改行区切り・文字コード・表示形式・項目の表示設定を引き継ぎます。未保存の編集がある場合は、変更を破棄するか確認します。

## ダウンロード

最新版 **v0.1.38** では、データ文字コードに UTF-8 を追加し、F5 での再読み込みに対応しました。左寄せ表示・列幅自動調整・ドラッグ＆ドロップにも対応しています。

[FixedDataBuilder-v0.1.38.zip をダウンロード](https://github.com/konikatsu/FixedDataBuilder/releases/download/v0.1.38/FixedDataBuilder-v0.1.38.zip)

[v0.1.38 の変更内容](https://github.com/konikatsu/FixedDataBuilder/releases/tag/v0.1.38) ／ [リリース一覧](https://github.com/konikatsu/FixedDataBuilder/releases)

zip を展開して `FixedDataBuilder.exe` を実行してください。zip には `samples/` 配下のサンプル定義・サンプルデータも同梱しています。

## 定義書 CSV / COBOL コピー句

UTF-8 CSV を想定しています。基本形式は `項目名,定義` の 2 列です。

```csv
項目名,定義
名前,N(10)
英名,X(10)
年齢,9(2V1)
体重,S9(3V2)
攻撃力,S9(9) COMP-3
```

COBOL コピー句（`.cbl` / `.cpy`）も定義ファイルとして読み込めます。コピー句ファイルは UTF-8、固定形式の 7 桁目ルールありとして扱います。選択ボタンからコピー句を選ぶと、PIC 句付きの項目を表へ表示します。

![COBOL コピー句読み込みのスクリーンショット](docs/screen-image-field-visibility-v0.1.23.png)

```cobol
       01 SAMPLE-RECORD.
          05 名前       PIC N(10).
          05 英名       PIC X(10).
          05 年齢       PIC 9(2)V9(1).
          05 体重       PIC S9(3)V9(2).
          05 攻撃力     PIC S9(9) COMP-3.
```

コピー句読み込みの初期対応範囲:

- `PIC X(n)` / `PIC N(n)`
- `PIC XXXXXX` / `PIC 999V99` のような括弧なし表記
- `PIC 9(n)` / `PIC S9(n)`
- `PIC 9(n)V9(m)` / `PIC S9(n)V9(m)`
- `COMP-3` / `PACKED-DECIMAL`
- 同一 PIC 行の基本項目 `OCCURS n TIMES`
- 集団項目 `OCCURS n TIMES` の限定対応
- `REDEFINES` 配下の `OCCURS`、多重 `OCCURS`、`OCCURS DEPENDING ON` は未対応です
- `REDEFINES` は物理レコード長には加算せず、保存時は重複領域として書き出しません。画面の表示対象からは除外します
- 66 レベル、88 レベルは固定長データ項目ではないため読み飛ばします
- 英字の COBOL 項目名は内蔵辞書で可能な範囲だけ日本語表示名に変換します
- コピー句由来のデータ読み込みでは、通常のデータ文字コードの初期値は UTF-8 です。`データ読込条件` で Shift_JIS / UTF-8 を選べます。N 項目は別に選択した型N文字コードで読み込みます

対応している COBOL 表記:

- `9(n)`: 符号なし数字
- `S9(n)`: 符号付き数字
- `9(nVm)`: 小数桁付き符号なし数字
- `S9(nVm)`: 小数桁付き符号付き数字
- `X(n)`: 選択した Shift_JIS / UTF-8 の n バイト領域
- `N(n)`: 全角文字、全角 n 文字領域
- `9(n) COMP-3`: 符号なし packed decimal
- `S9(n) COMP-3`: 符号付き packed decimal

## できること

- 定義書 CSV 読み込み
- 定義書 CSV 作成
- 定義書 CSV 作成時の型選択
- 定義書 CSV 作成時、文字型の小数桁は入力不可
- `定義作成` は空の新規作成、`定義修正` は読み込み済み定義の編集
- 固定長データ読み込み
- 定義・データ・Excel のドラッグ＆ドロップ読み込みと、定義＋データの同時ドロップ
- 読み込み時に改行あり / 改行なし、データ文字コード（Shift_JIS / UTF-8）、型N文字コード（Shift_JIS / UTF-16LE / UTF-32LE）を選択
- `データ読込条件` 画面で、選択中の条件によるデータプレビューを確認できます

- `ファイル → 再読み込み` / F5 による、同じ条件でのデータ再読み込み
- 保存時に読み込み時の改行形式を継承
- 上書き保存 / 名前を付けて保存
- 項目縦表示 / レコード縦表示の切り替え
- `項目表示` から、画面に表示する項目を選択できます。非表示にした項目も内部データとしては保持され、保存時のレコード構造からは除外されません
- レコード縦表示時の項目ヘッダーにバイト位置ルーラを表示
- OCCURS 展開項目は、項目縦表示ではグループ見出し行、レコード縦表示では OCCURS 専用行の帯表示でまとまりを表示
- レコード追加、複製、削除
- セル編集
- 表とHEX表示のフォント名・サイズ設定と記憶
- 初期フォントは `MS ゴシック 12pt`
- 数値のゼロ埋め表示
- 符号付き数値の符号表示
- 文字項目で半角スペース・全角スペース・埋めスペースを `･` / `□` で表示し、空白部分だけを薄黄色で表示
- 型・桁数の簡易検証
- 検証エラーセルの薄赤表示
- 選択項目の HEX 表示
- 画面表示形式の Excel 出力
- Excel 出力後に作成したブックを開く
- FixedDataBuilder が出力した Excel の取り込み
- 定義に応じた Shift_JIS / UTF-8 の固定長データ保存
- `N(n)` の未入力値は全角スペースで保存
- `S9(n)` / `S9(nVm)` の表示数値は末尾桁の ASCII ゾーン符号で保存
- COMP-3 / packed decimal の暫定エンコード・デコード

COMP-3 / packed decimal は、現時点では以下の符号ニブルを前提にしています。

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

## サンプル

サンプルは、定義ファイルとデータファイルの先頭名をなるべくそろえています。

### CSV 定義サンプル

まずは `samples/basic-definition.csv` と `samples/basic-data-sjis-crlf.dat` を読み込んでください。Shift_JIS 前提、改行ありの固定長データです。

```csv
ジナン,JINAN,7,6.7,100
キナコ,KINAKO,3,3.8,50
オジュン,OJUN,18,45.1,-9999
```

攻撃力は `S9(9) COMP-3` の packed decimal としてデータファイルに格納しています。旧名の `definition.csv`、`sample.dat`、`sample-shiftjis.dat` も互換用に残しています。

### COBOL コピー句サンプル

コピー句サンプルは、定義ファイル名とデータファイル名の先頭をそろえています。データファイル名の末尾は読み込み条件を表します。

- コピー句ファイルは UTF-8 です
- データファイル名の `crlf` は `改行あり (CRLF/LF)`、`none` は `改行なし` を表します
- データファイル名の `n-utf16le` / `n-utf32le` / `n-sjis` は、型N文字コードの選択値を表します
- 型Nは固定長領域として扱うため、可変長のUTF-8は選択肢から外しています
- 以下の表にない組み合わせは、エラーまたは文字化けになります

| 用途 | 定義ファイル | データファイル | 改行区切り | 型N文字コード |
| --- | --- | --- | --- | --- |
| 基本コピー句 | `copybook-basic-definition.cbl` | `copybook-basic-data-crlf-utf8-n-utf16le.dat` | 改行あり (CRLF/LF) | UTF-16LE |
| 基本コピー句 | `copybook-basic-definition.cbl` | `copybook-basic-data-none-sjis-n-sjis.dat` | 改行なし | Shift_JIS |
| 基本コピー句 | `copybook-basic-definition.cbl` | `copybook-basic-data-none-utf8-n-utf16le.dat` | 改行なし | UTF-16LE |
| 基本コピー句 | `copybook-basic-definition.cbl` | `copybook-basic-data-none-utf8-n-utf32le.dat` | 改行なし | UTF-32LE |
| 基本項目OCCURS | `copybook-occurs-definition.cbl` | `copybook-occurs-data-crlf-utf8-n-utf16le.dat` | 改行あり (CRLF/LF) | UTF-16LE |
| 集団項目OCCURS | `copybook-group-occurs-definition.cbl` | `copybook-group-occurs-data-crlf-utf8-n-utf16le.dat` | 改行あり (CRLF/LF) | UTF-16LE |

基本コピー句サンプルでは、顧客名、英名、年齢、金額の 3 レコードを確認できます。金額は `S9(9) COMP-3` の packed decimal で、3 レコード目は `-9999` です。

基本項目OCCURSサンプルでは、以下のように PIC 付き項目に直接指定された `OCCURS` を展開します。

```cobol
05 ITEM-CODE  PIC X(4) OCCURS 3 TIMES.
05 ITEM-COUNT PIC 9(3) OCCURS 3 TIMES.
```

集団項目OCCURSサンプルでは、以下のように集団項目の配下にあるPIC付き項目を回数分展開します。

```cobol
05 DETAIL OCCURS 3 TIMES.
   10 PRODUCT-CODE PIC X(4).
   10 QUANTITY     PIC 9(3).
```

画面上では、たとえば以下のような項目名になります。

```text
明細-1.商品コード
明細-1.数量
明細-2.商品コード
明細-2.数量
明細-3.商品コード
明細-3.数量
```

項目縦表示では `明細-1` / `明細-2` のような見出し行を挟み、レコード縦表示では OCCURS 専用行でグループごとの帯を表示します。データセルの背景は通常色のままにして、値を読み取りやすくしています。

![OCCURS レコード縦表示のスクリーンショット](docs/screen-image-occurs-visual-record-v0.1.33.png)

`REDEFINES` 配下の `OCCURS`、多重 `OCCURS`、`OCCURS DEPENDING ON` は現時点では未対応です。

旧名の `definition-english.cbl`、`sample-copybook-*.dat` も互換用に残しています。

## ビルド済み exe

`release/FixedDataBuilder.exe` に Release ビルド済みの実行ファイルを配置しています。

## 今後の対応予定

- 定義書フォーマットの追加対応
- COMP-3 / packed decimal の詳細ルール設定
  - 符号ニブルを環境に合わせて選択できるようにする
    - 例: 正数を `C`、負数を `D`、符号なしを `F` として保存する
    - 例: 符号なし COMP-3 でも末尾ニブルを `F` にするか、別のルールにするかを選べるようにする
  - 桁数とバイト数の扱いを画面で確認できるようにする
    - 例: `S9(9) COMP-3` が 5 バイトになることを表示する
    - 例: 奇数桁 / 偶数桁で先頭 0 を補うかどうかを確認できるようにする
  - 小数桁付き COMP-3 の端数処理を選択できるようにする
    - 例: `S9(3V2) COMP-3` に `12.345` を入れたとき、エラーにする / 四捨五入する / 切り捨てる、を選べるようにする
  - 読み込んだバイト列と画面表示値の対応を確認しやすくする
    - 例: packed decimal の元バイト、符号ニブル、画面上の数値を並べて確認できるようにする
- HEX 表示の編集支援
- 検証内容の拡充
