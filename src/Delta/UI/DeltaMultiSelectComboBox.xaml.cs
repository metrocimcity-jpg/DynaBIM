using System.Windows;
using System.Windows.Controls;
using Autodesk.DesignScript.Runtime;

namespace Delta.UI;

[SupressImportIntoVM]
public partial class DeltaMultiSelectComboBox : UserControl
{
    public DeltaMultiSelectComboBox()
    {
        InitializeComponent();
    }

    private void Popup_Opened(object sender, EventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
