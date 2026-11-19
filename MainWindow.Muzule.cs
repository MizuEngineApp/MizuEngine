// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System.Windows.Controls;

namespace MizuEngine
{
    /// <summary>Вкладка Muzule в главном окне и то, что агенту нужно от окна (перейти в раздел, перекрасить окно).</summary>
    public partial class MainWindow
    {
        MuzulePage _muzulePage;

        void InitMuzule()
        {
            _muzulePage = new MuzulePage(PageMuzule, _s, ShowToast);
            Plus.Changed += OnMuzulePlusChanged;
            Closed += (o, e) =>
            {
                Plus.Changed -= OnMuzulePlusChanged;
                _muzulePage.Detach();
            };
        }

        void OnMuzulePlusChanged() => _muzulePage.Refresh();

        /// <summary>Открыть раздел по имени пункта меню (NavSnow, NavDock, NavMuzule…).</summary>
        internal void MuzuleOpenNav(string nav)
        {
            if (FindName(nav) is not RadioButton rb) return;
            if (rb.IsChecked == true) OnPageShown(nav);
            else rb.IsChecked = true;
        }

        /// <summary>Открыть Muzule прямо на этом чате (клик по уведомлению или Ctrl+O в быстром окне).</summary>
        internal void MuzuleShowChat(string chatId)
        {
            _muzulePage.OpenChat(chatId);
            if (NavMuzule.IsChecked == true) _muzulePage.Show();
            else NavMuzule.IsChecked = true;
        }

        /// <summary>Агент сменил тему — плавный переход, как при выборе темы руками.</summary>
        internal void MuzuleReapplyTheme() => ReapplyThemeOnly();

        /// <summary>Агент применил сборку — перечитываем все настройки и переключатели.</summary>
        internal void MuzuleReapplyAll() => ReapplyAll();
    }
}
