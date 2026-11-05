# 公開時の確認事項

1. Release / win-x64 / framework-dependentでビルドし、警告とエラーを確認する。
2. 同じ条件でpublish --no-buildを行い、製品と試験対象のDLLを照合する。
3. 製品・自動テストの実行は利用者へ依頼し、結果を別々に記録する。
4. 起動確認したファイルを再ビルドせずZIP化し、SHA256とZIPの内容を照合する。
5. READMEとリリースに.NET 8 Windows Desktop Runtime x64の要件を記載する。
6. 公開後に匿名ダウンロードのサイズ・SHA256、main、タグ、READMEを確認する。
