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
| 右クリック長押し＋左クリック | 味方への指示 |
| Esc | ポーズ・戻る |
| F1 | 味方AIの判断表示 |

## AIと差し替え

視野角・距離・遮蔽による認識、期限付きの記憶と味方報告、追従・防衛・攻撃指示、NavMeshによる迂回、包囲位置の分散を実装しています。記憶や報告だけでは発砲せず、直接視認と銃口からの射線を確認します。

モデルは仮素材です。`Assets/Wings/Resources/Characters`の設定から見た目のPrefabを差し替えられます。AIの調整値は`Assets/Wings/Resources/WingsAI.asset`にあります。

- [基本仕様・キャラクター・モデル差し替え](Docs/ALPHA_GUIDE.md)
- [認識・協調AIの仕様と検証結果](Docs/AI_REFINEMENT.md)
- [AI検証レポート](Docs/AI_Verification/ai-report.json)
- [既存機能の回帰確認](Docs/AI_Regression/report.json)

## Windows実行版を作る

Unityのメニューから **WINGS > Build Refined AI** を実行します。`Builds/AIRefined/WINGS_REVOLT_AI.exe`が生成されます。Windows向けのビルドサポートが必要です。

`--wings-ai-test --wings-report <出力先>`で認識・協調の自動検証、`--wings-smoke --wings-report <出力先>`で既存機能の回帰確認を実行できます。

Unityの生成キャッシュ、個人用設定、ビルド済みファイルは`.gitignore`で除外しています。難易度や操作感はα版として調整中です。
