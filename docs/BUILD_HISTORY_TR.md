# CouchSubs GTA Türkçe Dublaj — Sürüm ve Build geçmişi

Bu dosya **yalnız Txgoood/couchsubs** deposunun kullanıcıya teslim ettiği eşleştirilmiş Windows + LG webOS ikilisini takip eder. Orijinal upstream ve XIPTV projeleriyle ilgisi yoktur.

| Çift Build | Windows EXE sürümü | LG webOS IPK sürümü | İçerik | Durum |
| --- | --- | --- | --- | --- |
| #130 | v0.1.6 | v0.9.8 | Silero ONNX VAD, duygu hafızası, doğal ses koruma | Başarılı GitHub Run #186 |
| #131 | v0.1.7 | v0.9.9 | TV dublaj ses oynatma tanılaması, eski sesin temizlenmesi, webOS panelinde eşleşen Build no, IPK'nin bağımsız artifact olarak sunulması | GitHub Actions testi gerekli |

| **#132** | **v0.1.8** | **v0.9.10** | **Deneysel Sherpa ONNX konuşmacı ses izi + Silero konuşma algılama; isimli altyazı öncelikli, ses kimliği ikinci** | **CI ve gerçek cihaz doğrulaması bekleniyor** |

## Build #132 – dinamik GTA6 konuşmacı algılama

- Altyazıda karakter adı varsa bu isim kesinlikle önceliklidir.
- Ad yoksa sadece Silero'nun doğruladığı PS5 konuşmasından ONNX ses izi çıkarılır. Aynı konuşmacı sesiyle güvenli eşleşme bulunursa geçici `Ses 1` / `Ses 2` kimliği atanır.
- Ses modeli çalışmazsa, konuşmalar çakışırsa veya eşleşme belirsizse mevcut Türkçe dublaj ve çeviri devam eder; karakter ismi uydurulmaz.
- İlk ses kimliği için en az iki ayrı, yeterli uzunlukta gözlem gerekir. Kısa konuşmalar ve karışık oyun sesleri yanlış eşleşmeye yol açabilir.
- Ses izi kümeleri oturum içindedir; adlandırma/ses kimliği henüz oyunlar arasında biyometrik şablon olarak kalıcılaştırılmıyor.
- Windows: **v0.1.8, Build #132**; webOS: **v0.9.10, Build #132**. TV yalnız eş sürüme güncellenir; model bilgisayarda çalışır.

| **#133** | **v0.1.9** | **v0.9.11** | **Oyun başlığından otomatik GTA5/GTA6 profili, JSON dışarı/içe aktarma ve ayrı silme işlemleri** | **CI doğrulaması bekleniyor** |

| **#134** | **v0.2.0** | **v0.9.12** | **Yapay zekâ çalışma süresi ölçümü, otomatik Silero/metin tabanlı güvenli seçim, eski motorlara dönüş** | **GitHub CI kontrolünde** |

| **#135** | **v0.2.1** | **v0.9.13** | **F11 tam ekran çevirisi, F12 serbest alan; ayardan değiştirebilen kalıcı iki kısayol** | **CI testinde** |

| **#136** | **v0.2.2** | **v0.9.14** | **Num0/1/2/3/8, F11 ve F12 dahil 7 kısayolun tamamı tek panelden değiştirilebilir; çakışma kontrolü** | **GitHub CI doğrulaması bekleniyor** |

| **#137** | **v0.2.3** | **v0.9.15** | **Deneysel söz kesme güvenlik koruması: hızlı ses izi değişiminde yanlış karakter atamasını geçici olarak önle; dublaj/çeviri sürsün** | **CI kontrolünde** |

| **#138** | **v0.2.4** | **v0.9.16** | **Oyun stereo konumunu koruyan yeni PULSE 3D mekânsal miks profilleri; eski miks varsayılan kalır** | **CI doğrulaması bekleniyor** |

| **#149** | **v0.3.5** | **v0.9.27** | **PS5 oyun sırasında Türkçe Dublaj tikini açıp kapatınca PULSE 3D ses hattını koparmadan TTS yeniden başlatma; Headset ses akışını tikten bağımsız yakalama, başarısız ses aygıtı açılışında tekrar deneme, oyun atmosferini kesmeme.** | **CI ve PS5/PULSE 3D gerçek cihaz testi bekleniyor** |

