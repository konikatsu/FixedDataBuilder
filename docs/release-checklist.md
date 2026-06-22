# Release checklist

FixedDataBuilder の GitHub 公開前に確認すること。

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
