# README_FOR_CODEX

## 2026-10-26 更新：公開リリースは最新3版、ダウンロードをREADME上部へ

- ユーザーが「gitには最新から3バージョンを残し、それ以外は削除」と明示。以前のSAC比較のため公開Releaseをすべて保持する運用を更新し、GitHub Releasesは最新3つだけとする。タグ・ソース履歴・ローカル比較用ファイルは削除しない。
- 今回残す公開版はv0.1.49・v0.1.48・v0.1.45。v0.1.44〜v0.1.31の公開されていた14リリースを削除。未公開のv0.1.46/v0.1.47を公開版数に数えない。
- 削除前にRelease説明とアセットの保管・SHA256一致を確認。既存ローカルZIP11個を再利用し、ローカルになかった3個だけ取得して重複を避けた。削除対象の配布アセットは合計2,552,520バイト。新規取得は506,296バイト。証拠とバックアップの索引はreviews/release-retention-20261026/v001。
- ユーザーの追加指示で、READMEのダウンロードリンク、必要ランタイム、リリースへのリンクを紹介文の直後・画面画像より上へ移動した。製品コード・配布ZIP・タグの差し替えは行わず、文書だけを更新する。

## v0.1.49：未保存表示・読込条件の記憶・定義専用再読込

- ユーザーが提案の優先2件（未保存表示と読込条件の記憶）の実装とpushを依頼。v0.1.48からcodex/unsaved-and-read-settings-v0.1.49で作業。追加指示で定義ファイル専用の再読込ボタンも追加した。
- 実際のセル変更と追加・複製・削除でタイトルに【未保存】を表示。成功した読込・保存で解除し、失敗や取消では保持する。終了、定義・データ切替、Excel取込、定義編集、全体再読込の前に未保存を確認する。UnsavedChangesFormの保存・破棄・キャンセルを用い、既定と初期フォーカスをキャンセルにする。保存の取消・失敗では後続処理へ進めない。
- DataLoadSettingsStoreが定義とデータの正規化パス、選択レイアウトIDごとに形式・CSV文字コード・改行・型N文字コードを最大100組記憶。保存先は%LOCALAPPDATA%/FixedDataBuilder/data-load-settings.json。正常なGUI読込・保存時だけ記録し、アプリの再起動後も読込画面の初期値にする。破損JSONは既定値へ戻り、設定保存失敗でもデータ読込を継続する。CLIで明示した読込条件は従来どおり適用する。
- 新しい「定義を再読み込み」はデータ・読込条件・未保存状態を維持する。項目数、順序、型、桁数、REDEFINES等の構造や文字コード条件が変わる場合は適用せず、全体再読込を案内。定義のみの未編集状態なら項目追加等も反映。元のF5は「全体再読込 (F5)」と表示する。Excel由来の定義は実ファイルではないため、元のExcelの全体再読込を案内する。
- 既存66件と新規14件で80件成功・終了0。テストでは利用者の履歴とdata-load-settings.jsonを終了時に元のバイト列に戻す。追加テストは保存／取消／失敗、再起動時のCSV・固定長条件復元、設定の破損・書込失敗、定義再読込のデータ保持を確認する。
- 最終成果物はreviews/unsaved-and-read-settings-v0.1.49/v003。v001は優先2機能、v002で定義専用ボタン、v003で確認ダイアログの初期フォーカスをキャンセルへ調整。すべて通常の機能変更で、信頼回避用の再ビルドではない。最終DLL SHA256=5DBD70285F6E18AD019F21A918D3C02121A48554B7263320D864BF052BCFFBAD、EXE SHA256=1B0DD602F10DBE4637B1874A3CFEEB023C6ACBF3C8F76D302BE1FB6B7A50DCB0。テストとpublishのDLLハッシュ一致、SAC Onで製品EXEのメイン画面とPC側.NET8.0.32利用を確認。
- READMEの通常・編集中・未保存確認・記憶した読込条件・CSV画面を実際のフォームから撮影して更新。確認した製品5ファイルを再ビルドせず配布する。軽量版のみを公開し、過去版・業務データ・SAC比較ファイルは削除しない。reviewsと利用者の条件ファイルをコミットしない。ダウンロード後の別PC確認はユーザーが担当する。

## v0.1.48：v0.1.45ベースで見出しを項目名・型・位置の3行に分離

