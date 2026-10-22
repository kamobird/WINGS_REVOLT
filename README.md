# WINGS REVOLT

WINGSリメイク版のUnityプロジェクトです。5羽から4羽を編成し、操作キャラクターを切り替えながら、味方AIとタワーを防衛するα版です。

## 開く・遊ぶ

1. Unity Hubでこのフォルダーを追加し、**Unity 6000.3.8f1**で開きます。
2. パッケージの復元とインポートが終わったら、`Assets/Wings/Scenes/WingsAlpha.unity`を開きます。
3. Playを押し、ステージ・編成を選んで出撃します。

URP、Input System、AI Navigationを使用します。依存パッケージは`Packages`内に記録しています。

## 操作

| 操作 | 内容 |
| --- | --- |
| WASD / マウス | 移動 / 視点・照準 |
| 左クリック / R | 射撃 / リロード |
| Q / E / 左Shift / X | スキル / ボム / ムーブ / アルティメット |
| Space | ジャンプ |
| 1〜4 | 操作キャラクター切替 |
| 右クリック長押し＋左クリック | 対象（全員／個別）→ 作戦の順で味方へ指示 |
| Esc | ポーズ・戻る |
| F1 | 味方AIの判断表示 |
| F2（mapC） | 俯瞰観察・全員AI・各味方の視野表示 / 操作へ戻る |

## AIと差し替え

視野角・距離・遮蔽による認識、期限付きの記憶と味方報告、追従・防衛・攻撃指示、NavMeshによる迂回、包囲位置の分散を実装しています。記憶や報告だけでは発砲せず、直接視認と銃口からの射線を確認します。

モデルは仮素材です。`Assets/Wings/Resources/Characters`の設定から見た目のPrefabを差し替えられます。AIの調整値は`Assets/Wings/Resources/WingsAI.asset`にあります。

- [敵の協調・引き撃ち・被弾認識・個別指示](Docs/TACTICAL_AI_GUIDE.md)
- [mapCの地形・観察モード・高さ対応](Docs/MAP_C_GUIDE.md)
- [設計Excel（Git管理対象）](Docs/Design/README.md)
- [基本仕様・キャラクター・モデル差し替え](Docs/ALPHA_GUIDE.md)
- [AIコードの読み方（処理の流れと技術の説明）](Docs/AI_CODE_GUIDE.md)
- [認識・協調AIの仕様と検証結果](Docs/AI_REFINEMENT.md)
- [AI検証レポート](Docs/AI_Verification/ai-report.json)
- [既存機能の回帰確認](Docs/AI_Regression/report.json)

## Windows実行版を作る

Unityのメニューから **WINGS > Build Tactics** を実行します。A/B/Cを含む`Builds/Tactics/WINGS_REVOLT_Tactics.exe`が生成されます。Windows向けのビルドサポートが必要です。

`--wings-tactics-test --wings-report <出力先>`で今回の協調・自己防衛・個別指示を確認できます。`--wings-mapc-test`でmapCの地形と実移動、`--wings-ai-test`で認識・協調、`--wings-smoke`で既存機能の回帰確認も実行できます。

Unityの生成キャッシュ、個人用設定、ビルド済みファイルは`.gitignore`で除外しています。難易度や操作感はα版として調整中です。
