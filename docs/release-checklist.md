# Release checklist

FixedDataBuilder の GitHub 公開前に確認すること。

## 配布形式と起動確認

- 2026-10-25のユーザー指示により、今後は.NET非同梱のWindows x64軽量版だけを配布する。
- `dotnet publish FixedDataBuilder/FixedDataBuilder.csproj -p:PublishProfile=Lightweight -o release` を使う。
- 実際に公開する製品EXEからメイン画面が開くことを確認し、その5ファイルを再ビルドせずZIP化する。
- `.NET 8 Windows Desktop Runtime x64` が必要とREADMEとリリースに記載する。SDKは不要。
- 製品起動、製品UIでの操作確認、自動テストの実行結果をそれぞれ明記する。
- ダウンロード後の別PC確認はユーザーが担当する。

## README 画像の反映漏れ対策

v0.1.10 で `Excel出力` ボタンを追加したとき、Excel 出力結果の画像は追加したが、README が参照している既存画像 `docs/screen-image-cobol-sample.png` の撮り直し確認が漏れた。

原因:

- 新規に作成した確認画像と、README が実際に表示している画像を別物として扱いきれていなかった。
- `git diff --cached --stat` では画像ファイルが入っていることだけを見て、README の参照先画像が最新画面かを確認しなかった。

今後の確認:

1. README の画像参照を `rg "!\[|screen-image|excel-" README.md` で確認する。
2. README が参照している画像をローカルで開き、最新機能が写っていることを確認する。
3. 画面変更を伴う場合は、README 参照先のスクリーンショットを必ず撮り直す。
4. `git diff --cached --stat` で、README 参照先画像が staged に含まれていることを確認する。
5. push 後に `git status --short --branch` で `main...origin/main` が同期済みであることを確認する。
6. GitHub の README は画像キャッシュが残ることがあるため、必要なら画像 URL に直接アクセスして最新 commit の画像を確認する。

## GitHub Releases の整理

GitHub Releases は、原則として最新 3 バージョンだけを残します。

2026-10-26のユーザー指示により、最新3バージョンを残す整理を再開します。対象はGitHub Releases本体と配布アセットです。Gitタグ・ソース履歴・ローカルの比較用ファイルは残します。削除前に配布アセットのローカル保管とハッシュを確認し、既存ZIPは重複コピーせず再利用します。

目的:

- 利用者が古い配布物で迷わないようにする。
- Release アセットの不要な蓄積を避ける。

運用:

1. 新しい Release を作成したあと、`gh release list --repo konikatsu/FixedDataBuilder --limit 20` で一覧を確認する。
2. 最新 3 バージョンを残す。
3. 削除対象のリリース説明と配布アセットをローカルに保管し、サイズ・SHA256を確認してから、4つ前より古い GitHub Release を削除する。
4. 削除対象は GitHub Release 本体。Git タグは原則として削除しない。
5. 削除後、再度 `gh release list --repo konikatsu/FixedDataBuilder --limit 20` で残っている Release を確認する。

例:

```powershell
gh release delete v0.1.xx --repo konikatsu/FixedDataBuilder --yes
```