- 2026-10-26、ユーザーがv0.1.45をベースに項目名、型、[1-22]などを別の行にしたいと明示。origin/mainのv0.1.45からcodex/separate-header-rows-v0.1.48を作成し、未公開v0.1.46/v0.1.47のソースを継ぎ足していない。
- レコード縦の列ヘッダーには項目名だけを置き、その下の2行を型・位置専用のReadOnly/Frozen行にする。3段の見出しが独立し、スクロールしても残る。位置は[開始-終了]、CSVはCSV列N。データ行番号は1からで、型・位置は件数に含めない。項目縦表示は従来どおり。
- RecordRowsDataStartIndexに2行分を反映。編集、選択、HEX、複製・削除、空白描画、列幅計算から見出し行を除外。OCCURS帯の描画位置も2行下へ移動した。
- Excelの先頭3行も項目名・型・位置とし、すべてHeaderスタイル（水色）。ExcelImporterは型・位置ラベルから3行形式を識別し、従来の複数行セル・2行ヘッダーも受け付ける。OCCURS帯はデータ行として取り込まない。
- 既存61件を新しいデータ開始位置に合わせて更新し、見出し4件とGUIボタン1件を追加、計66件成功・終了0。最初のOCCURS追加テストは単項目OCCURSのサンプルを帯付きとして扱って失敗したため、正しいグループOCCURSのサンプルに修正した。製品コードの修正による信頼回避ではない。
- テストと同じビルドをpublish --no-buildで軽量版として配置し、製品DLLのハッシュ一致を確認。SAC Onで製品EXEからv0.1.48のメイン画面が開いた。EXE SHA256=5E457A56D7E850FF9948A10CB16B39A390E49D30FED5E1DF69587625AFB448CA、DLL SHA256=A10471AA912EEFA6262AC630A0911636B32F688B793D3D7DA1FCE1FB36560CA7。PC側.NET8.0.32を使用。
- 追加のユーザー指示で、データファイル欄の右に「再読み込み (F5)」ボタンを追加。既存のReloadCurrentFilesを共用し確認処理も共通。メイン・読込・保存条件に「半角の英字・数字はUTF-8でもShift_JISでも同じ」と、日本語・半角カナは異なる旨を表示する。X項目の実際の文字コード表示と読込・保存処理は維持する。
- 通常画面・CSV画面・読込条件・保存条件を実際のフォームから撮影してREADMEへ反映。撮影ヘルパーの保存画像名も製品版数から生成し、過去のv0.1.39画像を上書きしないよう修正した。以前のF5操作画像とExcel配色例は過去版の参考として維持する。確認後の製品ファイルを再ビルドせずrelease/とZIPにコピーして公開する。
- 見出しだけの初回検証はv001、GUIボタンと説明追加後の最終証拠はC:/dev/FixedDataBuilder/reviews/separate-header-rows-v0.1.48/v002。v002の製品EXEでもSAC Onの起動を確認しており、このファイルを配布する。今後の配布は軽量版。過去リリース・ユーザーデータ・SAC比較ファイルを削除しない。業務ファイルとreviewsは公開しない。ダウンロード後の別PC確認はユーザーが担当する。

## v0.1.45：F5再読み込みと軽量版への統一

- ユーザーの最終指示は「今後、軽量版で、gitにpush」。以後の配布はWindows x64の.NET非同梱版に統一する。Properties/PublishProfiles/Lightweight.pubxmlを使用し、起動確認した成果物を再ビルドせず配布する。
- v0.1.44を基にファイル→再読み込み/F5を追加。形式、文字コード、型N文字コード、改行条件を維持する。編集破棄の確認は既定No。ReadDefinitionを解析と適用に分離し、両ファイルの解析成功後にモデルを置換する。失敗時は現在の定義とデータを保持する。XML表示欄は引き続き非表示。
- v001では製品EXE・検証EXEがSAC拒否。ユーザーの明示指示で軽量版を再作成したv002では製品EXEのメイン画面表示、SAC On、PC側coreclr8.0.32利用を確認した。前回も軽量版だったため、軽量化が許可の原因とは断定しない。
- 確認済み製品DLL SHA256=528842AB9A89E97263B250DDBBC2A680E919C35A94EAE49A5657069029FF8225。製品EXEから架空データを開き、CSV変更反映、確認取消の編集保持、固定長の定義とデータの変更反映、読込エラー時の内容保持の4項目を実画面で確認。通常画面・CSV画面・メニュー・確認ダイアログの実写をREADMEへ反映した。
- 回帰テスト8件を追加（計61件）、警告0・エラー0でビルド済み。テスト側の製品DLLと配布DLLのハッシュ一致を確認したが、検証EXE自体が拒否されるため61件の実行は未完了。製品UIの4項目確認と自動テストを混同しない。
- 起動確認済みv002の5ファイルをrelease/とZIPへそのままコピーし公開する。ユーザーの業務データ、reviews、イベントログはコミット・配布しない。過去リリースはSAC比較用に保持し、削除しない。ダウンロード後の別PC確認はユーザーが担当する。
- 証拠はC:/dev/FixedDataBuilder/reviews/reload-v0.1.45/v002。セキュリティ設定変更や、別ホスト経由の信頼回避は行っていない。