| **#148** | **v0.3.4** | **v0.9.26** | **28px sarı, %85 arka plan, cover/original ve adaptive altyazı ayarları; English OCR clear ile kısa-okuma koruması, yeni konuşmada hemen eski altyazı temizleme; geç kalmış eski çeviriyi düşürme.** | **CI ve saha testi bekleniyor** |

| **#147** | **v0.3.3** | **v0.9.25** | **Acil Sherpa kaynaklı 0xC0000005 çökme düzeltmesi: ana süreçte yerel konuşmacı modeli çalıştırılmaz, altyazı/Türkçe dublaj/manuel ses atama/Silero korunur. Otomatik ses izi geçici devre dışı, ayrık işlem motoru sonraki aşama.** | **CI ve PS5 cihaz testi bekleniyor** |

| **#146** | **v0.3.2** | **v0.9.24** | **48 kHz stereo zaman hizalı konuşma stem giriş noktası; ses kare sıra kontrolü, eksik/geçersiz model çıktısında birebir oyun sesi ve regresyon testleri. Gerçek AI model henüz bağlı değil.** | **CI doğrulaması bekleniyor** |\n\n| **#145** | **v0.3.1** | **v0.9.23** | **Oyun ses tamponu meta-veri tanıları: kuyruk tepe derinliği, atılan ses örnekleri, eksik örnekler; yeni regresyon testleri. Gerçek GPU inference optimizasyonu/ayırma modeli henüz tamamlanmadı.** | **CI ve cihaz testi bekleniyor** |\n\n| **#144** | **v0.3.0** | **v0.9.22** | **Güvenilir konum meta verisiyle Türkçe sesi yönlendirme arayüzü ve PULSE 3D oynatıcı bağlantısı; belirsiz konumda merkez, oyun stereo kanalları korunur. Otomatik konuşmacı örtüşme/konum modeli henüz yok.** | **CI doğrulaması bekleniyor** |\n\n| **#143** | **v0.2.9** | **v0.9.21** | **Deneysel konuşma ayrıştırma güvenlik altyapısı: eşzamanlı stem doğrulama, >=%90 güven / <=90ms gecikme, en fazla %25 bastırma ve orijinal sese geçiş; gerçek ayrıştırma modeli henüz bağlı değil** | **CI doğrulaması bekleniyor; model entegrasyonu ayrı aşama** |\n\n| **#142** | **v0.2.8** | **v0.9.20** | **Yeni isteğe bağlı AdaptiveDialogue profili: TTS seviyesine göre sınırlı merkez azaltma, stereo side koruma ve taban oyun ses sınırı; eski modlar aynı kalır** | **CI doğrulaması bekleniyor** |\n\n| **#141** | **v0.2.7** | **v0.9.19** | **Bozuk PCM ses örneklerini güvenli hale getirme, limiter olayları ve veri bozulması sayaçları, ses regresyon testleri** | **CI doğrulaması bekleniyor** |\n\n| **#140** | **v0.2.6** | **v0.9.18** | **Eski konuşma oynatma beklemesini nesil geçişinde en geç 50ms içinde sonlandırma; kuyruk düşme/kesilme ve son TTS süresi ölçümleri** | **CI doğrulaması bekleniyor** |\n\n| **#139** | **v0.2.5** | **v0.9.17** | **Kritik stabilite: ayar dosyası atomik kaydet/yedek kurtarma, eski ses üretimini iptal, webOS SSE + dublaj temizliği, düşük bellekli ağ taraması** | **CI doğrulaması bekleniyor** |

## Build #139 — Windows + LG webOS 2.0 stabilizasyonu

- Windows kullanıcı ayarları ve oyun/karakter kütüphaneleri `settings.json` dosyasına geçici dosya ve atomik değiş-tokuş ile yazılır. Önceki sağlam `settings.json.bak` saklanır, geçersiz/yarım yazılmış ana dosyada bu yedek açılır.
- Oyun altyazısı değiştiğinde eskimiş konuşmanın TTS isteğine `CancellationToken` gönderilir. Eski nesil zaten oynatılmaz; iptal artık yanlış hata mesajı üretmez. Model iptal isteğini desteklemiyorsa nesil kontrolü devrede kalır.
- LG webOS 2.0 uygulamasında eski EventSource nesnesinin gecikmiş olayları yeni oturuma dokunmaz; uzun bağlantı kesintisinde eski konuşma durdurulur.
- webOS ses oynatıcı nesneleri bittiğinde veya hata verdiğinde serbest bırakılır; 45 saniyelik emniyet zaman aşımı takılı medya oynatımını durdurur.
- TV'nin yerel ağ otomatik taramasında aynı anda açık istek sayısı 24 → 8 azaltılır, sağlıklı bağlantı varken yeni tarama başlatılmaz.
- GitHub CI `node --check tv-webos/app.js` ve `platform.js` doğrulaması yapar.
- **Windows v0.2.5 / Build #139**, **LG webOS v0.9.17 / Build #139**, TV paket ID'si `com.couchsubs.tv` değişmez. Android TV, Tizen ve XIPTV projeleri değiştirilmez.
- Test kapsamı: eski oyun profilleri/kısayol ayarları, bozuk ana JSON kurtarma, yedeğin bozuk yazımla kaybolmaması; temel ses ve dublaj regresyon testleri korunur.
- Gerçek PS5, PULSE3D, LG webOS2 cihazlarında uzun kullanım ve ağ kesinti testi hâlâ gereklidir; CI bu testlerin yerine geçmez.

