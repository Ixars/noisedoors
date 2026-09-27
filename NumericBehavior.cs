using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Noise
{
    public static class NumericBehavior
    {
        [AttachedPropertyBrowsableForType(typeof(TextBox))]
        [Category("NumericBehavior")]
        [Description("Restricts TextBox input to numeric values only")]
        [DefaultValue(false)]
        public static bool GetNumericOnly(DependencyObject obj) => (bool)obj.GetValue(NumericOnlyProperty);
        public static void SetNumericOnly(DependencyObject obj, bool value) => obj.SetValue(NumericOnlyProperty, value);

        public static readonly DependencyProperty NumericOnlyProperty = DependencyProperty.RegisterAttached(
            "NumericOnly",
            typeof(bool),
            typeof(NumericBehavior),
            new UIPropertyMetadata(false, (d, e) =>
            {
                TextBox tb = d as TextBox;
                if ((bool)e.NewValue) tb.PreviewTextInput += (s, args) => args.Handled = !args.Text.All(char.IsDigit);
            })
        );
    }
}