using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace LyricConnector
{
    internal sealed class CandidateRowView : Grid
    {
        public CandidateRowView()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            var names = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var title = new Run();
            title.SetBinding(Run.TextProperty, new Binding("Record.trackName"));
            names.Inlines.Add(title);
            names.Inlines.Add(new Run("   "));
            var artist = new Run { FontSize = 11, Foreground = Brush("#71647E") };
            artist.SetBinding(Run.TextProperty, new Binding("Record.artistName"));
            names.Inlines.Add(artist);
            names.Inlines.Add(new Run("   "));
            var album = new Run { FontSize = 11, Foreground = Brush("#81758D") };
            album.SetBinding(Run.TextProperty, new Binding("Record.albumName"));
            names.Inlines.Add(album);
            Children.Add(names);
            var duration = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            duration.SetBinding(TextBlock.TextProperty, Field("duration"));
            Grid.SetColumn(duration, 1);
            Children.Add(duration);
            var provider = new TextBlock { FontSize = 10.5, Foreground = Brush("#67517E"), TextAlignment = TextAlignment.Center };
            provider.SetBinding(TextBlock.TextProperty, new Binding("Record.Source"));
            provider.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(provider, 2);
            Children.Add(provider);
            var timing = new TextBlock { FontSize = 11, Foreground = Brush("#71647E"), TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            timing.SetBinding(TextBlock.TextProperty, Field("kind"));
            Grid.SetColumn(timing, 3);
            Children.Add(timing);
            var current = new TextBlock { Text = "使用中", FontSize = 11, Foreground = Brush("#67517E"), TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            current.SetBinding(UIElement.VisibilityProperty, Field("current"));
            Grid.SetColumn(current, 4);
            Children.Add(current);
        }

        private MultiBinding Field(string name)
        {
            var binding = new MultiBinding { Converter = new CandidateFieldConverter(), ConverterParameter = name };
            binding.Bindings.Add(new Binding("Record"));
            binding.Bindings.Add(new Binding("Tag") { Source = this });
            return binding;
        }

        private static SolidColorBrush Brush(string value)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value);
            brush.Freeze();
            return brush;
        }
    }

    internal static class ControlTheme
    {
        private static readonly ResourceDictionary resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Input' TargetType='{x:Type TextBox}'>
    <Setter Property='SelectionBrush' Value='#CDB7E0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type TextBox}'>
          <Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='3'>
            <ScrollViewer x:Name='PART_ContentHost' Focusable='False'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocusWithin' Value='True'>
              <Setter TargetName='surface' Property='BorderBrush' Value='#8F6BAC'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='surface' Property='Opacity' Value='0.65'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Select' TargetType='{x:Type ComboBox}'>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
    <Setter Property='ScrollViewer.VerticalScrollBarVisibility' Value='Auto'/>
    <Setter Property='ItemContainerStyle'>
      <Setter.Value>
        <Style TargetType='{x:Type ComboBoxItem}'>
          <Setter Property='Padding' Value='10,8'/>
          <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
          <Setter Property='Template'>
            <Setter.Value>
              <ControlTemplate TargetType='{x:Type ComboBoxItem}'>
                <Border x:Name='item' Padding='{TemplateBinding Padding}' Background='Transparent' CornerRadius='4'>
                  <ContentPresenter/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsHighlighted' Value='True'>
                    <Setter TargetName='item' Property='Background' Value='#EEE6F6'/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
      </Setter.Value>
    </Setter>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ComboBox}'>
          <Grid>
            <ToggleButton x:Name='toggle' Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='{x:Type ToggleButton}'>
                  <Border x:Name='selectSurface' Background='{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}'
                          BorderBrush='#CFC3DB' BorderThickness='1' CornerRadius='3'>
                    <Path Data='M 0 0 L 4 4 L 8 0' Stroke='#44354F' StrokeThickness='1.3' Width='8' Height='4'
                          HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,13,0'/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsChecked' Value='True'>
                      <Setter TargetName='selectSurface' Property='BorderBrush' Value='#8F6BAC'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter x:Name='selection' Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                              ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'
                              Margin='11,0,34,0' VerticalAlignment='Center' IsHitTestVisible='False'/>
            <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}'
                   AllowsTransparency='True' Focusable='False' PopupAnimation='None'>
              <Border Background='#FFFEFF' BorderBrush='#CFC3DB' BorderThickness='1' CornerRadius='4' Padding='4'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                      MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                <ScrollViewer CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'>
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Switch' TargetType='{x:Type ToggleButton}'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Foreground' Value='#272235'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='MinHeight' Value='24'/>
    <Setter Property='HorizontalContentAlignment' Value='Left'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ToggleButton}'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='34'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
            <Border x:Name='rail' Width='32' Height='18' CornerRadius='9' Background='#D8D2DE'
                    BorderBrush='#BEB4C9' BorderThickness='1' VerticalAlignment='Center'>
              <Ellipse x:Name='knob' Width='14' Height='14' Fill='White' HorizontalAlignment='Left' Margin='1'/>
            </Border>
            <ContentPresenter Grid.Column='1' Margin='6,0,0,0' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='rail' Property='Background' Value='#77528F'/>
              <Setter TargetName='rail' Property='BorderBrush' Value='#77528F'/>
              <Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='rail' Property='BorderBrush' Value='#8E759F'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='rail' Property='BorderBrush' Value='#493257'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='rail' Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='VerticalScrollBar' TargetType='{x:Type ScrollBar}'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ScrollBar}'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'
                   Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
                   Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'>
                  <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                </RepeatButton>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb MinHeight='28' Width='5'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='{x:Type Thumb}'>
                      <Border x:Name='thumb' Background='#CFC4DC' CornerRadius='2.5'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='thumb' Property='Background' Value='#AB92BF'/></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'>
                  <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                </RepeatButton>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <ControlTemplate x:Key='PlaybackProgress' TargetType='{x:Type ProgressBar}'>
    <Grid x:Name='PART_Track'>
      <Border Background='{TemplateBinding Background}' CornerRadius='2.5'/>
      <Border x:Name='PART_Indicator' HorizontalAlignment='Left'
              Background='{TemplateBinding Foreground}' CornerRadius='2.5'/>
    </Grid>
  </ControlTemplate>

  <ControlTemplate x:Key='Candidates' TargetType='{x:Type ListBox}'>
    <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
            BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='0' Padding='0'>
      <ScrollViewer Focusable='False' CanContentScroll='False' HorizontalScrollBarVisibility='Disabled'
                    VerticalScrollBarVisibility='{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}'>
        <ItemsPresenter/>
      </ScrollViewer>
    </Border>
  </ControlTemplate>
