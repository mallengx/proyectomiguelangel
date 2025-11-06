using System.Globalization;

namespace proyectomiguelangel.Converters
{
    public class StringToBoolConverter : IValueConverter
    {
        public static StringToBoolConverter Instance { get; } = new StringToBoolConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool result = false;

            if (value is string stringValue)
            {
                result = !string.IsNullOrEmpty(stringValue);
            }

            // Si el parámetro es "invert", invertimos el resultado
            if (parameter is string paramString && paramString == "invert")
            {
                result = !result;
            }

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}