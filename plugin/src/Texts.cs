using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace SandboxMenu
{
    /// <summary>
    /// Строки меню и чат-сообщений: русский и английский.
    ///
    /// Базовые пары живут рядом с вызовом (Texts.T("ру", "en")) - так строку
    /// видно там, где она рисуется, - но перевод при этом вынесен в ФАЙЛ:
    /// BepInEx/plugins/lang/lang.tsv (колонки: russian&lt;TAB&gt;english).
    /// Файл создаётся плагином при первом запуске из встроенных пар и дальше
    /// правится руками: значения из файла перекрывают встроенные, поэтому
    /// полную редакцию перевода можно сделать без пересборки dll.
    ///
    /// Динамические строки (интерполяция с числами) в файл не выносятся:
    /// ключом служил бы шаблон, а не текст; они остаются в коде, а файл
    /// покрывает статичные подписи - основную массу переводимого.
    ///
    /// Формат сообщений чата - как в остальных плагинах автора: префикс из
    /// двух цветных букв, разделитель, одна строка сути (GameApi.Prefix).
    /// </summary>
    internal static class Texts
    {
        public static bool Russian = true;

        private static bool _loaded;
        private static readonly Dictionary<string, string> _overrideRu = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _overrideEn = new Dictionary<string, string>();

        public static string T(string ru, string en)
        {
            EnsureLang();
            string v;
            if (Russian)
            {
                return _overrideRu.TryGetValue(ru, out v) ? v : ru;
            }
            return _overrideEn.TryGetValue(ru, out v) ? v : en;
        }

        /// <summary>
        /// Один раз читает lang/lang.tsv рядом с плагином; при отсутствии файла
        /// создаёт его из вызовов, которые плагин уже запросил к этому моменту,
        /// плюс встроенный минимум, чтобы переводчику было от чего править.
        /// </summary>
        private static void EnsureLang()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string dir = Path.Combine(Paths.PluginPath, "lang");
                string path = Path.Combine(dir, "lang.tsv");
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (string.IsNullOrEmpty(line) || line[0] == '#') continue;
                        int tab = line.IndexOf('\t');
                        if (tab <= 0 || tab == line.Length - 1) continue;
                        string ru = line.Substring(0, tab);
                        string en = line.Substring(tab + 1);
                        _overrideRu[ru] = ru;
                        _overrideEn[ru] = en;
                    }
                    GameApi.Info($"lang: loaded {path}");
                }
                else
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path,
                        "# RoR2 Sandbox Menu localization: russian<TAB>english.\n" +
                        "# Edit the values to reword either language; they override the built-in pairs.\n");
                    GameApi.Info($"lang: created empty {path}");
                }
            }
            catch (Exception e) { GameApi.WarnOnce("Texts.EnsureLang", e); }
        }

        /// <summary>Дописать недостающую пару в файл (вызывать не нужно: файл правят руками).</summary>
        public static void AppendMissing(string ru, string en)
        {
            try
            {
                string path = Path.Combine(Paths.PluginPath, "lang", "lang.tsv");
                File.AppendAllText(path, ru + "\t" + en + "\n");
            }
            catch (Exception e) { GameApi.WarnOnce("Texts.AppendMissing", e); }
        }
    }
}

