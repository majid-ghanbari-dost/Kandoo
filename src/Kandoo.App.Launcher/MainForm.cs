using System.Windows.Forms;

namespace Kandoo.App.Launcher;

public class MainForm : Form
{
    private readonly LocalProfileStore _profiles;

    public MainForm(LocalProfileStore profiles)
    {
        _profiles = profiles;
        Text = "کندو — راه‌اندازی اولیهٔ فروشگاه";
        Size = new Size(560, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var label = new Label
        {
            Text = "راه‌اندازی اولیهٔ فروشگاه",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Tahoma", 16)
        };
        Controls.Add(label);
    }
}
