# Dört öncelik için tek PS5 ve PULSE 3D oturumu

Windows deneme paketi, dört önceliğin ortak adayıdır. Kararlı eşleşme
Windows v0.3.20 Build #164 ve LG webOS v0.9.42 Build #164’tür.

1. PULSE 3D’yi Windows ses çıkışı olarak seç. PS5’te konuşma, müzik ve
   efekt bulunan bir sahne aç. Yerel kayıt istiyorsan parti sohbetini kapat.
2. MSI’daki “Start Four Priority Test.cmd” dosyasını aç. 15 saniyelik
   yerel kayıt için Y, yalnız tanı ölçümü için N seç.
3. CouchSubs’ta Doğal Türkçe ses, PULSE 3D çıkışı ve duygu modu açıkken
   “Duyguyu aynı konuşma içinde sürdür” seçeneğini aç. PS5 bağlantısını
   başlat. Oyunda İngilizce altyazı ve Türkçe dublajın geldiğini kontrol et.
4. Oyuna devam et. Birkaç konuşmacı, farklı duygular ve sessiz sahneler
   dinle. Ses kesilirse, gecikirse veya yanlış duyguyla okunursa zamanı
   ve sahneyi not al. Uzun oturum için uygun olduğunda üç saat hedefle.
5. Bitişte CouchSubs içinden bağlantıyı durdur. Kayıt seçildiğinde
   `four-priority-game.wav` yerel karşılaştırma için kullanılabilir.
   `four-priority-session.jsonl` kare yaşı ile ses tamponu sayaçlarını,
   `four-priority-translation.txt` ise çeviri kodlayıcısının çalışma
   durumunu içerir. Bu iki raporda diyalog metni veya ham ses bulunmaz.

RNNoise bu adayda gerçek oyun PCM’sini tanı için işler. Duyulabilir
ayrıştırma önce yerel kayıt üzerinden karşılaştırılacak. Böylece
arka planı etkileyen adayın canlı mikse alınması önlenir.
