# SJISとMS932の選択

v0.1.51から、CSV、固定長のX項目、固定長の型NでSJISとMS932を区別します。

- **SJIS (JIS X 0208)**：ASCII（制御文字を含む128）、JIS X 0201の半角カナ63、JIS X 0208の6,879文字。NEC・IBM拡張、外字領域、未定義コードは受け付けません。JIS X 0213 / Shift_JIS-2004は対象に含みません。
- **MS932 (CP932)**：Windowsのコードページ932。従来のShift_JIS指定はこの実装でした。既存の設定ファイルの数値を変えず、新しいSJISを追加しています。
- **UTF-8**：CSVと固定長のX文字コードで選択できます。型Nは固定バイト幅が必要なので、SJIS / MS932 / UTF-16LE / UTF-32LEから選びます。

固定長の補完、桁数、数値・COMP-3の扱い、CSVの空欄・空白・先頭ゼロを保持する仕様は従来どおりです。選択した文字コードを、表示・HEX・保存・F5・読込条件の記憶へ引き継ぎます。SJISへの変換に失敗した場合は、既存の保存先ファイルを変更しません。

## 記号の対応

| バイト | SJIS | MS932 |
| --- | --- | --- |
| 8160 | U+301C 波ダッシュ `〜` | U+FF5E 全角チルダ `～` |
| 8161 | U+2016 二重縦線 | U+2225 平行記号 |
| 817C | U+2212 マイナス | U+FF0D 全角ハイフンマイナス |
| 8191 | U+00A2 セント | U+FFE0 全角セント |
| 8192 | U+00A3 ポンド | U+FFE1 全角ポンド |
| 81CA | U+00AC 否定記号 | U+FFE2 全角否定記号 |

SJISの0x00〜0x7FはASCIIとして扱い、0x5CはU+005C（バックスラッシュ）、0x7EはU+007E（チルダ）です。0x815FはU+FF3C（全角バックスラッシュ）です。フォントによってバックスラッシュが円記号に見えることがあります。U+00A5（円記号）やU+203E（オーバーライン）をASCIIへ自動置換せず、SJISで保存する場合はエラーにします。代替文字やベストフィット変換を使いません。

## 実装と検証の出典

.NETのコードページ932自体はWindowsのShift-JIS実装なので、新しいSJISは別の厳密な変換処理を使用します。[Microsoftのコードページ一覧](https://learn.microsoft.com/en-us/windows/win32/intl/code-page-identifiers)を参照してください。

JIS X 0208の割当位置は[Unicode ConsortiumのJIS X 0208対応情報（Mozcに保存された表）](https://code.googlesource.com/mozc/+/HEAD/src/data/unicode/JIS0208.TXT)を参照しました。元の表ファイルは配布せず、割当位置の範囲と記号の差分をコードで表現しています。Unicode, Inc.による情報をUnicode対応製品の作成に使用しています。

ASCIIと全角バックスラッシュの対応は[CPythonのCJK codec仕様](https://github.com/python/cpython/blob/main/Modules/cjkcodecs/README)と同じ規則です。SJISの全7,070文字コードについて、CPythonのshift_jis変換から独立に作成した期待値のSHA256と一致し、各文字コードの往復も確認します。上記の円記号・オーバーラインの一方向エンコード別名は採用していません。

## 起動引数

- CSV：`--format csv --csv-encoding sjis`、`--csv-encoding ms932`、`--csv-encoding utf8`
- 固定長X：`--text-encoding sjis` / `ms932` / `utf8`
- 固定長N：`--national-encoding sjis` / `ms932` / `utf16` / `utf32`

`sjis` / `shift_jis` は新しい厳密なSJIS、`ms932` / `cp932` / `windows-31j` はMS932です。以前のCLIで `sjis` を指定してWindows拡張文字を扱っていた場合は、`ms932` に変更してください。引数を省略した際の既存定義ごとの初期値と、記憶済み条件の互換性は維持します。
