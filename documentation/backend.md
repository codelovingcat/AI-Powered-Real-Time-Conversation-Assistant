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

JWT Bearer authentication composition root'ta doğrulanıyor; issuer, audience, signing key ve token lifetime kontrol ediliyor. Conversation ve WebSocket erişimi authenticated user context üzerinden çalışıyor. P22 ile web için kısa ömürlü access JWT üretimi ve HttpOnly persistent session cookie eklendi; ilk kullanıcı credential'ı yine repository dışında çalışan identity system tarafından sağlanıyor.

## 7. Persistent web session ekledik — P22

**Ne yaptık?**
- Dış identity sisteminden gelen ilk Bearer token'ı doğrulayıp Conversa için kısa ömürlü access JWT üreten session endpoint'i ekledik.
- Uzun ömürlü session bilgisini `HttpOnly` cookie olarak ASP.NET Core Cookie Authentication ticket'ında tuttuk.
- Browser reload sonrası cookie üzerinden access token yenileyen refresh endpoint'i ekledik.
- Logout ile persistent session cookie'sini invalidate ettik.
- Cookie tabanlı POST endpoint'lerinde Origin kontrolü ve frontend için credentials-enabled CORS sınırı ekledik.
- Access token JavaScript belleğinde kalmaya devam ediyor; browser storage kullanılmıyor.

**Neden?**
Access token'ı localStorage'a taşımadan reload sonrası oturumu korumak istedik. Persistent credential'ın JavaScript tarafından okunamaması XSS etkisini azaltan önemli bir sınır oluşturuyor. Session süresi ve access token süresi configuration üzerinden kontrol ediliyor.

Production hosting'de persistent session cookie'lerinin uygulama yeniden başlatmaları/instance değişimleri arasında geçerli kalması için ASP.NET Core Data Protection key ring'inin kalıcı ve güvenli biçimde saklanması gerekir.

## 8. Hassas exception detaylarını loglamadık — Issue #4

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

## 17. Şu anki durum

Backend foundation ve security hardening'in önemli kısmı tamamlandı; web persistent session akışı da P22 kapsamında eklendi. Ancak identity provider/token issuance, deployment/multi-instance yapı ve web/mobile ürün kapsamının tamamı henüz tamamlanmış değil.


## Distributed rate limiting

Rate limiting is backed by a shared Valkey/Redis store in production so multiple API instances consume the same counters.

Configured policies:
- Global: 120 requests / 60 seconds
- AI assistant: 20 requests / 60 seconds
- Audio WebSocket: 10 connections / 60 seconds

Authenticated requests are partitioned by the validated JWT `sub`/`NameIdentifier` claim. Client-supplied user identifiers are never used for abuse-control keys. Anonymous requests use the connection IP address.

Rate-limit keys contain a SHA-256 hash of the partition identity, so raw user identifiers are not stored in the rate-limit backend.

When the limit is exceeded, the API returns HTTP 429 with a `Retry-After` header and the existing `rate_limit_exceeded` response shape. If the shared rate-limit store is unavailable, the API fails closed with HTTP 503 rather than silently falling back to process-local protection.

For local development and isolated unit tests, the API uses an in-memory store. Production and staging require `RateLimiting__RedisConnectionString`.

The Render production Blueprint provisions the shared Valkey instance in Frankfurt and injects its connection string into the API service.


## OpenTelemetry observability

P26 adds configurable OpenTelemetry tracing and metrics.

- ASP.NET Core HTTP requests produce automatic traces and metrics.
- Outbound HttpClient calls are traced when observability is enabled, including Gemini and Deepgram provider calls.
- Custom AI metrics record request count and duration by provider, operation and outcome.
- Custom STT metrics record request count and duration by provider, operation and outcome.
- Audio WebSocket metrics record active connections, connection duration and failures.
- Custom Activities provide provider-level spans for assistant, AI, STT and WebSocket operations.

Observability is disabled by default in committed configuration. Enable it with `Observability__Enabled=true` and direct the OTLP exporter with `Observability__OtlpEndpoint`. Outside Development and Testing, configured OTLP endpoints must use HTTPS and cannot contain embedded credentials.

Telemetry intentionally excludes prompts, transcripts, access tokens, API keys, database credentials and conversation identifiers. Correlation is provided by the standard W3C trace context and the trace ID already returned by the API error model.


## P27 — Cursor pagination

Conversation and message history endpoints use cursor pagination rather than returning the full collection.

- `GET /api/conversations?limit=50&cursor=...` returns the newest page for the authenticated user.
- `GET /api/conversations/{id}/messages?limit=50&cursor=...` returns the newest page for an owned conversation.
- Responses use `{ items, nextCursor }`; `nextCursor` is `null` on the final page.
- Ordering is deterministic: conversations use `UpdatedAt` then `Id` descending; messages use `CreatedAt` then `Id` descending at the query boundary and are returned oldest-to-newest to the client.
- Cursor values contain only pagination position data. They do not contain conversation content or authorization state.
- Ownership is applied independently of cursor parameters: conversation pages always filter by `currentUser.UserId`, and message pages first resolve the conversation through the authenticated user before querying messages.
- Queries request at most one extra row to determine whether another page exists, avoiding a separate count query and keeping history payloads bounded.


## P28 — Conversation search and filtering

Conversation listing remains cursor-paginated and now supports server-side search and update-date filters:

- GET `/api/conversations?limit=50&search=hotel`
- GET `/api/conversations?limit=50&updatedFrom=...&updatedTo=...`
- Filters can be combined with `cursor` for incremental loading.
- Search matches conversation title and stored source/target language metadata case-insensitively.
- `%`, `_`, and `\` in search input are escaped so user text is not interpreted as a SQL `LIKE` pattern.
- `search` is capped at 100 characters; invalid date ranges are rejected before the repository query.
- Ownership filtering is always applied from the authenticated user before search/filter predicates.
- `updatedFrom` is inclusive and `updatedTo` is exclusive; the web UI sends UTC day boundaries.
- Pagination remains deterministic with `UpdatedAt` and `Id`, and filtered results use the same cursor contract.

The web sidebar applies filters on submit rather than on every keystroke, then keeps loading older pages within the active filter scope.
