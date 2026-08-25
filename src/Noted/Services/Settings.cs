using Microsoft.Win32;

namespace Noted.Services;

// preferencias da app no registo, ao lado da chave de arranque automatico.
// nao vale um ficheiro de configuracao para dois booleanos
internal static class Settings
{
    private const string Key = @"Software\NOTED";

    // notas fora da barra de tarefas e do alt-tab e o comportamento de origem, mas
    // deixa a app sem forma de se alcancar quando o windows esconde o icone da bandeja
    public static bool ShowInTaskbar
    {
        get => Read("ShowInTaskbar", false);
        set => Write("ShowInTaskbar", value);
    }

    private static bool Read(string name, bool fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is int v ? v != 0 : fallback;
        }
        catch { return fallback; }
    }

    private static void Write(string name, bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* registo trancado: fica no valor por omissao */ }
    }
}
