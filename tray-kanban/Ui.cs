using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TrayKanban
{
    static class Ui
    {
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public const string GAdd = "", GPin = "", GPinned = "", GSettings = "", GAttach = "",
            GLink = "", GCalendar = "", GCheck = "", GDelete = "", GSearch = "", GPhoto = "",
            GMore = "", GClose = "", GPaste = "", GOpen = "", GBoard = "", GDoc = "";

        // Label colours offered on cards (key stored in JSON).
        public static readonly Dictionary<string, Color> Labels = new Dictionary<string, Color>
        {
            { "azure", Hex("#0078D4") }, { "teal", Hex("#00A3AE") }, { "green", Hex("#2EA043") },
            { "amber", Hex("#E8A100") }, { "red", Hex("#E74856") }, { "purple", Hex("#8764B8") }, { "grey", Hex("#8A8A8A") },
        };

        public static Color Hex(string s) { return (Color)ColorConverter.ConvertFromString(s); }

        public static void ApplyAzure(ResourceDictionary r)
        {
            // Azure accent regardless of the system accent colour.
            r["SystemAccentColor"] = Hex("#0078D4");
            r["SystemAccentColorLight1"] = Hex("#2B8FE0");
            r["SystemAccentColorLight2"] = Hex("#5CB2F2");
            r["SystemAccentColorLight3"] = Hex("#99D0F7");
            r["SystemAccentColorDark1"] = Hex("#0067C0");
            r["SystemAccentColorDark2"] = Hex("#00509A");
            r["SystemAccentColorDark3"] = Hex("#003C75");
            bool dark = Native.IsDark;
            r["AccentFillColorDefaultBrush"] = Frozen(dark ? "#5CB2F2" : "#0067C0");
            r["AccentFillColorSecondaryBrush"] = Frozen(dark ? "#E65CB2F2" : "#E60067C0");
            r["AccentFillColorTertiaryBrush"] = Frozen(dark ? "#CC5CB2F2" : "#CC0067C0");
            r["AccentTextFillColorPrimaryBrush"] = Frozen(dark ? "#99D0F7" : "#00509A");
            r["AccentTextFillColorSecondaryBrush"] = Frozen(dark ? "#99D0F7" : "#003C75");
            r["Azure"] = Frozen(dark ? "#5CB2F2" : "#0078D4");
        }

        static SolidColorBrush Frozen(string hex) { var b = new SolidColorBrush(Hex(hex)); b.Freeze(); return b; }

        const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Subtle' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource TextFillColorPrimaryBrush}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Padding' Value='8,5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource SubtleFillColorSecondaryBrush}'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource SubtleFillColorTertiaryBrush}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Pill' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource TextFillColorSecondaryBrush}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Margin' Value='0,0,4,0'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='RadioButton'>
        <Border x:Name='b' Background='Transparent' CornerRadius='15' Padding='14,6'>
          <ContentPresenter VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource SubtleFillColorSecondaryBrush}'/></Trigger>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='b' Property='Background' Value='{DynamicResource AccentFillColorDefaultBrush}'/>
            <Setter Property='Foreground' Value='{DynamicResource TextOnAccentFillColorPrimaryBrush}'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Card' TargetType='Border'>
    <Setter Property='Background' Value='{DynamicResource CardBackgroundFillColorDefaultBrush}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource CardStrokeColorDefaultBrush}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='CornerRadius' Value='8'/>
    <Setter Property='Margin' Value='0,0,0,8'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='BorderBrush' Value='{DynamicResource Azure}'/></Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";

        public static ResourceDictionary Styles() { return (ResourceDictionary)XamlReader.Parse(StylesXaml); }

        public static T Res<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement
        {
            e.SetResourceReference(p, key);
            return e;
        }

        public static TextBlock Glyph(string g, double size = 14)
        {
            return new TextBlock { Text = g, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        }

        public static TextBlock Text(string s, double size = 13, FontWeight? weight = null, string brush = "TextFillColorPrimaryBrush")
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            return t.Res(TextBlock.ForegroundProperty, brush);
        }

        public static Button IconButton(string glyph, string tip, Action onClick, string label = null)
        {
            object content = Glyph(glyph);
            if (label != null)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(Glyph(glyph, 13));
                sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                content = sp;
            }
            var b = new Button { Content = content, ToolTip = tip };
            b.SetResourceReference(FrameworkElement.StyleProperty, "Subtle");
            b.Click += delegate { onClick(); };
            return b;
        }

        public static Button Accent(string label, Action onClick)
        {
            var b = new Button { Content = label, Padding = new Thickness(16, 6, 16, 6), MinWidth = 80 };
            b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
            b.Click += delegate { onClick(); };
            return b;
        }

        public static Button Plain(string label, Action onClick)
        {
            var b = new Button { Content = label, Padding = new Thickness(14, 6, 14, 6) };
            b.Click += delegate { onClick(); };
            return b;
        }

        public static MenuItem Item(string header, Action onClick, string glyph = null)
        {
            var m = new MenuItem { Header = header };
            if (glyph != null) m.Icon = Glyph(glyph);
            m.Click += delegate { onClick(); };
            return m;
        }

        // TextBox with a grey placeholder that hides while there is text or focus.
        public static Grid Hinted(TextBox box, string hint)
        {
            var g = new Grid();
            var h = Text(hint, box.FontSize, null, "TextFillColorTertiaryBrush");
            h.IsHitTestVisible = false; h.Margin = new Thickness(12, 0, 0, 0); h.TextWrapping = TextWrapping.NoWrap;
            h.VerticalAlignment = box.AcceptsReturn ? VerticalAlignment.Top : VerticalAlignment.Center;
            if (box.AcceptsReturn) h.Margin = new Thickness(12, 7, 0, 0);
            Action sync = delegate { h.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed; };
            box.TextChanged += delegate { sync(); };
            sync();
            g.Children.Add(box); g.Children.Add(h);
            return g;
        }

        public static BitmapImage Thumb(string path, int decodeWidth)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad; // read now so the file isn't kept locked
                    bi.DecodePixelWidth = decodeWidth;
                    bi.StreamSource = fs;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { return null; }
        }

        public static ImageSource AppIcon()
        {
            using (var s = typeof(Ui).Assembly.GetManifestResourceStream("app.ico"))
                return BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }

        public static void Open(string target)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Couldn't open " + target + "\n\n" + ex.Message, "Tray Kanban"); }
        }

        public static string Ago(DateTime utc)
        {
            var d = DateTime.UtcNow - utc;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "m ago";
            if (d.TotalDays < 1) return (int)d.TotalHours + "h ago";
            if (d.TotalDays < 30) return (int)d.TotalDays + "d ago";
            return utc.ToLocalTime().ToString("d MMM yyyy");
        }
    }
}
