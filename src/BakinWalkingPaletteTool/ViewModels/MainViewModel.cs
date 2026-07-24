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
    private CharacterGroup? _selectedCharacter;
    private SpriteFile? _selectedSpriteFile;
    private BitmapSource? _previewImage;
    private string _currentFolder = "フォルダーが選択されていません";
    private string _statusMessage = "フォルダーを選択してください";

    public MainViewModel()
        : this(new SpriteFileLoader(), new ImageAnalysisService())
    {
    }

    public MainViewModel(
        SpriteFileLoader spriteFileLoader,
        ImageAnalysisService imageAnalysisService)
    {
        _spriteFileLoader = spriteFileLoader;
        _imageAnalysisService = imageAnalysisService;
        SelectFolderCommand = new RelayCommand(SelectFolder);
        SelectSpriteCommand = new ParameterizedRelayCommand<SpriteFile>(SelectSprite);
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

    public void LoadFolder(string folderPath)
    {
        try
        {
            var groups = _spriteFileLoader.LoadFromFolder(folderPath);

            CharacterGroups.Clear();
            foreach (var group in groups)
            {
                CharacterGroups.Add(group);
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
            MessageBox.Show(
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

            var image = _imageAnalysisService.LoadImage(spriteFile.FilePath);
            var palette = _imageAnalysisService.ExtractPalette(image);

            SelectedSpriteFile = spriteFile;
            SelectedSpriteFile.IsSelected = true;
            SelectedCharacter = CharacterGroups.FirstOrDefault(
                group => group.Files.Contains(spriteFile));
            PreviewImage = image;

            PaletteColors.Clear();
            foreach (var color in palette)
            {
                PaletteColors.Add(color);
            }

            StatusMessage =
                $"{spriteFile.FileName} — {image.PixelWidth}×{image.PixelHeight}px、{palette.Count}色";
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            MessageBox.Show(
                exception.Message,
                "PNG画像の読み込みに失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
