# Mono Courier Mobile

مشتری وب موبایل پیک برای دیدن یک پیشنهاد فعال، مشاهدهٔ مبدأ و مقصد روی نقشه و قبول یا رد پیشنهاد. رابط فعلی، ورود آزمایشی با توکن دستی دارد؛ توکن فقط در حافظهٔ تب نگهداری می‌شود.

## اجرا و بررسی

```sh
npm ci
npm test
npm run build
npm run dev
```

در توسعه، مسیرهای `/courier` به API محلی `http://localhost:5000` پراکسی می‌شوند. برای میزبانی جداگانه، `VITE_API_BASE_URL` را روی مبدأ API تنظیم کنید.

## پیکربندی نقشه

- `VITE_MAP_STYLE_URL` باید به Style JSON سرویس کاشی خودمیزبان Mono اشاره کند.
- برنامه از MapLibre GL برای نمایش استفاده می‌کند و fallback به tile server عمومی ندارد.
- دادهٔ نمایشی یا مختصات ساختگی نمایش داده نمی‌شود. نشانگرها فقط از پاسخ احرازشدهٔ `GET /courier/v1/mission-offers/{offerId}/route-context` می‌آیند و هر دو مختصات باید معتبر و وضعیت `COMPLETE` باشد.
- وقتی مختصات آماده نیست یا نقشه تنظیم نشده، پیام وضعیت نشان داده می‌شود و نشانگر/مسیر ساخته نمی‌شود.
- انتساب © OpenStreetMap contributors نمایش داده می‌شود. API فعلی مسیر خطی یا ETA برنمی‌گرداند؛ برنامه نیز آن‌ها را حدس نمی‌زند.

## API مورد استفاده

- `GET /courier/v1/session` برای اعتبارسنجی نشست
- `GET /courier/v1/mission-offers` برای پیشنهادها
- `GET /courier/v1/mission-offers/{offerId}/route-context` برای مختصات محدود مبدأ/مقصد
- `POST /courier/v1/mission-offers/{offerId}/accept` و `/decline` با `Idempotency-Key`

## پیش‌نیازهای بیرونی برای محصول

- اتصال OIDC واقعی پیک و توکن کوتاه‌عمر؛ endpoint ورود در این نسخه وجود ندارد.
- سرویس کاشی و Style JSON خودمیزبان روی محیط Stage/Production.
- HTTPS و دامنهٔ اپ برای نصب‌پذیری PWA.
- دریافت پیشنهاد در این مرحله با refresh دستی انجام می‌شود؛ push یا polling هنوز به این اپ اضافه نشده است.
- مسیریابی با اپ خارجی در این slice نیست؛ API فعلی تنها مختصات دو نقطه را می‌دهد.

- After acceptance, restores the courier's assigned active mission from `GET /courier/v1/missions/active`, opens supported mobile map apps through `geo:` coordinates, and advances the existing authenticated mission lifecycle one event at a time.
- Mission lifecycle actions are manual courier confirmations; the app does not infer arrival from GPS or geofence.
- Pending offers are refreshed every 15 seconds while the app is visible, and immediately when it returns to the foreground. Polling pauses in the background and while an accepted mission is active; manual refresh remains available.
