namespace ItemFinder;

/// <summary>The look panel on the right of the main window: what is picked (a category, items or one part), its tick,
/// whether it follows the look above it or has its own, the look settings and a 3D preview. Changes apply at once -
/// the main window listens to the events and stores them.</summary>
sealed class LookPanel : UserControl
{
    readonly Label title = new() { Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label sub = new() { Dock = DockStyle.Top, Height = 38, ForeColor = Color.DimGray, UseMnemonic = false, AutoEllipsis = true };
    readonly CheckBox tick = new() { Dock = DockStyle.Top, Height = 28, Text = "Selected - lights up in the game", AutoCheck = false };
    readonly FlowLayoutPanel switchRow = new() { Dock = DockStyle.Top, Height = 32, WrapContents = false };
    readonly RadioButton same = new() { AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 5, 12, 3) };
    readonly RadioButton own = new() { Text = "Own look", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
    readonly Label sameHint = new() { Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, Text = "Pick \"Own look\" to change it.", Padding = new Padding(20, 0, 0, 0) };
    /// <summary>The look settings (the main window reads <see cref="StyleEditor.Value"/> on <see cref="LookEdited"/>).</summary>
    public readonly StyleEditor Editor = new() { Dock = DockStyle.Top, Sketch = false, Compact = true };
    readonly Panel pickRow = new() { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 4, 0, 0) };
    readonly ComboBox pick = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    /// <summary>The 3D preview (the main window shows the item in it).</summary>
    public readonly ModelView View = new() { Dock = DockStyle.Fill, ShowParts = false };
    readonly Panel body = new() { Dock = DockStyle.Fill };
    readonly Label empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, Padding = new Padding(24), UseMnemonic = false };
    bool loading;

    /// <summary>The "Same as ... / Own look" row (dev: the tour's pictures).</summary>
    internal Control SwitchRow => switchRow;

    /// <summary>The look settings were changed.</summary>
    public event Action? LookEdited;
    /// <summary>"Own look" (true) or "Same as ..." (false) was picked.</summary>
    public event Action<bool>? OwnSwitched;
    /// <summary>The tick was clicked: true = light it up.</summary>
    public event Action<bool>? TickSwitched;
    /// <summary>Another item was picked to preview a category's look on.</summary>
    public event Action<Entry>? PreviewPicked;

    public LookPanel()
    {
        tick.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        switchRow.Controls.AddRange(new Control[] { same, own });
        pickRow.Controls.Add(pick);
        pickRow.Controls.Add(new Label { Text = "Preview on:", Dock = DockStyle.Left, Width = 80, TextAlign = ContentAlignment.MiddleLeft });
        // docked top-down in this order: the last added sits on top
        body.Controls.Add(View);
        body.Controls.Add(pickRow);
        body.Controls.Add(Editor);
        body.Controls.Add(sameHint);
        body.Controls.Add(switchRow);
        body.Controls.Add(tick);
        body.Controls.Add(sub);
        body.Controls.Add(title);
        Controls.Add(body);
        Controls.Add(empty);
        Padding = new Padding(8, 4, 4, 4);
        Load += (_, _) => Editor.FitHeight();
        Editor.HeightChanged += (_, _) => body.PerformLayout(); // "More settings" folded / unfolded: the preview takes the room

        Editor.Changed += (_, _) => { if (!loading) LookEdited?.Invoke(); };
        same.CheckedChanged += (_, _) => { if (!loading && same.Checked) OwnSwitched?.Invoke(false); };
        own.CheckedChanged += (_, _) => { if (!loading && own.Checked) OwnSwitched?.Invoke(true); };
        tick.Click += (_, _) => TickSwitched?.Invoke(tick.CheckState != CheckState.Checked);
        pick.SelectedIndexChanged += (_, _) => { if (!loading && pick.SelectedItem is Entry e) PreviewPicked?.Invoke(e); };
    }

    /// <summary>Nothing to edit here (a section or the overview is picked): just a hint.</summary>
    public void ShowEmpty(string text)
    {
        empty.Text = text;
        body.Visible = false;
        empty.Visible = true;
    }

    /// <param name="ticked">The tick to show; null = no tick (a category).</param>
    /// <param name="sameAs">"Same as Bosses" - the look it can follow; null = no switch (a category has its own look).</param>
    /// <param name="previewOn">Items to choose the preview from (a category); null = no choice.</param>
    public void ShowLook(string heading, string text, CheckState? ticked, string? sameAs, bool isOwn, Style look, bool icons3D, List<Entry>? previewOn)
    {
        loading = true;
        title.Text = heading;
        sub.Text = text;
        tick.Visible = ticked != null;
        if (ticked is CheckState t) tick.CheckState = t;
        switchRow.Visible = sameAs != null;
        same.Text = sameAs ?? "";
        same.Checked = !isOwn;
        own.Checked = isOwn;
        bool editable = sameAs == null || isOwn;
        sameHint.Visible = !editable;
        Editor.Icons3D = icons3D;
        Editor.Value = look;
        Editor.Enabled = editable;
        pickRow.Visible = previewOn is { Count: > 0 };
        pick.Items.Clear();
        if (previewOn != null) foreach (var e in previewOn) pick.Items.Add(e);
        loading = false;
        empty.Visible = false;
        body.Visible = true;
        if (pick.Items.Count > 0) pick.SelectedIndex = 0; // shows the first in the preview
    }
}
