using System.Runtime.CompilerServices;

// The model's mutators are internal until the command pipeline (WT16) owns them; its tests drive them.
[assembly: InternalsVisibleTo("WinTimeline.Model.Tests")]