## v0.1.44 追記：隠し機能の表示欄を常時非表示にする

- ユーザーが上部の「XMLレイアウト」を見せたくないと指示。ラベル・xmlLayoutTextBoxをVisible=false、該当行の高さを0とし、空き行を残さない。XML読込と内部の選択レイアウト情報は維持する。
- 公開用README・リリース説明では隠し機能を宣伝しない。「画面上部の表示を整理」と説明し、通常表示とCSV表示の画像をv0.1.44へ更新。旧画像を加工せず、新しいフォームから撮影した。
- 既存53件すべて成功。SDK8のdotnet run --artifacts-pathはビルド先と起動先が食い違ったため、同コマンドで作成された実際のテストEXEを実行した。最初の失敗はファイル未検出であり、SAC拒否ではない。
- テストと同じビルド出力をpublish --no-buildで配布し、DLLハッシュ一致を確認。軽量版の製品EXEでもSAC Onの環境でv0.1.44のメイン画面表示を確認した。起動成功したファイルを再ビルドせずZIP化する。
- ユーザーはダウンロード後の別PC確認を自分で行うと指定済み。未確認の同梱版は公開しない。v0.1.41はSAC比較に必要なため、過去リリースの自動削除はしない。
- 途中でリロード機能の有無を質問された。現行版にはF5再読み込みはないと回答。追加の依頼はまだなく、今回の変更には含めない。
- 証拠はローカルreviews/hide-xml-layout-v0.1.44/v001。公開サンプル以外の業務データ・ログをコミットしない。

## 2026-10-25 v0.1.43軽量版の公開

- ユーザーが同梱版・分離版の公開を依頼した後、「起動を確認できた版だけ公開する」と指定。最終指示は「軽量版の起動は確認済なんだね？じゃ、それを公開してよ」「ダウンロード確認は僕が別のPCでやる」。今回公開するのは起動確認済みの軽量版だけ。
- アプリのソースはv0.1.41を基点とする175386aのまま。通常の`dotnet publish -c Release -r win-x64 --self-contained false`、UseAppHost=true、SingleFile/ReadyToRun/Trimmed=falseで作成。SDK8.0.426。構成別の`--artifacts-path`と出力先を分離した。
- 実ファイルからメイン画面の起動を確認し、Windows側でSAC=On、PID25072、タイトルFixedDataBuilder v0.1.43、coreclrがPC側の.NET8.0.32から読み込まれることを記録した。EXE SHA256=D968CD024D9DE6F2C16B99083C2F329CE94B309EDFFC5AD12A382675D8A1546C、DLL SHA256=4CA98FE57A18B83C174163E3BD3408CDB207168252B8770D32D9466DD390487F。
- 確認済みpublishフォルダーの5ファイルをそのままreleaseとZIPへコピーし、再ビルドしない。以前の未公開ZIP（DLL FE267DAB...）を取り違えないこと。
- 公開ZIP名は`FixedDataBuilder-v0.1.43-win-x64-framework-dependent.zip`。EXE等とsamplesが直下。.NET8 Windows Desktop Runtime x64が別途必要。同梱版は公開しない。
- ダウンロード由来の状態での別PC検証は、ユーザーの明示指示でユーザー側に任せる。53件の回帰テスト完了や全環境のSAC通過を主張しない。README画像は従来版の実写を参考として維持し、新形式を本文で説明する。
- 証拠はローカル`reviews/dual-package-v0.1.43/v001`。実行に使った軽量版のハッシュ、SAC状態、読み込んだランタイムを保存。同梱版は正常起動を確認できていない。

## v0.1.43 追記：v0.1.41ベースの開始位置・長さ表示

