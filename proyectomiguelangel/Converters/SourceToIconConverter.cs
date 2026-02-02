using System.Globalization;

namespace proyectomiguelangel.Converters
{
    public class SourceToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string source && parameter is string param)
            {
                // Si viene el parámetro "showquery", devolver bool para mostrar/ocultar
                if (param == "showquery")
                {
                    return source == "LyricsSearch";
                }

                // Si viene el parámetro "icon", devolver el icono
                if (param == "icon")
                {
                    return source switch
                    {
                        "AudioRecognition" => "🎤",
                        "LyricsSearch" => "🔍",
                        _ => "📝"
                    };
                }
            }

            // Por defecto, devolver texto descriptivo
            if (value is string sourceDefault)
            {
                return sourceDefault switch
                {
                    "AudioRecognition" => "🎤 Identificada",
                    "LyricsSearch" => "🔍 Buscada",
                    _ => "📝"
                };
            }
            return "📝";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}