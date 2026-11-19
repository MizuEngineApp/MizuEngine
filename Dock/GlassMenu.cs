// MizuEngine © 2026 rainy (t.me/MrRainthisisme). Все права защищены. Копировать код и выпускать изменённые версии запрещено — см. LICENSE.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace MizuEngine
{
    /// <summary>Тёмное стеклянное контекстное меню в стиле macOS — для дока и Dynamic Island.</summary>
    internal static class GlassMenu
    {
        // Стили принадлежат потоку, в котором созданы, — у каждого потока свои
        [ThreadStatic] static Style _menu, _item, _sep;

        const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

        static void Ensure()
        {
            if (_menu != null) return;
            _menu = (Style)XamlReader.Parse(
                "<Style " + Ns + " TargetType='ContextMenu'>" +
                "<Setter Property='OverridesDefaultStyle' Value='True'/>" +
                "<Setter Property='HasDropShadow' Value='False'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ContextMenu'>" +
                "<Border Background='#F21B2030' BorderBrush='#45FFFFFF' BorderThickness='1' CornerRadius='12' Padding='5' Margin='8'>" +
                "<Border.Effect><DropShadowEffect BlurRadius='22' ShadowDepth='4' Opacity='0.45'/></Border.Effect>" +
                "<StackPanel IsItemsHost='True'/>" +
                "</Border></ControlTemplate></Setter.Value></Setter></Style>");
            _item = (Style)XamlReader.Parse(
                "<Style " + Ns + " TargetType='MenuItem'>" +
                "<Setter Property='OverridesDefaultStyle' Value='True'/>" +
                "<Setter Property='Foreground' Value='White'/>" +
                "<Setter Property='FontSize' Value='13'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>" +
                "<Border x:Name='Bd' Background='Transparent' CornerRadius='7' Padding='12,6,22,6' MinWidth='190'>" +
                "<ContentPresenter ContentSource='Header' RecognizesAccessKey='False'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='#FF3A7BFF'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            _sep = (Style)XamlReader.Parse(
                "<Style " + Ns + " TargetType='Separator'>" +
                "<Setter Property='OverridesDefaultStyle' Value='True'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'>" +
                "<Border Height='1' Background='#30FFFFFF' Margin='8,4'/>" +
                "</ControlTemplate></Setter.Value></Setter></Style>");
        }

        public static ContextMenu Create()
        {
            Ensure();
            return new ContextMenu { Style = _menu };
        }

        public static MenuItem Item(ContextMenu menu, string text, Action click, bool enabled = true)
        {
            Ensure();
            var mi = new MenuItem { Header = text, Style = _item, IsEnabled = enabled };
            if (click != null) mi.Click += (s, e) => click();
            menu.Items.Add(mi);
            return mi;
        }

        public static void Separator(ContextMenu menu)
        {
            Ensure();
            if (menu.Items.Count > 0 && !(menu.Items[menu.Items.Count - 1] is Separator))
                menu.Items.Add(new Separator { Style = _sep });
        }
    }
}
