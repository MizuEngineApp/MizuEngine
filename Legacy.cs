// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>
    /// Переезд со старого названия программы. Пользователь эти строки не видит: они нужны только для того,
    /// чтобы после переименования сохранились настройки, аккаунты, сборки и автозапуск.
    /// Все старые имена собраны здесь, в одном месте.
    /// </summary>
    internal static class Legacy
    {
        /// <summary>Старое имя: папка в AppData и значение автозапуска в реестре.</summary>
        const string OldName = "NeroEngine";

        /// <summary>Расширение старых файлов сборок — их по-прежнему можно открыть.</summary>
        public const string PresetExt = "*.neropreset";

        /// <summary>Старое имя сборки без названия.</summary>
        public const string PresetDefaultName = OldName + " preset";

        /// <summary>Старый id фиолетового набора курсоров Plus (теперь "mizu").</summary>
        public const string CursorPack = "nero";

        /// <summary>
        /// «Соль» отпечатка ПК. Менять нельзя: сервер привязывает аккаунты к отпечатку,
        /// с новой солью все ПК стали бы «новыми» (сбросился бы лимит аккаунтов и привязка ПК владельца).
        /// </summary>
        public const string DeviceSalt = OldName + "-device-v1|";

        static string OldFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), OldName);

        /// <summary>Переносит %AppData%\NeroEngine в %AppData%\MizuEngine (один раз, при первом запуске новой версии).</summary>
        public static void MigrateDataFolder()
        {
            try
            {
                string oldDir = OldFolder, newDir = Settings.Folder;
                if (!Directory.Exists(oldDir)) return;

                if (!Directory.Exists(newDir))
                {
                    Directory.Move(oldDir, newDir);
                }
                else
                {
                    // Новая папка уже есть (например, туда успел записаться лог) — переносим то, чего в ней нет
                    MergeInto(oldDir, newDir);
                    try { Directory.Delete(oldDir, true); } catch { }
                }

                // В настройках лежат полные пути к своим картинкам, видео и звукам — меняем их на новую папку
                string file = Path.Combine(newDir, "settings.json");
                if (File.Exists(file))
                {
                    string text = File.ReadAllText(file);
                    string fixedText = text.Replace(oldDir.Replace("\\", "\\\\"), newDir.Replace("\\", "\\\\"))
                                           .Replace(oldDir, newDir);
                    if (fixedText != text) File.WriteAllText(file, fixedText);
                }
            }
            catch (Exception e) { Settings.Log(e); }
        }

        static void MergeInto(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Move(f, dest);
            }
            foreach (var d in Directory.GetDirectories(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(d));
                if (!Directory.Exists(dest)) Directory.Move(d, dest);
                else MergeInto(d, dest);
            }
        }

        /// <summary>Старый автозапуск «NeroEngine» заменяем на «MizuEngine» с путём к новому exe.</summary>
        public static void MigrateAutostart(string runKey)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
                if (key?.GetValue(OldName) == null) return;
                key.DeleteValue(OldName, false);
                key.SetValue("MizuEngine", "\"" + Environment.ProcessPath + "\" --tray");
            }
            catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Старый id набора курсоров → новый.</summary>
        public static string FixCursorPack(string id) => id == CursorPack ? "mizu" : id;
    }
}
