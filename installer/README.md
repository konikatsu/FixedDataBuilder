# 確認用MSI

既存のWindows x64軽量版をMSIへ格納します。製品の再ビルドは行いません。.NET 8 Windows Desktop Runtime（x64）は別途必要です。

- インストール先：`%LOCALAPPDATA%\Programs\FixedDataBuilder`
- ユーザー単位のインストール、スタートメニュー登録、Windowsのアンインストール登録
- 製品5ファイルと、Gitで追跡している公開samplesだけを格納
- 製品の自動起動、ランタイム導入、SACや証明書・除外設定の変更は行わない
- 本MSIは未署名。MSI化だけでSAC通過を保証しない

WiX Toolset 5.0.2のCLIを指定して、リポジトリのルートから実行します。既存の出力ファイルは上書きしません。

```powershell
.\installer\Build-Msi.ps1 `
  -PayloadDirectory "C:\dev\FixedDataBuilder\FixedDataBuilder-v0.1.61-win-x64-framework-dependent" `
  -OutputPath "C:\dev\FixedDataBuilder\FixedDataBuilder-v0.1.61-win-x64-framework-dependent.msi" `
  -WixPath "C:\tools\wix5\wix.exe" `
  -IntermediateDirectory "C:\dev\FixedDataBuilder\reviews\msi-build"
```

`Package.wxs`は構成と登録先、`Build-Msi.ps1`は公開ファイル一覧・版数の取得と格納前後のハッシュ照合を担当します。出力のMSIをdecompileして展開した40ファイルが元のv0.1.61軽量版と一致することを確認済みです。利用者の確認ではインストール・アンインストールは正常終了しましたが、導入後の製品EXEはSACに拒否されました。MSI化によるSAC通過は確認できていません。
