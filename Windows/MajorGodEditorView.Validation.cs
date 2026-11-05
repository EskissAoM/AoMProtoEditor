using System.Xml.Linq;
using Avalonia.Controls;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class MajorGodEditorView
{
    private void BindMajorGodValidationFields(XElement entry)
    {
        foreach (var (control, path) in new (Control, string)[]
        {
            (_iconEditor, "icon"), (_portraitEditor, "portrait"), (_breakoutPortraitEditor, "breakoutportrait"),
            (_minimapBorderBox, "minimapborderimage"), (_backgroundScenarioBox, "backgroundscenario"),
            (_ageBox, "agetech/@age"), (_ageTechBox, "agetech/tech"),
            (_deathmatchTechBox, "deathmatchtech"), (_teamTechBox, "teamtech"),
            (_partisansCheckBox, "partisans"), (_devotionCommandsCheckBox, "devotioncommands")
        }) EditorValidationBindings.Bind(control, entry, path);
    }
}
