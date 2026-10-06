# Frontend

## 1. React web foundation hazır

React web client artık repository içinde `frontend/` altında bulunuyor. React 19.3, TypeScript 7 ve Vite 8 ile oluşturuldu. Vite resmi olarak React + TypeScript template'ini destekliyor; local geliştirme için Vite'ın güncel dokümantasyonundaki standart akış kullanılıyor.

Temel komutlar:

    cd frontend
    npm install
    npm run dev

Vite geliştirme sunucusu varsayılan olarak 5173 portunda çalışıyor.

## 2. Authentication ve session akışı

Frontend authenticated session için ortak bir auth context ve session service kullanıyor.

Akış:

**Externally issued access token → `POST /api/auth/session` → short-lived in-memory token + HttpOnly session cookie → authenticated UI**

Repository hâlâ kullanıcı/parola veya identity-provider credential issuance yapmıyor. Dış identity sisteminin verdiği ilk access token yalnızca oturumu başlatmak için kullanılıyor.

Session davranışı:

- Conversa access token sadece JavaScript belleğinde tutuluyor.
- Persistent session `HttpOnly` cookie ile tutuluyor; JavaScript cookie değerini okuyamıyor.
- `localStorage` veya `sessionStorage` kullanılmıyor.
- Sayfa yenilendiğinde `POST /api/auth/refresh` ile session cookie doğrulanıp yeni access token alınıyor.
- Protected API isteği HTTP 401 dönerse client tek bir refresh denemesi yapıp isteği bir kez tekrar ediyor.
- Refresh başarısızsa access token temizleniyor ve UI sign-in ekranına dönüyor.
- Sign out hem sunucu session cookie'sini temizliyor hem de in-memory access token'ı siliyor.
- Token değerleri loglanmıyor veya frontend environment değişkenlerine konulmuyor.

Access token kısa ömürlüdür; persistent session daha uzun ömürlüdür. Bu ayrım reload rahatlığını sağlarken uzun ömürlü credential'ı browser JavaScript storage'ından uzak tutar.

## 3. Conversation merkezli bir UI planladık

Frontend'in ana modeli bir conversation olacak:
- conversation listesi / sidebar
- aktif conversation
- conversation instruction'ı
- mesaj geçmişi
- canlı konuşma alanı

**Neden?**

Instruction ve geçmiş conversation'a ait olduğu için kullanıcı her yeni konuşmada aynı ayarları yeniden kurmak zorunda kalmamalı.

## 4. Canlı konuşma akışını iki parçaya ayırdık

Planlanan akış:

**Microphone → WebSocket → STT → transcript → Assistant API → AI result → UI**

WebSocket'i doğrudan AI'a bağlamak yerine transcript üretim sınırı olarak tuttuk.

**Neden?**

Speech-to-text ile AI conversation logic birbirinden bağımsız olsun istedik. Böylece STT provider değişse bile conversation/AI katmanı değişmek zorunda kalmasın.

## 5. AI sonucunu structured response olarak tasarladık

Frontend'in sadece bir "cevap metni" almasını istemedik. Backend şu tür bilgileri ayrı alanlarda taşıyor:
- original
- translation
- explanation
- suggestedAnswer
- suggestedAnswerTranslation
- questionDetected
- questionDirectedAtUser
- response type

**Neden?**

UI bu alanları ayrı gösterebilir: örneğin çeviri üstte, açıklama altında, kullanıcıya söylenebilecek İngilizce cevap ayrı bir kartta.

## 6. Backend bağlantı ve config ayrımı

Frontend tarafında `VITE_API_BASE_URL` ile backend origin'i ayrı tutuluyor.

Local geliştirmede:
- `/api`
- `/health`
- `/ws`

Vite dev server üzerinden backend'e proxy ediliyor. Böylece local geliştirmede frontend'in doğrudan provider veya database bilgisine ihtiyacı yok.

API client ortak authenticated HTTP davranışını taşıyor; Bearer token ekleme ve 401/429/5xx gibi hata sınıflandırmaları component'lara yayılmıyor.

## 7. Instruction değişimini frontend'de görünür yapacağız

Kullanıcı aktif conversation'ın instruction'ını değiştirebilecek. Backend bunu kalıcı saklıyor ve değişikliği conversation geçmişinde system note olarak tutuyor.

**Neden?**

Kullanıcı "neden AI artık cevap önermeye başladı?" dediğinde bunun hangi talimat değişikliğiyle olduğunu görebilmeli.

## 8. Web ve mobile aynı backend sözleşmesini kullanacak

React web ve React Native mobile için ayrı backend mantıkları üretmek yerine aynı conversation/assistant/speech sözleşmelerini kullanmayı planladık.

**Neden?**

Aynı iş kurallarını iki client'ta tekrar etmek istemiyoruz. Client'lar UI/interaction tarafında farklılaşabilir; conversation ve AI davranışı backend'de ortak kalmalı.

## 9. Frontend yapılırken takip edeceğimiz sıra

1. Authentication/session akışı
2. Conversation listesi ve oluşturma
3. Active conversation + message history
4. Instruction düzenleme
5. Text-based assistant flow
6. Microphone permission ve audio capture
7. WebSocket/STT transcript akışı
8. Translation/explanation/suggested answer UI
9. Error, rate-limit ve connection-state UI'ları
10. Web ve mobile davranışlarının ayrıştırılması

Bu sıra özellikle seçildi: önce backend sözleşmelerini kullanan basit text flow'u doğrulamak, sonra gerçek zamanlı audio tarafına geçmek daha az riskli.

## 10. Frontend için güvenlik notu

Frontend hiçbir zaman Gemini API key, database credential veya signing secret taşımayacak. AI çağrıları backend üzerinden yapılacak.

Rate limit, input limitleri, authorization ve prompt security backend'de kalacak.

Frontend CI ayrıca `typecheck`, test ve production build çalıştırıyor; external Gemini/Deepgram smoke çağrıları frontend CI'ına eklenmiyor.


## P30 — Suggested-answer actions

Suggested English replies in the web conversation timeline support:

- copying the English reply through the browser Clipboard API, with a local textarea fallback when Clipboard API access is unavailable or denied;
- explicit copied/copy-failed feedback;
- regenerating a reply through the existing authenticated assistant API using the original message input;
- explicit regeneration loading/success/failure feedback.

Regeneration creates a new persisted message rather than modifying the previous AI result. The authenticated conversation boundary remains enforced by the existing assistant endpoint, and copied text never leaves the browser through a separate third-party request.
