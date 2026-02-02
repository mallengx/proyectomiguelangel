using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using CommunityToolkit.Maui;

namespace proyectomiguelangel
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit() // ¡IMPORTANTE! Para FilePicker y otros controles
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

                    // Opcional: Agregar más fuentes si las necesitas
                    fonts.AddFont("FontAwesome6FreeBrands.otf", "FontAwesomeBrands");
                    fonts.AddFont("FontAwesome6FreeRegular.otf", "FontAwesomeRegular");
                    fonts.AddFont("FontAwesome6FreeSolid.otf", "FontAwesomeSolid");
                })
                .ConfigureMauiHandlers(handlers =>
                {
                    // Configuraciones específicas de handlers si son necesarias
#if ANDROID
                    // Configuraciones específicas para Android
#endif
#if IOS
                    // Configuraciones específicas para iOS
#endif
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            // Registrar servicios globales
            builder.Services.AddSingleton(AudioManager.Current);

            // Registrar tus páginas como servicios
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<LyricsTranscriptionPage>();

            // Registrar servicios personalizados
            builder.Services.AddSingleton<AssemblyAIService>();
          //  builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
            // Configurar rutas de navegación
            builder.Services.AddTransient<LyricsTranscriptionPage>();


            return builder.Build();
        }
    }
}