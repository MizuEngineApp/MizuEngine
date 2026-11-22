// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;

namespace MizuEngine
{
    /// <summary>Сборки (0.11.5): после применения сборки заново включаем всё оформление, открытие «Лучших сборок».</summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Сборка применена к настройкам — перечитываем ВСЁ: тему и живой фон окна, курсор, эффекты, док (вкл/выкл и вид),
        /// Dynamic Island, живые обои, анимации интерфейса, звуки, виджеты и рамки окон.
        /// </summary>
        internal void ApplyBuildEffects()
        {
            Motion.Style = _s.UiAnim;
            Motion.Speed = _s.UiAnimSpeed;
            UiSounds.On = _s.Sounds;
            UiSounds.Pack = _s.SoundPack;
            UiSounds.Volume = _s.UiVolume;

            ReapplyAll();   // тема, курсор, эффекты, вид дока, виджеты и рамки
            bool dock = _s.DockEnabled;
            _fx.Run(() => _fx.Dock.SetEnabled(dock));
            try { global::MizuEngine.Shell.Island?.Apply(); } catch (Exception e) { Settings.Log(e); }
            try { LiveWallpaper.Sync(_s); } catch (Exception e) { Settings.Log(e); }
            SetVideoDim(_s.VideoDim);
            SetLiveSpeed(_s.LiveBgSpeed);
            try { _custom.Refresh(); } catch (Exception e) { Settings.Log(e); }
        }

        /// <summary>Открыть «Лучшие сборки»; id &gt; 0 — сразу эту опубликованную сборку.</summary>
        void OpenSharedBuild(long id)
        {
            _pendingShared = id;
            if (NavTopBuilds.IsChecked == true) OnPageShown("NavTopBuilds");
            else NavTopBuilds.IsChecked = true;
        }
    }
}
