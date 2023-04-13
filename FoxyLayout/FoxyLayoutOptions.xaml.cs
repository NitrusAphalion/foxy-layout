using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls.WpfPropertyGrid;
using System.Windows.Media;
using Serilog.Events;
using static FoxyLayout.FoxyUtilities;
using System.Xml.Serialization;
using System.Windows.Input;
using System.IO;

namespace FoxyLayout
{
    /// <summary>
    /// Interaction logic for FoxyLayoutOptions.xaml
    /// </summary>
    internal sealed partial class FoxyLayoutOptions : NTWindow
    {
        public FoxyLayoutOptions()
        {
            InitializeComponent();
            Loaded += (o, e) =>
            {
                propertyGrid.SelectedObject = FoxyUtilities.Options.Clone();
                foreach (CategoryItem categoryItem in propertyGrid.Categories)
                    categoryItem.IsExpanded = true;
            };
            
        }

        public FoxyLayoutOptions(Window owner) : this()
        {
            Owner = owner;
        }

        private void cancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void applyButton_OnClick(object sender, RoutedEventArgs e)
        {
            FoxyUtilities.Options = (propertyGrid.SelectedObject as Options).Clone() as Options;
            Close();
        }
    }

    [CategoryOrder("Hotkeys",   0)]
    [CategoryOrder("Misc",      1)]
    [CategoryOrder("Visual",    2)]
    [CategoryOrder("Logging",   3)]
    public sealed class Options : ICloneable
    {
        private ModifierKeyName modifierKey = ModifierKeyName.ALT;

        public Options()
        {
        }

        [Display(Name = "Modifier Disables", GroupName = "Hotkeys", Order = 0)]
        public bool ModifierDisables { get; set; } = true;

        [Display(Name = "Modifier key", GroupName = "Hotkeys", Order = 1)]
        public ModifierKeyName ModifierKey
        {
            get => modifierKey;
            set
            {
                modifierKey = value;
                switch (modifierKey)
                {
                    case ModifierKeyName.ALT:
                        ModifierVirtualKey = 0x12;
                        break;
                    case ModifierKeyName.CTRL:
                        ModifierVirtualKey = 0x11;
                        break;
                    case ModifierKeyName.SHIFT:
                        ModifierVirtualKey = 0x10;
                        break;
                    default:
                        ModifierVirtualKey = int.MinValue;
                        break;
                }
            }
        }

        [Display(Name = "Bring to front", GroupName = "Misc", Order = 0, Description = "Bring all NT windows on screen to front when window is accessed")]
        public bool BringToFront { get; set; } = true;

        [TypeConverter(typeof(StrokeConverter))]
        [Display(Name ="Split Stroke", GroupName = "Visual", Order = 0)]
        public Stroke SplitStroke { get; set; } = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 5f, 50);

        [TypeConverter(typeof(StrokeConverter))]
        [Display(Name = "Cancel Stroke", GroupName = "Visual", Order = 1)]
        public Stroke CancelStroke { get; set; } = new Stroke(Brushes.Firebrick, DashStyleHelper.Dash, 5f, 50);

        [Display(Name = "Log Level", GroupName = "Logging", Order = 0)]
        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Warning;

        [XmlIgnore]
        [Browsable(false)]
        public int ModifierVirtualKey { get; set; }

        public object Clone()
        {
            Options clone       = MemberwiseClone() as Options;
            clone.SplitStroke   = SplitStroke.Clone() as Stroke;
            clone.CancelStroke  = CancelStroke.Clone() as Stroke;
            return clone;
        }
    }
}
