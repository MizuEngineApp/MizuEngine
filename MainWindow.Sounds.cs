// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace MizuEngine
{
    /// <summary>Сообщения: цифра непрочитанных на вкладке «Чаты», уведомления в углу окна, звуки сообщений в «Настройках».</summary>
    public partial class MainWindow
    {
        /// <summary>Открытое главное окно (null — окно закрыто, программа в трее).</summary>
        internal static MainWindow Current { get; private set; }

        /// <summary>Какой чат сейчас открыт на экране (чтобы не присылать уведомление о нём же).</summary>
        internal string ActiveChatKey => _social?.ActiveKey;

        void InitMessages()
        {
            Current = this;
            Inbox.Changed += UpdateChatsBadge;
            Loc.Changed += FillSoundChips;
            Closed += (o, e) =>
            {
                if (Current == this) Current = null;
                Inbox.Changed -= UpdateChatsBadge;
                Loc.Changed -= FillSoundChips;
            };
            Activated += (o, e) => _social?.OnWindowActivated();
            UpdateChatsBadge();
            LoadMessageControls();
        }

        // ================== цифра на «Чатах» ==================

        int _badgeShown;

        void UpdateChatsBadge()
        {
            int n = Inbox.Unread;
            ChatsBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            ChatsBadgeText.Text = n > 99 ? "99+" : n.ToString();
            if (n > _badgeShown && IsLoaded)
            {
                // Новое сообщение — цифра «подпрыгивает»
                var sc = (ScaleTransform)ChatsBadge.RenderTransform;
                Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 520, from: 0.4, ease: Anim.Spring);
                Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 520, from: 0.4, ease: Anim.Spring);
            }
            _badgeShown = n;
        }

        // ================== уведомления в окне ==================

        internal void ShowMessageCard(InboxMessage m) => Notifier.Push(NotifyHost, m);

        /// <summary>Открыть переписку (клик по уведомлению).</summary>
        internal void OpenConversation(string friendId, long groupId)
        {
            _social.OpenAt(friendId, groupId);
            SocialNavigate("chats");
        }

        // ================== звуки сообщений ==================

        void LoadMessageControls()
        {
            MsgSoundsSwitch.IsChecked = _s.MsgSounds;
            MsgNotifySwitch.IsChecked = _s.MsgNotify;
            MsgVolumeSlider.Value = _s.MsgVolume;
            MsgVolumeValue.Text = $"{_s.MsgVolume}%";
            FillSoundChips();
        }

        void MsgOption_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.MsgSounds = MsgSoundsSwitch.IsChecked == true;
            _s.MsgNotify = MsgNotifySwitch.IsChecked == true;
            _s.NotifyPreview = NotifyPreviewSwitch.IsChecked == true;
            _s.NotifyDnd = NotifyDndSwitch.IsChecked == true;
            bool island = NotifyIslandSwitch.IsChecked == true;
            bool islandChanged = island != _s.IslandNotify;
            _s.IslandNotify = island;
            _s.Save();
            if (islandChanged) { IslandNotifySwitch.IsChecked = island; global::MizuEngine.Shell.Island?.Apply(); }
        }

        void MsgVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || MsgVolumeValue == null) return;
            _s.MsgVolume = (int)Math.Round(MsgVolumeSlider.Value);
            MsgVolumeValue.Text = $"{_s.MsgVolume}%";
            _dockSave.Stop();   // сохраняем, когда отпустили ползунок
            _dockSave.Start();
        }

        void FillSoundChips()
        {
            if (MsgInPanel == null) return;
            FillSounds(MsgInPanel, ChatSounds.Incoming, true);
            FillSounds(MsgOutPanel, ChatSounds.Outgoing, false);
        }

        void FillSounds(WrapPanel panel, (string Id, string Key)[] list, bool incoming)
        {
            panel.Children.Clear();
            string current = incoming ? _s.MsgSoundIn : _s.MsgSoundOut;
            string custom = incoming ? _s.MsgCustomIn : _s.MsgCustomOut;
            foreach (var (id, key) in list)
            {
                string sid = id;
                panel.Children.Add(SoundChip(Loc.T(key), current == sid, () => Select(incoming, sid, custom)));
            }
            // Свой звук: выбрать файл (.wav / .mp3); если уже выбран — подписан именем файла
            bool hasCustom = !string.IsNullOrEmpty(custom) && File.Exists(custom);
            string label = hasCustom && current == "custom" ? "★  " + Loc.T("snd.custom") : Loc.T("set.msg.custom");
            panel.Children.Add(SoundChip(label, current == "custom", () => PickCustom(incoming)));
        }

        void Select(bool incoming, string id, string custom)
        {
            if (incoming) _s.MsgSoundIn = id; else _s.MsgSoundOut = id;
            _s.Save();
            ChatSounds.Preview(id, custom);
            FillSoundChips();
        }

        void PickCustom(bool incoming)
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("set.msg.filter") + "|*.wav;*.mp3;*.wma;*.m4a" };
            if (dlg.ShowDialog(this) != true)
            {
                // Отменили, но свой звук уже есть — просто выбираем его
                string had = incoming ? _s.MsgCustomIn : _s.MsgCustomOut;
                if (!string.IsNullOrEmpty(had) && File.Exists(had)) Select(incoming, "custom", had);
                return;
            }
            string path = ChatSounds.ImportCustom(dlg.FileName, incoming);
            if (path == null) { ShowToast(Loc.T("set.msg.custom.bad")); return; }
            if (incoming) _s.MsgCustomIn = path; else _s.MsgCustomOut = path;
            Select(incoming, "custom", path);
        }

        FrameworkElement SoundChip(string text, bool selected, Action click)
        {
            var t = new TextBlock { Text = text, FontSize = 13, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 8, 8),
                BorderThickness = new Thickness(1), Child = t, Cursor = Cursors.Hand
            };
            if (selected)
            {
                chip.SetResourceReference(Border.BackgroundProperty, "Accent");
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255));
                t.Foreground = Brushes.White;
            }
            else
            {
                chip.SetResourceReference(Border.BackgroundProperty, "CardBg");
                chip.SetResourceReference(Border.BorderBrushProperty, "CardRim");
                t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            }
            chip.MouseEnter += (s, e) => chip.Opacity = 0.85;
            chip.MouseLeave += (s, e) => chip.Opacity = 1;
            chip.MouseLeftButtonUp += (s, e) => click();
            return chip;
        }
    }
}
