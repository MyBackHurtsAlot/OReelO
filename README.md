# OReelO 0.6.1

Premiere Pro 25.6 以上版本的全域圓盤工具。

- 按住右鍵：顯示本次工作階段已開啟過的 Sequence，移向目標並放開即可切換。
- Shift＋按住右鍵：顯示使用者收藏的 Graphic Templates，放開後插入播放頭並置於最上方既有視訊軌。
- Esc 或在圓心放開：取消。
- Windows Helper 只在 Premiere 位於前景時攔截手勢；短按右鍵仍是 Premiere 原本的選單。

## 正式安裝與日常使用

UXP Developer Tools 只供開發，正式使用不必開著它。

Windows 使用者請從 [GitHub Releases](https://github.com/MyBackHurtsAlot/SequenceWheel/releases) 下載最新版 ZIP，執行其中的 `SequenceWheel-Windows-Setup.exe` 一次，然後在 Creative Cloud Desktop 確認安裝 `SequenceWheel.ccx`。安裝程式會把 Helper 放在使用者的 LocalAppData、登記登入後自動啟動，並立即在背景啟動。以後登入 Windows 後直接開 Premiere 即可。

第一次安裝後，請在 Premiere 開啟一次「視窗 > UXP Plugins > OReelO」，並把面板停駐在工作區。Premiere 會隨工作區恢復面板；Helper 不需要可見視窗。

macOS 使用相同的 UXP `.ccx`，但全域滑鼠 Helper 必須另外取得「輔助使用」／「輸入監控」權限，且需在 macOS 上編譯、簽署與公證。目前 Windows 版可交付；macOS 版在 Mac 實機完成這三項驗證前不能標成正式 release。

## 解除安裝 Windows Helper

在命令提示字元執行：

```text
SequenceWheel-Windows-Setup.exe --uninstall
```

UXP 外掛則在 Creative Cloud Desktop 內解除安裝。

## 開發檢查

```text
node test_geometry.js
tools\windows\SequenceWheelHelper.exe --self-test
release\SequenceWheel-0.6.1\SequenceWheel-Windows-Setup.exe --self-test
```

## 技術限制

Premiere UXP 目前沒有正式 API 可精準列出「現在仍開著的 Timeline 分頁」。圓盤因此保存本次 Premiere 工作階段曾啟用過的 Sequence，切換則使用官方 `setActiveSequence()`／`openSequence()`。
