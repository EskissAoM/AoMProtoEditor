using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CultureEditorInitializationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(CultureEditorWindow editor, string name) =>
        (T)typeof(CultureEditorWindow).GetField(name, Private)!.GetValue(editor)!;
    private static Task Save(CultureEditorWindow editor) =>
        (Task)typeof(CultureEditorWindow).GetMethod("SaveAsync", Private)!.Invoke(editor, null)!;
    private static void CloseFixture(CultureEditorWindow editor)
        => Field<CompactEditorLifecycle>(editor, "_lifecycle").CloseAfterSave(editor.Close);
    private static CultureDefinition Definition(string id = "STR_SHARED", bool builtIn = false) =>
        new("FixtureCulture", builtIn, XElement.Parse($"<culture><name>FixtureCulture</name><displaynameid>{id}</displaynameid><compendiumtopic>topic</compendiumtopic><future keep='yes'/></culture>"));
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "Culture initialization did not reach the expected state.");
    }

    [Theory]
    [InlineData("My new name")]
    [InlineData("")]
    public void DelayedDisplayNameDoesNotReplaceTextEnteredAfterOpening(string entered)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var resolution = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var editor = new CultureEditorWindow(Definition(), [], _ => resolution.Task, (_, _, _) => Task.FromResult(true));
            editor.Show();
            try
            {
                var box = Field<TextBox>(editor, "_displayNameBox");
                Assert.True(box.IsEnabled);
                box.Text = entered;
                resolution.SetResult("Original name");
                await Until(() => Field<string>(editor, "_loadedDisplayName") == "Original name");
                Assert.Equal(entered, box.Text);
                if (entered.Length == 0)
                {
                    var saving = Save(editor);
                    await Until(() => editor.OwnedWindows.OfType<Prompt>().Any());
                    var error = Assert.Single(editor.OwnedWindows.OfType<Prompt>());
                    Assert.Equal("Invalid Culture", error.PromptTitle);
                    Assert.Contains("Display name is required", error.PromptText);
                    error.Close();
                    await saving;
                    Assert.True(editor.IsVisible);
                    Assert.Contains("invalid-field", box.Classes);
                    Assert.True(box.IsKeyboardFocusWithin);
                    box.Text = "Corrected culture";
                    Assert.DoesNotContain("invalid-field", box.Classes);
                }
            }
            finally { CloseFixture(editor); }
        });
    }

    [Theory]
    [InlineData("Original name")]
    [InlineData("Edited name")]
    public void SaveWaitsForBaselineAndOnlyRequestsIntentionalSharedStringChanges(string entered)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var resolution = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var definition = Definition();
            var originalXml = definition.SourceElement.ToString(SaveOptions.DisableFormatting);
            var calls = 0;
            XElement? saved = null;
            string? update = "not saved";
            var editor = new CultureEditorWindow(definition, [], _ => resolution.Task, (name, value, display) =>
            {
                Assert.Equal("FixtureCulture", name);
                calls++; saved = value; update = display;
                return Task.FromResult(true);
            });
            editor.Show();
            try
            {
                Field<TextBox>(editor, "_displayNameBox").Text = entered;
                Field<TextBox>(editor, "_topicBox").Text = "new topic";
                var saving = Save(editor);
                Assert.False(saving.IsCompleted);
                Assert.Equal(0, calls);
                // A second click must not start another save while initialization is pending.
                await Save(editor);
                Assert.Equal(0, calls);
                resolution.SetResult("Original name");
                await saving.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(1, calls);
                Assert.Equal(entered == "Original name" ? null : entered, update);
                Assert.Equal("STR_SHARED", CultureCatalog.ReadField(saved!, "displaynameid"));
                Assert.Equal("new topic", CultureCatalog.ReadField(saved!, "compendiumtopic"));
                Assert.Equal("yes", (string?)saved!.Element("future")!.Attribute("keep"));
                Assert.Equal(originalXml, definition.SourceElement.ToString(SaveOptions.DisableFormatting));
                Assert.False(editor.IsVisible);
            }
            finally { CloseFixture(editor); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrCancelledLookupKeepsEditsReportsRefusalAndSaveCanRetry(bool cancelled)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var resolution = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = 0;
            var failRetry = true;
            var saves = 0;
            string? update = null;
            var editor = new CultureEditorWindow(Definition(), [], _ =>
            {
                attempts++;
                return attempts == 1 ? resolution.Task : failRetry
                    ? Task.FromException<string?>(new IOException("String lookup unavailable"))
                    : Task.FromResult<string?>("Original name");
            }, (_, _, display) => { saves++; update = display; return Task.FromResult(true); });
            editor.Show();
            try
            {
                var box = Field<TextBox>(editor, "_displayNameBox");
                box.Text = "My preserved edit";
                if (cancelled) resolution.SetCanceled();
                else resolution.SetException(new IOException("String lookup unavailable"));
                Assert.False(await Field<Task<bool>>(editor, "_displayNameLoadTask").WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal("My preserved edit", box.Text);
                Assert.Contains("Save retries", box.PlaceholderText);
                var saving = Save(editor);
                await Until(() => editor.OwnedWindows.OfType<Prompt>().Any());
                var error = Assert.Single(editor.OwnedWindows.OfType<Prompt>());
                Assert.Equal("Unable to load Culture display name", error.PromptTitle);
                Assert.Contains("No changes were saved", error.PromptText);
                error.Close();
                await saving.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(0, saves);
                Assert.Equal("My preserved edit", box.Text);
                Assert.True(editor.IsVisible);
                failRetry = false;
                await Save(editor).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(3, attempts);
                Assert.Equal(1, saves);
                Assert.Equal("My preserved edit", update);
                Assert.Equal("Original name", Field<string>(editor, "_loadedDisplayName"));
            }
            finally
            {
                foreach (var prompt in editor.OwnedWindows.ToArray()) prompt.Close();
                CloseFixture(editor);
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ClosingDuringLookupPreventsLateUiChangesAndPendingSaves(bool savePending, bool failure)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var resolution = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var saves = 0;
            var editor = new CultureEditorWindow(Definition(), [], _ => resolution.Task,
                (_, _, _) => { saves++; return Task.FromResult(true); });
            editor.Show();
            try
            {
                var box = Field<TextBox>(editor, "_displayNameBox");
                box.Text = "Unsaved input";
                var loading = Field<Task<bool>>(editor, "_displayNameLoadTask");
                var saving = savePending ? Save(editor) : Task.CompletedTask;
                editor.Close();
                var discard = Assert.Single(editor.OwnedWindows.OfType<Prompt>());
                Assert.Equal("Unsaved changes", discard.PromptTitle);
                discard.GetType().GetMethod("ConfirmButton_Click", Private)!.Invoke(discard, [null, new Avalonia.Interactivity.RoutedEventArgs()]);
                await Until(() => !editor.IsVisible);
                if (failure) resolution.SetException(new IOException("Late lookup failure"));
                else resolution.SetResult("Late loaded name");
                Assert.False(await loading.WaitAsync(TimeSpan.FromSeconds(3)));
                await saving.WaitAsync(TimeSpan.FromSeconds(3));
                await Save(editor);
                Assert.Equal(0, saves);
                Assert.Equal("Unsaved input", box.Text);
                Assert.Equal("", Field<string>(editor, "_loadedDisplayName"));
                Assert.Empty(editor.OwnedWindows);
            }
            finally { CloseFixture(editor); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateResolutionPreservesUnchangedSharedIdAndReadOnlyCultures(bool builtIn)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var saves = 0;
            string? update = "not saved";
            var editor = new CultureEditorWindow(Definition(builtIn: builtIn), [],
                id => { Assert.Equal("STR_SHARED", id); return Task.FromResult<string?>("Shared display name"); },
                (_, value, display) =>
                {
                    saves++; update = display;
                    Assert.Equal("STR_SHARED", CultureCatalog.ReadField(value, "displaynameid"));
                    return Task.FromResult(true);
                });
            editor.Show();
            try
            {
                var box = Field<TextBox>(editor, "_displayNameBox");
                Assert.Equal("Shared display name", box.Text);
                Assert.Equal(!builtIn, box.IsEnabled);
                await Save(editor);
                Assert.Equal(builtIn ? 0 : 1, saves);
                if (!builtIn) Assert.Null(update);
            }
            finally { CloseFixture(editor); }
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("STR_MISSING")]
    public void MissingIdsOrNullResolutionStillAllowAnExplicitValidDisplayName(string id)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var lookups = 0;
            var saves = 0;
            var editor = new CultureEditorWindow(Definition(id), [],
                _ => { lookups++; return Task.FromResult<string?>(null); },
                (_, value, display) =>
                {
                    saves++;
                    Assert.Equal(id.Length == 0 ? CultureCatalog.BuildDisplayNameId("FixtureCulture") : id,
                        CultureCatalog.ReadField(value, "displaynameid"));
                    Assert.Equal("Replacement", display);
                    return Task.FromResult(true);
                });
            editor.Show();
            try
            {
                Field<TextBox>(editor, "_displayNameBox").Text = "Replacement";
                await Save(editor);
                Assert.Equal(id.Length == 0 ? 0 : 1, lookups);
                Assert.Equal(1, saves);
            }
            finally { CloseFixture(editor); }
        });
    }
}
