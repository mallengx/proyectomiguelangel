using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Globalization;

namespace proyectomiguelangel.Converters
{
    public class InvertedBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }

            // Si es string, convertir a bool primero
            if (value is string stringValue)
            {
                return string.IsNullOrEmpty(stringValue);
            }

            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}