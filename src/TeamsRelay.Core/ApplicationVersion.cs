using System.Reflection;

namespace TeamsRelay.Core;

public static class ApplicationVersion
{
    public static string Value =>
        typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(ApplicationVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
