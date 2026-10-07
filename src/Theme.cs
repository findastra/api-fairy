using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace ApiFairy
{
    // Fairy-pink shell with a heavy ink outline and mint LCD screens, after Data Dealer's Chip.
    // The XAML here is fixed text; nothing from the scanner or the person's notes is ever parsed as XAML.
    public static class Theme
    {
        public static readonly Color ShellC = C("#FFA9DA"), InkC = C("#1E0A19"), PaleC = C("#FFEAF6"),
            LcdC = C("#CFE5CC"), LcdInkC = C("#1B2A1C"), LcdMutedC = C("#4A664D"), LcdSelC = C("#B5D2B1"),
            DangerC = C("#A3122E"), WarnC = C("#7A4B00"), GoodC = C("#1E6B3A");

        public static readonly Brush Shell = B(ShellC), Ink = B(InkC), Pale = B(PaleC), Lcd = B(LcdC), LcdInk = B(LcdInkC),
            LcdMuted = B(LcdMutedC), LcdSel = B(LcdSelC), Danger = B(DangerC), Warn = B(WarnC), Good = B(GoodC);

        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas, Courier New");
        public static readonly FontFamily Body = new FontFamily("Segoe UI");

        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Focus'>
    <Setter Property='Control.Template'><Setter.Value><ControlTemplate>
      <Rectangle Margin='-3' Stroke='#1E0A19' StrokeThickness='2' StrokeDashArray='2 1' RadiusX='8' RadiusY='8'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType='Button'>
    <Setter Property='Background' Value='#1E0A19'/>
    <Setter Property='Foreground' Value='#FFA9DA'/>
    <Setter Property='FontFamily' Value='Cascadia Mono, Consolas'/>
    <Setter Property='FontWeight' Value='Bold'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Padding' Value='13,8'/>
    <Setter Property='Margin' Value='0,0,6,6'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource Focus}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='16' Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='False'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.82'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.65'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='LcdButton' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Background' Value='#1B2A1C'/>
    <Setter Property='Foreground' Value='#CFE5CC'/>
    <Setter Property='FontSize' Value='11.5'/>
    <Setter Property='Padding' Value='10,6'/>
  </Style>

  <Style x:Key='Tab' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#1E0A19'/>
    <Setter Property='FontFamily' Value='Cascadia Mono, Consolas'/>
    <Setter Property='FontWeight' Value='Bold'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Margin' Value='0,0,8,0'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource Focus}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>
      <Border x:Name='b' Background='Transparent' BorderBrush='#1E0A19' BorderThickness='3' CornerRadius='10' Padding='16,8'>
        <ContentPresenter x:Name='c' HorizontalAlignment='Center' RecognizesAccessKey='False'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'>
          <Setter TargetName='b' Property='Background' Value='#1E0A19'/>
          <Setter Property='Foreground' Value='#FFA9DA'/>
        </Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='Filter' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#1B2A1C'/>
    <Setter Property='FontFamily' Value='Cascadia Mono, Consolas'/>
    <Setter Property='FontWeight' Value='Bold'/>
    <Setter Property='FontSize' Value='11'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Margin' Value='0,0,6,6'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource Focus}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>
      <Border x:Name='b' Background='Transparent' BorderBrush='#1B2A1C' BorderThickness='2' CornerRadius='12' Padding='10,4'>
        <ContentPresenter HorizontalAlignment='Center' RecognizesAccessKey='False'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'>
          <Setter TargetName='b' Property='Background' Value='#1B2A1C'/>
          <Setter Property='Foreground' Value='#CFE5CC'/>
        </Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='Choice' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#1B2A1C'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='Margin' Value='0,0,0,6'/>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#1B2A1C'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='FontSize' Value='13.5'/>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='#F1F8EF'/>
    <Setter Property='Foreground' Value='#1B2A1C'/>
    <Setter Property='BorderBrush' Value='#1B2A1C'/>
    <Setter Property='BorderThickness' Value='2'/>
    <Setter Property='FontFamily' Value='Cascadia Mono, Consolas'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Padding' Value='6,4'/>
    <Setter Property='CaretBrush' Value='#1B2A1C'/>
    <Setter Property='SelectionBrush' Value='#6E8C6F'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
      <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
        <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Auto'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderThickness' Value='3'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='KeyItem' TargetType='ListBoxItem'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
      <Border x:Name='b' Background='Transparent' BorderBrush='Transparent' BorderThickness='4,0,0,0' CornerRadius='6' Padding='8,7' Margin='0,0,0,2'>
        <ContentPresenter/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#C3DCC0'/></Trigger>
        <Trigger Property='IsSelected' Value='True'>
          <Setter TargetName='b' Property='Background' Value='#B5D2B1'/>
          <Setter TargetName='b' Property='BorderBrush' Value='#1B2A1C'/>
        </Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#1B2A1C'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";

        public static ResourceDictionary Load()
        {
            return (ResourceDictionary)XamlReader.Parse(Xaml);
        }
    }
}
