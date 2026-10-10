using System.IO;
using System.Reflection;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.App.Services;

internal static class RuntimeDependencies
{
    // WPF loads this lazily when text-editing context menus expose accessibility. Check it before an engineer
    // starts work, so an incomplete portable extraction has a useful remedy instead of a mid-session UI error.
    public static void VerifyTextEditing()
    {
        try
        {
            Assembly.Load("Accessibility, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
                .GetType("Accessibility.IAccessible", throwOnError: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
        {
            throw new ConfigurationException("The Windows text-editing dependency Accessibility.dll could not be loaded. Extract the entire portable ZIP into a new folder and run Start.cmd there. Keep the app folder and all its DLL files together; copying only the EXE is not sufficient. Check the package checksums if the problem continues. Do not download individual DLLs from other sites.", ex);
        }
    }
}
