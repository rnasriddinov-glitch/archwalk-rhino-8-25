using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Observers;
using ArchWalk.RhinoPlugin.Data;
using ArchWalk.RhinoPlugin.Observers;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Session;
using ArchWalk.RhinoPlugin.Settings;
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
    readonly ListBox _list;
    readonly TextBox _rename;
    readonly TextBox _heightBox;
    readonly TextBox _speedBox;
    readonly Label _settingsCaption;
    readonly Button _place;
    readonly Button _enter;
    readonly Button _done;
    readonly Button _editFoot;
    readonly Button _editAim;
    readonly Button _cancel;
    readonly Button _update;
    readonly Button _newHere;
    readonly Button _dup;
    readonly Button _delete;
    readonly Button _saveView;
    readonly Button _renameApply;
    bool _suppressEvents;
    bool _settingsDirty;

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

        _settingsCaption = new Label
        {
            Text = "Высота и скорость — для всех наблюдателей в этом файле",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Margin = new Padding(0, 0, 0, 4)
        };

        var settingsRow = new TableLayoutPanel
        {
            ColumnCount = 4,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10)
        };
        settingsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        settingsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        settingsRow.Controls.Add(new Label
        {
            Text = "Высота глаз, мм",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 6, 0)
        }, 0, 0);
        _heightBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 12, 2) };
        _heightBox.Leave += OnSettingsCommit;
        _heightBox.KeyDown += OnSettingsKeyDown;
        _heightBox.TextChanged += (_, _) => { if (!_suppressEvents) _settingsDirty = true; };
        settingsRow.Controls.Add(_heightBox, 1, 0);

        settingsRow.Controls.Add(new Label
        {
            Text = "Скорость, м/с",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 6, 0)
        }, 2, 0);
        _speedBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
        _speedBox.Leave += OnSettingsCommit;
        _speedBox.KeyDown += OnSettingsKeyDown;
        _speedBox.TextChanged += (_, _) => { if (!_suppressEvents) _settingsDirty = true; };
        settingsRow.Controls.Add(_speedBox, 3, 0);

        var title = new Label
        {
            Text = "Наблюдатель",
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        _hint = new Label
        {
            Text = "Кликните место, затем направление взгляда.",
            AutoSize = true,
            MaximumSize = new Size(340, 0),
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

        _list = new ListBox { Height = 110, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
        _list.DisplayMember = nameof(ObserverRecord.Name);
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            if (_list.SelectedItem is ObserverRecord record)
                ObserverWorkflow.Select(record.Id);
            RefreshUi();
        };
        _list.DoubleClick += (_, _) => OnEnterSelected(null, EventArgs.Empty);

        _rename = new TextBox { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4) };
        _renameApply = MakeButton("Переименовать", OnRename);

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
        _update = MakeButton("Обновить наблюдателя", OnUpdate);
        _newHere = MakeButton("Новый наблюдатель здесь", OnNewHere);
        _dup = MakeButton("Дублировать", OnDup);
        _delete = MakeButton("Удалить", OnDelete);
        _saveView = MakeButton("Сохранить вид Rhino", OnSaveView);
        row.Controls.AddRange([_enter, _done, _editFoot, _editAim, _cancel, _update, _newHere, _dup, _delete, _saveView]);

        root.Controls.Add(_settingsCaption);
        root.Controls.Add(settingsRow);
        root.Controls.Add(title);
        root.Controls.Add(_hint);
        root.Controls.Add(_place);
        root.Controls.Add(_targetCaption);
        root.Controls.Add(_targetList);
        root.Controls.Add(_lookAt3D);
        root.Controls.Add(new Label { Text = "Сохранённые", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        root.Controls.Add(_list);
        root.Controls.Add(_rename);
        root.Controls.Add(_renameApply);
        root.Controls.Add(_previewCaption);
        root.Controls.Add(_preview);
        root.Controls.Add(_status);
        root.Controls.Add(row);
        Controls.Add(root);

        PlacementController.Changed += OnPlacementChanged;
        ObserverRepository.Changed += OnRepoChanged;
        ObserverWorkflow.UiChanged += OnPlacementChanged;
        WalkUserSettings.Changed += OnPlacementChanged;
        HandleDestroyed += (_, _) =>
        {
            PlacementController.Changed -= OnPlacementChanged;
            ObserverRepository.Changed -= OnRepoChanged;
            ObserverWorkflow.UiChanged -= OnPlacementChanged;
            WalkUserSettings.Changed -= OnPlacementChanged;
        };
        Load += (_, _) =>
        {
            ObserverWorkflow.EnsureMarkers(RhinoDoc.ActiveDoc, true);
            RefreshUi();
        };
    }

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
        ObserverWorkflow.EnsureMarkers(RhinoDoc.ActiveDoc, true);
        RefreshUi();
    }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) =>
        ObserverWorkflow.EnsureMarkers(null, false);

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        if (SessionController.IsActive)
            SessionController.Exit(WalkExitKind.KeepView, "panel-close");
        ObserverWorkflow.EnsureMarkers(null, false);
    }

    void OnPlacementChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(RefreshUi);
    }

    void OnRepoChanged(RhinoDoc doc)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(RefreshUi);
    }

    void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;
        e.SuppressKeyPress = true;
        CommitSettingsFromPanel();
    }

    void OnSettingsCommit(object? sender, EventArgs e) => CommitSettingsFromPanel();

    void CommitSettingsFromPanel()
    {
        if (_suppressEvents || !_settingsDirty)
            return;

        var doc = RhinoDoc.ActiveDoc;
        if (doc is not null && ObserverRepository.IsWriteForbidden(doc))
        {
            _status.Text = "Неизвестная схема данных — запись в 3dm заблокирована";
            SyncSettingsFields();
            return;
        }

        if (!WalkSettings.TryParseEyeHeight(_heightBox.Text, out var height))
        {
            _status.Text = "Высота: введите 300–2500 мм или 0,30–2,50 м";
            SyncSettingsFields();
            return;
        }

        if (!WalkSettings.TryParseBaseSpeed(_speedBox.Text, out var speed))
        {
            _status.Text = "Скорость: введите 0,1–6,0 м/с";
            SyncSettingsFields();
            return;
        }

        WalkUserSettings.Set(height, speed);
        PlacementController.SetEyeHeight(height);
        PlacementController.SetBaseSpeed(speed);

        if (doc is not null)
        {
            ObserverRepository.ApplyHeightAndSpeedToAll(doc, height, speed);
            ObserverWorkflow.EnsureMarkers(doc, true);
        }

        if (SessionController.IsActive)
            SessionController.ApplyHeightAndSpeed(height, speed);

        _settingsDirty = false;
        var applied =
            "Применено ко всем: H=" + WalkSettings.FormatEyeHeightMillimetres(height) +
            " мм, v=" + WalkSettings.FormatBaseSpeed(speed) + " м/с";
        RefreshUi();
        _status.Text = applied;
    }

    void SyncSettingsFields()
    {
        _suppressEvents = true;
        try
        {
            _heightBox.Text = WalkSettings.FormatEyeHeightMillimetres(WalkUserSettings.EyeHeightMeters);
            _speedBox.Text = WalkSettings.FormatBaseSpeed(WalkUserSettings.BaseSpeedMetersPerSecond);
            _settingsDirty = false;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    void RefreshUi()
    {
        if (IsDisposed) return;
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
            if (doc is not null && ObserverRepository.IsWriteForbidden(doc))
                _status.Text = "Неизвестная схема данных — запись в 3dm заблокирована";

            var writeOk = doc is null || !ObserverRepository.IsWriteForbidden(doc);
            _heightBox.ReadOnly = !writeOk;
            _speedBox.ReadOnly = !writeOk;
            if (!_settingsDirty)
            {
                _heightBox.Text = WalkSettings.FormatEyeHeightMillimetres(WalkUserSettings.EyeHeightMeters);
                _speedBox.Text = WalkSettings.FormatBaseSpeed(WalkUserSettings.BaseSpeedMetersPerSecond);
            }

            _lookAt3D.Checked = draft.LookAt3D;
            _enter.Enabled = (draft.IsReady || ObserverWorkflow.SelectedId != Guid.Empty) && !SessionController.IsActive;
            _done.Enabled = draft.IsReady;
            _editFoot.Enabled = draft.Phase != PlacementPhase.Idle;
            _editAim.Enabled = draft.HasFoot;
            _cancel.Enabled = draft.Phase != PlacementPhase.Idle;
            var hasSelection = ObserverWorkflow.SelectedId != Guid.Empty;
            _dup.Enabled = hasSelection;
            _delete.Enabled = hasSelection;
            _renameApply.Enabled = hasSelection;
            _update.Enabled = SessionController.IsActive && (ObserverWorkflow.SessionRecordId != Guid.Empty || hasSelection);
            _newHere.Enabled = SessionController.IsActive;
            _saveView.Enabled = true;

            if (doc is not null)
            {
                RebuildTargets(doc, draft);
                RebuildList(doc);
            }

            var enterLabel = draft.IsReady
                ? (draft.CreateNewTargetView
                    ? "Войти в новое окно"
                    : "Войти в " + (string.IsNullOrWhiteSpace(draft.TargetLabel) ? "Perspective" : draft.TargetLabel))
                : "Войти";
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

    void RebuildList(RhinoDoc doc)
    {
        var selected = ObserverWorkflow.SelectedId;
        _list.Items.Clear();
        var records = ObserverRepository.List(doc);
        var index = -1;
        for (var i = 0; i < records.Count; i++)
        {
            _list.Items.Add(records[i]);
            if (records[i].Id == selected)
                index = i;
        }
        if (index >= 0)
            _list.SelectedIndex = index;
        else if (records.Count > 0 && selected == Guid.Empty)
        {
            _list.SelectedIndex = 0;
            ObserverWorkflow.SelectedId = records[0].Id;
        }

        if (_list.SelectedItem is ObserverRecord rec)
            _rename.Text = rec.Name;
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
    }

    void OnTargetChanged()
    {
        if (_suppressEvents || _targetList.SelectedItem is not TargetViewChoice choice)
            return;
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null) return;
        PlacementController.SetTargetChoice(doc, choice);
    }

    void OnPlace(object? sender, EventArgs e)
    {
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
        if (doc is null) return;
        if (PlacementController.Draft.IsReady)
        {
            if (!PlacementController.TryEnter(doc, deferCapture: true, out var message))
                _status.Text = message;
            else
                RefreshUi();
            return;
        }

        OnEnterSelected(sender, e);
    }

    void OnEnterSelected(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null || ObserverWorkflow.SelectedId == Guid.Empty)
            return;
        if (!ObserverWorkflow.EnterRecord(doc, ObserverWorkflow.SelectedId, deferCapture: true))
            _status.Text = "Не удалось войти в запись";
        RefreshUi();
    }

    void OnDone(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null) return;
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

    void OnUpdate(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null) return;
        ObserverWorkflow.UpdateSelectedFromWalk(doc);
        RefreshUi();
    }

    void OnNewHere(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null) return;
        ObserverWorkflow.NewObserverHere(doc);
        RefreshUi();
    }

    void OnDup(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null || ObserverWorkflow.SelectedId == Guid.Empty) return;
        var copy = ObserverRepository.Duplicate(doc, ObserverWorkflow.SelectedId);
        if (copy is not null)
            ObserverWorkflow.Select(copy.Id);
        RefreshUi();
    }

    void OnDelete(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null || ObserverWorkflow.SelectedId == Guid.Empty) return;
        ObserverRepository.Delete(doc, ObserverWorkflow.SelectedId);
        ObserverWorkflow.SelectedId = Guid.Empty;
        RefreshUi();
    }

    void OnRename(object? sender, EventArgs e)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc is null || ObserverWorkflow.SelectedId == Guid.Empty) return;
        ObserverRepository.Rename(doc, ObserverWorkflow.SelectedId, _rename.Text);
        RefreshUi();
    }

    void OnSaveView(object? sender, EventArgs e) => RhinoApp.RunScript("_AWSaveView", false);

    static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 6, 6) };
        b.Click += onClick;
        return b;
    }
}
