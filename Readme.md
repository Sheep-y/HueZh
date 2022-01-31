# HueZh #
## Chinese conversion mod of Hue (2016 game) ##

HueZh 是 2016 年遊戲 Hue 的繁體中文/正體中文漢化補丁。
本補丁不修改任何檔案，在遊戲目錄中解壓即可將遊戲變成中文。


# 安裝 #

1. 從 GitHub 下載最新版：
https://github.com/Sheep-y/Automodchef/releases

2. 用 7-zip 或其他軟件解壓。

3. 將 `winhttp.dll`, `doorstop_config.ini`, 和 `HueZh` 目錄搬到遊戲目錄：

> %ProgramFiles(x86)%\Steam\steamapps\common\Hue
> 
> %ProgramFiles%\Epic Games\Hue

4. 重啓遊戲。完工。

src 目錄包括了源碼和授權，不影響運作，不需要搬入遊戲目錄，可以刪除。
發佈包必需包括此目錄以符合開源授權。

驗證檔案不會移除補丁，一般不用重打。


# 設定 #

每次啓動有補丁的遊戲時，補丁會檢查數據目錄是否包含 `HueZh.csv`。
數據目錄為 %HOMEPATH%\My Documents\My Games\Curve Digital\Hue\

如果檔案不存在，就會用內建的漢化資源建立它。
這個檔案包括全部中文資源，修改這個檔案就可以修改漢化。請自行備分此檔案。
假若檔案無法讀取，補丁會直接使用預設值。

如需參考原文，同一目錄下有 GameText.csv，是從遊戲生成的原文資料，可以作為參考。


# 相容性 #

支持 Epic Games 版本，也應該支援 Windows Steam 版本。
其餘看架構。只要是 .Net 版本的 Unity 就支援。遊樂器、原生 Mac 等免問。它們的賣點就是不開放。

由於補丁不修改遊戲資源檔案，預期能相容於未來的遊戲升級。
萬一遊戲加入了新的未翻譯文字，它們會保留英文原文。

本補丁不影響遊戲進度。


# 技術詳情 #

本補丁使用 Unity Doorstop 進行動態注入，再用 HarmonyX 動態加載中文數據。

動態注入也常見於各種比較古舊的病毒，某些防毒軟件可能會因此跳出來。當然我會說我的補丁是安全的。
你也可以自己組譯；安裝包有完整源碼，最簡單是用 Visual Studio 組譯，會匯出到 deploy 目錄並打包。（需 7-zip）

動態注入不是新技術。特別是跳過片頭的補丁，基本都是動態注入。
不過這遊戲的片頭不長又可以 Esc 跳過，就不搞它了。


# 除錯 #

補丁會輸出紀錄檔，位置是 %HOMEPATH%\My Documents\My Games\Curve Digital\Hue\
紀錄檔是 HueZh.log。出錯的話，應該會有紀錄。

正常的話，最後三行會用英文說載入了多少行中文，改寫成功云云。
如果看上去有問題，試試看刪掉 HueZh.csv，重設翻譯。

如果目錄/檔案不存在，那補丁沒有被載入。請依從安裝程序檢查檔案是否都正確。
HueZh 目錄內應該有五個 dll 檔案。

還有一個可能性是遊戲推出了 64 位元版本。本補丁是 32 位元版。
如果是真的話，下載合適的 Unity Doorstop 取代 winhttp.dll 就可以解決。

遊戲的 Hue_Data 目錄也有 output_log.txt，適用於遊戲引擎的除錯。


# 反安裝 #

從遊戲目錄中刪除或移走 `winhttp.dll` 和/或 `doorstop_config.ini` 就可以停用補丁（需重啓遊戲）。
真心要刪的話 HueZh 目錄 和 src 目錄(如有) 也可以刪除。

真的。不用驗證檔案。
相對的，驗證檔案無法移除漢化補丁。你必需刪除或移走上述檔案。


# 授權 #

補丁為 GPL v3 授權。程序庫都使用 MIT 授權，除了一個是公眾領域。

本補丁完全開源，跟遊俠漢化完全沒有關係，畢竟我也沒能駕馭那些富具中國特色的補丁下載網。可惜。參考一下不壞。
個人漢化。翻譯、校對、潤色、程序、發佈全都是我。