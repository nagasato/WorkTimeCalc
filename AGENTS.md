# WorkTimeCalc エージェント向けガイドライン

このリポジトリ（`WorkTimeCalc`）は、C# / .NET 向けの稼働日・実稼働時間算出ライブラリです（`net8.0;netstandard2.0` マルチターゲット、ソリューション: `src/WorkTimeCalc.slnx`）。

## Git・ブランチ運用
- 機能追加・修正作業は `.worktrees/<作業名>` 配下に `git worktree` で作業用ブランチ（例: `feature/...`）を切って進め、`master` へ直接コミットしない。
- コミットメッセージは日本語で簡潔かつ具体的に記述する。
- ユーザーから明示的な指示がない限り、`master` へのマージやリモートへの `git push` は行わない。

## 設計・コーディング規約
- **日本語での統一**: 公開 API の XML ドキュメントコメント、例外メッセージ、設計ドキュメント・README は日本語で統一する。
- **マルチターゲット互換（`net8.0;netstandard2.0`）**: `netstandard2.0` でのビルド互換を常に維持する。`DateOnly` / `TimeOnly` 等の .NET 6+ 専用オーバーロードは `#if NET6_0_OR_GREATER` で分岐する。
- **後方互換性の維持**: 既存の公開 API シグネチャ、既定の計算挙動、および `WorkCalendarDefinition` の JSON 形式との互換性を壊さない。
- **イミュータブル・スレッドセーフ**: カレンダー本体やスケジュール等のドメインモデルはイミュータブルかつスレッドセーフに保つ。
- **ネットワーク非依存**: 祝日判定を含むコア機能で外部ネットワーク接続を必須にしない。
- **ドキュメント表現**: `README.md`・パッケージ説明・XML コメント等では「オフライン／完全オフライン」といった表現を過度に強調しない。

## ビルド・テスト・検証
- 変更後は `src/WorkTimeCalc.slnx` の Release ビルドで `net8.0` / `netstandard2.0` ともに **0 警告・0 エラー**（XML ドキュメント警告を含む）であることを確認する。
- xUnit 単体テスト（`dotnet test src/WorkTimeCalc.slnx -c Release --no-build`）が全件成功することを確認する。
- 公開 API や仕様を変更した場合は、必要に応じて `README.md` および `docs/implementation-plan.md` も合わせて更新する。