## Build #138 — Mekânsal atmosfer koruma

- **Windows:** v0.2.4 Build #138. **LG webOS:** v0.9.16 Build #138, aynı `com.couchsubs.tv` App ID.
- Yalnız Windows/PULSE 3D ortak ses çıkışına uygulanan, kullanıcı tarafından seçilebilir dört profil: Önceki miks, Orijinal stereo, Mekânsal dengeli ve Sinematik.
- Mekânsal miks, stereo `mid=(L+R)/2` bileşenini hafif kısarken yön içeren `side=(L-R)/2` bileşenini korur. Bu stereo kulaklıkta sağ/sol efektlerin korunmasına yardımcı olabilir.
- Güvenli profil, oyun sesini azaltmadan Türkçe TTS ekler; tek tek İngilizce konuşmaları ayırmaz.
- Mekânsal dengeli seçildiğinde önceki varsayılan atmosfer %82 ise başlangıçta %90'a geçilir; Türkçe ses %85 kalır. Önceden özelleştirilmiş yüzdeler değiştirilmez.
- Eski kullanıcıların davranışı değişmesin diye varsayılan `AtmosphereMixMode.Legacy` olarak tutulur.
- Bu sürüm **konuşmacı sesini otomatik 3D konumlandırmaz** ve **kaynak konuşma/efekt ayrıştırması içermez**. Bunun için ayrı güvenilir model gerekir; oyun atmosferine zarar veren sahnede geri dönüş seçilebilir.
- Testler: stereo `L-R` farkı, önceki miks davranışı, orijinal ses modu, sessiz sahnede sabit ses ve kırpma koruması. Silero, Sherpa, GTA oyun kütüphanesi, JSON yedekleri, tüm tuşlar, söz kesme koruması korunur.

## Build #137 — Söz kesme için güvenli başlangıç

- Hedef: İsimsiz altyazı + oyun sesinde bir konuşmacıdan diğerine **1,5 saniye içinde** geçiş olduğunda yanlış karakter atamasını azaltmak.
- `SpeakerTurnGuard`, yalnız gerçek Sherpa ses izi eşleşmesinden gelen ve yeterince güvenilir kimlikleri değerlendirir. Şüpheli hızlı geçişte 1,8 saniye otomatik isimsiz kimlik atamaz; normal Türkçe çeviri ve anlatıcı/karşılıklı seslendirme devam eder.
- Kadın/erkek kimliği otomatik varsayılmaz. İsmi altyazıda açıkça bulunan karakterin adı korunur.
- Menü seçeneği **deneysel, varsayılan kapalı**; kullanıcı isterse devreye alır.
- İstatistikler: hızlı ses geçişleri ve güvenli belirsiz bırakılan kimlik sayısı; ham oyun sesi veya metin saklanmaz.
- **Önemli sınır:** Bu **gerçek overlap diarization / iki sesi ayırma modeli değildir**. Hızlı konuşmacı değişimi yalnızca olası söz kesme işaretidir. Gerçek iki sesin aynı anda ayrıştırılması ve paralel Türkçe dublaj sonraki onaylı model/işleme aşamasına aittir.
- Windows **v0.2.3 Build #137**; LG webOS **v0.9.15 Build #137**. Paket ID değişmez, TV yalnız eş build olur.
- Önceki tüm kısayollar, GTA karakter hafızası, JSON dışarı/içe aktar, Silero, Sherpa, Türkçe dublaj ve ses atmosferi korunur.

## Build #136 — Tam kısayol özelleştirme

