using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace DogSC;

// Only application-owned copy enters this catalog. Project names, memo text,
// paths and device names continue to come directly from their model/control.
public sealed class LanguageCatalog : INotifyPropertyChanged
{
    public static LanguageCatalog Current { get; } = new();
    readonly Dictionary<string, string> _english;
    public LanguageCatalog()
    {
        using var source = typeof(LanguageCatalog).Assembly.GetManifestResourceStream("DogSC.Localization.en.json")!;
        _english = JsonSerializer.Deserialize<Dictionary<string, string>>(source)!;
    }
    public string this[string key] => SettingsStore.Current.Language == "en" && _english.TryGetValue(key, out var text) ? text : key;
    public string Language => SettingsStore.Current.Language;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
}

public static class L
{
    public static string Text(string key) => LanguageCatalog.Current[key];
    public static string Format(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Text(key), values);
    public static Binding Binding(string key) => new(nameof(LanguageCatalog.Language)) { Source = LanguageCatalog.Current, Converter = new TranslationConverter(), ConverterParameter = key, Mode = BindingMode.OneWay };
    public static void Bind(DependencyObject control, DependencyProperty property, string key) => BindingOperations.SetBinding(control, property, Binding(key));

    public static void Change(string language)
    {
        SettingsStore.Current.Language = language == "en" ? "en" : "zh-Hans";
        SettingsStore.Save();
        LanguageCatalog.Current.Refresh();
        foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
        {
            switch (window)
            {
                case SettingsWindow settings: settings.RefreshLanguage(); break;
                case EditorWindow editor: editor.RefreshLanguage(); break;
                case RecorderBarWindow recorder: recorder.RefreshLanguage(); break;
                case ExportWindow export: export.RefreshLanguage(); break;
            }
        }
    }
}

sealed class TranslationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => L.Text((string)parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LocalizeExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => L.Binding(Key).ProvideValue(serviceProvider);
}
