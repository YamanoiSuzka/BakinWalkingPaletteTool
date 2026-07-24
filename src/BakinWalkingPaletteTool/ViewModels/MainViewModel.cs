using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BakinWalkingPaletteTool.Models;
using BakinWalkingPaletteTool.Services;
using Microsoft.Win32;

namespace BakinWalkingPaletteTool.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SpriteFileLoader _spriteFileLoader;
    private readonly ImageAnalysisService _imageAnalysisService;
    private readonly CharacterExportService _characterExportService;
    private readonly Dictionary<CharacterGroup, CharacterEditState> _editStates = [];
    private CharacterGroup? _selectedCharacter;
    private SpriteFile? _selectedSpriteFile;
    private BitmapSource? _previewImage;
    private string _currentFolder = "フォルダーが選択されていません";
    private string _statusMessage = "フォルダーを選択してください";

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
        SelectSpriteCommand = new ParameterizedRelayCommand<SpriteFile>(SelectSprite);
        SelectPaletteColorCommand =
            new ParameterizedRelayCommand<PaletteColor>(SelectPaletteColor);
        UndoCommand = new RelayCommand(Undo, CanUndo);
        RedoCommand = new RelayCommand(Redo, CanRedo);
        SaveCharacterCommand =
            new RelayCommand(SaveCharacter, () => SelectedCharacter is not null);
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

    public ICommand SelectFolderCommand { get; }

    public ICommand SelectSpriteCommand { get; }

    public ICommand SelectPaletteColorCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    public RelayCommand SaveCharacterCommand { get; }

    public void LoadFolder(string folderPath)
    {
        try
        {
            var groups = _spriteFileLoader.LoadFromFolder(folderPath);

            CharacterGroups.Clear();
            _editStates.Clear();
            foreach (var group in groups)
            {
                CharacterGroups.Add(group);
                _editStates[group] = new CharacterEditState();
            }

            CurrentFolder = folderPath;
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
                ? "有効なPNGファイルが見つかりませんでした"
                : $"{CharacterGroups.Count}キャラクター、{fileCount}ファイルを読み込みました";
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

        var affectedSources = state.Replacements
            .Where(entry => entry.Value == paletteColor.ArgbKey)
            .Select(entry => entry.Key)
            .Append(paletteColor.ArgbKey)
            .Distinct()
            .ToList();

        var operation = new ColorReplacementOperation
        {
            TargetArgb = targetArgb
        };

        foreach (var sourceArgb in affectedSources)
        {
            operation.PreviousValues[sourceArgb] =
                state.Replacements.TryGetValue(sourceArgb, out var previousTarget)
                    ? previousTarget
                    : null;
            state.Replacements[sourceArgb] = targetArgb;
        }

        state.UndoStack.Push(operation);
        state.RedoStack.Clear();
        RefreshPreviewAndPalette();
        UpdateHistoryCommands();
    }

    private void Undo()
    {
        var state = GetSelectedEditState();
        if (state is null || !state.UndoStack.TryPop(out var operation))
        {
            return;
        }

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

        state.RedoStack.Push(operation);
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

        foreach (var sourceArgb in operation.PreviousValues.Keys)
        {
            state.Replacements[sourceArgb] = operation.TargetArgb;
        }

        state.UndoStack.Push(operation);
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
            $"{SelectedCharacter.CharacterName}_variant",
            Directory.Exists(CurrentFolder)
                ? CurrentFolder
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

            StatusMessage =
                $"{dialog.NewCharacterName}：{savedPaths.Count}ファイルを保存しました";
            System.Windows.MessageBox.Show(
                $"{savedPaths.Count}個のアニメーション画像を保存しました。\n\n{dialog.OutputFolder}",
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
            PaletteColors.Add(color);
        }

        StatusMessage =
            $"{SelectedSpriteFile.FileName} — {displayedImage.PixelWidth}×{displayedImage.PixelHeight}px、{palette.Count}色";
    }

    private void UpdateHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveCharacterCommand.NotifyCanExecuteChanged();
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
        UpdateHistoryCommands();
    }

    private sealed class CharacterEditState
    {
        public Dictionary<uint, uint> Replacements { get; } = [];

        public Stack<ColorReplacementOperation> UndoStack { get; } = [];

        public Stack<ColorReplacementOperation> RedoStack { get; } = [];
    }

    private sealed class ColorReplacementOperation
    {
        public required uint TargetArgb { get; init; }

        public Dictionary<uint, uint?> PreviousValues { get; } = [];
    }

    private void SelectFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "歩行グラフィックが入っているフォルダーを選択",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            LoadFolder(dialog.FolderName);
        }
    }
}