- ユーザーの明示指示により、v0.1.41のコミット `df3c84cfd668e2486d8241293e8715a41ebad8f3` からブランチを作成した。v0.1.42はベースにしない。
- v0.1.41との製品コード差分は、レコード縦の位置見出しを `[開始-終了]` から `[開始位置(長さ)]` にする1行と、版数プロパティだけ。例は `[1(22)]`、`[23(15)]`。開始位置は1始まり、長さはバイト数。Excel出力も同じ見出しを使う。
- 固定長・CSVの処理、CSV列番号の見出し、Excelの水色と元ファイル名プレフィクスはv0.1.41どおり。既存の表示テストの期待値2箇所だけを新形式へ更新する。
- ビルドは成功したが、検証ホストが製品 `FixedDataBuilder.dll` を読み込む段階でSAC拒否（FileLoadException 0x800711C7）。53件の実行と新しい画面の撮影は未完了。別ホストでの実行やポリシー変更は試していない。
- READMEは実際に撮影したv0.1.39の画像を参考として維持し、新形式と実行確認の制限を明記する。架空のv0.1.43画面を作らない。
- 10月25日、ユーザーのダウンロード版v0.1.42についても製品EXEの3077をWindows側ツールで取得した（RecordId3877、SAC標準ポリシー、未署名）。通常execのイベントログ表示は古い記録までだったため、最新ログはdev_runnerのrun_powershellから取得する。関連証拠はローカルreviews/smart-app-control-v0.1.42/v001に保存。

## v0.1.41 追記：Excel出力名の元ファイルプレフィクス

- Excel出力の既定名を `元データファイル名_FixedDataBuilder_項目縦.xlsx` / `元データファイル名_FixedDataBuilder_レコード縦.xlsx` に変更。元ファイルの拡張子は除く。データファイルがなければ定義名、どちらもなければ従来の名前へフォールバックする。
- 保存ダイアログの既定名だけを変更し、利用者が編集できる。Excelのデータ内容・水色背景は維持する。
- このプレフィクスは、画面上で現在選択されているデータファイル名から取得する。名前を付けてデータ保存した後は、その保存先名になる。
- ビルドは成功したが、この環境で検証用EXE/DLLがSmart App Controlに起動拒否された。v0.1.41の53件の実行結果は未取得。`reviews/excel-filename-v0.1.41/v001` に起動エラーとCodeIntegrity 3077を保存し、リリースにも実行確認未完了と記載する。Windows設定の変更・ポリシーの無効化はしていない。

## v0.1.40 追記：Excel出力の水色背景

- Excel出力の行番号・項目名・定義欄・見出しを、水色 `#DDEBF7` のベタ塗りに統一した。データ部分は塗りつぶしなし。
- Excel標準のfill 0/1をnone/gray125として予約し、実セルの見出し・定義スタイルはfill 2だけを参照する。以前のfill 1参照を避け、実セルにgray125を適用しない。
- 固定長・CSVの処理やメイン画面の色は変更していない。既存53件の検証成功。両レイアウトの出力xlsxで実セルのパターン参照が0件、水色と白の使い分け、元データ値の保持を確認した。
- `--capture-excel <レコード縦xlsx> <項目縦xlsx>` で架空5件のExcel出力を作れる。READMEに両レイアウトの出力プレビューを追加。冒頭の従来メイン画像は維持する。
- 番号を行の横・項目の上に付ける資料作成の依頼はユーザーが取消済み。今回の対象はExcel出力の背景色のみ。
- v0.1.39はユーザーPCで警告なしに起動できたとの報告あり。SACの一般的な拒否原因の確定とは区別する。

## v0.1.39 追記：v0.1.37ベースの固定長／可変長CSV入出力