</ResourceDictionary>");

        public static Style Input { get { return (Style)resources["Input"]; } }
        public static Style Select { get { return (Style)resources["Select"]; } }
        public static Style Switch { get { return (Style)resources["Switch"]; } }
        public static Style VerticalScrollBar { get { return (Style)resources["VerticalScrollBar"]; } }
        public static ControlTemplate Candidates { get { return (ControlTemplate)resources["Candidates"]; } }
        public static ControlTemplate PlaybackProgress { get { return (ControlTemplate)resources["PlaybackProgress"]; } }

        public static Geometry Glyph(string name)
        {
            string path;
            switch (name)
            {
                case "search": path = "M 11 11 L 16 16 M 12 7 A 5 5 0 1 1 2 7 A 5 5 0 1 1 12 7"; break;
                case "import": path = "M 8 12 L 8 1 M 4 5 L 8 1 L 12 5 M 2 10 L 2 16 L 14 16 L 14 10"; break;
                case "refresh": path = "M 14 5 A 6 6 0 1 0 14 12 M 14 1 L 14 5 L 10 5"; break;
                case "connect": path = "M 4 3 L 4 7 M 11 3 L 11 7 M 2 7 L 13 7 L 13 10 A 5.5 5.5 0 0 1 2 10 Z M 7.5 15.5 L 7.5 18"; break;
                case "disconnect": path = "M 9 2 L 2 2 L 2 16 L 9 16 M 7 9 L 17 9 M 13 5 L 17 9 L 13 13"; break;
                case "music": path = "M 6 14 L 6 3 L 15 1 L 15 12 M 6 14 A 2.6 2 0 1 1 1 14 A 2.6 2 0 1 1 6 14 M 15 12 A 2.6 2 0 1 1 10 12 A 2.6 2 0 1 1 15 12 M 6 6 L 15 4"; break;
                case "phone": path = "M 4 1 L 13 1 Q 14 1 14 2 L 14 17 Q 14 18 13 18 L 4 18 Q 3 18 3 17 L 3 2 Q 3 1 4 1 Z M 7 15 L 10 15"; break;
                case "bluetooth": path = "M 7 1 L 13 6 L 2 14 M 7 1 L 7 17 L 13 12 L 2 4"; break;
                case "minimize": path = "M 2 9 L 14 9"; break;
                case "maximize": path = "M 3 3 L 13 3 L 13 13 L 3 13 Z"; break;
                case "restore": path = "M 6 3 L 14 3 L 14 11 M 3 6 L 11 6 L 11 14 L 3 14 Z"; break;
                case "close": path = "M 3 3 L 13 13 M 13 3 L 3 13"; break;
                case "palette": path = "M 2 2 L 7 2 L 7 7 L 2 7 Z M 10 2 L 15 2 L 15 7 L 10 7 Z M 2 10 L 7 10 L 7 15 L 2 15 Z M 10 10 L 15 10 L 15 15 L 10 15 Z"; break;
                default: throw new System.ArgumentException("Unknown control glyph: " + name);
            }
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            return geometry;
        }
    }
}