- Windows ayarlarında **Klavye kısayolları - hepsini değiştir** panelinde 7 işlevin ayrı açılır listesi bulunur.
- F1..F12 ve NumPad0..9 kullanılabilir; bir tuş iki farklı işleve atanamaz.
- Varsayılanlar: F11 ekran çevir, F12 serbest alan, Num0 durdur/devam, Num1/2/3 ek bölge, Num8 önizleme.
- Değişiklikler kalıcı UserSettings içine yazılır, çeviri sürerken Windows hotkey kaydı yeniden yapılır.
- Eski Build #135 F11/F12 tercihleri korunur. Diğer eski kısayolların varsayılanı değişmez.
- **Windows v0.2.2 / Build #136**, **LG webOS v0.9.14 / Build #136**. TV'nin klavye ayarları değiştirilmez.
- Silero, Sherpa, GTA karakter hafızası, dışarı/içe aktar/sil, dublaj ve oyun atmosferi ile PS5 bağlantısı korunur.

## Build #135 — Kısayolları kayıpsız güncelle

- Ekranı çevir: **F11** (eski Num9). Tekrar basınca çeviriyi kaldırır.
- Serbest alan: **F12** (eski Num5). Esc ile kapanır.
- Windows Ayarlar'da iki ayrı ComboBox ile **F1..F12** veya **Num5/Num9** atanabilir. Diğer Num 0,1,2,3,8 kısayolları ayrılmıştır, değiştirilmez.
- Seçimler kalıcı `settings.json` içine yazılır; canlı çeviri açıksa Windows kısayolları yeniden kaydedilir. Aynı tuşu iki işleve atamak yasaktır.
- Windows v0.2.1 Build #135; LG webOS eş paket v0.9.13 Build #135. TV'nin kısayol girişi yoktur; değişiklik yalnız bilgisayara uygulanır.
- Önceki Silero, Sherpa, Türkçe dublaj, karakter hafızası ve oyun çeviri özelliklerine dokunulmadı.

## Build #134 — Kayıpsız yapay zekâ altyapısı

- **Önceki tüm özellikler korunur:** GTA oyun adı, JSON yedekleme, karakter sesleri, Sherpa ses izi, Silero VAD, oyun atmosferinin korunması, webOS ses çıktısı.
- **Yeni Windows sürümü:** v0.2.0 Build #134. **webOS:** v0.9.12 Build #134; paket kimliği değişmez.
- **Seçilebilir konuşma algılama:** Otomatik (mevcut yerel Silero), açık Silero ONNX, yapay zekâ kapalı (metin tabanlı).
- **Performans ölçümü:** Silero ve Sherpa işlem sayısı, ortalama/en yüksek çıkarım gecikmesi ve hata sayısı. Ham ses veya replik tutulmaz.
- **Varsayılan:** önceki kurulumların davranışını bozmadan Otomatik/Silero.
- **TextOnly:** yalnız neural konuşma analizi kapatılır; OCR, Marian/DeepL çeviri, mevcut ses motoru, oyun atmosferi ve çevrimdışı modeller silinmez.
- **TEN VAD, FireRedVAD, yeni ASR/TTS:** henüz pakete bağlı değil; kullanıcıya çalışıyor gibi gösterilmez.
- **Testler:** varsayılanların korunması, çok iş parçacıklı sayaçlar, metin gizliliği ve mevcut temel test paketi.

## Build #133 — Oyun kütüphanesi yönetimi

- PS5 yerel ağ DDP sorgusunda `running-app-name` alanı varsa iki ardışık tutarlı yanıtla oyun profili oluşturulur ve seçilir. Yanıt yoksa ekran OCR yalnız tam GTA5/GTA6 başlık metnini yakalarsa profil seçer. Kullanıcı o oturumda elle oyun seçmişse bu tercih korunur.
- Oyun seçici menüsünde **Dışarı aktar** karakter/ses eşleşmelerini JSON'a yazar.
- **İçe aktar** JSON yedeğini doğrular; aynı oyun varsa kullanıcı açıkça onay vermeden üzerine yazılmaz.
- **Oyunu sil** işlemi iki aşamalı onayla yapılır; yalnız seçilen oyunun eşleşmelerini siler. Ortak model ve diğer oyunlar korunur.
- Bu JSON dosyası ses modeli, PCM veya oturumluk konuşmacı embeddinglerini içermez. İleride ses modelini de yedeklemek için bağımsız onaylı bir özellik gerekir.
- Windows **v0.1.9 / Build #133**, webOS **v0.9.11 / Build #133**.

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