- ユーザーの明示指示で、コードはv0.1.37を基にした。v0.1.38の固定長UTF-8選択・BOM除去・F5リロードを引き継がない。固定長Reader/Writer、RecordValidator、CopybookDefinitionReaderはv0.1.37と同一。
- 読込条件に固定長／可変長CSVの選択を追加。CSVは定義の物理項目順、ヘッダーなし。UTF-8（BOM可）／Shift_JIS、引用符、カンマ、引用符内改行、空欄を解析する。REDEFINESはCSV列から除外し、非表示項目は保持する。
- 保存条件でも形式を選択し、読込形式を初期値とする。上書き保存は現在の形式。CSVは全列引用符・CRLF・UTF-8 BOMなし、またはShift_JIS。変換不能文字では書込前に失敗させる。固定長保存は従来の補完・型検証・エンコーダを使用する。
- 空欄、先頭・末尾の半角／全角空白、数値の先頭ゼロをCSVでは保持。数値や空白の桁埋めを表示・保存で追加しない。空白記号は実在する空白だけ。CSV見出しはCSV列番号、HEXは選択値のCSV文字コードによるバイト列。
- 既存37件とCSV16件の計53件、提供された定義とCSVのローカル統合確認1件の計54件が成功。提供データは10行×29項目で、CSV往復の値一致と元ファイル未変更を確認した。実データ・業務定義は公開しない。
- Geminiのv001レビュー指摘に対応：CSV列を持たない数値REDEFINESの内部初期値を0にして固定長変換を許可。CSVの新規行・定義拡張時の表示項目は空欄。CSV HEXも厳密エンコード。同形式の保存では手動列幅・選択セルを維持する。修正差分と再検証はレビューv002に保存する。
- 公開用は架空の5行CSVサンプルのみ。README冒頭は従来の5行表示確認サンプルの構成を保ってv0.1.39として撮影。CSV読込・メイン・保存条件画像を別に掲載する。
- ユーザーはAntigravity CLI Gemini 4 Argon Highでのファイルベースのレビューを明示依頼。依頼書と結果は版付きで、ローカルreviewsに保存。認証情報や無関係な個人情報を含めず、提供業務ファイルは検証・レビューだけに使う。
- COPY句でうまく動かない場合があるとの追加報告あり。既存サンプルは通るが、問題のCOPY句と具体的症状は未提供で、未解決として引き継ぐ。今回、推測によるパーサ変更はしない。
- v0.1.38のSAC問題レビューは別途ローカル保存。v0.1.39がSACで許可されることを、この環境のテスト成功から保証しない。

## v0.1.37 追記：行番号と位置見出し

- レコード縦表示とデータ読込プレビューの左端見出しを「行」、値を `1`、`2`…に変更。項目縦表示の列見出し `Rec 1` 等は今回の対象外。
- レコード縦表示のバイト位置見出しは `byte 1-22` から `[1-22]` に変更した。
- Excel取込は新しい「行」見出しと、従来の「レコード」見出しの両方を認識する。既存テストに新旧形式の確認を加え、37件成功を確認する。
- READMEの説明・冒頭画像・ダウンロード案内、配布物を v0.1.37 へ更新。冒頭画像は `docs/screen-image-row-labels-v0.1.37.png`。今回もpushとReleases公開を行う。

## v0.1.36 追記：文字位置の統一と初期列幅

- 空白を記号で表示するセルだけ通常セルより3px右にずれることを画素比較で再現し、標準セルと同じ余白・描画フラグに修正した。フォント変更で対処する問題ではなかった。
- ユーザー方針に合わせ文字・数値・見出しを左寄せに統一。選択中は選択色で文字と空白記号を表示する。
- グリッド再構築時と表示フォント変更時に、見出し・データ・補完される空白記号まで収まる列幅へ調整する。自動サイズモードはNoneのままにして、読込後の手動幅調整とセル編集時の幅を保持する。
- `tests/FixedDataBuilder.Tests/CellDisplayTests.cs` に表示回帰テスト6件を追加し、計37件成功。項目縦/レコード縦、MS Gothic/Consolas、12pt/18ptで文字開始位置を比較し、列幅・選択色・手動幅保持も検証した。
- `--capture-display <PNG絶対パス>` は実データを使わず架空5レコードで表示確認画像を作成する。README冒頭画像は `docs/screen-image-cell-display-v0.1.36.png`。
- v0.1.36の実行ファイル・サンプル入りZIP・READMEのダウンロードリンクを更新する。継続してGitHubへのpushとReleasesへの公開を行う。

## v0.1.35 追記：ファイルのドラッグ＆ドロップ

