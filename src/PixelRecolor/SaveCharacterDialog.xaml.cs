using System.IO;
using Microsoft.Win32;

namespace PixelRecolor;

public partial class SaveCharacterDialog : System.Windows.Window
{
    private readonly string _originalCharacterName;

    public SaveCharacterDialog(
        string originalCharacterName,
        string initialCharacterName,
        string initialOutputFolder)
    {
        _originalCharacterName = originalCharacterName;
        InitializeComponent();
        CharacterNameTextBox.Text = initialCharacterName;
        OutputFolderTextBox.Text = initialOutputFolder;
        CharacterNameTextBox.SelectAll();
        CharacterNameTextBox.Focus();
        ValidateInput();
    }

    public string NewCharacterName =>
        CharacterNameTextBox.Text.Trim();

    public string OutputFolder =>
        OutputFolderTextBox.Text.Trim();

    private void BrowseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "保存先フォルダーを選択",
            Multiselect = false,
            InitialDirectory = Directory.Exists(OutputFolder)
                ? OutputFolder
                : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void Input_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateInput();
    }

    private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!ValidateInput())
        {
            return;
        }

        DialogResult = true;
    }

    private bool ValidateInput()
    {
        // InitializeComponentの途中でTextChangedが発生しても、
        // まだ生成されていないコントロールへアクセスしないようにします。
        if (!IsInitialized)
        {
            return false;
        }

        string? message = null;
        var characterName = NewCharacterName;

        if (string.IsNullOrWhiteSpace(characterName))
        {
            message = "新しいキャラクター名を入力してください。";
        }
        else if (string.Equals(
            characterName,
            _originalCharacterName,
            StringComparison.OrdinalIgnoreCase))
        {
            message = "元の名前とは異なる名前を入力してください。";
        }
        else if (characterName is "." or ".."
            || characterName.EndsWith(' ')
            || characterName.EndsWith('.')
            || characterName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            message = "名前にファイル名として使用できない文字が含まれています。";
        }
        else if (!Directory.Exists(OutputFolder))
        {
            message = "存在する出力フォルダーを指定してください。";
        }

        ValidationMessageTextBlock.Text = message ?? string.Empty;
        SaveButton.IsEnabled = message is null;
        return message is null;
    }
}
