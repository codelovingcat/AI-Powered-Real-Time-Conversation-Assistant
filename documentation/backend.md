# Backend

## 1. Temel mimariyi kurduk

**Ne yaptık?**
- ASP.NET Core Web API oluşturduk.
- Domain, Application, Infrastructure ve API katmanlarını ayırdık.
- PostgreSQL + EF Core kullandık.
- Projeyi başlangıçta modular monolith tuttuk.

**Neden?**
İlk aşamada mikroservis karmaşıklığına girmeden domain kurallarını, use-case'leri ve dış servisleri birbirinden ayırmak istedik. Böylece ileride büyütmek veya provider değiştirmek daha kolay olsun istedik.

## 2. Conversation ve mesaj geçmişini kalıcı yaptık

**Ne yaptık?**
- Conversation oluşturma/güncelleme/silme ve listeleme ekledik.
- Her conversation için instruction, başlık ve dil çifti sakladık.
- Mesaj geçmişini PostgreSQL'e kaydettik.

**Neden?**
Conversa tek seferlik bir çeviri aracı değil. Aynı konuşmanın bağlamını ve kullanıcının o konuşma için verdiği talimatı sonraki isteklerde de koruması gerekiyor.

## 3. AI'ı provider bağımsız tuttuk

**Ne yaptık?**
- Application tarafında `IAiProvider` kullandık.
- İlk provider olarak Gemini'yi Infrastructure katmanına koyduk.
- AI çıktısını serbest metin yerine belirli bir JSON yapısında bekledik ve parse ettik.

**Neden?**
AI sağlayıcısını değiştirmek istediğimizde tüm uygulamayı değiştirmek istemedik. Ayrıca frontend'in güvenebileceği tutarlı bir response yapısı gerekiyordu.

## 4. Konuşma talimatını ayrı ve kalıcı tuttuk

**Ne yaptık?**
Conversation'ın aktif instruction'ını her AI isteğine dahil ettik. Instruction değiştiğinde bunu conversation'a kaydettik ve geçmişte iz bırakacak şekilde sistem notu oluşturduk.

**Neden?**
Kullanıcı örneğin önce "sadece çevir" deyip daha sonra "soruları da tespit et ve cevap öner" diyebiliyor. Bu davranışın yalnızca o conversation'a ait olmasını istedik.

## 5. Input validation ve boyut sınırları ekledik — Issue #5

**Ne yaptık?**
- Title, instruction, dil kodları ve assistant input'u doğruladık.
- Conversation request body için 64 KB, assistant request body için 32 KB sınırı koyduk.

**Neden?**
Backend'e gelen veriyi doğrudan işlemek yerine önce beklediğimiz formatta ve makul boyutta olduğundan emin olmak istedik. Bu hem hatalı kullanımı hem de gereksiz kaynak tüketimini azaltıyor.

## 6. Kimlik ve yetkilendirme güvenlik sınırı ekledik — Issue #2

**Ne yaptık?**
Conversation endpoint'lerini authenticated user context üzerinden çalışacak şekilde tasarladık ve controller'larda authorization kullandık. WebSocket tarafında da kullanıcının conversation sahibi olup olmadığını kontrol ediyoruz.

**Neden?**
Bir kullanıcının başka bir kullanıcının conversation veya mesajlarına erişebilmesini istemiyoruz. Kullanıcı kimliği request'ten gelen sıradan bir değer olmaktan çıkıp güvenlik sınırının parçası olmalı.

> Not: Repository'deki mevcut `Program.cs` kaydında `ICurrentUser` wiring'i ayrıca gözden geçirilmeli; authentication sınıfı mevcut olsa da composition root'ta eski identity kaydı görülüyor. Bu nedenle bu bölüm "hedeflenen güvenlik sınırı" olarak okunmalı, eksiksiz production authentication tamamlandı şeklinde değil.

## 7. Hassas exception detaylarını loglamadık — Issue #4

**Ne yaptık?**
API exception handler normal loglarda exception message/stack trace gibi hassas ayrıntıları yazmıyor; response tarafında da kullanıcıya güvenli, genel hata mesajları dönüyor.

**Neden?**
Loglar da bir veri kaynağıdır. API key, bağlantı bilgisi veya iç sistem ayrıntılarının hata loglarına istemeden taşınmasını azaltmak istedik.

## 8. Prompt injection ve veri sızıntısına karşı AI katmanını güçlendirdik — Issue #7

**Ne yaptık?**
- Security kurallarını conversation instruction ve conversation content'ten daha yüksek önceliğe koyduk.
- Konuşmadan gelen metni "instruction" değil, analiz edilecek untrusted data olarak tanımladık.
- Prompt'a giren dinamik değerleri escape ettik.
- API key, token, connection string, internal prompt ve başka conversation verilerinin açığa çıkarılmaması için kurallar ekledik.

**Neden?**
Kullanıcının duyduğu veya gönderdiği metin teorik olarak "ignore previous instructions" gibi model davranışını değiştirmeye çalışan içerik taşıyabilir. Bu metni güvenilir talimat gibi kabul etmek istemedik.

## 9. Configuration ve secret'ları source control'dan ayırdık — Issue #8

**Ne yaptık?**
- Secret değerleri örnek configuration'dan ayırdık.
- Docker Compose'ta PostgreSQL secret'larını environment variable üzerinden alacak şekilde düzenledik.
- Local secret/config dosyalarını gitignore kapsamına aldık.

