using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.UI;

namespace ArchWalk.RhinoPlugin.UI;

[Guid(PanelIdString)]
public sealed class ObserverPanel : UserControl, IPanel
{
    public const string PanelIdString = "d0a6e733-9c77-4b32-bf40-3eaf2c8d5f66";
    public static Guid PanelId => new(PanelIdString);

    readonly Label _hint;
    readonly Label _status;
    readonly Label _targetCaption;
    readonly ComboBox _targetList;
    readonly CheckBox _lookAt3D;
    readonly PictureBox _preview;
    readonly Label _previewCaption;
    readonly Button _place;
    readonly Button _enter;
    readonly Button _done;
    readonly Button _editFoot;
    readonly Button _editAim;
    readonly Button _cancel;
    bool _suppressEvents;

    public ObserverPanel()
    {
        Text = "Наблюдатель";
        BackColor = SystemColors.Window;
        Padding = new Padding(10);
        AutoScroll = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Наблюдатель",
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        _hint = new Label
        {
            Text = "Кликните место, затем направление взгляда. Высота 1550 мм, скорость 1,3 м/с.",
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            Margin = new Padding(0, 0, 0, 8)
        };

        _place = MakeButton("Поставить наблюдателя", OnPlace);
        _place.Height = 36;
        _place.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);

        _targetCaption = new Label { Text = "Целевой вид", AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        _targetList = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
        _targetList.SelectedIndexChanged += (_, _) => OnTargetChanged();

        _lookAt3D = new CheckBox { Text = "Смотреть в точку 3D", AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        _lookAt3D.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            PlacementController.SetLookAt3D(_lookAt3D.Checked);
            RefreshUi();
        };

        _previewCaption = new Label { Text = "Превью Shaded", AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
        _preview = new PictureBox
        {
            Width = 360,
            Height = 220,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(32, 32, 32),
            Margin = new Padding(0, 0, 0, 8)
        };

        _status = new Label { Text = "Готово к установке", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        _enter = MakeButton("Войти", OnEnter);
        _done = MakeButton("Готово", OnDone);
        _editFoot = MakeButton("Изменить место", OnEditFoot);
        _editAim = MakeButton("Изменить взгляд", OnEditAim);
        _cancel = MakeButton("Отмена", OnCancel);
        row.Controls.AddRange([_enter, _done, _editFoot, _editAim, _cancel]);

        root.Controls.Add(title);
        root.Controls.Add(_hint);
        root.Controls.Add(_place);
        root.Controls.Add(_targetCaption);
        root.Controls.Add(_targetList);
        root.Controls.Add(_lookAt3D);
        root.Controls.Add(_previewCaption);
        root.Controls.Add(_preview);
        root.Controls.Add(_status);
        root.Controls.Add(row);
        Controls.Add(root);

        PlacementController.Changed += OnPlacementChanged;
        HandleDestroyed += (_, _) => PlacementController.Changed -= OnPlacementChanged;
        Load += (_, _) => RefreshUi();
    }

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason) => RefreshUi();
    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }
    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) { }

    void OnPlacementChanged()
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke(RefreshUi);
    }

    void RefreshUi()
    {
        if (IsDisposed)
            return;
        _suppressEvents = true;
        try
        {
            var doc = RhinoDoc.ActiveDoc;
            var draft = PlacementController.Draft;
            var otherCommand = Rhino.Commands.Command.InCommand() && draft.Phase == PlacementPhase.Idle;
            _place.Enabled = !SessionController.IsActive && !otherCommand;
            _place.Text = otherCommand ? "Завершите текущую команду Rhino" : "Поставить наблюдателя";

            _status.Text = string.IsNullOrWhiteSpace(draft.StatusMessage)
                ? (draft.Phase == PlacementPhase.Idle ? "Готово к установке" : draft.Phase.ToString())
                : draft.StatusMessage;

            _lookAt3D.Checked = draft.LookAt3D;
            _enter.Enabled = draft.IsReady && !SessionController.IsActive;
            _done.Enabled = draft.IsReady;
            _editFoot.Enabled = draft.Phase != PlacementPhase.Idle;
            _editAim.Enabled = draft.HasFoot;
            _cancel.Enabled = draft.Phase != PlacementPhase.Idle;

            if (doc is not null)
                RebuildTargets(doc, draft);

            var enterLabel = draft.CreateNewTargetView
                ? "Войти в новое окно"
                : "Войти в " + (string.IsNullOrWhiteSpace(draft.TargetLabel) ? "Perspective" : draft.TargetLabel);
            _enter.Text = enterLabel;
            _previewCaption.Text = draft.Phase is PlacementPhase.Aiming or PlacementPhase.Ready
                ? "Превью Shaded · " + enterLabel
                : "Превью Shaded";

            var frame = PlacementController.LastPreviewFrame;
            if (frame is not null)
            {
                var old = _preview.Image;
                _preview.Image = (Image)frame.Clone();
                old?.Dispose();
                frame.Dispose();
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    void RebuildTargets(RhinoDoc doc, PlacementDraft draft)
    {
        var source = doc.Views.ActiveView ?? TargetViewResolver.FindView(doc, draft.SourceViewId);
        _targetList.Items.Clear();
        if (source is null)
            return;

        var choices = TargetViewResolver.ListChoices(doc, source);
        var selected = 0;
        for (var i = 0; i < choices.Count; i++)
        {
            _targetList.Items.Add(choices[i]);
            if (choices[i].CreateNew == draft.CreateNewTargetView &&
                choices[i].ViewId == draft.TargetViewId)
                selected = i;
        }

        if (_targetList.Items.Count > 0)
            _targetList.SelectedIndex = selected;
        _targetList.DisplayMember = nameof(TargetViewChoice.Label);
    }

    void OnTargetChanged()
    {
        if (_suppressEvents || _targetList.SelectedItem is not TargetViewChoice choice)
            return;
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null)
            return;
        PlacementController.SetTargetChoice(doc, choice);
    }

    void OnPlace(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null)
            return;
        if (Rhino.Commands.Command.InCommand())
        {
            _status.Text = "Завершите текущую команду Rhino";
            return;
        }

        RhinoApp.RunScript("_AWPlace", false);
    }

    void OnEnter(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null)
            return;
        if (!PlacementController.TryEnter(doc, deferCapture: true, out var message))
            _status.Text = message;
        else
            RefreshUi();
    }

    void OnDone(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null)
            return;
        if (!PlacementController.TryFinishWithoutEnter(doc, out var message))
            _status.Text = message;
        else
            RefreshUi();
    }

    void OnEditFoot(object? sender, EventArgs e)
    {
        PlacementController.EditFoot();
        RhinoApp.RunScript("_AWPlace", false);
    }

    void OnEditAim(object? sender, EventArgs e)
    {
        PlacementController.EditAim();
        RhinoApp.RunScript("_AWPlace", false);
    }

    void OnCancel(object? sender, EventArgs e) => PlacementController.Cancel("panel");

    static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 6, 6) };
        b.Click += onClick;
        return b;
    }
}
