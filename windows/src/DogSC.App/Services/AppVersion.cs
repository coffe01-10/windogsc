using System.Reflection;

namespace DogSC;

static class AppVersion
{
    public static string Current
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
