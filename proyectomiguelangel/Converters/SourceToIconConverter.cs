using System.Globalization;

namespace proyectomiguelangel.Converters
{
    public class SourceToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string source)
            {
                // Si viene el parámetro "showquery", devolver bool para mostrar/ocultar
                if (parameter is string param)
                {
                    switch (param)
                    {
                        case "showquery":
                            return source == "LyricsSearch";

                        case "icon":
                            return source switch
                            {
                                "AudioRecognition" => "🎤",
                                "LyricsSearch" => "🔍",
                                _ => "📝"
                            };

                        case "color":
                            return source switch
                            {
                                "AudioRecognition" => Color.FromArgb("#27AE60"), // Verde
                                "LyricsSearch" => Color.FromArgb("#3498DB"),     // Azul
                                _ => Color.FromArgb("#9B59B6")                  // Púrpura
                            };

                        case "bgcolor":
                            return source switch
                            {
                                "AudioRecognition" => Color.FromArgb("#27AE60"), // Verde
                                "LyricsSearch" => Color.FromArgb("#3498DB"),     // Azul
                                _ => Color.FromArgb("#9B59B6")                  // Púrpura
                            };

                        case "textcolor":
                            return source switch
                            {
                                "AudioRecognition" => Color.FromArgb("#27AE60"), // Verde
                                "LyricsSearch" => Color.FromArgb("#3498DB"),     // Azul
                                _ => Color.FromArgb("#9B59B6")                  // Púrpura
                            };
                    }
                }
            }

            // Por defecto, devolver texto descriptivo
            if (value is string sourceDefault)
            {
                return sourceDefault switch
                {
                    "AudioRecognition" => "🎤 Identificada por audio",
                    "LyricsSearch" => "🔍 Buscada por letra",
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