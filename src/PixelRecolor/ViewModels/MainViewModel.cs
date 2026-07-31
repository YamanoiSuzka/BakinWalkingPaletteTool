using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PixelRecolor.Models;
using PixelRecolor.Services;
using Microsoft.Win32;

namespace PixelRecolor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SpriteFileLoader _spriteFileLoader;
    private readonly ImageAnalysisService _imageAnalysisService;
    private readonly CharacterExportService _characterExportService;

    // 置換内容と履歴はキャラクターごとに分離し、同じ元色を持つ別キャラへ波及させません。
    private readonly Dictionary<CharacterGroup, CharacterEditState> _editStates = [];
    private CharacterGroup? _selectedCharacter;
    private SpriteFile? _selectedSpriteFile;
    private BitmapSource? _previewImage;
    private BitmapSource? _adjustmentPreviewBaseImage;
    private string _currentFolder = "フォルダーが選択されていません";
    private string _statusMessage = "フォルダーを選択してください";
    private bool _isUpdatingSelectAll;
    private bool _isAdjustmentPreviewActive;
    private bool _isUpdatingAdjustments;
    private bool _areAllPaletteColorsSelected;
    private double _hueShift;
    private double _saturationAdjustment;
    private double _brightnessAdjustment;
    private double _transparencyAdjustment;

    public MainViewModel()
        : this(
            new SpriteFileLoader(),
            new ImageAnalysisService(),
            new CharacterExportService(new ImageAnalysisService()))
    {
    }

    public MainViewModel(
        SpriteFileLoader spriteFileLoader,
        ImageAnalysisService imageAnalysisService,
        CharacterExportService characterExportService)
    {
        _spriteFileLoader = spriteFileLoader;
        _imageAnalysisService = imageAnalysisService;
        _characterExportService = characterExportService;
        SelectFolderCommand = new RelayCommand(SelectFolder);
        SelectFileCommand = new RelayCommand(SelectFile);
        SelectSpriteCommand = new ParameterizedRelayCommand<SpriteFile>(SelectSprite);
        SelectPaletteColorCommand =
            new ParameterizedRelayCommand<PaletteColor>(SelectPaletteColor);
        UndoCommand = new RelayCommand(Undo, CanUndo);
        RedoCommand = new RelayCommand(Redo, CanRedo);
        SaveCharacterCommand =
            new RelayCommand(SaveCharacter, () => SelectedCharacter is not null);
        ApplyColorAdjustmentCommand =
            new RelayCommand(ApplyColorAdjustment, CanApplyColorAdjustment);
        ResetColorAdjustmentsCommand =
            new RelayCommand(ResetColorAdjustments, CanResetColorAdjustments);
    }

    public ObservableCollection<CharacterGroup> CharacterGroups { get; } = [];

    public ObservableCollection<PaletteColor> PaletteColors { get; } = [];

    public CharacterGroup? SelectedCharacter
    {
        get => _selectedCharacter;
        set => SetProperty(ref _selectedCharacter, value);
    }

    public SpriteFile? SelectedSpriteFile
    {
        get => _selectedSpriteFile;
        private set => SetProperty(ref _selectedSpriteFile, value);
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public string CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool AreAllPaletteColorsSelected
    {
        get => _areAllPaletteColorsSelected;
        set
        {
            if (!SetProperty(ref _areAllPaletteColorsSelected, value)
                || _isUpdatingSelectAll)
            {
                return;
            }

            SetAllPaletteColorsSelected(value);
        }
    }

    public int SelectedColorCount => GetSelectedArgbValues().Count;

    public bool HasSelectedColors => SelectedColorCount > 0;

    public bool HasPendingColorAdjustments =>
        HueShift != 0
        || SaturationAdjustment != 0
        || BrightnessAdjustment != 0
        || TransparencyAdjustment != 0;

    public bool HasUnsavedChanges => _editStates.Values.Any(
        state => !AreReplacementMapsEqual(
            state.Replacements,
            state.SavedReplacements));

    public double HueShift
    {
        get => _hueShift;
        set
        {
            if (SetProperty(
                ref _hueShift,
                ClampAdjustment(value, 0, 180)))
            {
                OnPropertyChanged(nameof(HasPendingColorAdjustments));
                UpdateAdjustmentCommands();
            }
        }
    }

    public double SaturationAdjustment
    {
        get => _saturationAdjustment;
        set
        {
            if (SetProperty(
                ref _saturationAdjustment,
                ClampAdjustment(value, -100, 100)))
            {
                OnPropertyChanged(nameof(HasPendingColorAdjustments));
                UpdateAdjustmentCommands();
            }
        }
    }

    public double BrightnessAdjustment
    {
        get => _brightnessAdjustment;
        set
        {
            if (SetProperty(
                ref _brightnessAdjustment,
                ClampAdjustment(value, -100, 100)))
            {
                OnPropertyChanged(nameof(HasPendingColorAdjustments));
                UpdateAdjustmentCommands();
            }
        }
    }

    public double TransparencyAdjustment
    {
        get => _transparencyAdjustment;
        set
        {
            if (SetProperty(
                ref _transparencyAdjustment,
                ClampAdjustment(value, -100, 100)))
            {
                OnPropertyChanged(nameof(HasPendingColorAdjustments));
                UpdateAdjustmentCommands();
            }
        }
    }

    public ICommand SelectFolderCommand { get; }

    public ICommand SelectFileCommand { get; }

    public ICommand SelectSpriteCommand { get; }

    public ICommand SelectPaletteColorCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    public RelayCommand SaveCharacterCommand { get; }

    public RelayCommand ApplyColorAdjustmentCommand { get; }

    public RelayCommand ResetColorAdjustmentsCommand { get; }

    public event EventHandler? ColorAdjustmentApplied;

    /// <summary>
    /// 色調整ダイアログを開いたとき、一時プレビューを有効にします。
    /// この段階では置換マップやUNDO履歴を変更しません。
    /// </summary>
    public void BeginColorAdjustmentPreview()
    {
        _isAdjustmentPreviewActive = true;
        _adjustmentPreviewBaseImage = PreviewImage;
        RefreshAdjustmentPreview();
    }

    /// <summary>
    /// 未適用の調整値を破棄し、最後に確定した画像へ戻します。
    /// </summary>
    public void EndColorAdjustmentPreview()
    {
        _isAdjustmentPreviewActive = false;
        _isUpdatingAdjustments = true;
        ResetColorAdjustments();
        _isUpdatingAdjustments = false;
        RefreshPreviewAndPalette();
        _adjustmentPreviewBaseImage = null;
        UpdateAdjustmentCommands();
    }

    public void LoadFolder(string folderPath)
    {
        try
        {
            var groups = _spriteFileLoader.LoadFromFolder(folderPath);
            if (!ConfirmDiscardUnsavedChanges("別のフォルダーを読み込む"))
            {
                return;
            }

            SetLoadedGroups(groups, folderPath);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "フォルダーの読み込みに失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void LoadFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "PNGファイルが見つかりません。",
                    filePath);
            }

            var groups = _spriteFileLoader.LoadFiles([filePath]);
            if (!ConfirmDiscardUnsavedChanges("別のPNGを読み込む"))
            {
                return;
            }

            SetLoadedGroups(groups, filePath);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "PNGファイルの読み込みに失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SetLoadedGroups(
        IReadOnlyList<CharacterGroup> groups,
        string sourcePath)
    {
        // 読み込み元を切り替えた時点で、前の素材に対する編集履歴は破棄します。
        CharacterGroups.Clear();
        _editStates.Clear();
        foreach (var group in groups)
        {
            CharacterGroups.Add(group);
            _editStates[group] = new CharacterEditState();
        }

        CurrentFolder = sourcePath;
        SelectedCharacter = CharacterGroups.FirstOrDefault();
        if (SelectedCharacter is not null)
        {
            SelectedCharacter.IsExpanded = true;
        }

        var firstFile = SelectedCharacter?.Files.FirstOrDefault();
        if (firstFile is not null)
        {
            SelectSprite(firstFile);
        }
        else
        {
            ClearPreview();
        }

        UpdateHistoryCommands();

        var fileCount = CharacterGroups.Sum(group => group.FileCount);
        StatusMessage = CharacterGroups.Count == 0
            ? "PNGファイルが見つかりませんでした"
            : $"{CharacterGroups.Count}グループ、{fileCount}ファイルを読み込みました";
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    public bool ConfirmDiscardUnsavedChanges(string actionDescription)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(
            $"保存されていない色変更があります。\n\n"
            + $"{actionDescription}と、変更内容とUNDO・REDO履歴は破棄されます。"
            + "\n続行しますか？",
            "未保存の変更",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void SelectSprite(SpriteFile spriteFile)
    {
        try
        {
            if (SelectedSpriteFile is not null)
            {
                SelectedSpriteFile.IsSelected = false;
            }

            SelectedSpriteFile = spriteFile;
            SelectedSpriteFile.IsSelected = true;
            SelectedCharacter = CharacterGroups.FirstOrDefault(
                group => group.Files.Contains(spriteFile));
            RefreshPreviewAndPalette();
            UpdateHistoryCommands();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "PNG画像の読み込みに失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SelectPaletteColor(PaletteColor paletteColor)
    {
        if (SelectedCharacter is null)
        {
            return;
        }

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(
                paletteColor.Color.R,
                paletteColor.Color.G,
                paletteColor.Color.B)
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        var targetArgb = ((uint)paletteColor.Color.A << 24)
            | ((uint)dialog.Color.R << 16)
            | ((uint)dialog.Color.G << 8)
            | dialog.Color.B;

        if (targetArgb == paletteColor.ArgbKey)
        {
            return;
        }

        var state = GetSelectedEditState();
        if (state is null)
        {
            return;
        }

        // 表示中の色へ既に変換されている元色もまとめて次の色へ向けます。
        // 例: 赤→青の後に青→緑を行うと、元の赤と青の両方が緑になります。
        var affectedSources = state.Replacements
            .Where(entry => entry.Value == paletteColor.ArgbKey)
            .Select(entry => entry.Key)
            .Append(paletteColor.ArgbKey)
            .Distinct()
            .ToList();

        var operation = new ColorReplacementOperation();

        foreach (var sourceArgb in affectedSources)
        {
            // nullは「この操作以前は置換マップに未登録だった」ことを表します。
            // UNDO時は値を戻すのではなく、そのキー自体を削除します。
            operation.PreviousValues[sourceArgb] =
                state.Replacements.TryGetValue(sourceArgb, out var previousTarget)
                    ? previousTarget
                    : null;
            operation.NewValues[sourceArgb] = targetArgb;
            state.Replacements[sourceArgb] = targetArgb;
        }

        if (state.SelectedColors.Remove(paletteColor.ArgbKey))
        {
            state.SelectedColors.Add(targetArgb);
        }

        state.UndoStack.Push(operation);
        state.RedoStack.Clear();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RefreshPreviewAndPalette();
        UpdateHistoryCommands();
    }

    /// <summary>
    /// パレット色の右クリックから、複数選択状態を切り替えます。
    /// </summary>
    public void TogglePaletteColorSelection(PaletteColor paletteColor)
    {
        var state = GetSelectedEditState();
        if (state is null)
        {
            return;
        }

        paletteColor.IsSelected = !paletteColor.IsSelected;
        if (paletteColor.IsSelected)
        {
            state.SelectedColors.Add(paletteColor.ArgbKey);
        }
        else
        {
            state.SelectedColors.Remove(paletteColor.ArgbKey);
        }

        UpdateSelectionState();
    }

    /// <summary>
    /// プレビュー画像上で取得した色のカラーピッカーを開きます。
    /// </summary>
    public void OpenPreviewPixelColorPicker(int x, int y)
    {
        var paletteColor = GetPreviewPaletteColor(x, y);
        if (paletteColor is not null)
        {
            SelectPaletteColor(paletteColor);
        }
    }

    /// <summary>
    /// プレビュー画像上で取得した色の選択状態を切り替えます。
    /// </summary>
    public void TogglePreviewPixelSelection(int x, int y)
    {
        var paletteColor = GetPreviewPaletteColor(x, y);
        if (paletteColor is not null)
        {
            TogglePaletteColorSelection(paletteColor);
        }
    }

    private PaletteColor? GetPreviewPaletteColor(int x, int y)
    {
        if (PreviewImage is null)
        {
            return null;
        }

        var argb = _imageAnalysisService.GetArgbAt(PreviewImage, x, y);
        return PaletteColors.FirstOrDefault(color => color.ArgbKey == argb);
    }

    private void SetAllPaletteColorsSelected(bool isSelected)
    {
        var state = GetSelectedEditState();
        if (state is null)
        {
            return;
        }

        if (!isSelected)
        {
            state.SelectedColors.Clear();
        }

        foreach (var color in PaletteColors)
        {
            color.IsSelected = isSelected;
            if (isSelected)
            {
                state.SelectedColors.Add(color.ArgbKey);
            }
        }

        UpdateSelectionState();
    }

    private void ApplyColorAdjustment()
    {
        var state = GetSelectedEditState();
        var selectedColors = GetSelectedArgbValues();
        if (state is null || selectedColors.Count == 0)
        {
            return;
        }

        // 画面上の選択状態も履歴側へ同期してから処理します。
        // これにより、右クリックによる個別選択も全選択と同じ経路で適用されます。
        state.SelectedColors.UnionWith(selectedColors);
        var operation = new ColorReplacementOperation();
        var unchangedColors = new List<uint>();

        foreach (var currentArgb in selectedColors)
        {
            var targetArgb = _imageAnalysisService.AdjustArgb(
                currentArgb,
                HueShift,
                SaturationAdjustment,
                BrightnessAdjustment,
                TransparencyAdjustment);
            if (targetArgb == currentArgb)
            {
                unchangedColors.Add(currentArgb);
                continue;
            }

            var affectedSources = state.Replacements
                .Where(entry => entry.Value == currentArgb)
                .Select(entry => entry.Key)
                .Append(currentArgb)
                .Distinct()
                .ToList();

            foreach (var sourceArgb in affectedSources)
            {
                if (!operation.PreviousValues.ContainsKey(sourceArgb))
                {
                    operation.PreviousValues[sourceArgb] =
                        state.Replacements.TryGetValue(sourceArgb, out var previousTarget)
                            ? previousTarget
                            : null;
                }

                operation.NewValues[sourceArgb] = targetArgb;
                state.Replacements[sourceArgb] = targetArgb;
            }
        }

        if (operation.NewValues.Count == 0)
        {
            var message = BuildNoChangeMessage(
                changedColorCount: 0,
                unchangedColors);
            StatusMessage = "指定した調整では選択色が変化しませんでした";
            System.Windows.MessageBox.Show(
                message,
                "色が変化しませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var changedColorCount = selectedColors.Count - unchangedColors.Count;
        var partialNoChangeMessage = unchangedColors.Count > 0
            ? BuildNoChangeMessage(changedColorCount, unchangedColors)
            : null;

        state.SelectedColors.Clear();
        state.UndoStack.Push(operation);
        state.RedoStack.Clear();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        _isUpdatingAdjustments = true;
        ResetColorAdjustments();
        _isUpdatingAdjustments = false;
        RefreshPreviewAndPalette();
        if (_isAdjustmentPreviewActive)
        {
            _adjustmentPreviewBaseImage = PreviewImage;
        }

        StatusMessage = unchangedColors.Count == 0
            ? $"{changedColorCount}色へ調整を適用しました"
            : $"{changedColorCount}色を変更、{unchangedColors.Count}色は変化しませんでした";
        UpdateHistoryCommands();

        if (partialNoChangeMessage is not null)
        {
            System.Windows.MessageBox.Show(
                partialNoChangeMessage,
                "一部の色が変化しませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        ColorAdjustmentApplied?.Invoke(this, EventArgs.Empty);
    }

    private bool CanApplyColorAdjustment()
    {
        return GetSelectedArgbValues().Count > 0
            && (HueShift != 0
                || SaturationAdjustment != 0
                || BrightnessAdjustment != 0
                || TransparencyAdjustment != 0);
    }

    private bool CanResetColorAdjustments()
    {
        return HueShift != 0
            || SaturationAdjustment != 0
            || BrightnessAdjustment != 0
            || TransparencyAdjustment != 0;
    }

    private void ResetColorAdjustments()
    {
        HueShift = 0;
        SaturationAdjustment = 0;
        BrightnessAdjustment = 0;
        TransparencyAdjustment = 0;
    }

    private HashSet<uint> GetSelectedArgbValues()
    {
        var selectedColors = GetSelectedEditState()?.SelectedColors is { } stored
            ? new HashSet<uint>(stored)
            : [];

        // PaletteColor.IsSelectedを正として取り込むことで、
        // マウス操作直後でも選択状態の同期漏れを起こさないようにします。
        selectedColors.UnionWith(
            PaletteColors
                .Where(color => color.IsSelected)
                .Select(color => color.ArgbKey));
        return selectedColors;
    }

    private static double ClampAdjustment(
        double value,
        double minimum,
        double maximum)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : 0;
    }

    private string BuildNoChangeMessage(
        int changedColorCount,
        IReadOnlyCollection<uint> unchangedColors)
    {
        var heading = changedColorCount == 0
            ? $"選択した{unchangedColors.Count}色は、指定した調整では変化しませんでした。"
            : $"{changedColorCount}色は変更されましたが、{unchangedColors.Count}色は変化しませんでした。";
        var reasons = new List<string>();

        if (HueShift != 0 && unchangedColors.Any(IsGrayscale))
        {
            reasons.Add(
                "グレー・白・黒など彩度が0の色は、色相だけを変更しても見た目が変わりません。彩度も上げてください。");
        }

        if (SaturationAdjustment > 0
            && unchangedColors.Any(IsMaximumSaturation))
        {
            reasons.Add("既に彩度が最大の色は、それ以上鮮やかにできません。");
        }
        else if (SaturationAdjustment < 0
            && unchangedColors.Any(IsGrayscale))
        {
            reasons.Add("既に彩度が0の色は、それ以上彩度を下げられません。");
        }

        if (BrightnessAdjustment > 0
            && unchangedColors.Any(color => GetMaximumRgb(color) == byte.MaxValue))
        {
            reasons.Add("既に明度が最大の色は、それ以上明るくできません。");
        }
        else if (BrightnessAdjustment < 0
            && unchangedColors.Any(color => GetMaximumRgb(color) == 0))
        {
            reasons.Add("既に明度が最小の黒は、それ以上暗くできません。");
        }

        if (TransparencyAdjustment < 0
            && unchangedColors.Any(color => (byte)(color >> 24) == byte.MaxValue))
        {
            reasons.Add(
                "透明度のマイナス値は色を不透明にします。既に完全に不透明な色は変化しません。");
        }
        else if (TransparencyAdjustment > 0
            && unchangedColors.Any(color => (byte)(color >> 24) == 0))
        {
            reasons.Add(
                "既に完全に透明な色は、それ以上透明にできません。");
        }

        if (reasons.Count == 0)
        {
            reasons.Add(
                "選択色が指定方向の上限または下限に達している可能性があります。調整値を変更してください。");
        }

        return $"{heading}\n\n{string.Join("\n", reasons.Select(reason => $"・{reason}"))}";
    }

    private static bool IsGrayscale(uint argb)
    {
        var red = (byte)(argb >> 16);
        var green = (byte)(argb >> 8);
        var blue = (byte)argb;
        return red == green && green == blue;
    }

    private static bool IsMaximumSaturation(uint argb)
    {
        var red = (byte)(argb >> 16);
        var green = (byte)(argb >> 8);
        var blue = (byte)argb;
        return Math.Max(red, Math.Max(green, blue)) > 0
            && Math.Min(red, Math.Min(green, blue)) == 0;
    }

    private static byte GetMaximumRgb(uint argb)
    {
        var red = (byte)(argb >> 16);
        var green = (byte)(argb >> 8);
        var blue = (byte)argb;
        return Math.Max(red, Math.Max(green, blue));
    }

    private static bool AreReplacementMapsEqual(
        IReadOnlyDictionary<uint, uint> first,
        IReadOnlyDictionary<uint, uint> second)
    {
        return first.Count == second.Count
            && first.All(entry =>
                second.TryGetValue(entry.Key, out var value)
                && value == entry.Value);
    }

    private void Undo()
    {
        var state = GetSelectedEditState();
        if (state is null || !state.UndoStack.TryPop(out var operation))
        {
            return;
        }

        // 操作前の値をキーごとに復元します。未登録だったキーは削除します。
        foreach (var (sourceArgb, previousTarget) in operation.PreviousValues)
        {
            if (previousTarget.HasValue)
            {
                state.Replacements[sourceArgb] = previousTarget.Value;
            }
            else
            {
                state.Replacements.Remove(sourceArgb);
            }
        }

        state.SelectedColors.Clear();
        state.RedoStack.Push(operation);
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RefreshPreviewAndPalette();
        UpdateHistoryCommands();
    }

    private void Redo()
    {
        var state = GetSelectedEditState();
        if (state is null || !state.RedoStack.TryPop(out var operation))
        {
            return;
        }

        // UNDOした操作で影響を受けた全元色を、同じ置換先へ再適用します。
        foreach (var (sourceArgb, targetArgb) in operation.NewValues)
        {
            state.Replacements[sourceArgb] = targetArgb;
        }

        state.SelectedColors.Clear();
        state.UndoStack.Push(operation);
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RefreshPreviewAndPalette();
        UpdateHistoryCommands();
    }

    private void SaveCharacter()
    {
        if (SelectedCharacter is null)
        {
            return;
        }

        var dialog = new SaveCharacterDialog(
            SelectedCharacter.CharacterName,
            $"{SelectedCharacter.CharacterName}-variant",
            Path.GetDirectoryName(SelectedSpriteFile?.FilePath) is { } sourceFolder
                && Directory.Exists(sourceFolder)
                ? sourceFolder
                : Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures))
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var outputPaths = _characterExportService.GetOutputPaths(
                SelectedCharacter,
                dialog.NewCharacterName,
                dialog.OutputFolder);
            // 複数ファイルのうち1つでも既存なら、書き込みを始める前にまとめて確認します。
            var existingPaths = outputPaths
                .Where(File.Exists)
                .ToList();

            if (existingPaths.Count > 0)
            {
                var confirmation = System.Windows.MessageBox.Show(
                    $"{existingPaths.Count}個のファイルが既に存在します。上書きしますか？",
                    "上書きの確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var replacements = GetSelectedEditState()?.Replacements
                ?? new Dictionary<uint, uint>();
            var savedPaths = _characterExportService.Export(
                SelectedCharacter,
                dialog.NewCharacterName,
                dialog.OutputFolder,
                replacements);

            // 正常に保存できた時点の置換内容を記録し、以後の未保存判定の基準にします。
            var editState = GetSelectedEditState();
            if (editState is not null)
            {
                editState.SavedReplacements.Clear();
                foreach (var (sourceArgb, targetArgb) in editState.Replacements)
                {
                    editState.SavedReplacements[sourceArgb] = targetArgb;
                }
            }

            OnPropertyChanged(nameof(HasUnsavedChanges));
            var savedItemName = SelectedCharacter.Files.Any(
                file => file.IsAnimationFile)
                    ? "アニメーション画像"
                    : "PNG画像";

            StatusMessage =
                $"{dialog.NewCharacterName}：{savedPaths.Count}ファイルを保存しました";
            System.Windows.MessageBox.Show(
                $"{savedPaths.Count}個の{savedItemName}を保存しました。\n\n{dialog.OutputFolder}",
                "保存完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "保存に失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool CanUndo() => GetSelectedEditState()?.UndoStack.Count > 0;

    private bool CanRedo() => GetSelectedEditState()?.RedoStack.Count > 0;

    private CharacterEditState? GetSelectedEditState()
    {
        return SelectedCharacter is not null
            && _editStates.TryGetValue(SelectedCharacter, out var state)
                ? state
                : null;
    }

    private void RefreshPreviewAndPalette()
    {
        if (SelectedSpriteFile is null)
        {
            ClearPreview();
            return;
        }

        // 変換済み画像へ再変換すると誤差や置換漏れが生じるため、
        // 毎回元PNGを読み、現在の置換マップを最初から適用します。
        var originalImage = _imageAnalysisService.LoadImage(SelectedSpriteFile.FilePath);
        var state = GetSelectedEditState();
        var displayedImage = state is null
            ? originalImage
            : _imageAnalysisService.ApplyReplacements(
                originalImage,
                state.Replacements);
        var palette = _imageAnalysisService.ExtractPalette(displayedImage);

        PreviewImage = displayedImage;
        PaletteColors.Clear();
        foreach (var color in palette)
        {
            color.IsSelected = state?.SelectedColors.Contains(color.ArgbKey) == true;
            PaletteColors.Add(color);
        }

        UpdateSelectionState();
        StatusMessage =
            $"{SelectedSpriteFile.FileName} — {displayedImage.PixelWidth}×{displayedImage.PixelHeight}px、{palette.Count}色";
    }

    private void UpdateHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveCharacterCommand.NotifyCanExecuteChanged();
        ApplyColorAdjustmentCommand.NotifyCanExecuteChanged();
    }

    private void UpdateAdjustmentCommands()
    {
        ApplyColorAdjustmentCommand.NotifyCanExecuteChanged();
        ResetColorAdjustmentsCommand.NotifyCanExecuteChanged();

        if (_isAdjustmentPreviewActive && !_isUpdatingAdjustments)
        {
            RefreshAdjustmentPreview();
        }
    }

    private void RefreshAdjustmentPreview()
    {
        if (!_isAdjustmentPreviewActive
            || _adjustmentPreviewBaseImage is null)
        {
            return;
        }

        var temporaryReplacements = GetSelectedArgbValues()
            .Select(sourceArgb => new
            {
                SourceArgb = sourceArgb,
                TargetArgb = _imageAnalysisService.AdjustArgb(
                    sourceArgb,
                    HueShift,
                    SaturationAdjustment,
                    BrightnessAdjustment,
                    TransparencyAdjustment)
            })
            .Where(replacement =>
                replacement.SourceArgb != replacement.TargetArgb)
            .ToDictionary(
                replacement => replacement.SourceArgb,
                replacement => replacement.TargetArgb);

        PreviewImage = _imageAnalysisService.ApplyReplacements(
            _adjustmentPreviewBaseImage,
            temporaryReplacements);
        StatusMessage = temporaryReplacements.Count == 0
            ? "色調整プレビュー：変化なし"
            : $"色調整プレビュー：{temporaryReplacements.Count}色を一時表示中";
    }

    private void UpdateSelectionState()
    {
        _isUpdatingSelectAll = true;
        AreAllPaletteColorsSelected = PaletteColors.Count > 0
            && PaletteColors.All(color => color.IsSelected);
        _isUpdatingSelectAll = false;
        OnPropertyChanged(nameof(SelectedColorCount));
        OnPropertyChanged(nameof(HasSelectedColors));
        ApplyColorAdjustmentCommand.NotifyCanExecuteChanged();
    }

    private void ClearPreview()
    {
        if (SelectedSpriteFile is not null)
        {
            SelectedSpriteFile.IsSelected = false;
        }

        SelectedSpriteFile = null;
        PreviewImage = null;
        PaletteColors.Clear();
        UpdateSelectionState();
        UpdateHistoryCommands();
    }

    private sealed class CharacterEditState
    {
        // キーと値はいずれも 0xAARRGGBB 形式です。
        public Dictionary<uint, uint> Replacements { get; } = [];

        // 最後に正常保存した置換内容です。現在値との比較により未保存状態を判定します。
        public Dictionary<uint, uint> SavedReplacements { get; } = [];

        public Stack<ColorReplacementOperation> UndoStack { get; } = [];

        public Stack<ColorReplacementOperation> RedoStack { get; } = [];

        // 画像を切り替えても維持する、現在の表示色ARGBの複数選択です。
        public HashSet<uint> SelectedColors { get; } = [];
    }

    private sealed class ColorReplacementOperation
    {
        // UNDOで復元する操作前の値です。nullは操作前にキーが存在しなかったことを示します。
        public Dictionary<uint, uint?> PreviousValues { get; } = [];

        // REDO時に各元色へ再設定する置換先です。複数色の一括調整にも対応します。
        public Dictionary<uint, uint> NewValues { get; } = [];
    }

    private void SelectFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "PNG画像が入っているフォルダーを選択",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            LoadFolder(dialog.FolderName);
        }
    }

    private void SelectFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "編集するPNG画像を選択",
            Filter = "PNG画像 (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            LoadFile(dialog.FileName);
        }
    }
}
