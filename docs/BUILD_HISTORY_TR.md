# CouchSubs GTA Türkçe Dublaj — Sürüm ve Build geçmişi

Bu dosya **yalnız Txgoood/couchsubs** deposunun kullanıcıya teslim ettiği eşleştirilmiş Windows + LG webOS ikilisini takip eder. Orijinal upstream ve XIPTV projeleriyle ilgisi yoktur.

| Çift Build | Windows EXE sürümü | LG webOS IPK sürümü | İçerik | Durum |
| --- | --- | --- | --- | --- |
| #130 | v0.1.6 | v0.9.8 | Silero ONNX VAD, duygu hafızası, doğal ses koruma | Başarılı GitHub Run #186 |
| **#131** | **v0.1.7** | **v0.9.9** | TV dublaj ses oynatma tanılaması, eski sesin temizlenmesi, webOS panelinde eşleşen Build no, IPK'nin bağımsız artifact olarak sunulması | GitHub Actions testi gerekli |

## Build #131 paketleri

- Windows: Actions `couchsubs-windows-installer` içindeki `CouchSubs-windows-Setup.exe`.
- LG webOS: Actions `couchsubs-webos-v0.9.9-build131-ipk` içindeki `com.couchsubs.tv_0.9.9_all.ipk`.
- Windows uygulama menüsünde `v0.1.7 · Build #131`, LG webOS panelinde `v0.9.9 · Build #131` görünür.
- Android TV APK veya Samsung Tizen sürümleri **bu çift sürüme dâhil değildir**; farklı ürün ve paket sürümleri korunur.

## Güvenli kullanım ve sınırlar

- Windows, Türkçe dublaj, OCR, Silero VAD ve karakter seslerini yönetir; LG webOS yardımcı uygulaması dublaj üretmez, yalnız gönderilen sesi çalar.
- webOS oynatıcısı gelen Türkçe WAV'ı ayrı HTML Audio öğesinde çalar; oyun/HDMI ses seviyesine müdahale etmez.
- AI konuşma tespiti, kaynak konuşma ayırma/susturma veya tam duygusal ses klonlamaya eşit değildir.
- GitHub CI yeşil olması PS5 + LG webOS 2.0 gerçek cihaz testinin yapıldığı anlamına gelmez.
