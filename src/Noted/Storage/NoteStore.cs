using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Noted.Models;

namespace Noted.Storage;

// notas em ficheiros .md com front-matter yaml simples, sem dependencias
public sealed class NoteStore
{
    public string Root { get; }

    public NoteStore(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NOTED", "notes");
        Directory.CreateDirectory(Root);
    }

    public List<Note> LoadAll()
    {
        var list = new List<(DateTime Stamp, Note Note)>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.md"))
        {
            try { list.Add((File.GetLastWriteTimeUtc(file), Parse(File.ReadAllText(file, Encoding.UTF8), file))); }
            catch { /* ficheiro corrompido: ignora, nunca rebenta o arranque */ }
        }

        // mais recentes primeiro: e a ordem util na pesquisa e no "mostrar todas"
        list.Sort((a, b) => b.Stamp.CompareTo(a.Stamp));

        var result = new List<Note>(list.Count);
        foreach (var (_, n) in list) result.Add(n);
        return result;
    }

    public void Save(Note n)
    {
        n.Path ??= Path.Combine(Root, n.Id + ".md");
        var tmp = n.Path + ".tmp";
        try
        {
            File.WriteAllText(tmp, Serialize(n), new UTF8Encoding(false));
            File.Move(tmp, n.Path, overwrite: true); // escrita atomica
        }
        catch
        {
            // sem isto, uma escrita falhada deixava .tmp orfaos na pasta das notas
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    public void Delete(Note n)
    {
        if (n.Path is not null && File.Exists(n.Path)) File.Delete(n.Path);
    }

    private static string Serialize(Note n)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("id: ").Append(n.Id).Append('\n');
        sb.Append("color: ").Append(n.Color).Append('\n');
        sb.Append("pos: ").Append(F(n.X)).Append(',').Append(F(n.Y)).Append('\n');
        sb.Append("size: ").Append(F(n.W)).Append(',').Append(F(n.H)).Append('\n');
        sb.Append("topmost: ").Append(n.Topmost ? "true" : "false").Append('\n');
        sb.Append("collapsed: ").Append(n.Collapsed ? "true" : "false").Append('\n');
        sb.Append("rolled: ").Append(n.Rolled ? "true" : "false").Append('\n');
        sb.Append("opacity: ").Append(F(n.Opacity)).Append('\n');
        // uma quebra de linha aqui partia o front-matter em dois
        if (n.Name.Length > 0)
            sb.Append("name: ")
              .Append(n.Name.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '))
              .Append('\n');
        if (n.Tags.Count > 0) sb.Append("tags: ").Append(string.Join(", ", n.Tags)).Append('\n');
        if (n.Remind is DateTime r)
            sb.Append("remind: ").Append(r.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("---\n");
        // o TextBox escreve CRLF e o ficheiro ficava com os dois misturados
        sb.Append(n.Body.Replace("\r\n", "\n"));
        return sb.ToString();

        static string F(double d) =>
            double.IsNaN(d) ? "auto" : d.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static Note Parse(string text, string path)
    {
        var n = new Note { Path = path, Id = Path.GetFileNameWithoutExtension(path) };
        text = text.Replace("\r\n", "\n");

        if (!text.StartsWith("---\n", StringComparison.Ordinal)) { n.Body = text; return n; }
        int end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        if (end < 0) { n.Body = text; return n; }

        var head = text[4..(end + 1)];
        n.Body = text[(end + 5)..];

        bool sawRolled = false;

        foreach (var raw in head.Split('\n'))
        {
            int c = raw.IndexOf(':');
            if (c <= 0) continue;
            var key = raw[..c].Trim();
            var val = raw[(c + 1)..].Trim();
            switch (key)
            {
                case "id": if (val.Length > 0) n.Id = val; break;
                case "color": n.Color = val; break;
                case "pos": (n.X, n.Y) = Pair(val, double.NaN, double.NaN); break;
                case "size": (n.W, n.H) = Pair(val, 300, 280); break;
                case "topmost": n.Topmost = val == "true"; break;
                case "collapsed": n.Collapsed = val == "true"; break;
                case "rolled": n.Rolled = val == "true"; sawRolled = true; break;
                case "name": n.Name = val; break;
                case "opacity": n.Opacity = D(val, 1.0); break;
                case "tags":
                    n.Tags.Clear();
                    foreach (var t in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        n.Tags.Add(t);
                    break;
                case "remind":
                    n.Remind = DateTime.TryParse(val, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt) ? dt : null;
                    break;
            }
        }

        // ficheiros anteriores ao campo "rolled" podem ter ficado com a altura presa nos
        // 60px pelo bug de enrolar; sem isto reabrem como uma tira inutilizavel
        if (!sawRolled && n.H <= 60) n.H = 280;

        return n;

        static (double, double) Pair(string s, double da, double db)
        {
            var parts = s.Split(',');
            return parts.Length == 2 ? (D(parts[0], da), D(parts[1], db)) : (da, db);
        }

        static double D(string s, double fallback) =>
            double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
