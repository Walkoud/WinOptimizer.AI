using System.Windows;

namespace WinOptimizer.AI
{
    public partial class PromptEditorWindow : Window
    {
        public PromptEditorWindow(string promptText)
        {
            InitializeComponent();
            PromptEditorTextBox.Text = promptText ?? string.Empty;
            ApplyLanguage();
        }

        public string EditedPrompt => PromptEditorTextBox.Text;

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(EditedPrompt ?? string.Empty);
            MessageBox.Show(LanguageManager.GetString("StatusCopied"), LanguageManager.GetString("PromptEditorWindowTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ApplyLanguage()
        {
            Title = LanguageManager.GetString("PromptEditorWindowTitle");
            PromptEditorTitleText.Text = LanguageManager.GetString("PromptEditorHeader");
            BtnCopyPromptEditor.Content = LanguageManager.GetString("PromptEditorBtnCopy");
            BtnClosePromptEditor.Content = LanguageManager.GetString("PromptEditorBtnClose");
        }
    }
}
