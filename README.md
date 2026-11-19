<p align="center">
  <img src="Assets/MizuEngine_logo_128.png" width="96" alt="MizuEngine">
</p>

<h1 align="center">MizuEngine</h1>

<p align="center">
  Кастомизация Windows 10/11: эффекты на рабочем столе, док как в macOS, интерфейс «жидкое стекло».<br>
  Customization for Windows 10/11: desktop effects, a macOS-style dock, liquid-glass UI.
</p>

<p align="center">
  <a href="https://github.com/MizuEngineApp/MizuEngine/releases/latest"><b>⬇ Скачать последнюю версию / Download the latest version</b></a>
</p>

---

## Что умеет

- **Эффекты на рабочем столе**: снег с сугробами на окнах, дождь, сакура, листья, светлячки, метель, звёзды, северное сияние и другие. Курсор счищает снег, эффекты разлетаются от мыши. Клики проходят сквозь эффекты.
- **Док как в macOS** вместо панели задач: значки увеличиваются под курсором, закрепление, трей, часы. Окна сворачиваются в док «джинном», как на Mac. Есть Dynamic Island с музыкой и уведомлениями.
- **Интерфейс «жидкое стекло»**: 4 темы и своя тема из картинки, 8 языков, плавные анимации.
- **Кастомизация**: наборы курсоров, счётчик FPS поверх игр, виджеты (система, часы, погода), рамки окон, звуки интерфейса, горячие клавиши.
- **Wallpaper Engine**: выбираешь скачанные обои прямо в MizuEngine.
- **Сборки**: сохраняешь все настройки в файл `.mizupreset` и отправляешь другу.
- **Аккаунты**: профиль, друзья, чаты, группы, посты, сообщество.
- **Mizu Plus** (подписка): живые темы и обои, эффекты, курсоры, Muzule (Claude прямо в программе).

## 🔒 Это не вирус: как проверить

Я понимаю, что незнакомому exe из интернета доверять не хочется. Вот всё, что можно проверить самому:

1. **Код открыт.** Весь код интерфейса, эффектов, дока и аккаунтов лежит в этом репозитории, его может прочитать любой. Не публикуются только серверная часть (база, оплата) и защита сборки от взлома.
2. **Подпись автора.** Каждый официальный `MizuEngine.exe` подписан сертификатом **rainy** (`CN=rainy, O=MizuEngine`). Проверить: правой кнопкой по файлу → «Свойства» → «Цифровые подписи». Если подписи нет или в exe изменили хотя бы байт, MizuEngine сам откажется запускаться.
3. **Контрольная сумма.** В описании каждого релиза указан SHA-256 файла. Проверить в PowerShell: `Get-FileHash .\MizuEngine.exe`. Хэш должен совпасть.
4. **Скачивай только отсюда**: [Releases](https://github.com/MizuEngineApp/MizuEngine/releases) или Telegram-канал автора. Файлы из других мест могут быть подменены.

**Почему Windows показывает «Windows защитила ваш компьютер»?** Сертификат rainy самоподписанный: платный сертификат у крупного центра стоит сотни долларов в год. SmartScreen так предупреждает о любой новой программе от небольшого автора. Нажми «Подробнее» → «Выполнить в любом случае». После этого MizuEngine обновляется сам, и такое окно больше не появляется.

### Что MizuEngine делает с системой

- Пока включён док, прячет панель задач Windows. При выключении дока, выходе или вылете программы панель возвращается.
- Меняет курсоры, только если ты выбрал набор. При выходе возвращаются стандартные.
- Добавляет себя в автозапуск, только если включить это в настройках.
- Настройки и вход в аккаунт хранятся в `%AppData%\MizuEngine`, данные входа зашифрованы средствами Windows (DPAPI).
- В интернет ходит к своему серверу аккаунтов и чатов (Supabase), к GitHub за обновлениями, к open-meteo.com за погодой и в Telegram за аватаркой автора. Вход через Google/GitHub открывается в твоём браузере. Muzule отправляет запросы напрямую в Anthropic с **твоим** ключом, через сервер MizuEngine ничего не идёт.
- Счётчику FPS нужны права администратора, программа спрашивает их отдельной кнопкой и только если ты сам её нажмёшь.

## Обновления

MizuEngine проверяет новые версии на GitHub при запуске и каждые 2 часа. Когда вышла новая версия, есть два варианта:

- **«Обновить сейчас»**: программа сама скачивает новый `MizuEngine.exe` из релиза, проверяет размер, SHA-256 и подпись rainy, заменяет старый файл и перезапускается.
- **«Страница на GitHub»**: открывает релиз, файл можно скачать вручную и заменить им старый.

Настройки и аккаунт при обновлении не теряются.

---

## What it does (English)

- **Desktop effects**: snow that piles up on windows, rain, sakura, leaves, fireflies, a blizzard, stars, an aurora and more. They react to your cursor, and clicks pass straight through them.
- **macOS-style dock** instead of the taskbar: icons magnify under the cursor, pinning, tray, clock, a “genie” minimize animation, and a Dynamic Island for music and notifications.
- **Liquid-glass UI**: 4 themes plus custom image themes, 8 languages, smooth animations.
- **Customization**: cursor packs, an FPS counter over games, widgets, window borders, UI sounds, hotkeys. Wallpaper Engine integration. Presets you can share.
- **Accounts**: profiles, friends, chats, groups, posts, a community.

### Is it safe?

- The source code (UI, effects, dock, accounts) is public in this repository. Only the server side and the anti-tamper part are private.
- Every official `MizuEngine.exe` is signed by **rainy** (`CN=rainy, O=MizuEngine`): right-click → Properties → Digital Signatures. A modified exe refuses to start.
- Each release lists the file's SHA-256. Check it with `Get-FileHash .\MizuEngine.exe`.
- SmartScreen warns because the certificate is self-signed. Click “More info” → “Run anyway”. After that, updates install from inside the app.
- Settings live in `%AppData%\MizuEngine`. The taskbar and cursors are restored when you quit.

### Updates

The app checks GitHub releases at startup and every 2 hours. **Update now** downloads the new exe, verifies its size, SHA-256 and rainy's signature, swaps the file and restarts. **Open on GitHub** lets you download it manually. Your settings and account are kept.

---

## Авторские права / Copyright

© 2026 **rainy**: Telegram [@MrRainthisisme](https://t.me/MrRainthisisme), Discord `vanixxrain`. Все права защищены / All rights reserved.

Код открыт **только для просмотра**. Копировать его, выкладывать изменённые версии, продавать и обходить защиту запрещено. Подробно: [LICENSE](LICENSE).
The source is published **for viewing only**. See [LICENSE](LICENSE).
