# Frontend

## 1. Frontend henüz uygulanmadı

React web ve React Native mobile client planlandı, ancak repository'nin mevcut durumunda frontend client bulunmuyor.

Bu nedenle burada "yaptık" diye yazmak yerine, backend'i tasarlarken frontend'in ihtiyaçlarını hangi kararlarla desteklediğimizi kaydediyoruz.

## 2. Conversation merkezli bir UI planladık

Frontend'in ana modeli bir conversation olacak:
- conversation listesi / sidebar
- aktif conversation
- conversation instruction'ı
- mesaj geçmişi
- canlı konuşma alanı

**Neden?**
Instruction ve geçmiş conversation'a ait olduğu için kullanıcı her yeni konuşmada aynı ayarları yeniden kurmak zorunda kalmamalı.

## 3. Canlı konuşma akışını iki parçaya ayırdık

Planlanan akış:

**Microphone → WebSocket → STT → transcript → Assistant API → AI result → UI**

WebSocket'i doğrudan AI'a bağlamak yerine transcript üretim sınırı olarak tuttuk.

**Neden?**
Speech-to-text ile AI conversation logic birbirinden bağımsız olsun istedik. Böylece STT provider değişse bile conversation/AI katmanı değişmek zorunda kalmasın.

## 4. AI sonucunu structured response olarak tasarladık

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

## 5. Instruction değişimini frontend'de görünür yapacağız

Kullanıcı aktif conversation'ın instruction'ını değiştirebilecek. Backend bunu kalıcı saklıyor ve değişikliği conversation geçmişinde system note olarak tutuyor.

**Neden?**
Kullanıcı "neden AI artık cevap önermeye başladı?" dediğinde bunun hangi talimat değişikliğiyle olduğunu görebilmeli.

## 6. Güvenlik frontend'e bırakılmadı

Frontend tarafında kullanıcıya güzel bir rate-limit mesajı, validation mesajı veya authentication akışı göstereceğiz; ancak güvenlik kontrolünü frontend'e emanet etmiyoruz.

**Neden?**
Frontend'deki bir kontrol tarayıcı dışında tekrar üretilebilir veya atlanabilir. Asıl güvenlik sınırları backend'de uygulanıyor.

## 7. Web ve mobile aynı backend sözleşmesini kullanacak

React web ve React Native mobile için ayrı backend mantıkları üretmek yerine aynı conversation/assistant/speech sözleşmelerini kullanmayı planladık.

**Neden?**
Aynı iş kurallarını iki client'ta tekrar etmek istemiyoruz. Client'lar UI/interaction tarafında farklılaşabilir; conversation ve AI davranışı backend'de ortak kalmalı.

## 8. Frontend yapılırken takip edeceğimiz sıra

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

## 9. Frontend için güvenlik notu

Frontend hiçbir zaman Gemini API key, database credential veya signing secret taşımayacak. AI çağrıları backend üzerinden yapılacak.

Rate limit, input limitleri, authorization ve prompt security backend'de kalacak.