- 2026-10-23、ユーザー指示でGitHubの最新 `da24013` を取得してから実装。ローカルの既存差分の破棄と、修正完了後のGitHubへのpushはユーザー承認済み。
- メイン画面と子コントロールで定義・固定長データ・Excelのファイルドロップを受け付ける。定義1件＋データ1件は選択順によらず定義から読み込む。
- データファイル欄への単独ドロップはデータとして扱い、データの拡張子がCSVでも定義と区別する。定義ファイル欄への単独ドロップでは独自拡張子の定義も扱える。Excelはどの欄でもExcel取込へ送る。
- フォルダー、3件以上、定義同士・データ同士などの曖昧な組み合わせは受け付けない。メニュー・履歴・ドロップは同じ読込処理を使い、条件・プレビュー・履歴登録・レイアウト選択を維持する。
- データ読込は解析成功後にレコードと型N文字コードを更新する。読込失敗時に既存データを消さない。
- `tests/FixedDataBuilder.Tests/DragDropTests.cs` に15件を追加。既存16件と合わせ31件成功。`--capture-drop <PNG絶対パス>` で通常サンプルを実際のドロップ処理経由で開いた画面を保存できる。
- READMEの冒頭画像は `docs/screen-image-file-drop-v0.1.35.png`。XMLを隠し機能とする既存方針は維持する。
- 後続指示でGitHub Releasesへの公開とREADME更新も依頼された。v0.1.35のZIPをReleaseアセットとして公開し、READMEのダウンロード欄にバージョン・ZIP直リンク・変更内容へのリンクを記載する。
- ビルド用SDKがなかったため、Microsoft公式スクリプトで .NET SDK 8.0.426 を `%LOCALAPPDATA%\Microsoft\dotnet` に管理者権限なしで導入した。PATHを変更していないので、この環境では同フォルダーの `dotnet.exe` を明示してビルド・テストする。

## v0.1.34 追記：汎用XML定義

- 2026-10-22の最新指示で、この機能は隠し機能として扱う。実装は維持するが、ユーザー向けREADME.mdには説明・対応機能一覧・XML例・起動引数・サンプル案内・専用画像を掲載しない。将来のREADME更新でも再掲載しない。
- 表示名・追加コード・公開サンプル・説明には「汎用XML定義」を使う。特定製品名を追加しない。
- `XmlDefinitionReader.cs` が `FileDefinitions / File / Record / Column` 形式を解析する。固定長SEQ、型X/9、UTF-8/Shift_JIS、改行なし/CRLFに対応する。
- 複数の対応レイアウトがある場合は `XmlLayoutSelectionForm.cs` で選ぶ。1つだけなら自動選択。起動引数 `--xml-layout ID` も利用できる。
- 実用定義や業務データは公開リポジトリに追加しない。公開サンプルは `samples/generic-xml-*` の架空データを使う。
- 検証は `dotnet run --project tests/FixedDataBuilder.Tests -c Release -- <リポジトリの絶対パス>`。`--capture <PNGの絶対パス>` を追加するとサンプル表示と選択画面を保存する。
- XMLは読み込みのみ。`定義修正` で保存する定義はCSV。文字コードを変える場合は対象の定義を読み直す。

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
- 現在の開発バージョン: `v0.1.44`（公開状況はGitHub Releasesで確認）

## 現在の成功状態

最新の成功状態:

