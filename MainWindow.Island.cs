// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MizuEngine
{
    /// <summary>Вкладка «Dynamic Island» (форма, прыжок, живой предпросмотр) и блок «Заряд устройств» на странице «Док».</summary>
    public partial class MainWindow
    {
        IslandPreview _islandPreview;
        DispatcherTimer _islandSave;
        bool _battHooked;

        void LoadIslandAndBattery()
        {
            IslandWSlider.Value = _s.IslandWidth;
            IslandHSlider.Value = _s.IslandHeight;
            IslandRSlider.Value = _s.IslandRound;
            IslandBSlider.Value = _s.IslandBounce;
            IslandSSlider.Value = _s.IslandSpring;
            UpdateIslandLabels();

            BattDockSwitch.IsChecked = _s.BattDock;
            BattTraySwitch.IsChecked = _s.BattTray;
            BattHpSwitch.IsChecked = _s.BattHeadphones;
            BattMouseSwitch.IsChecked = _s.BattMouse;
            BattKbSwitch.IsChecked = _s.BattKeyboard;
            BattWarnSwitch.IsChecked = _s.BattWarn;
            BattWarnSoundSwitch.IsChecked = _s.BattWarnSound;
            BattWarnAtSlider.Value = _s.BattWarnAt;
            BattWarnLowSlider.Value = _s.BattWarnLow;
            UpdateWarnLabels();
            IslandLayerTop.IsChecked = _s.IslandLayer == "top";
            IslandLayerAlways.IsChecked = _s.IslandLayer == "always";
            IslandLayerDesktop.IsChecked = _s.IslandLayer == "desktop";
            UpdateLayerHint();
            if (!_battHooked)
            {
                _battHooked = true;
                DeviceBattery.Changed += () => Dispatcher.BeginInvoke(new Action(FillBattList));
                Loc.Changed += () => { UpdateIslandLabels(); FillBattList(); UpdateLayerHint(); UpdateWarnLabels(); };
            }
        }

        void ShowIslandPage()
        {
            if (_islandPreview == null)
            {
                _islandPreview = new IslandPreview(_s);
                IslandPreviewHost.Child = _islandPreview;
            }
            _islandPreview.Apply();
        }

        void UpdateIslandLabels()
        {
            if (IslandWValue == null) return;
            IslandWValue.Text = $"{(int)IslandWSlider.Value} px";
            IslandHValue.Text = $"{(int)IslandHSlider.Value} px";
            IslandRValue.Text = $"{(int)IslandRSlider.Value}%";
            IslandBValue.Text = IslandBSlider.Value < 1 ? Loc.T("island.off") : $"{(int)IslandBSlider.Value}%";
            IslandSValue.Text = $"{(int)IslandSSlider.Value}%";
        }

        void IslandSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || IslandWValue == null) return;
            _s.IslandWidth = (int)Math.Round(IslandWSlider.Value);
            _s.IslandHeight = (int)Math.Round(IslandHSlider.Value);
            _s.IslandRound = (int)Math.Round(IslandRSlider.Value);
            _s.IslandBounce = (int)Math.Round(IslandBSlider.Value);
            _s.IslandSpring = (int)Math.Round(IslandSSlider.Value);
            UpdateIslandLabels();
            _islandPreview?.Apply();
            global::MizuEngine.Shell.Island?.Reshape();   // настоящая капсула меняется сразу, пока тянешь ползунок
            // Сохраняем не на каждый шаг ползунка, а когда его отпустили
            if (_islandSave == null)
            {
                _islandSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _islandSave.Tick += (o, a) => { _islandSave.Stop(); _s.Save(); };
            }
            _islandSave.Stop();
            _islandSave.Start();
        }

        void IslandTest_Click(object sender, RoutedEventArgs e)
        {
            _islandPreview?.Test();
            global::MizuEngine.Shell.Island?.TestBounce();
        }

        void IslandReset_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            IslandWSlider.Value = 126; IslandHSlider.Value = 32; IslandRSlider.Value = 100;
            IslandBSlider.Value = 35; IslandSSlider.Value = 32;
            _loading = false;
            IslandSlider_Changed(null, null);
        }

        // ================== когда видна капсула ==================

        void IslandLayer_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.IslandLayer = IslandLayerAlways.IsChecked == true ? "always" : IslandLayerDesktop.IsChecked == true ? "desktop" : "top";
            _s.Save();
            UpdateLayerHint();
            global::MizuEngine.Shell.Island?.Apply();
        }

        void UpdateLayerHint()
        {
            if (IslandLayerHint == null) return;
            IslandLayerHint.Text = Loc.T(_s.IslandLayer == "always" ? "island.layer.always.d" : _s.IslandLayer == "desktop" ? "island.layer.desktop.d" : "island.layer.top.d");
        }

        // ================== предупреждение о низком заряде ==================

        void BattWarn_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.BattWarn = BattWarnSwitch.IsChecked == true;
            _s.BattWarnSound = BattWarnSoundSwitch.IsChecked == true;
            _s.Save();
            UpdateWarnLabels();
        }

        void BattWarnSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || BattWarnAtValue == null) return;
            _s.BattWarnAt = (int)Math.Round(BattWarnAtSlider.Value);
            // Второй порог всегда ниже первого
            if (BattWarnLowSlider.Value >= _s.BattWarnAt) BattWarnLowSlider.Value = Math.Max(1, _s.BattWarnAt - 1);
            _s.BattWarnLow = (int)Math.Round(BattWarnLowSlider.Value);
            UpdateWarnLabels();
            if (_islandSave == null)
            {
                _islandSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _islandSave.Tick += (o, a) => { _islandSave.Stop(); _s.Save(); };
            }
            _islandSave.Stop();
            _islandSave.Start();
        }

        void UpdateWarnLabels()
        {
            if (BattWarnAtValue == null) return;
            BattWarnAtValue.Text = $"{(int)BattWarnAtSlider.Value}%";
            BattWarnLowValue.Text = $"{(int)BattWarnLowSlider.Value}%";
            BattWarnBox.IsEnabled = _s.BattWarn;
            BattWarnBox.Opacity = _s.BattWarn ? 1 : 0.45;
        }

        void BattWarnTest_Click(object sender, RoutedEventArgs e) => DeviceBattery.TestWarning();

        // ================== заряд устройств ==================

        void BattOption_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.BattDock = BattDockSwitch.IsChecked == true;
            _s.BattTray = BattTraySwitch.IsChecked == true;
            _s.BattHeadphones = BattHpSwitch.IsChecked == true;
            _s.BattMouse = BattMouseSwitch.IsChecked == true;
            _s.BattKeyboard = BattKbSwitch.IsChecked == true;
            _s.Save();
            BatteryTray.Instance?.Sync();
            global::MizuEngine.Shell.Run(() => global::MizuEngine.Shell.Dock?.RefreshAll());
            DeviceBattery.PollSoon();
        }

        /// <summary>Список найденных приборов под переключателями: «Наушники — AirPods Pro · 82%».</summary>
        void FillBattList()
        {
            if (BattList == null) return;
            BattList.Children.Clear();
            var all = DeviceBattery.All;
            if (all.Count == 0)
            {
                BattList.Children.Add(new TextBlock { Text = Loc.T("batt.none"), Opacity = 0.6, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 2) });
                return;
            }
            foreach (var d in all)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var val = new TextBlock { Text = d.FromApp ? d.State + " · " + Loc.T("batt.fromapp") : d.State, Opacity = 0.75, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(val, System.Windows.Controls.Dock.Right);
                row.Children.Add(val);
                if (d.NeedsApp)
                {
                    // Мышь на приёмнике без программы бренда: процент даст только она — предлагаем скачать
                    string url = d.AppUrl;
                    var get = new Button
                    {
                        Style = (Style)FindResource("GlassButton"),
                        Content = string.Format(Loc.T("batt.app.get"), string.IsNullOrEmpty(d.AppBrand) ? d.Name : d.AppBrand),
                        Margin = new Thickness(8, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3), FontSize = 12
                    };
                    get.Click += (o, a) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
                    DockPanel.SetDock(get, System.Windows.Controls.Dock.Right);
                    row.Children.Add(get);
                }
                var icon = new TextBlock { Text = DeviceBattery.Glyph(d.Kind), FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"), FontSize = 14, Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(icon);
                row.Children.Add(new TextBlock { Text = DeviceBattery.KindName(d.Kind) + (string.IsNullOrEmpty(d.Name) ? "" : " — " + d.Name), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
                BattList.Children.Add(row);
            }
        }
    }
}
