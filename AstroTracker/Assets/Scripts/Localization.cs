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
            { "pick", new[] { "Choose photo", "Выбрать фото", "Вибрати фото" } },
            { "noPhoto", new[] { "No photo selected", "Фото не выбрано", "Фото не вибрано" } },
            { "object", new[] { "Object", "Объект", "Об’єкт" } },
            { "targetRa", new[] { "Target RA, hours", "RA цели, часы", "RA цілі, години" } },
            { "targetDec", new[] { "Target Dec, degrees", "Dec цели, градусы", "Dec цілі, градуси" } },
            { "radius", new[] { "Search radius, degrees", "Радиус поиска, градусы", "Радіус пошуку, градуси" } },
            { "scale", new[] { "Pixel scale, ″/pixel", "Масштаб, ″/пиксель", "Масштаб, ″/піксель" } },
            { "solve", new[] { "Solve", "Решить", "Розв’язати" } },
            { "solving", new[] { "Solving…", "Решаю…", "Розв’язую…" } },

            { "photoError", new[] { "Could not open the photo: ", "Не удалось открыть фото: ", "Не вдалося відкрити фото: " } },
            { "error", new[] { "Error: ", "Ошибка: ", "Помилка: " } },
            {
                "raError", new[]
                {
                    "Target RA: hours from 0 to 24, for example \"9 55 52\" or \"9.93\".",
                    "RA цели: нужны часы от 0 до 24, например «9 55 52» или «9.93».",
                    "RA цілі: потрібні години від 0 до 24, наприклад «9 55 52» або «9.93».",
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
                    "Check the target coordinates, the search radius and the pixel scale.",
                    "Проверьте координаты цели, радиус поиска и масштаб.",
                    "Перевірте координати цілі, радіус пошуку та масштаб.",
                }
            },
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
