using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Maintenance_app.orders_Windows;

/// <summary>Small EF-backed add dialog for the three lookup buttons on the order window.</summary>
internal sealed class OrderLookupWindow : Window
{
    private sealed record Scope(T_Location Location)
    {
        public override string ToString() => Location.Location + " — " + Location.Branch.Branch;
    }
    private readonly ComboBoxEdit scope = new() { IsTextEditable = false, Height = 42 };
    private readonly ComboBoxEdit management = new() { IsTextEditable = false, Height = 42, DisplayMember = "Manag" };
    private readonly TextEdit name = new() { Height = 42, MaxLength = 50 };
    private readonly TextEdit address = new() { Height = 42, MaxLength = 150 };
    private readonly SimpleButton save = new() { Content = "حفظ", Height = 42, Margin = new Thickness(0, 20, 0, 0) };
    private readonly Func<Maintenance_DabaseContext> createContext;
    private readonly string kind;
    private readonly List<T_Manage> managements;
    public object? Added { get; private set; }

    public OrderLookupWindow(string kind, List<T_Location> locations, List<T_Manage> managements,
        T_Build? building, T_Manage? selectedManagement, Func<Maintenance_DabaseContext> createContext)
    {
        this.kind = kind; this.createContext = createContext; this.managements = managements;
        Title = kind switch { "Buildings" => "إضافة مبنى", "Managements" => "إضافة إدارة", _ => "إضافة قسم" };
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
        Background = new SolidColorBrush(Color.FromRgb(242, 245, 251));
        ThemeManager.SetThemeName(this, "Win11Light");
        var panel = new StackPanel { Margin = new Thickness(26) };
        void Field(string label, FrameworkElement editor)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 7) });
            panel.Children.Add(editor);
        }
        Field("الموقع والفرع", scope);
        if (kind == "Departments") Field("الإدارة", management);
        Field(kind == "Buildings" ? "اسم المبنى" : kind == "Managements" ? "اسم الإدارة" : "اسم القسم", name);
        if (kind == "Buildings") Field("العنوان", address);
        panel.Children.Add(save); Content = panel;
        scope.EditValueChanged += (_, _) => FilterManagements();
        scope.ItemsSource = locations.Select(x => new Scope(x)).ToList();
        scope.SelectedItem = ((IEnumerable<Scope>)scope.ItemsSource).FirstOrDefault(x => building is not null
            && x.Location.Location == building.Location && x.Location.Location_ID == building.Location_ID
            && x.Location.Branch_ID == building.Branch_ID) ?? ((IEnumerable<Scope>)scope.ItemsSource).FirstOrDefault();
        FilterManagements();
        if (selectedManagement is not null) management.SelectedItem = selectedManagement;
        save.Click += Save_Click;
        Loaded += (_, _) => name.Focus();
    }
    private void FilterManagements()
    {
        var location = (scope.SelectedItem as Scope)?.Location;
        management.ItemsSource = managements.Where(x => location is not null && x.Location == location.Location
            && x.Location_ID == location.Location_ID && x.Branch_ID == location.Branch_ID).ToList();
        management.SelectedItem = null;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
        if (scope.SelectedItem is not Scope chosen) { scope.Focus(); return; }
        if (kind == "Departments" && management.SelectedItem is not T_Manage) { management.Focus(); return; }
        IsEnabled = false;
        try
        {
            var location = chosen.Location;
            string text = name.Text.Trim();
            await using var db = createContext();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            short Next(short? last) => checked((short)((last ?? 0) + 1));
            switch (kind)
            {
                case "Buildings":
                    var buildings = db.T_Builds.Where(x => x.Location == location.Location && x.Location_ID == location.Location_ID && x.Branch_ID == location.Branch_ID);
                    if (await buildings.AnyAsync(x => x.Build == text)) throw new InvalidOperationException("المبنى موجود بالفعل.");
                    var building = new T_Build { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID,
                        Build_ID = Next(await buildings.MaxAsync(x => (short?)x.Build_ID)), Build = text, address = address.Text?.Trim(), Date_Modify = DateOnly.FromDateTime(DateTime.Today) };
                    db.T_Builds.Add(building); Added = building; break;
                case "Managements":
                    var managers = db.T_Manages.Where(x => x.Location == location.Location && x.Location_ID == location.Location_ID && x.Branch_ID == location.Branch_ID);
                    if (await managers.AnyAsync(x => x.Manag == text)) throw new InvalidOperationException("الإدارة موجودة بالفعل.");
                    var manager = new T_Manage { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID,
                        Manag_ID = Next(await managers.MaxAsync(x => (short?)x.Manag_ID)), Manag = text, Date_Modify = DateOnly.FromDateTime(DateTime.Today) };
                    db.T_Manages.Add(manager); Added = manager; break;
                default:
                    var sections = db.T_Sections.Where(x => x.Location == location.Location && x.Location_ID == location.Location_ID && x.Branch_ID == location.Branch_ID);
                    var parent = (T_Manage)management.SelectedItem;
                    if (await sections.AnyAsync(x => x.Manag_ID == parent.Manag_ID && x.Secti == text)) throw new InvalidOperationException("القسم موجود بالفعل.");
                    var section = new T_Section { Location = location.Location, Location_ID = location.Location_ID, Branch_ID = location.Branch_ID,
                        Secti_ID = Next(await sections.MaxAsync(x => (short?)x.Secti_ID)), Manag_ID = parent.Manag_ID,
                        Secti = text, Date_Modify = DateOnly.FromDateTime(DateTime.Today) };
                    db.T_Sections.Add(section); Added = section; break;
            }
            await db.SaveChangesAsync(); await tx.CommitAsync();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Added = null;
            Maintenance_app.Dashboard.UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }
}
