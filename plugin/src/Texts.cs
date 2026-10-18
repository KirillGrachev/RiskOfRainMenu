namespace SandboxMenu
{
    /// <summary>
    /// Строки меню и чат-сообщений: русский и английский рядом, выбор - настройка
    /// General.Language (ru по умолчанию, как в игре у автора).
    ///
    /// Правило оформления (то же, что в других плагинах автора): короткий префикс
    /// из двух цветных букв, разделитель и одна строка сути без воды. В меню -
    /// только подписи действий, никаких абзацев-пояснений: подробности живут в
    /// BepInEx/LogOutput.log и docs/.
    /// </summary>
    internal static class Texts
    {
        public static bool Russian = true;

        public static string T(string ru, string en)
        {
            return Russian ? ru : en;
        }
    }
}