- `v0.1.37` で左端を「行」と番号に、バイト位置を `[開始-終了]` 表示に変更。READMEと配布物も更新する。
- `v0.1.36` で文字の左端を統一し、読込時の列幅を自動調整。表示検証6件を含む37件のテストが成功。
- READMEの冒頭画像・ダウンロード案内と `release/` の配布物は v0.1.36 に更新する。
- `v0.1.35` でファイルのドラッグ＆ドロップ読み込みを追加。定義＋データ同時ドロップ・Excel・キャンセル・読込失敗時の保持を含む31件のテストが成功。
- `release/` の実行ファイルとサンプル入り `FixedDataBuilder-v0.1.35.zip` を更新。README冒頭画像も v0.1.35 に更新。
- `v0.1.34` で汎用XML定義の読み込み・レイアウト選択を追加
- `release/FixedDataBuilder.exe` は v0.1.44 の Release ビルドを使う
- `FixedDataBuilder-v0.1.34.zip` を作成済み
- zip の中身は exe 等と `samples/` が直下に入る形
- README 冒頭は `docs/screen-image-compact-header-v0.1.44.png` を参照。隠し機能の専用画面画像はREADMEに掲載しない。
- XMLの形式チェック、UTF-8/Shift_JISの保存、CSV/コピー句との互換性、キャンセル時の状態保持、Excel経由の文字コード継承を確認。提供された13項目・109バイト定義と10件データの往復保存を含め、17件のテストが成功。
- README に COMP-3 の現行前提 `正数=C / 負数=D / 符号なし=F` を記載済み
- `docs/development-history.md` に作成経緯を整理済み
- v0.1.20 で、履歴選択時の落ちやすさを抑制し、半角/全角スペースを記号化して空白部分だけ薄黄色で表示する改善を実装済み
- v0.1.21 で、COBOL コピー句（`.cbl` / `.cpy`）読み込みの初期対応を追加済み。PIC 句から既存の `FieldDefinition` に変換する。
- v0.1.22 で、コピー句 UTF-8/固定形式前提、英字項目名の日本語変換、REDEFINES 表示、`PIC XXXXXX` / `PIC 999V99`、コピー句由来データの UTF-8 + N 項目 UTF-16LE/UTF-32LE 混在読み込み推定を追加。
- v0.1.23 で、`項目表示` ボタンからグリッドに表示する項目を選択できるようにした。非表示項目も内部レコードには保持し、保存時のレコード構造から除外しない。
- v0.1.24 で、データ読み込み時に改行コード選択の後で型Nの文字コードを `Shift_JIS` / `UTF-8` / `UTF-16LE` / `UTF-32LE` から選べるようにする。コピー句サンプルとして、N項目UTF-16LE・その他UTF-8の `sample-copybook-utf16.dat` を追加する。
- v0.1.25 で、画面上部に `サンプル条件` 欄を追加する。既知のコピー句サンプルを誤った定義・改行・型N文字コードで開こうとした場合は、読み込み前に専用エラーを出す。コピー句サンプルを改行有無・型N文字コード別に追加する。
- v0.1.26 で、REDEFINES 項目を画面表示・項目表示選択の対象から除外する。
- v0.1.27 で、データ読み込み時の「改行区切り」と「型N文字コード」を1つの `データ読込条件` ダイアログに統合し、選択条件で先頭レコードを試し読みするプレビュー欄を追加する。既知のコピー句サンプルは対応条件を初期選択する。
- v0.1.28 で、上部の操作ボタン群を一般的なWindowsアプリ寄りの `ファイル` / `編集` / `定義` / `表示` / `ツール` メニューへ整理する。`データ読込条件` ダイアログはリサイズ可能にし、プレビューを表形式にする。README スクリーンショットも v0.1.28 に更新する。
- v0.1.29 で、画面上部の `サンプル条件` 行を削除し、定義/データのファイル欄を読み取り専用表示にする。最近使ったファイルは `ファイル` メニュー配下の `最近使った定義ファイル` / `最近使ったデータファイル` へ移動する。サンプル名は `basic-*` / `copybook-basic-*` / `copybook-occurs-*` の分かりやすい別名を追加し、基本項目OCCURSサンプルを追加する。集団項目OCCURSは未対応としてREADMEに明記する。
- v0.1.30 で、集団項目OCCURSの限定対応を追加する。対応範囲は、非REDEFINES配下の単一集団項目 `OCCURS n TIMES` と、その配下の通常PIC項目の展開。REDEFINES配下OCCURS、多重OCCURS、OCCURS DEPENDING ON は未対応。
- v0.1.31 で、型N文字コードの選択肢からUTF-8を削除する。型Nは固定長領域なので、可変長のUTF-8は扱わない。選択肢は `Shift_JIS` / `UTF-16LE` / `UTF-32LE`。型N UTF-8サンプルは削除し、OCCURS系サンプルはUTF-16LE版に置き換える。
- v0.1.32 で、OCCURS 展開項目の視覚的なまとまりを追加する。項目縦表示では `明細-1` / `明細-2` のような見出し行を挟み、子項目をインデントする。レコード縦表示では各 OCCURS グループの先頭に見出し列を挟み、OCCURS列を淡色背景にする。スクリーンショット作成用に起動引数 `--layout record` / `--layout field` も使える。
- v0.1.33 で、レコード縦表示の OCCURS 表示を見出し列方式から OCCURS 専用行の帯表示に変更する。OCCURS 行だけグループ内の縦線を消してまとまりを見せ、データセル背景は通常色に戻す。

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
  - 同一 PIC 行の基本項目 `OCCURS n TIMES`
  - 集団項目 `OCCURS n TIMES` の限定対応
  - `REDEFINES` 配下の `OCCURS`、多重 `OCCURS`、`OCCURS DEPENDING ON` は未対応
  - `REDEFINES` は物理長に加算せず保存時は書き出さない。画面表示・項目表示選択の対象からは除外する
  - 66 レベル、88 レベルは読み飛ばし
  - コピー句ファイルは UTF-8、固定形式の 7 桁目ルールあり
  - コピー句由来のデータは X/9/S9/COMP-3 を UTF-8、N 項目をデータ読み込み時に選択した文字コードで読み込む
