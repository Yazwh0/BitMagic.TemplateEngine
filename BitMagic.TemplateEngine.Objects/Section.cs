using System.Text.RegularExpressions;

namespace BitMagic.TemplateEngine.Objects;

/// <summary>
/// One library's code for a section.
/// </summary>
public sealed class SectionPart
{
    internal SectionPart(string owner, Action write)
    {
        Owner = owner;
        Write = write;
    }

    /// <summary>
    /// The library's source file.
    /// </summary>
    public string Owner { get; }

    /// <summary>
    /// Writes the library's code for the section.
    /// </summary>
    public Action Write { get; }

    /// <summary>
    /// Set once the part's placement is resolved, as the sections are written.
    /// </summary>
    public bool Placed { get; internal set; }
}

/// <summary>
/// Named code that libraries add to, and that is written wherever it's placed. 'place code'
/// places every part not placed elsewhere, 'place Lib.code' places one library's part.
/// Placements reserve a spot in the output; the sections are written into those spots once
/// the main file's code has run, so the order of placements doesn't matter.
/// </summary>
public sealed partial class Section
{
    private static readonly Dictionary<string, Section> _sections = new(StringComparer.Ordinal);
    private static readonly List<Placement> _placements = new();

    private readonly List<SectionPart> _parts = new();

    private Section(string name)
    {
        Name = name;
    }

    /// <summary>
    /// A section name is a plain name, like 'code' or 'irq_handlers'. It can't contain a '.',
    /// as 'place Lib.code' uses one.
    /// </summary>
    public static bool IsValidName(string? name) => ValidName().IsMatch(name ?? "");

    public static Section Get(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException($"'{name}' isn't a valid section name. It must be a name, like 'code' or 'irq_handlers'.", nameof(name));

        if (!_sections.TryGetValue(name!, out var section))
        {
            section = new Section(name!);
            _sections.Add(name!, section);
        }

        return section;
    }

    public static IEnumerable<Section> All => _sections.Values;

    public string Name { get; }

    public IReadOnlyList<SectionPart> Parts => _parts;

    /// <summary>
    /// True once an unqualified placement ('place code') exists.
    /// </summary>
    public bool RestPlaced => _placements.Any(i => i.Section == this && i.Owner == null);

    /// <summary>
    /// How the parts at a placement are written. Called once per placement, with the parts
    /// placed there. The default writes each part in turn.
    /// </summary>
    public Action<IReadOnlyList<SectionPart>> Writer { get; set; } = WriteEach;

    /// <summary>
    /// Adds a library's code. A second call for the same owner is ignored, so a library
    /// imported by more than one file is only written once.
    /// </summary>
    public void Add(string owner, Action write)
    {
        if (_parts.Any(i => SameOwner(i.Owner, owner)))
            return;

        _parts.Add(new SectionPart(owner, write));
    }

    /// <summary>
    /// Places every part not placed by owner, here.
    /// </summary>
    public void Place(string? file = null, int line = -1) => PlaceAt(Template.Anchor(), file, line);

    /// <summary>
    /// Places every part not placed by owner, at the anchor.
    /// </summary>
    public void PlaceAt(Anchor anchor, string? file = null, int line = -1)
    {
        var existing = _placements.FirstOrDefault(i => i.Section == this && i.Owner == null);

        if (existing != null)
            throw new TemplateSourceException(file, line, $"'place {Name}' is already used{At(existing)}.");

        _placements.Add(new Placement(this, anchor, null, file, line));
    }

    /// <summary>
    /// Places one library's part, here.
    /// </summary>
    public void Place(string? file, int line, string owner) => PlaceAt(Template.Anchor(), file, line, owner);

    /// <summary>
    /// Places one library's part, at the anchor.
    /// </summary>
    public void PlaceAt(Anchor anchor, string? file, int line, string owner)
    {
        var existing = _placements.FirstOrDefault(i => i.Section == this && i.Owner != null && SameOwner(i.Owner, owner));

        if (existing != null)
            throw new TemplateSourceException(file, line, $"'{SourceName(owner)}' {Name} is already placed{At(existing)}.");

        _placements.Add(new Placement(this, anchor, owner, file, line));
    }

