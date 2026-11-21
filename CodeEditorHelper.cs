using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;
using ICSharpCode.Decompiler.CSharp;
using TextMateSharp.Grammars;
using AvaloniaEdit.Search;


namespace SpyTool2;

public class GameCompletionItem : ICompletionData
{
    public GameCompletionItem(string text, string description)
    {
        Text = text;
        Description = description;
    }

    public IImage? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object Description { get; }
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}

public static class CodeEditorHelper
{
    public static TextEditor CreateCSharpEditor(
        bool isReadOnly,
        Func<IEnumerable<CSharpDecompiler>>? getDecompilers = null)
    {
        var editor = new TextEditor
        {
            FontFamily = new FontFamily("Consolas, Monospace"),
            FontSize = 13,
            ShowLineNumbers = true,
            WordWrap = true,
            IsReadOnly = isReadOnly,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        SearchPanel.Install(editor);
        editor.WordWrap = true;
        editor.Options.WordWrapIndentation = 28;
        editor.Options.InheritWordWrapIndentation = true;

        bool isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var registryOptions = new RegistryOptions(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        var installation = editor.InstallTextMate(registryOptions);
        installation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".cs").Id));

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (_, _) =>
            {
                bool darkNow = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                var newReg = new RegistryOptions(darkNow ? ThemeName.DarkPlus : ThemeName.LightPlus);
                installation.SetTheme(newReg.LoadTheme(darkNow ? ThemeName.DarkPlus : ThemeName.LightPlus));
            };
        }

        if (!isReadOnly && getDecompilers != null)
        {
            editor.TextArea.TextEntered += (_, e) =>
            {
                if (e.Text == ".")
                    ShowCompletions(editor, getDecompilers, afterDot: true);
            };

            editor.TextArea.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    e.Handled = true;
                    ShowCompletions(editor, getDecompilers, afterDot: false);
                }
            };
        }

        return editor;
    }

    private static void ShowCompletions(
        TextEditor editor,
        Func<IEnumerable<CSharpDecompiler>> getDecompilers,
        bool afterDot)
    {
        try
        {
            int caret = editor.CaretOffset;
            string textBefore = editor.Document.GetText(0, caret);

            string prefixTarget = "";
            if (afterDot && textBefore.Length >= 2)
            {
                int end = textBefore.Length - 1;
                int start = end - 1;
                while (start >= 0 && (char.IsLetterOrDigit(textBefore[start]) || textBefore[start] == '_'))
                    start--;
                prefixTarget = textBefore.Substring(start + 1, end - start - 1);
            }

            var items = new Dictionary<string, GameCompletionItem>(StringComparer.Ordinal);

            foreach (var dec in getDecompilers())
            {
                foreach (var type in dec.TypeSystem.MainModule.TypeDefinitions)
                {
                    if (type.Name.StartsWith("<")) continue;

                    if (afterDot && !string.IsNullOrEmpty(prefixTarget))
                    {
                        bool typeMatches = type.Name.Equals(prefixTarget, StringComparison.OrdinalIgnoreCase)
                            || prefixTarget.EndsWith(type.Name, StringComparison.OrdinalIgnoreCase);

                        if (typeMatches)
                        {
                            foreach (var m in type.Members)
                            {
                                if (m.Name.StartsWith("<") || m.Name.StartsWith(".")) continue;
                                items.TryAdd(m.Name, new GameCompletionItem(m.Name, $"{type.Name}.{m.Name} ({m.ReturnType.Name})"));
                            }
                        }
                    }
                    else if (!afterDot)
                    {
                        items.TryAdd(type.Name, new GameCompletionItem(type.Name, $"Класс {type.FullName}"));
                    }
                }
            }

            if (afterDot && items.Count == 0)
            {
                foreach (var dec in getDecompilers())
                {
                    foreach (var type in dec.TypeSystem.MainModule.TypeDefinitions.Where(t => !t.Name.StartsWith("<")))
                    {
                        foreach (var m in type.Members.Where(m => !m.Name.StartsWith("<") && !m.Name.StartsWith(".")))
                        {
                            items.TryAdd(m.Name, new GameCompletionItem(m.Name, $"{type.Name}.{m.Name} ({m.ReturnType.Name})"));
                            if (items.Count > 400) break;
                        }
                        if (items.Count > 400) break;
                    }
                }
            }

            if (items.Count == 0) return;

            var window = new CompletionWindow(editor.TextArea);
            foreach (var item in items.Values.OrderBy(i => i.Text))
                window.CompletionList.CompletionData.Add(item);

            window.Show();
        }
        catch { }
    }
}