- 固定長データ読み込み
- 読み込み時の改行あり / 改行なしと型N文字コード選択。`データ読込条件` ダイアログに統合済み
- `データ読込条件` ダイアログで、選択中の条件による先頭レコードのプレビューを表示
- コピー句サンプルの対応条件表示。表にない組み合わせはエラーまたは文字化けすることをREADMEに明示
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
- `ファイル` メニューの最近使った定義ファイル・データファイルから読み込み

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
- 型Nの選択肢は `Shift_JIS` / `UTF-16LE` / `UTF-32LE`
- 型Nは固定長領域のため、可変長のUTF-8は選択肢に入れない
- `N(n)` は全角 n 文字領域として扱い、保存時の不足分は全角スペース
- `X(n)` は定義上は半角文字として扱う想定

### コピー句サンプル

READMEでは新しい分かりやすい名前を主に説明します。旧名の `definition-english.cbl` / `sample-copybook-*.dat` は互換用に残します。以下の組み合わせ以外はエラーまたは文字化けします。

| 定義ファイル | データファイル | 改行区切り | 型N文字コード |
| --- | --- | --- | --- |
| `copybook-basic-definition.cbl` | `copybook-basic-data-crlf-utf8-n-utf16le.dat` | 改行あり (CRLF/LF) | UTF-16LE |
| `copybook-basic-definition.cbl` | `copybook-basic-data-none-sjis-n-sjis.dat` | 改行なし | Shift_JIS |
| `copybook-basic-definition.cbl` | `copybook-basic-data-none-utf8-n-utf16le.dat` | 改行なし | UTF-16LE |
| `copybook-basic-definition.cbl` | `copybook-basic-data-none-utf8-n-utf32le.dat` | 改行なし | UTF-32LE |
| `copybook-occurs-definition.cbl` | `copybook-occurs-data-crlf-utf8-n-utf16le.dat` | 改行あり (CRLF/LF) | UTF-16LE |
| `copybook-group-occurs-definition.cbl` | `copybook-group-occurs-data-crlf-utf8-n-utf16le.dat` | 改行あり (CRLF/LF) | UTF-16LE |

OCCURSはPIC付き基本項目の同一行指定と、限定的な集団項目OCCURSに対応します。REDEFINES配下OCCURS、多重OCCURS、OCCURS DEPENDING ON は未対応です。

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
- `samples/basic-definition.csv`
  - CSV定義の基本サンプル
- `samples/basic-data-sjis-crlf.dat`
  - CSV定義用のShift_JIS・改行あり固定長データ
- `samples/basic-records.csv`
  - 固定長データ作成元の見やすいCSV
- `samples/copybook-basic-definition.cbl`
  - コピー句基本サンプル
- `samples/copybook-basic-data-*.dat`
  - コピー句基本サンプル用の固定長データ。ファイル名で改行有無と型N文字コードを表す
- `samples/copybook-occurs-definition.cbl`
  - PIC付き基本項目OCCURSのサンプル
- `samples/copybook-occurs-data-crlf-utf8-n-utf16le.dat`
  - OCCURSサンプル用の、X/9/S9/COMP-3はUTF-8・型NはUTF-16LE・改行あり固定長データ
- `samples/copybook-group-occurs-definition.cbl`
  - 集団項目OCCURSの限定対応サンプル
- `samples/copybook-group-occurs-data-crlf-utf8-n-utf16le.dat`
  - 集団項目OCCURSサンプル用の、X/9/S9/COMP-3はUTF-8・型NはUTF-16LE・改行あり固定長データ
- `samples/definition.csv` / `samples/sample.dat` / `samples/sample-copybook-*.dat`
  - 旧名互換用。READMEでは新しい名前を主に案内する
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
dotnet publish FixedDataBuilder\FixedDataBuilder.csproj -p:PublishProfile=Lightweight -o release
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
4. `dotnet publish ... -p:PublishProfile=Lightweight -o release` で.NET非同梱版を作成し、製品EXEの起動を確認。確認後は再ビルドしない
5. zip 作成
6. README / screenshot 更新
7. commit / push
8. `gh release create vX.Y.Z FixedDataBuilder-vX.Y.Z.zip ...`
9. `gh release view vX.Y.Z --repo konikatsu/FixedDataBuilder --json tagName,url,assets`
10. GitHub Releasesは最新3版を残す。古いアセットの保管とハッシュを確認して古いReleaseを削除し、タグ・ソース履歴は保持する

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
