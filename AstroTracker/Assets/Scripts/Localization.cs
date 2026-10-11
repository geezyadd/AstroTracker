using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstroTracker
{
    public enum Language
    {
        English,
        Russian,
        Ukrainian,
    }

    /// <summary>Interface texts in every supported language. The choice is remembered between launches.</summary>
    public static class Localization
    {
        const string PreferenceKey = "language";

        static Language? _current;

        public static event Action Changed;

        public static Language Current
        {
            get
            {
                _current ??= PlayerPrefs.HasKey(PreferenceKey)
                    ? (Language)Mathf.Clamp(PlayerPrefs.GetInt(PreferenceKey), 0, 2)
                    : FromSystem();
                return _current.Value;
            }
            set
            {
                if (value == Current)
                    return;
                _current = value;
                PlayerPrefs.SetInt(PreferenceKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>Text for a key in the current language; the key itself when it is unknown.</summary>
        public static string Get(string key)
        {
            return Texts.TryGetValue(key, out string[] variants) ? variants[(int)Current] : key;
        }

        /// <summary>Name of an OpenNGC object type, or null for types that have none.</summary>
        public static string ObjectType(string code)
        {
            return Texts.TryGetValue("type." + code, out string[] variants) ? variants[(int)Current] : null;
        }

        static Language FromSystem()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian: return Language.Russian;
                case SystemLanguage.Ukrainian: return Language.Ukrainian;
                default: return Language.English;
            }
        }

        // English, Russian, Ukrainian.
        static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
        {
            { "pick", new[] { "From gallery", "Из галереи", "З галереї" } },
            { "fromCamera", new[] { "From camera", "С камеры", "З камери" } },
            { "noPhoto", new[] { "No photo selected", "Фото не выбрано", "Фото не вибрано" } },
            { "object", new[] { "Object", "Объект", "Об’єкт" } },
            { "targetRa", new[] { "Target RA, hours", "RA цели, часы", "RA цілі, години" } },
            { "targetDec", new[] { "Target Dec, degrees", "Dec цели, градусы", "Dec цілі, градуси" } },
            { "optional", new[] { "optional", "необязательно", "необов’язково" } },
            { "radius", new[] { "Search radius, degrees", "Радиус поиска, градусы", "Радіус пошуку, градуси" } },
            { "scale", new[] { "Pixel scale, ″/pixel", "Масштаб, ″/пиксель", "Масштаб, ″/піксель" } },
            { "solve", new[] { "Solve", "Решить", "Розв’язати" } },
            { "solving", new[] { "Solving…", "Решаю…", "Розв’язую…" } },
            { "cancel", new[] { "Stop the search", "Остановить поиск", "Зупинити пошук" } },
            {
                "searchingSky", new[]
                {
                    "Not found near the target. Searching the whole sky: area {0} of {1}…",
                    "Рядом с целью не найдено. Ищу по всему небу: участок {0} из {1}…",
                    "Поруч із ціллю не знайдено. Шукаю по всьому небу: ділянка {0} з {1}…",
                }
            },
            {
                "searchingSkyNoTarget", new[]
                {
                    "Searching the whole sky: area {0} of {1}…",
                    "Ищу по всему небу: участок {0} из {1}…",
                    "Шукаю по всьому небу: ділянка {0} з {1}…",
                }
            },
            { "searchStopped", new[] { "Search stopped.", "Поиск остановлен.", "Пошук зупинено." } },

            { "photoError", new[] { "Could not open the photo: ", "Не удалось открыть фото: ", "Не вдалося відкрити фото: " } },
            { "error", new[] { "Error: ", "Ошибка: ", "Помилка: " } },
            {
                "raError", new[]
                {
                    "Target RA: hours from 0 to 24, for example \"9 55 52\" or \"9.93\". Clear both target fields to solve without a target.",
                    "RA цели: нужны часы от 0 до 24, например «9 55 52» или «9.93». Чтобы решать без цели, очистите оба поля.",
                    "RA цілі: потрібні години від 0 до 24, наприклад «9 55 52» або «9.93». Щоб розв’язувати без цілі, очистіть обидва поля.",
                }
            },
            {
                "decError", new[]
                {
                    "Target Dec: degrees from −90 to +90, for example \"+69 40 47\".",
                    "Dec цели: нужны градусы от −90 до +90, например «+69 40 47».",
                    "Dec цілі: потрібні градуси від −90 до +90, наприклад «+69 40 47».",
                }
            },
            {
                "radiusError", new[]
                {
                    "Search radius: a number of degrees between 0 and 45.",
                    "Радиус поиска: нужно число от 0 до 45 градусов.",
                    "Радіус пошуку: потрібне число від 0 до 45 градусів.",
                }
            },
            {
                "scaleError", new[]
                {
                    "Pixel scale: a positive number of arcseconds per pixel.",
                    "Масштаб: нужно положительное число угловых секунд на пиксель.",
                    "Масштаб: потрібне додатне число кутових секунд на піксель.",
                }
            },

            { "noSolution", new[] { "No solution found.", "Решение не найдено.", "Розв’язок не знайдено." } },
            {
                "noSolutionStats", new[]
                {
                    "Stars in the frame: {0}. Time: {1:F1} s.",
                    "Звёзд на кадре: {0}. Время: {1:F1} с.",
                    "Зір на кадрі: {0}. Час: {1:F1} с.",
                }
            },
            {
                "noSolutionHint", new[]
                {
                    "The whole sky was searched. Check the pixel scale; the frame may also have too few stars.",
                    "Проверено всё небо. Проверьте масштаб; возможно, на кадре слишком мало звёзд.",
                    "Перевірено все небо. Перевірте масштаб; можливо, на кадрі замало зір.",
                }
            },
            { "inFrame", new[] { "In the frame:", "В кадре:", "У кадрі:" } },
            {
                "inFrameNone", new[]
                {
                    "No catalogue objects in the frame.",
                    "Объектов из каталога в кадре нет.",
                    "Об’єктів із каталогу в кадрі немає.",
                }
            },
            { "fromCentre", new[] { "{0} from the centre", "{0} от центра", "{0} від центру" } },
            { "andMore", new[] { "and {0} more", "и ещё {0}", "і ще {0}" } },
            { "frameCentre", new[] { "Frame centre (J2000)", "Центр кадра (J2000)", "Центр кадру (J2000)" } },
            { "toTarget", new[] { "Distance to target: ", "До цели: ", "До цілі: " } },
            {
                "toTargetAxes", new[]
                {
                    "RA {0:+0.000;−0.000}°, Dec {1:+0.000;−0.000}°",
                    "по RA {0:+0.000;−0.000}°, по Dec {1:+0.000;−0.000}°",
                    "за RA {0:+0.000;−0.000}°, за Dec {1:+0.000;−0.000}°",
                }
            },
            {
                "scaleLine", new[]
                {
                    "Scale {0:F3}″/pixel, frame up is {1:F1}° east of north{2}",
                    "Масштаб {0:F3}″/пиксель, верх кадра {1:F1}° от севера к востоку{2}",
                    "Масштаб {0:F3}″/піксель, верх кадру {1:F1}° від півночі до сходу{2}",
                }
            },
            { "mirrored", new[] { ", mirrored", ", кадр зеркальный", ", кадр дзеркальний" } },
            {
                "starsLine", new[]
                {
                    "Stars in the frame {0}, matched to the catalogue {1}, rms {2:F2}″",
                    "Звёзд на кадре {0}, совпало с каталогом {1}, остаток {2:F2}″",
                    "Зір на кадрі {0}, збіглося з каталогом {1}, залишок {2:F2}″",
                }
            },
            { "time", new[] { "Time: {0:F1} s", "Время: {0:F1} с", "Час: {0:F1} с" } },
            { "magnitude", new[] { "mag ", "зв. вел. ", "зор. вел. " } },
            { "hours", new[] { "h", "ч", "год" } },
            { "minutes", new[] { "m", "м", "хв" } },
            { "seconds", new[] { "s", "с", "с" } },

            { "cameraTitle", new[] { "Photos on the camera", "Фото на камере", "Фото на камері" } },
            { "cameraRefresh", new[] { "Refresh", "Обновить", "Оновити" } },
            { "cameraClose", new[] { "Close", "Закрыть", "Закрити" } },
            { "cameraLoading", new[] { "Reading the photo list…", "Читаю список фото…", "Читаю список фото…" } },
            { "cameraDownloading", new[] { "Downloading {0}…", "Скачиваю {0}…", "Завантажую {0}…" } },
            {
                "cameraNone", new[]
                {
                    "No camera found. Connect it with the USB cable and switch it on.",
                    "Камера не найдена. Подключите её кабелем USB и включите.",
                    "Камеру не знайдено. Під’єднайте її кабелем USB і ввімкніть.",
                }
            },
            {
                "cameraDenied", new[]
                {
                    "Access to the camera was not granted.",
                    "Доступ к камере не разрешён.",
                    "Доступ до камери не надано.",
                }
            },
            {
                "cameraOpenFailed", new[]
                {
                    "Could not open the camera. Reconnect the cable and try again.",
                    "Не удалось открыть камеру. Переподключите кабель и попробуйте снова.",
                    "Не вдалося відкрити камеру. Перепід’єднайте кабель і спробуйте ще раз.",
                }
            },
            {
                "cameraEmpty", new[]
                {
                    "There are no JPG photos on the camera. When shooting RAW, choose RAW+JPG.",
                    "На камере нет фото JPG. При съёмке в RAW выберите RAW+JPG.",
                    "На камері немає фото JPG. Для зйомки в RAW виберіть RAW+JPG.",
                }
            },
            {
                "cameraDownloadFailed", new[]
                {
                    "Could not download the photo.",
                    "Не удалось скачать фото.",
                    "Не вдалося завантажити фото.",
                }
            },
            {
                "cameraUnsupported", new[]
                {
                    "Reading a camera over USB works on Android only.",
                    "Чтение камеры по USB работает только на Android.",
                    "Читання камери через USB працює лише на Android.",
                }
            },
            {
                "cameraError", new[]
                {
                    "Something went wrong while talking to the camera.",
                    "При работе с камерой произошла ошибка.",
                    "Під час роботи з камерою сталася помилка.",
                }
            },

            { "mountTitle", new[] { "Mount (SynScan app)", "Монтировка (приложение SynScan)", "Монтування (застосунок SynScan)" } },
            { "mountAddress", new[] { "SynScan address", "Адрес SynScan", "Адреса SynScan" } },
            { "mountCheck", new[] { "Check", "Проверить", "Перевірити" } },
            { "mountGoTo", new[] { "GoTo", "GoTo", "GoTo" } },
            { "mountSync", new[] { "Sync", "Синхр.", "Синхр." } },
            { "mountStop", new[] { "Stop", "Стоп", "Стоп" } },
            {
                "mountNoAnswer", new[]
                {
                    "SynScan does not answer at {0}. Is the app running?",
                    "SynScan не отвечает по адресу {0}. Приложение запущено?",
                    "SynScan не відповідає за адресою {0}. Застосунок запущено?",
                }
            },
            {
                "mountRefused", new[]
                {
                    "SynScan refused the command: {0}",
                    "SynScan отклонил команду: {0}",
                    "SynScan відхилив команду: {0}",
                }
            },
            {
                "mountBadAddress", new[]
                {
                    "Address: an IP or host name, optionally with a port, for example 127.0.0.1 or 192.168.4.2:11881.",
                    "Адрес: IP или имя устройства, можно с портом, например 127.0.0.1 или 192.168.4.2:11881.",
                    "Адреса: IP або ім’я пристрою, можна з портом, наприклад 127.0.0.1 або 192.168.4.2:11881.",
                }
            },
            {
                "mountNotConnected", new[]
                {
                    "SynScan is running but is not connected to the mount.",
                    "SynScan запущен, но не подключён к монтировке.",
                    "SynScan запущено, але не під’єднано до монтування.",
                }
            },
            {
                "mountInfo", new[]
                {
                    "SynScan {0} answers. It believes the telescope points at RA {1}, Dec {2} (J2000).",
                    "SynScan {0} отвечает. По его данным труба смотрит на RA {1}, Dec {2} (J2000).",
                    "SynScan {0} відповідає. За його даними труба дивиться на RA {1}, Dec {2} (J2000).",
                }
            },
            {
                "mountNoTarget", new[]
                {
                    "Enter the target coordinates first.",
                    "Сначала введите координаты цели.",
                    "Спершу введіть координати цілі.",
                }
            },
            { "mountSlewing", new[] { "Slewing to the target…", "Навожусь на цель…", "Наводжуся на ціль…" } },
            {
                "mountArrived", new[]
                {
                    "SynScan reports the slew is finished. Take a frame and solve it.",
                    "SynScan сообщает, что наведение закончено. Снимите кадр и решите его.",
                    "SynScan повідомляє, що наведення завершено. Зніміть кадр і розв’яжіть його.",
                }
            },
            {
                "mountNeedSolve", new[]
                {
                    "Solve a frame first: Sync sends the position found in it.",
                    "Сначала решите кадр: синхронизация отправляет найденное в нём положение.",
                    "Спершу розв’яжіть кадр: синхронізація надсилає знайдене в ньому положення.",
                }
            },
            {
                "mountNoSync", new[]
                {
                    "This SynScan does not accept Sync.",
                    "Этот SynScan не принимает синхронизацию.",
                    "Цей SynScan не приймає синхронізацію.",
                }
            },
            {
                "mountSynced", new[]
                {
                    "Synced: SynScan now knows where the telescope points. Press GoTo to centre the target.",
                    "Синхронизировано: SynScan теперь знает, куда смотрит труба. Нажмите GoTo, чтобы довести на цель.",
                    "Синхронізовано: SynScan тепер знає, куди дивиться труба. Натисніть GoTo, щоб довести на ціль.",
                }
            },
            { "mountStopped", new[] { "Stopped.", "Остановлено.", "Зупинено." } },

            { "type.G", new[] { "galaxy", "галактика", "галактика" } },
            { "type.GPair", new[] { "galaxy pair", "пара галактик", "пара галактик" } },
            { "type.GTrpl", new[] { "galaxy triplet", "тройка галактик", "трійка галактик" } },
            { "type.GGroup", new[] { "galaxy group", "группа галактик", "група галактик" } },
            { "type.OCl", new[] { "open cluster", "рассеянное скопление", "розсіяне скупчення" } },
            { "type.GCl", new[] { "globular cluster", "шаровое скопление", "кулясте скупчення" } },
            { "type.Cl+N", new[] { "cluster with nebula", "скопление с туманностью", "скупчення з туманністю" } },
            { "type.*Ass", new[] { "stellar association", "звёздная ассоциация", "зоряна асоціація" } },
            { "type.PN", new[] { "planetary nebula", "планетарная туманность", "планетарна туманність" } },
            { "type.HII", new[] { "H II region", "область H II", "область H II" } },
            { "type.Neb", new[] { "nebula", "туманность", "туманність" } },
            { "type.EmN", new[] { "emission nebula", "эмиссионная туманность", "емісійна туманність" } },
            { "type.RfN", new[] { "reflection nebula", "отражательная туманность", "відбивна туманність" } },
            { "type.DrkN", new[] { "dark nebula", "тёмная туманность", "темна туманність" } },
            { "type.SNR", new[] { "supernova remnant", "остаток сверхновой", "залишок наднової" } },
            { "type.Nova", new[] { "nova", "новая", "нова" } },
            { "type.*", new[] { "star", "звезда", "зоря" } },
            { "type.**", new[] { "double star", "двойная звезда", "подвійна зоря" } },
        };
    }
}
