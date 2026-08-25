using System;
using System.Collections.Generic;

namespace Noted.Models;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public double W { get; set; } = 300;
    public double H { get; set; } = 280;
    public string Color { get; set; } = "amber";
    public bool Topmost { get; set; } = true;
    // fora do ecra (fechada, mas guardada)
    public bool Collapsed { get; set; }
    // enrolada ate a barra, ainda no ecra
    public bool Rolled { get; set; }
    public double Opacity { get; set; } = 1.0;
    public List<string> Tags { get; set; } = new();
    public DateTime? Remind { get; set; }
    public string Body { get; set; } = string.Empty;

    // nome dado pelo utilizador; vazio deixa o titulo sair da primeira linha do texto
    public string Name { get; set; } = string.Empty;

    // caminho no disco, preenchido pelo store
    public string? Path { get; set; }

    // o que se mostra na barra e na pesquisa
    public string Display => Name.Length > 0 ? Name : Title;

    // para a primeira linha com texto; corre a cada tecla, por isso nao parte o corpo todo
    public string Title
    {
        get
        {
            int pos = 0;
            while (pos <= Body.Length)
            {
                int nl = Body.IndexOf('\n', pos);
                int end = nl < 0 ? Body.Length : nl;
                var t = StripMarkers(Body[pos..end]);
                if (t.Length > 0) return t.Length > 40 ? t[..40] : t;
                if (nl < 0) break;
                pos = nl + 1;
            }
            return "(vazia)";
        }
    }

    // tira so o marcador de inicio de linha. o TrimStart antigo levava com ele o 'x'
    // de qualquer titulo comecado por x ("xpto" dava "pto")
    private static string StripMarkers(string line)
    {
        var s = line.Trim();
        if (s.Length == 0) return "";

        // regra horizontal nao e titulo de nada
        if (s.Length >= 3 && (s.Trim('-').Length == 0 || s.Trim('*').Length == 0 || s.Trim('_').Length == 0))
            return "";

        while (s.Length > 0 && (s[0] == '#' || s[0] == '>')) s = s[1..].TrimStart();
        if (s.Length >= 2 && (s[0] is '-' or '*' or '+') && s[1] == ' ') s = s[2..].TrimStart();
        if (s.Length >= 3 && s[0] == '[' && s[2] == ']') s = s[3..].TrimStart();
        return s.Trim();
    }
}

public static class Palette
{
    // nome -> (fundo, barra, texto)
    public static readonly Dictionary<string, (string Bg, string Bar, string Fg)> Colors = new()
    {
        ["amber"]  = ("#FFF6C9", "#FFE47A", "#3A3320"),
        ["lime"]   = ("#E6F7C9", "#C8ED8A", "#2C3A20"),
        ["sky"]    = ("#D9EEFF", "#A8D8FF", "#1F3243"),
        ["rose"]   = ("#FFE0E6", "#FFB8C6", "#43202A"),
        ["violet"] = ("#EDE2FF", "#D2BAFF", "#2E2043"),
        ["slate"]  = ("#2B2E33", "#3C4149", "#E6E8EB"),
    };

    public static (string Bg, string Bar, string Fg) Get(string name) =>
        Colors.TryGetValue(name, out var c) ? c : Colors["amber"];

    public static string Next(string name)
    {
        var keys = new List<string>(Colors.Keys);
        int i = keys.IndexOf(name);
        return keys[(i + 1) % keys.Count];
    }
}
