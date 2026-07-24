# Bakin 歩行グラフィック一括パレット変換ツール

RPG Developer Bakin向け歩行グラフィックの色違い素材を、キャラクター単位で一括生成するWindowsデスクトップツールです。

## 現在の状態

WPFアプリケーションのプロジェクトと、今後の実装に向けた基本構成を作成済みです。
画像の読み込み・パレット編集・一括変換・JSON設定保存は、次の開発段階で実装します。

## 開発環境

- Windows 10 / Windows 11
- Visual Studio 2022（.NETデスクトップ開発ワークロード）
- .NET 8

## 開き方

1. `BakinWalkingPaletteTool.sln` をVisual Studioで開きます。
2. `BakinWalkingPaletteTool` をスタートアッププロジェクトに設定します。
3. `F5` キーで実行します。

## 想定するファイル名

```text
キャラクター名_アニメーション名.png
```

例：

```text
villager01_wait.png
villager01_walk.png
villager01_run.png
```