**Neden?**
Git repository'ye API key, database password veya signing key koymak kalıcı bir sızıntı riski oluşturur. Örnek dosyalar sadece hangi ayarın gerektiğini göstermeli, gerçek secret'ı içermemeli.

## 10. Security regression testleri ekledik — Issue #9

**Ne yaptık?**
Authorization/ownership davranışları, input validation ve WebSocket güvenlik yüzeyi için regression testleri ekledik.

**Neden?**
Bir güvenlik kuralının bugün çalışması yeterli değil. Sonraki geliştirmede yanlışlıkla bozulmasını da yakalamak istedik.

## 11. WebSocket audio akışını sınırlandırdık — Issue #10

**Ne yaptık?**
- Binary audio dışında mesajları kabul etmiyoruz.
- Fragmented WebSocket mesajlarının toplam boyutunu takip ediyoruz.
- Mesaj başına 256 KB sınırı var.
- Connection lifetime'ı 600 saniye ile sınırlıyoruz.
- Cancellation ve STT session disposal yönetiyoruz.

**Neden?**
Sürekli mikrofon bağlantısı normal HTTP request'inden farklıdır: uzun süre açık kalır ve büyük/sonsuz veri akışı oluşturabilir. Bu nedenle bağlantının hem boyut hem süre açısından kontrollü olması gerekiyor.

## 12. STT provider sözleşmesini netleştirdik — Issue #11

**Ne yaptık?**
One-shot ve streaming STT için provider-neutral contract'lar tanımladık. Transcript/result durumlarına completed, cancelled ve failed gibi durumlar ekledik.

**Neden?**
Henüz belirli bir speech provider'a bağlanmadan önce backend ile provider arasındaki sınırı netleştirmek istedik. Böylece vendor seçimi uygulamanın geri kalanına yayılmasın.

## 13. AI timeout, retry ve güvenli observability ekledik — Issue #12

**Ne yaptık?**
- Gemini çağrılarına timeout koyduk.
- Geçici hatalarda sınırlı retry ve exponential backoff kullandık.
- Structured response bozuksa yeniden deneme desteği ekledik.
- Loglarda prompt, API key ve response body tutmuyoruz; status, attempt, süre ve hata tipi gibi sınırlı bilgiler kullanıyoruz.

**Neden?**
AI dış servisi her zaman hızlı veya başarılı olmayabilir. Kullanıcıyı gereksiz şekilde başarısızlığa düşürmeden transient sorunları tolere etmek, ama aynı zamanda sınırsız retry ile sistemi yormamak istedik.

## 14. Rate limiting ekledik — Issue #6

**Ne yaptık?**
- Genel endpoint'ler için kullanıcı/IP bazlı fixed-window rate limit koyduk.
- AI assistant endpoint'i için daha sıkı ayrı limit koyduk.
- Audio WebSocket bağlantıları için daha sıkı ayrı limit koyduk.
- Limitler configuration üzerinden değiştirilebilir.
- Limit aşımında HTTP 429 ve `Retry-After` dönüyoruz.

Mevcut örnek değerler:
- Genel: 120 istek / 60 saniye
- AI: 20 istek / 60 saniye
- Audio: 10 bağlantı / 60 saniye

**Neden rate limit'e ihtiyaç duyduk?**
Conversa'nın bazı işlemleri normal CRUD'dan daha pahalı: AI çağrıları dış servise gider, audio WebSocket uzun süre kaynak tutabilir. Kötü niyetli veya hatalı çalışan bir client çok kısa sürede aşırı sayıda request/connection açarsa API, AI provider ve altyapı gereksiz şekilde tüketilebilir.

Buradaki düşünce "kullanıcıyı engellemek" değil; **kaynak tüketimini kontrollü tutmak ve servisin tamamını tek bir istemcinin aşırı kullanımından korumak**.

Özellikle AI tarafında limitin ayrı olmasının nedeni, her HTTP isteğinin aynı maliyete sahip olmaması. Audio tarafında ise uzun yaşayan bağlantıların ayrı değerlendirilmesi gerekiyor.

## 15. Genel güvenlik yaklaşımı

Şu ana kadar güvenliği tek bir özellik olarak değil, katmanlar halinde ele aldık:

1. **Kimlik/yetki:** Kullanıcının hangi conversation'a erişebildiği.
2. **Input validation:** Backend'e ne büyüklükte ve ne formatta veri girebildiği.
3. **Rate limiting:** Ne hızda istek/bağlantı açılabildiği.
4. **AI prompt security:** Modele hangi verinin güvenilir talimat, hangisinin untrusted data olduğu.
5. **Secret management:** API key, password ve signing key'in source control'dan uzak tutulması.
6. **Safe logging:** Hata anında hassas bilgilerin loglara taşınmaması.
7. **Realtime hardening:** WebSocket'in süre, boyut ve mesaj tipi açısından sınırlandırılması.
8. **Regression tests:** Bu kuralların sonraki kod değişikliklerinde korunması.

Bu yaklaşımın temel fikri şu: **"Sisteme güveniyoruz" yerine, sistemin her sınırında kötü veya hatalı girdiyi kontrol ediyoruz.**

## 16. Şu anki durum

Backend foundation ve security hardening'in önemli kısmı tamamlandı. Ancak gerçek STT provider, production authentication wiring, deployment/multi-instance yapı ve web/mobile client henüz tamamlanmış ürün özellikleri değil.
