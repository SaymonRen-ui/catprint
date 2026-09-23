using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using CatPrint.Imaging;

namespace CatPrint
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    /// <summary>
    /// Настройки в %AppData%/CatPrint/settings.json
    /// </summary>
    public sealed class AppSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.Light;
        public ulong? LastDeviceAddress { get; set; }
        public string LastDeviceName { get; set; } = string.Empty;
        public byte Intensity { get; set; } = 0x5D;
        /// <summary>Автоплотность: снижать жар на плотной заливке.</summary>
        public bool AutoDensity { get; set; } = true;
        /// <summary>Адаптивный жар: модуляция A2 по группам строк.</summary>
        public bool AdaptiveHeat { get; set; } = true;
        public PrintBitOrder BitOrder { get; set; } = PrintBitOrder.LsbFirst;
        /// <summary>Пауза между строками растра, мс (рабочая — 15)</summary>
        public int LineDelayMs { get; set; } = 15;
        /// <summary>Строк в блоке непрерывной отправки</summary>
        public int BlockLines { get; set; } = 40;
        /// <summary>Пауза между блоками для дренажа буфера принтера, мс</summary>
        public int BlockPauseMs { get; set; } = 500;
        /// <summary>Делить задание на части (по умолчанию выкл — как в рабочем)</summary>
        public bool SplitEnabled { get; set; } = false;
        /// <summary>Максимум строк в одном задании</summary>
        public int SplitLines { get; set; } = 96;
        /// <summary>Пауза между частями задания, мс</summary>
        public int SplitPauseMs { get; set; } = 1500;
        /// <summary>Часто используемые шрифты (сверху списка)</summary>
        public List<string> RecentFonts { get; set; } = new();
        /// <summary>Недавние эмодзи (вкладка в пикере)</summary>
        public List<string> RecentEmojis { get; set; } = new();
        public int SettingsVersion { get; set; } = 5;

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CatPrint");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        // Миграции. Трогаем только значения по умолчанию
                        // из старых версий, ручные настройки не затираем.
                        bool migrated = false;

                        if (s.SettingsVersion < 1)
                        {
                            if (s.LineDelayMs == 25)
                                s.LineDelayMs = 15;
                            s.SettingsVersion = 1;
                            migrated = true;
                        }
                        if (s.SettingsVersion < 2)
                        {
                            s.SettingsVersion = 2;
                            migrated = true;
                        }
                        if (s.SettingsVersion < 3)
                        {
                            // Плотность вне 0x30..0x7A не проверялась
                            // и может вешать принтер — возвращаем 0x5D.
                            if (s.Intensity < 0x30 || s.Intensity > 0x7A)
                                s.Intensity = 0x5D;
                            s.SettingsVersion = 3;
                            migrated = true;
                        }
                        if (s.SettingsVersion < 4)
                        {
                            // Короче пачки + длиннее паузы против
                            // затухания печати (просадка питания).
                            if (s.SplitLines == 120)
                                s.SplitLines = 96;
                            if (s.SplitPauseMs == 1000)
                                s.SplitPauseMs = 1500;
                            s.SettingsVersion = 4;
                            migrated = true;
                        }
                        if (s.SettingsVersion < 5)
                        {
                            // Возврат к рабочей схеме: ровный поток 15мс,
                            // без блочных пауз и нарезки (по умолчанию).
                            if (s.LineDelayMs == 35)
                                s.LineDelayMs = 15;
                            if (s.BlockPauseMs == 500)
                                s.BlockPauseMs = 0;
                            s.SplitEnabled = false;
                            s.SettingsVersion = 5;
                            migrated = true;
                        }
                        if (migrated)
                            s.Save();
                        return s;
                    }
                }
            }
            catch { }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}