    private static void WriteEach(IReadOnlyList<SectionPart> parts)
    {
        foreach (var part in parts)
            part.Write();
    }

    /// <summary>
    /// Writes every section into its placements. Writing can place more sections (a library's
    /// code can contain a 'place'), so this repeats until nothing is left. Placements for one
    /// library go first, so 'place code' knows what's left for it.
    /// </summary>
    internal static void WriteAll()
    {
        while (true)
        {
            var pending = _placements.Where(i => !i.Written).OrderBy(i => i.Owner == null ? 1 : 0).ToList();

            if (pending.Count == 0)
                break;

            foreach (var placement in pending)
            {
                var section = placement.Section;
                List<SectionPart> parts;

                if (placement.Owner != null)
                {
                    parts = section._parts.Where(i => SameOwner(i.Owner, placement.Owner)).ToList();

                    if (parts.Count == 0)
                        throw new TemplateSourceException(placement.File, placement.Line, $"'{SourceName(placement.Owner)}' has no '{section.Name}' section.");

                    var placed = parts.FirstOrDefault(i => i.Placed);
                    if (placed != null)
                        throw new TemplateSourceException(placement.File, placement.Line, $"'{SourceName(placement.Owner)}' {section.Name} is already placed by 'place {section.Name}'.");
                }
                else
                {
                    // everything not claimed by a placement for its library
                    parts = section._parts.Where(i => !i.Placed &&
                        !_placements.Any(p => p.Section == section && p.Owner != null && SameOwner(p.Owner, i.Owner))).ToList();
                }

                foreach (var part in parts)
                    part.Placed = true;

                placement.Written = true;

                using (Template.WriteTo(placement.Anchor))
                    section.Writer(parts);
            }
        }

        foreach (var section in _sections.Values)
        {
            var unplaced = section._parts.Where(i => !i.Placed).ToList();

            if (unplaced.Count != 0)
                throw new TemplateSourceException(null, -1,
                    $"Section '{section.Name}' has code from {string.Join(", ", unplaced.Select(i => $"'{SourceName(i.Owner)}'"))} that is never placed. Add 'place {section.Name}', or 'place <alias>.{section.Name}' for one library.");
        }
    }

    /// <summary>
    /// After the output is generated: a placement whose anchor isn't in the output had its
    /// code dropped, eg by a view that didn't write everything it captured.
    /// </summary>
    internal static void CheckWritten(HashSet<Anchor> written)
    {
        foreach (var placement in _placements)
        {
            if (!written.Contains(placement.Anchor))
                throw new TemplateSourceException(placement.File, placement.Line, $"'place {(placement.Owner == null ? "" : SourceName(placement.Owner) + ".")}{placement.Section.Name}' isn't in the output, the code containing it was dropped.");
        }
    }

    internal static void Reset()
    {
        _sections.Clear();
        _placements.Clear();
    }

    // owners are source paths; the same file must match whichever separator it was written with
    internal static bool SameOwner(string a, string b) => string.Equals(Normalise(a), Normalise(b), StringComparison.Ordinal);

    private static string Normalise(string path) => path.Replace('\\', '/');

    /// <summary>
    /// A source file as shown in errors: relative to the main file's folder, when that's shorter.
    /// </summary>
    internal static string SourceName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
            return Normalise(relative.StartsWith("..") ? path : relative);
        }
        catch (ArgumentException)
        {
            return Normalise(path);
        }
    }

    private static string At(Placement placement) =>
        string.IsNullOrWhiteSpace(placement.File) ? "" : $" at {SourceName(placement.File)}({placement.Line + 1})";

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidName();

    private sealed class Placement(Section section, Anchor anchor, string? owner, string? file, int line)
    {
        public Section Section { get; } = section;
        public Anchor Anchor { get; } = anchor;
        public string? Owner { get; } = owner;
        public string? File { get; } = file;
        public int Line { get; } = line;
        public bool Written { get; set; }
    }
}
